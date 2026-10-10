using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 네비게이션 — 자동항해 목적지를 도시 <b>이름</b>으로 고르고, 잡힌 항로 몇 가지 가운데 하나를 고르는 창.
/// </summary>
/// <remarks>
/// 게임에는 없는 창이다. 제목 줄의 「네비게이션」 단추나 그 단축키로 연다. 꼴은 게임 목록 창
/// (<see cref="HintListDialog"/>)을 따른다 — 게임 제목 띠, 종이 판 위 검은 글씨, 고른 줄은 남색 바탕에 흰 글씨,
/// 아래에 게임 띠 단추 둘. 도시는 <b>문화권 탭</b>으로 갈라 낸다(탭 머리는 모드 창과 같은 꼴).
///
/// 두 걸음이다. 도시를 골라 「항해 시작」을 누르면 항로 후보가 뜨고(지도 미리보기 · 뱃머리를 트는 자리),
/// 하나를 골라 「확인」을 누르면 그 길로 떠난다.
///
/// 지도를 <b>클릭</b>해 목적지를 찍는 길은 이 창을 거치지 않고 발견물지도(<see cref="DiscoveryMapDialog"/>)와
/// 주 지도에서 바로 된다.
/// </remarks>
public sealed class AutoSailDialog : GameWindow
{
    /// <summary>항로 후보 하나.</summary>
    /// <param name="Label">이름 — 「가장 빠른 길」 따위.</param>
    /// <param name="Detail">이름 옆에 붙는 말 — 걸리는 날수 따위.</param>
    /// <param name="Route">바닷길 마디(칸).</param>
    /// <param name="Walk">배를 댄 뒤 걸어갈 도시 자리(칸). 항구로 들어가는 길이면 null.</param>
    /// <param name="Legs">뱃머리를 트는 자리들.</param>
    public sealed record Plan(string Label, string Detail, IReadOnlyList<(double X, double Y)> Route,
                              (double X, double Y)? Walk, IReadOnlyList<string> Legs);

    /// <summary>도시 한 칸의 폭과 한 줄에 놓는 칸 수.</summary>
    private const double CellWidth = 124;
    private const int Columns = 3;

    /// <summary>도시 판이 이보다 길어지면 굴린다.</summary>
    private const double ListMaxHeight = 264;

    /// <summary>항로 미리보기 지도의 높이와, 그 밑 조타 목록이 굴러가기 시작하는 높이.</summary>
    private const double MapHeight = 210, LegsMaxHeight = 96;

    private const double ButtonWidth = 106, ButtonGap = 12;

    /// <summary>판 · 탭 머리 · 고르개의 좌우 여백.</summary>
    private const double Side = 12;

    /// <summary>고른 칸의 바탕과 테 — 게임 목록 창과 같은 남색이다.</summary>
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0x43, 0x56, 0x7A));
    private static readonly Brush PickEdge = Frozen(Color.FromRgb(0x05, 0x06, 0x09));

    /// <summary>미리보기 지도 위의 선 — 고른 항로, 다른 후보, 걸어갈 길.</summary>
    private static readonly Brush RouteInk = Frozen(Color.FromRgb(0xFF, 0x8C, 0x1A));
    private static readonly Brush OtherInk = Frozen(Color.FromArgb(0xB0, 0x20, 0x20, 0x28));
    private static readonly Brush WalkInk = Frozen(Color.FromRgb(0x6A, 0x2C, 0x10));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Func<int, (bool Ok, string Message, IReadOnlyList<Plan> Plans)> _start;
    private readonly BitmapSource? _chart;

    /// <summary>도시 칸과 그 도시 번호.</summary>
    private readonly List<(Border Cell, int Id)> _cells = [];

    private readonly GameButton _go;

    private readonly TextBlock _note = new()
    {
        Margin = new Thickness(10, 0, 10, 6),
        Foreground = Brushes.OrangeRed,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = CellWidth * Columns,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>고른 도시. 아직 안 골랐으면 −1.</summary>
    private int _picked = -1;

    /// <summary>항로 후보를 띄운 도시 — 「항해 시작」을 누른 뒤다. 아직이면 −1.</summary>
    private int _bound = -1;

    /// <summary>후보 가운데 고른 것, 그리고 「확인」으로 닫았는지.</summary>
    private int _plan;
    private bool _confirmed;

    private IReadOnlyList<Plan> _plans = [];
    private readonly List<Border> _planRows = [];
    private readonly List<(Polyline Line, Line? Walk)> _planLines = [];
    private readonly StackPanel _legs = new() { Width = CellWidth * Columns };
    private double _mapScale = 1;

    /// <summary>도시 판들과 그 위에 겹쳐 두는 항로 판 · 탭 머리와 그 위에 겹쳐 두는 항로 제목.</summary>
    private readonly Grid _pages = new();
    private readonly Grid _heads = new();

    private AutoSailDialog(IReadOnlyList<(int Id, string Name, string Culture, bool Harbor)> cities, BitmapSource? chart,
                           Func<int, (bool Ok, string Message, IReadOnlyList<Plan> Plans)> start)
    {
        _start = start;
        _chart = chart;

        Title = "네비게이션";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var title = GameUi.TitleBar("네비게이션", Close);
        GameUi.EnableDrag(this, title);

        // 문화권마다 판 하나 — 한 칸에 겹쳐 두고 안 보이는 쪽은 Hidden 으로, 탭을 넘겨도 창 크기가 안 바뀐다.
        var tabs = new List<(string Text, FrameworkElement Page)>();
        foreach (var group in cities.GroupBy(c => c.Culture.Length > 0 ? c.Culture : "그 밖")
                                    .OrderBy(g => CultureRank(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var grid = new UniformGrid { Columns = Columns, Width = CellWidth * Columns, VerticalAlignment = VerticalAlignment.Top };
            foreach (var (id, name, _, harbor) in group.OrderBy(c => c.Name, StringComparer.Ordinal))
            {
                var cell = Cell(name, harbor ? Mark.Harbor : Mark.Inland);
                cell.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    Pick(id);
                    if (e.ClickCount == 2) Start();   // 두 번 누르면 곧바로 항로를 잡는다
                };
                _cells.Add((cell, id));
                grid.Children.Add(cell);
            }

            var page = Paper(GameUi.Scroller(grid, ListMaxHeight));
            _pages.Children.Add(page);
            tabs.Add(($"{group.Key} {group.Count()}", page));
        }

        // 자동이동 모드 — 속도 중시(기본)는 선에 바짝 붙어 가느라 뱃머리를 자주 틀고, 최소 조타는 곧게 뻗는 토막으로 간다.
        // 고르개는 모드 창 「자동 보급」 밑의 것과 같은 꼴이다.
        RadioButton Mode(string text, bool steady)
        {
            var radio = new RadioButton
            {
                Content = text, GroupName = "AutoSailMode",
                IsChecked = GameSettings.SteadyHelm == steady,
                Foreground = GameUi.Text, FontSize = 14,
                Margin = new Thickness(0, 0, 16, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            radio.Checked += (_, _) =>
            {
                GameSettings.SteadyHelm = steady;
                // 후보가 떠 있으면 그 모드로 다시 잡는다 — 길의 꼴이 달라진다.
                if (_bound >= 0) ShowPlans(_bound);
            };
            return radio;
        }
        var helm = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(Side, 8, Side, 2),
            Children =
            {
                new TextBlock
                {
                    Text = "자동이동 모드", Foreground = GameUi.Text, FontSize = 14, FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center,
                },
                Mode("속도 중시", false),
                Mode("최소 조타", true),
            },
        };

        // 아직 아무것도 안 골랐으면 「항해 시작」은 흐리다 — 게임 목록 창의 「결정」과 같다.
        _go = new GameButton("항해 시작", Start, BandStyle.Button, ButtonWidth)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(0, 0, ButtonGap / 2, 0),
            On = false,
        };
        var stop = new GameButton("중단", Close, BandStyle.Button, ButtonWidth)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(ButtonGap / 2, 0, 0, 0),
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
            Children = { _go, stop },
        };

        var stack = new StackPanel();
        stack.Children.Add(title);
        _heads.Children.Add(Tabs(tabs));
        stack.Children.Add(_heads);
        stack.Children.Add(_pages);
        stack.Children.Add(helm);
        stack.Children.Add(buttons);
        stack.Children.Add(_note);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape) Close();
            else if (e.Key is Key.Enter && (_picked >= 0 || _bound >= 0)) Start();
        };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>문화권 탭의 차례 — 도시 문화권 표의 차례를 따르고, 모르는 이름은 뒤로 보낸다.</summary>
    private static int CultureRank(string culture)
    {
        string[] order = ["이베리아", "북유럽", "지중해", "아프리카", "이슬람", "중근동", "인도", "중국", "중앙아시아", "동남아시아", "일본", "아메리카"];
        int at = Array.IndexOf(order, culture);
        return at < 0 ? order.Length : at;
    }

    /// <summary>종이 판 — 게임 목록 창의 판과 같은 바탕 · 테다.</summary>
    private static Border Paper(FrameworkElement child) => new()
    {
        Background = GameUi.PageFill,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(1),
        Margin = new Thickness(Side, 3, Side, 0),
        Padding = new Thickness(2, 1, 2, 1),
        VerticalAlignment = VerticalAlignment.Top,
        Child = child,
    };

    /// <summary>종이 위 검은 글씨 — 게임 비트맵 글꼴이다.</summary>
    private static GameUi.GameLabel Ink(string text) => new(GameFont.BlackColor, GameUi.ItemTextHeight)
    {
        Text = text,
        Bold = false,
        FallbackBrush = Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>칸 앞에 다는 표식 — 없음, 항구 도시(닻), 내륙 도시(닻 자리만 비워 글씨 줄을 맞춘다).</summary>
    private enum Mark { None, Harbor, Inland }

    /// <summary>닻 — 12 x 12 안에 선으로 그린다. 고리 · 자루 · 가로대 · 갈고리.</summary>
    private static readonly Geometry Anchor = FrozenShape(
        "M 6,3.4 A 1.2,1.2 0 1 1 6.01,3.4 M 6,3.4 V 11 M 3.6,5.4 H 8.4 M 1.6,7.4 C 1.6,9.8 4,11 6,11 C 8,11 10.4,9.8 10.4,7.4");

    private static Geometry FrozenShape(string data)
    {
        var shape = Geometry.Parse(data);
        shape.Freeze();
        return shape;
    }

    private const double MarkSize = 12, MarkGap = 4;

    /// <summary>고를 수 있는 칸 하나.</summary>
    private static Border Cell(string text, Mark mark = Mark.None)
    {
        FrameworkElement content = Ink(text);
        if (mark != Mark.None)
        {
            var anchor = new System.Windows.Shapes.Path
            {
                Data = Anchor,
                Width = MarkSize, Height = MarkSize,
                Stroke = Brushes.Black, StrokeThickness = 1.3,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Margin = new Thickness(0, 0, MarkGap, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = mark == Mark.Harbor ? Visibility.Visible : Visibility.Hidden,
                ToolTip = mark == Mark.Harbor ? "항구가 있는 도시" : null,
            };
            content = new StackPanel { Orientation = Orientation.Horizontal, Children = { anchor, content } };
        }
        return new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(1),
            Padding = new Thickness(3, 0, 3, 0),
            Cursor = Cursors.Hand,
            Child = content,
        };
    }

    /// <summary>칸을 고른 모양(남색 바탕 · 흰 글씨 · 흰 닻) 또는 쉬는 모양으로 칠한다.</summary>
    private static void Paint(Border cell, bool on)
    {
        cell.Background = on ? PickFill : Brushes.Transparent;
        cell.BorderBrush = on ? PickEdge : Brushes.Transparent;
        IEnumerable<UIElement> parts = cell.Child is Panel panel ? panel.Children.Cast<UIElement>() : [cell.Child];
        foreach (var part in parts)
        {
            if (part is GameUi.GameLabel label) label.TextColor = on ? GameFont.WhiteColor : GameFont.BlackColor;
            else if (part is System.Windows.Shapes.Path shape) shape.Stroke = on ? Brushes.White : Brushes.Black;
        }
    }

    private void Pick(int id)
    {
        _picked = id;
        foreach (var (cell, at) in _cells) Paint(cell, at == id);
        _go.On = true;
        _note.Text = "";
    }

    /// <summary>탭 머리 — 누른 쪽 판만 보이고 머리는 밝게 선다(모드 창과 같은 꼴). 많으면 다음 줄로 넘긴다.</summary>
    private static FrameworkElement Tabs(List<(string Text, FrameworkElement Page)> tabs)
    {
        var bar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Width = CellWidth * Columns + 6,
            Margin = new Thickness(Side, 8, Side, 0),
        };
        var heads = new List<(Border Head, FrameworkElement Page)>();

        void Select(FrameworkElement page)
        {
            foreach (var (head, p) in heads)
            {
                bool on = ReferenceEquals(p, page);
                p.Visibility = on ? Visibility.Visible : Visibility.Hidden;
                head.Background = on ? new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                ((TextBlock)head.Child).Opacity = on ? 1 : 0.6;
            }
        }

        foreach (var (text, page) in tabs)
        {
            var head = new Border
            {
                BorderBrush = GameUi.Edge,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 3, 2),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 14 },
            };
            head.MouseLeftButtonDown += (_, _) => Select(page);
            heads.Add((head, page));
            bar.Children.Add(head);
        }
        if (tabs.Count > 0) Select(tabs[0].Page);
        return bar;
    }

    /// <summary>
    /// 「항해 시작」 — 그 도시로 가는 항로 후보를 띄운다. 후보가 떠 있으면 같은 단추가 「확인」이라 고른 길로 닫는다.
    /// </summary>
    private void Start()
    {
        if (_bound >= 0) { _confirmed = true; Close(); return; }
        if (_picked < 0) { _note.Text = "목적지 도시를 고르세요."; return; }
        ShowPlans(_picked);
    }

    /// <summary>
    /// 항로 후보를 잡아 도시 목록 자리에 낸다 — 후보 줄, 지도 미리보기, 고른 후보가 뱃머리를 트는 자리들.
    /// </summary>
    private void ShowPlans(int city)
    {
        // 먼 도시는 길을 찾는 데 한두 초가 걸린다 — 멈춘 듯 보이지 않게 깜빡이는 쪽지를 띄운다(지도를 처음 읽을 때의 그것이다).
        bool ok;
        string message;
        IReadOnlyList<Plan> plans;
        var loading = LoadingDialog.Open(this, "항로를 계산합니다...");
        try { (ok, message, plans) = _start(city); }
        finally { loading.Close(); }
        if (!ok || plans.Count == 0) { _note.Text = ok ? "바닷길을 찾지 못했습니다" : message; return; }
        _note.Text = "";
        _plans = plans;
        _planRows.Clear();
        _planLines.Clear();

        // 앞서 띄운 후보 판 · 제목이 있으면 걷는다(모드를 바꿔 다시 잡았을 때).
        if (_bound >= 0)
        {
            _pages.Children.RemoveAt(_pages.Children.Count - 1);
            _heads.Children.RemoveAt(_heads.Children.Count - 1);
        }
        _bound = city;

        var rows = new StackPanel { Width = CellWidth * Columns };
        for (int i = 0; i < plans.Count; i++)
        {
            int at = i;
            var row = Cell(plans[i].Detail.Length > 0 ? $"{plans[i].Label}  {plans[i].Detail}" : plans[i].Label);
            row.MouseLeftButtonDown += (_, e) => { e.Handled = true; PickPlan(at); };
            _planRows.Add(row);
            rows.Children.Add(row);
        }

        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        body.Children.Add(Paper(rows));
        body.Children.Add(Paper(Map(plans)));
        body.Children.Add(Paper(GameUi.Scroller(_legs, LegsMaxHeight)));

        // 도시 판은 가려만 둔다(Hidden) — 창 폭이 그대로다. 탭 머리는 걷는다(Collapsed) — 가려만 두면 탭 두 줄
        // 높이가 그대로 남아 제목 띠와 항로 제목 사이가 휑하다.
        foreach (UIElement page in _pages.Children) page.Visibility = Visibility.Hidden;
        _pages.Children.Add(body);
        foreach (UIElement head in _heads.Children) head.Visibility = Visibility.Collapsed;
        _heads.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 14,
            Margin = new Thickness(Side, 6, Side, 1),
            VerticalAlignment = VerticalAlignment.Bottom,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = CellWidth * Columns,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        _go.Text = "확인";
        PickPlan(Math.Min(_plan, plans.Count - 1));
    }

    private void PickPlan(int index)
    {
        _plan = index;
        for (int i = 0; i < _planRows.Count; i++) Paint(_planRows[i], i == index);

        // 고른 길만 굵고 밝게 — 맨 위에 올려 다른 선에 가리지 않게 한다.
        for (int i = 0; i < _planLines.Count; i++)
        {
            var (line, walk) = _planLines[i];
            bool on = i == index;
            line.Stroke = on ? RouteInk : OtherInk;
            line.StrokeThickness = (on ? 2.4 : 1.4) / _mapScale;
            Panel.SetZIndex(line, on ? 2 : 1);
            if (walk == null) continue;
            walk.Opacity = on ? 1 : 0.45;
            Panel.SetZIndex(walk, on ? 2 : 1);
        }

        _legs.Children.Clear();
        foreach (string leg in _plans[index].Legs)
            _legs.Children.Add(new Border
            {
                Margin = new Thickness(1),
                Padding = new Thickness(3, 0, 3, 0),
                Child = Ink(leg),
            });
    }

    /// <summary>
    /// 항로 미리보기 — 세계 지도 가운데 후보들이 다 드는 만큼만 잘라 보이고, 그 위에 후보마다 선을 긋는다.
    /// </summary>
    /// <remarks>
    /// 지도는 칸 자리 그대로 놓고(가로 2500 · 세로 1250) 통째로 줄여 옮긴다. 항로의 가로 자리는 날짜변경선을
    /// 넘으면 0 밑이나 2500 위로 나가므로 지도를 좌우로 한 장씩 더 붙여 둔다.
    /// </remarks>
    private FrameworkElement Map(IReadOnlyList<Plan> plans)
    {
        const double mapW = WorldMapRenderer.UnfoldedW, mapH = WorldMapRenderer.CellH;
        double viewW = CellWidth * Columns, viewH = MapHeight;

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        void Grow((double X, double Y) p)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        foreach (var plan in plans)
        {
            foreach (var p in plan.Route) Grow(p);
            if (plan.Walk is { } w && plan.Route.Count > 0) Grow(NearTo(w, plan.Route[^1]));
        }
        // 둘레를 조금 띄우고, 너무 바짝 당겨지지 않게 한다. 보이는 네모의 가로세로 비는 지킨다.
        double spanX = Math.Max(60, (maxX - minX) * 1.25), spanY = Math.Max(60, (maxY - minY) * 1.25);
        double scale = _mapScale = Math.Min(viewW / spanX, viewH / spanY);
        double midX = (minX + maxX) / 2, midY = (minY + maxY) / 2;

        var world = new Canvas
        {
            Width = mapW, Height = mapH,
            RenderTransform = new TransformGroup
            {
                Children =
                {
                    new TranslateTransform(-midX, -midY),
                    new ScaleTransform(scale, scale),
                    new TranslateTransform(viewW / 2, viewH / 2),
                },
            },
        };
        if (_chart != null)
            for (int lap = -1; lap <= 1; lap++)
            {
                var sheet = new Image { Source = _chart, Width = mapW, Height = mapH, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(sheet, BitmapScalingMode.HighQuality);
                Canvas.SetLeft(sheet, lap * mapW);
                world.Children.Add(sheet);
            }

        foreach (var plan in plans)
        {
            var line = new Polyline { StrokeLineJoin = PenLineJoin.Round };
            foreach (var (x, y) in plan.Route) line.Points.Add(new Point(x, y));
            world.Children.Add(line);

            Line? walk = null;
            if (plan.Walk is { } to && plan.Route.Count > 0)
            {
                var from = plan.Route[^1];
                var near = NearTo(to, from);
                walk = new Line
                {
                    X1 = from.X, Y1 = from.Y, X2 = near.X, Y2 = near.Y,
                    Stroke = WalkInk, StrokeThickness = 1.8 / scale,
                    StrokeDashArray = [3, 2],
                };
                world.Children.Add(walk);
                world.Children.Add(Dot(near, 5 / scale, WalkInk));
            }
            _planLines.Add((line, walk));
        }
        if (plans[0].Route.Count > 0) world.Children.Add(Dot(plans[0].Route[0], 5 / scale, Brushes.White));

        return new Canvas
        {
            Width = viewW, Height = viewH,
            ClipToBounds = true,
            Background = GameUi.MapCover,
            Children = { world },
        };
    }

    /// <summary>가로가 이어진 지도에서 <paramref name="p"/> 를 <paramref name="near"/> 에 가까운 바퀴로 옮긴다.</summary>
    private static (double X, double Y) NearTo((double X, double Y) p, (double X, double Y) near)
    {
        const double mapW = WorldMapRenderer.UnfoldedW;
        double x = p.X;
        while (x - near.X > mapW / 2) x -= mapW;
        while (x - near.X < -mapW / 2) x += mapW;
        return (x, p.Y);
    }

    private static Ellipse Dot((double X, double Y) at, double size, Brush fill)
    {
        var dot = new Ellipse { Width = size, Height = size, Fill = fill, Stroke = Brushes.Black, StrokeThickness = size / 6 };
        Canvas.SetLeft(dot, at.X - size / 2);
        Canvas.SetTop(dot, at.Y - size / 2);
        Panel.SetZIndex(dot, 3);
        return dot;
    }

    /// <summary>창을 연다. 고를 도시가 하나도 없으면 아무 일도 안 한다.</summary>
    /// <param name="cities">고를 수 있는 도시(번호 · 이름 · 문화권 · 항구가 있는지).</param>
    /// <param name="chart">미리보기에 깔 세계 지도(칸 2500 × 1250 을 덮는 그림). 없으면 선만 긋는다.</param>
    /// <param name="start">
    /// 도시 번호를 받아 그리로 가는 항로 후보들을 잡는다 — 제목 한 줄과 후보들을 돌려준다. 앞의 것이 기본이다.
    /// 실패하면 그 까닭을 돌려준다 — 창은 안 닫힌다. 아직 떠나지는 않는다.
    /// </param>
    /// <returns>후보를 골라 「확인」으로 닫았으면 그 도시 번호와 후보 차례, 아니면 (−1, −1).</returns>
    public static (int City, int Plan) Show(Window owner, IEnumerable<(int Id, string Name, string Culture, bool Harbor)> cities,
                                            BitmapSource? chart,
                                            Func<int, (bool Ok, string Message, IReadOnlyList<Plan> Plans)> start)
    {
        var list = cities.ToList();
        if (list.Count == 0) { NoticeDialog.Show(owner, "아는 도시가 없습니다"); return (-1, -1); }
        var dialog = new AutoSailDialog(list, chart, start) { Owner = owner };
        dialog.ShowDialog();
        return dialog._confirmed && dialog._bound >= 0 ? (dialog._bound, dialog._plan) : (-1, -1);
    }
}
