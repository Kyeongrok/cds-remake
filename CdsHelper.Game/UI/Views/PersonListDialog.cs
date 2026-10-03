using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물정보 목록(모드 「인물정보 목록」) — 제독과 부하를 초상화 · 기능 · 언어로 늘어놓고, 고른 사람의 맡은 기능 효과를 오른쪽에 편다.
/// </summary>
/// <remarks>
/// 글씨는 <b>리디바탕</b>이다 — 기능 · 언어가 여러 줄로 길어 게임 비트맵 글꼴보다 읽기 쉽다. 틀 · 바탕 · 고른 줄 색은
/// 공용 목록 창(<see cref="HintListDialog"/>)과 같다. 줄 글에서 앞에 <c>~</c> 가 붙은 낱말은 흐린 색(<c>#80786C</c>)이다 —
/// 그 자리에서 안 쓰이는 기능 · 언어다. 설명 칸은 줄을 고르기 전에는 접혀 있고, 펴질 때 창이 가운데를 지킨다.
/// </remarks>
public sealed class PersonListDialog : GameWindow
{
    /// <summary>한 사람 — 이름 · 초상화 · 이름 밑 줄들(줄바꿈으로 가른다) · 오른쪽 자리 · 설명 · 판 열기.</summary>
    public sealed record Entry(string Name, uint[]? Face, string Lines, string Role, string Description, Action Open);

    private static readonly FontFamily Ridi =
        new(new Uri("pack://application:,,,/CdsHelper.Game;component/"), "./Fonts/#RIDIBatang");

    private static readonly Brush Ink = Frozen(Color.FromRgb(0x10, 0x0A, 0x08));
    private static readonly Brush Dim = Frozen(Color.FromRgb(0x80, 0x78, 0x6C));
    private static readonly Brush PickInk = Frozen(Color.FromRgb(0xF4, 0xE8, 0xE0));
    private static readonly Brush PickDim = Frozen(Color.FromRgb(0xA8, 0xA0, 0x94));
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0x43, 0x56, 0x7A));
    private static readonly Brush PickEdge = Frozen(Color.FromRgb(0x05, 0x06, 0x09));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>초상화 크기 — 원본(80x96)의 8할.</summary>
    private const double FaceW = 64, FaceH = 77;

    private const double ListWidth = 470, DescribeWidth = 340;

    private readonly IReadOnlyList<Entry> _entries;
    private readonly List<Border> _rows = [];

    /// <summary>줄마다의 글 조각 — 고르고 풀 때 색을 다시 칠한다(흐린 조각인지 함께 든다).</summary>
    private readonly List<List<(TextElement Run, bool Dim)>> _runs = [];
    private readonly List<List<TextBlock>> _blocks = [];

    private readonly Border _describePane;
    private readonly TextBlock _describe = new()
    {
        FontFamily = Ridi,
        FontSize = 15,
        LineHeight = 23,
        Foreground = Ink,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly GameButton _decide;
    private int _picked = -1;

    private PersonListDialog(IReadOnlyList<Entry> entries)
    {
        _entries = entries;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel { Width = ListWidth };
        for (int i = 0; i < entries.Count; i++) list.Children.Add(Row(i));

        var page = new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3, 3, 3, 0),
            Padding = new Thickness(2, 1, 2, 1),
            Child = list,
        };

        _describePane = new Border
        {
            Visibility = Visibility.Collapsed,
            Width = DescribeWidth,
            Margin = new Thickness(0, 3, 3, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Child = _describe,
        };

        var side = new StackPanel { Orientation = Orientation.Horizontal };
        side.Children.Add(page);
        side.Children.Add(_describePane);

        _decide = new GameButton("결정", Decide, BandStyle.Button, 106)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(0, 0, 6, 0),
            On = false,
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
            Children =
            {
                _decide,
                new GameButton("중단", Close, BandStyle.Button, 106)
                {
                    Height = UiSprites.BandHeight,
                    Margin = new Thickness(6, 0, 0, 0),
                },
            },
        };

        var title = GameUi.TitleBar("인물정보", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(side);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Background = GameUi.MenuBack,
                BorderBrush = GameUi.MenuEdge,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4),
                Child = stack,
            },
        };

        // 설명 칸이 펴지며 창이 넓어지면 가운데를 지키도록 왼쪽으로 반만큼 민다.
        SizeChanged += (_, e) =>
        {
            if (e.WidthChanged && e.PreviousSize.Width > 0)
                Left -= (e.NewSize.Width - e.PreviousSize.Width) / 2;
        };

        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Enter: Decide(); break;
                case Key.Up: Select(Math.Max(0, _picked - 1)); break;
                case Key.Down: Select(Math.Min(_entries.Count - 1, _picked + 1)); break;
            }
        };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>한 사람 줄 — 왼쪽 초상화, 가운데 이름과 줄들, 오른쪽 자리.</summary>
    private Border Row(int index)
    {
        var entry = _entries[index];
        var runs = new List<(TextElement, bool)>();
        var blocks = new List<TextBlock>();

        TextBlock Block(string text, bool bold = false)
        {
            var block = new TextBlock
            {
                FontFamily = Ridi,
                FontSize = 15,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Ink,
                TextWrapping = TextWrapping.Wrap,
            };
            string[] words = text.Split(' ');
            for (int k = 0; k < words.Length; k++)
            {
                bool dim = words[k].StartsWith('~');
                var run = new Run((dim ? words[k][1..] : words[k]) + (k < words.Length - 1 ? " " : ""))
                {
                    Foreground = dim ? Dim : Ink,
                };
                block.Inlines.Add(run);
                runs.Add((run, dim));
            }
            blocks.Add(block);
            return block;
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 4, 4, 4) };
        text.Children.Add(Block(entry.Name, bold: true));
        foreach (string line in entry.Lines.Split('\n'))
            if (line.Length > 0) text.Children.Add(Block(line));

        var face = new Image
        {
            Width = FaceW,
            Height = FaceH,
            Stretch = Stretch.Fill,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 2),
        };
        if (entry.Face is { } px && px.Length >= Portraits.Width * Portraits.Height)
        {
            var bitmap = Portraits.Bitmap(px);
            bitmap.Freeze();
            face.Source = bitmap;
            RenderOptions.SetBitmapScalingMode(face, Portraits.Scaling(face.Source));
        }

        var role = Block(entry.Role, bold: true);
        role.VerticalAlignment = VerticalAlignment.Center;
        role.Margin = new Thickness(8, 0, 6, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(face, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(role, 2);
        grid.Children.Add(face);
        grid.Children.Add(text);
        grid.Children.Add(role);

        var row = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(1),
            Padding = new Thickness(3, 0, 3, 0),
            Cursor = Cursors.Hand,
            Child = grid,
        };
        row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Select(index); };
        row.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { e.Handled = true; Select(index); Decide(); } };

        _rows.Add(row);
        _runs.Add(runs);
        _blocks.Add(blocks);
        return row;
    }

    /// <summary>줄을 고른다 — 남색으로 칠하고 오른쪽 설명 칸을 편다.</summary>
    private void Select(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        _picked = index;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = i == index;
            _rows[i].Background = on ? PickFill : Brushes.Transparent;
            _rows[i].BorderBrush = on ? PickEdge : Brushes.Transparent;
            foreach (var (run, dim) in _runs[i])
                run.Foreground = on ? dim ? PickDim : PickInk : dim ? Dim : Ink;
        }
        _describe.Text = _entries[index].Description;
        _describePane.Visibility = Visibility.Visible;
        _decide.On = true;
    }

    /// <summary>결정 — 고른 사람의 인물정보 판을 띄우고, 닫으면 목록으로 돌아온다.</summary>
    private void Decide()
    {
        if (_picked < 0) return;
        _entries[_picked].Open();
    }

    /// <summary>목록을 띄운다. 중단할 때까지 판을 몇 번이고 열 수 있다.</summary>
    public static void Show(Window owner, IReadOnlyList<Entry> entries) =>
        new PersonListDialog(entries) { Owner = owner }.ShowDialog();
}
