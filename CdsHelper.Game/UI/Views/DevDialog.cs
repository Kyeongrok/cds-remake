using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 개발용 창 — 소지금과 명성을 손으로 올리고 내린다.
/// </summary>
/// <remarks>
/// 게임에는 없는 창이다. 명성이 오르는 길(발견물 발표)이나 돈이 도는 길(교역)을 아직
/// 흉내내지 않아서, 후원자 알현처럼 값이 있어야 볼 수 있는 것들을 시험하려면 손으로
/// 넣어 줄 데가 필요하다.
///
/// 늘리고 줄이는 단추는 정해진 폭으로 움직이고, 칸에 수를 적어 넣으면 그 값이 그대로 된다.
/// 값은 0 밑으로 안 내려간다.
///
/// 놀 때 켜고 끄는 편의 기능(컨디션·미니맵·기능·언어·출입 일수)은 <see cref="ModDialog"/> 로
/// 옮겼다 — 여기는 값을 밀어 넣어 <b>시험</b>하는 데다.
/// </remarks>
public sealed class DevDialog : GameWindow
{
    /// <summary>단추 한 번에 움직이는 폭.</summary>
    private const int GoldStep = 10000, FameStep = 500;

    private readonly Player _player;
    private readonly TextBox _gold = Field();
    private readonly TextBox _fame = Field();

    /// <summary>개발 창이 만지는 것들. 늘어나서 묶어 두었다.</summary>
    public sealed class Options
    {
        /// <summary>좌표 겹쳐 보기.</summary>
        public Func<bool> CoordsOn { get; init; } = () => false;
        public Action<bool> SetCoords { get; init; } = _ => { };

        /// <summary>도구 앱(Editor.exe) 띄우기 — 창을 닫은 뒤 부른다.</summary>
        public Action? HelperApp { get; init; }

        /// <summary>묘책 확률 표(<c>0x00549B80</c>) 보고 고치기 — 창을 닫은 뒤 부른다.</summary>
        public Action? RuseTable { get; init; }

        /// <summary>시나리오(미리 만든 주인공 + 이야기 책) 짓기 — 창을 닫은 뒤 부른다.</summary>
        public Action? Scenarios { get; init; }

        /// <summary>싸움 셈을 돌려 보는 세 가지 — 일기토 · 육상전 모의전 · 모의해전. 창을 닫은 뒤 부른다.</summary>
        public Action? Duel { get; init; }

        /// <summary>아이템 넣기 — 세이브 파일을 골라 그 주인공 소지품에 아이템을 넣는다. 창을 닫은 뒤 부른다.</summary>
        public Action? GiveItem { get; init; }
        public Action? LandSpar { get; init; }
        public Action? SeaSpar { get; init; }

        /// <summary>퀘스트(개인 이야기) 상태 창 — 창을 닫은 뒤 부른다.</summary>
        public Action? Quest { get; init; }

        /// <summary>해를 바꾼 뒤 — 되돌렸으면(<c>true</c>) 앞으로만 가는 것들을 다시 연다.</summary>
        public Action<bool>? YearChanged { get; init; }

        /// <summary>발견물 표 — 「발견물」 탭이 늘어놓는다. 못 읽었으면 탭이 안 선다.</summary>
        public DiscoveryTable? Discoveries { get; init; }

        /// <summary>발견물 체크를 바꾸고 창을 닫은 뒤 — 지도의 발견물 그림을 다시 맞춘다.</summary>
        public Action? DiscoveriesChanged { get; init; }

        /// <summary>
        /// 대본이 발견을 안 적지만 사건을 마친 발견물인지 — 인물(다 빈치 · 코페르니쿠스 따위) · 비보가 그렇다.
        /// 발견물 지도(<see cref="DiscoveryMapDialog"/>)가 찾은 것으로 찍는 것과 같은 셈이다. 없으면 아무것도 그렇게 안 친다.
        /// </summary>
        public Func<int, bool>? EventDone { get; init; }

        /// <summary>커스텀 발견물 창 — 「발견물」 탭의 단추가 창을 닫은 뒤 부른다.</summary>
        public Action? UserDiscoveries { get; init; }

        /// <summary>등록된 역사 대본 칸들 — 「역사이벤트」 탭이 늘어놓는다. 못 읽었으면 탭이 안 선다.</summary>
        public Func<List<Engine.Market.CityHistory.Entry>>? HistoryEvents { get; init; }

        /// <summary>여급 표 — 「여급」 탭이 늘어놓는다. 못 읽었으면 탭이 안 선다.</summary>
        public BarmaidTable? Barmaids { get; init; }

        /// <summary>도시 번호 → 이름(「여급」 탭).</summary>
        public Func<int, string>? CityName { get; init; }
    }

    /// <summary>발견물 탭에서 체크를 하나라도 바꿨는지.</summary>
    private bool _findsChanged;

    private DevDialog(Player player, Options options)
    {
        _player = player;

        Title = "개발";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var rows = new StackPanel { Margin = new Thickness(12, 10, 12, 4) };
        // 「테스트」 탭 — 베타 테스트를 돕는 것(안 죽기 · 재해 줄이기).
        var tests = new StackPanel { Margin = new Thickness(12, 10, 12, 4) };
        rows.Children.Add(Row("소지금", _gold, GoldStep, v => _player.SetGold(v)));
        rows.Children.Add(Row("명성", _fame, FameStep, v => _player.Fame = v));
        rows.Children.Add(YearRow(options.YearChanged));
        rows.Children.Add(ShipRow());
        rows.Children.Add(EffectRow());
        rows.Children.Add(SpouseRow());

        // 좌표 겹쳐 보기 — 배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄운다.
        // 게임에는 없는 것이라 이 창으로 옮겨 두었다.
        rows.Children.Add(Toggle("좌표 겹쳐 보기", options.CoordsOn(), options.SetCoords,
            "배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄웁니다"));

        // 일기토 불사 — 베타 테스트용. 내 체력이 안 떨어지고, 판은 오른쪽 단추 「승리 · 항복」으로 끝낸다.
        tests.Children.Add(Toggle("일기토 불사", GameSettings.DuelImmortal, on => GameSettings.DuelImmortal = on,
            "일기토에서 죽지 않습니다. 명령을 고를 차례에 오른쪽 단추를 누르면 「승리 · 항복」으로 판을 끝냅니다"));

        // 「대항2」 탭 — 대항해시대2 모양으로 바꾸는 것들. 아직 만드는 중이라 모드 창에는 안 내놓고 여기서만 켠다.
        var dh2 = new StackPanel { Margin = new Thickness(12, 10, 12, 4) };
        dh2.Children.Add(Toggle("대항해시대2 스타일 대화창", GameSettings.Dh2TalkStyle, on => GameSettings.Dh2TalkStyle = on,
            "얼굴이 붙는 대사 창을 줄무늬 테 · 베이지 바탕 · 검은 글씨로 냅니다. 얼굴 없는 알림 · 물음창은 그대로입니다"));
        dh2.Children.Add(Toggle("대항해시대2 스타일 인물정보", GameSettings.Dh2PersonInfo, on => GameSettings.Dh2PersonInfo = on,
            "인물정보 판(제독 · 부하 · 술집에서 고용할 사람)을 줄무늬 테 · 베이지 바탕, 빨간 항목 이름 · 검은 값, 왕관 꼴 단추로 냅니다"));

        // 「애드온」 탭 — Lua 애드온(AddOnHost). 아직 만드는 중이라 모드 창에는 안 내놓고 여기서만 켠다.
        var addOns = new StackPanel { Margin = new Thickness(12, 10, 12, 4) };
        addOns.Children.Add(AddOnControls());

        // 반란 빈도 — 바다 반란이 일어날 몫. 100% 가 원본 그대로다.
        {
            var value = new TextBlock { Width = 48, Foreground = GameUi.Text, VerticalAlignment = VerticalAlignment.Center };
            var slider = new Slider
            {
                Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true,
                Width = 150, Value = GameSettings.MutinyRate, VerticalAlignment = VerticalAlignment.Center,
            };
            value.Text = $"{slider.Value:0}%";
            slider.ValueChanged += (_, _) =>
            {
                value.Text = $"{slider.Value:0}%";
                GameSettings.MutinyRate = (int)slider.Value;
            };
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 4),
                ToolTip = "바다에서 반란이 일어나는 빈도입니다. 100% 가 원본 그대로이고 0% 면 안 일어납니다",
            };
            line.Children.Add(new TextBlock
            {
                Text = "반란 빈도",
                Width = 80,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(slider);
            line.Children.Add(value);
            tests.Children.Add(line);
        }

        // 아이템 넣기 — 세이브 파일을 골라 그 주인공 소지품에 아이템을 넣는다(베타 테스트용).
        if (options.GiveItem is { } giveItem)
        {
            var give = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            give.Children.Add(new TextBlock
            {
                Text = "세이브",
                Width = 80,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            give.Children.Add(GameUi.PushButton("아이템 넣기…", () => { Close(); Engine.HardcoreRules.Mark("dev:item"); giveItem(); }, 180));
            tests.Children.Add(give);
        }

        // 모의전 셋 — 원본에 없는 줄이라 미니 게임 차림표에서 이 창으로 옮겼다.
        foreach (var (label, run) in new (string, Action?)[]
                 { ("일기토", options.Duel), ("육상전 모의전", options.LandSpar), ("모의해전", options.SeaSpar) })
        {
            if (run is not { } go) continue;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            line.Children.Add(new TextBlock
            {
                Text = "모의전",
                Width = 64,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(GameUi.PushButton(label + "…", () => { Close(); go(); }, 180));
            rows.Children.Add(line);
        }

        // 퀘스트 — 개인 이야기의 진행값과 지금 파트의 발동 조건을 풀어 본다.
        if (options.Quest is { } quest)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            line.Children.Add(new TextBlock
            {
                Text = "퀘스트",
                Width = 64,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(GameUi.PushButton("이야기 상태…", () => { Close(); Engine.HardcoreRules.Mark("dev:quest"); quest(); }, 180));
            rows.Children.Add(line);
        }

        // 묘책 확률 표 — 게임 표를 그대로 보여 주고 고치면 게임에도 바로 든다.
        if (options.RuseTable is { } ruses)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            line.Children.Add(new TextBlock
            {
                Text = "표",
                Width = 64,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(GameUi.PushButton("묘책 확률…", () => { Close(); Engine.HardcoreRules.Mark("dev:ruse"); ruses(); }, 180));
            rows.Children.Add(line);
        }

        // 시나리오 — 미리 만든 주인공과 이야기 책을 짓는다(모드 「시나리오 모드」).
        if (options.Scenarios is { } scenarios)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            line.Children.Add(new TextBlock
            {
                Text = "짓기",
                Width = 64,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(GameUi.PushButton("시나리오…", () => { Close(); Engine.HardcoreRules.Mark("dev:scenario"); scenarios(); }, 180));
            rows.Children.Add(line);
        }

        // 도구 앱 — 따로 도는 exe 다. 표를 손볼 일이 생기면 여기서 띄운다(햄버거에서 옮겼다).
        if (options.HelperApp is { } tools)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            line.Children.Add(new TextBlock
            {
                Text = "도구",
                Width = 64,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(GameUi.PushButton("도구 앱…", () => { Close(); tools(); }, 180));
            rows.Children.Add(line);
        }

        // 창 주인 보기 — 떠 있는 창을 주인 나무로 늘어놓는다. 게임을 하면서 보도록 따로 뜬다.
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            line.Children.Add(new TextBlock
            {
                Text = "창",
                Width = 64,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            line.Children.Add(GameUi.PushButton("창 주인 보기…", () => { Close(); WindowTreeWindow.Open(); }, 180));
            rows.Children.Add(line);
        }

        // 게임 폴더에 원본 CDS가 없을 때 내려받는 CDSX 에셋은 실행 폴더에 둔다.
        var cdsxLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };
        cdsxLine.Children.Add(new TextBlock
        {
            Text = "CDSX",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        cdsxLine.Children.Add(new TextBlock
        {
            Text = CdsAssetPath.DownloadDirectory,
            Width = 420,
            Foreground = GameUi.Text,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = CdsAssetPath.DownloadDirectory,
            VerticalAlignment = VerticalAlignment.Center,
        });
        cdsxLine.Children.Add(GameUi.PushButton("폴더 열기", OpenCdsxDirectory, 120));
        rows.Children.Add(cdsxLine);

        var bgmLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };
        bgmLine.Children.Add(new TextBlock
        {
            Text = "BGM",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        bgmLine.Children.Add(new TextBlock
        {
            Text = BgmAssetDownloader.CacheDirectory,
            Width = 420,
            Foreground = GameUi.Text,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = BgmAssetDownloader.CacheDirectory,
            VerticalAlignment = VerticalAlignment.Center,
        });
        bgmLine.Children.Add(GameUi.PushButton("폴더 열기", OpenBgmDirectory, 120));
        rows.Children.Add(bgmLine);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("개발", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        // 「색」 탭 — 원본이 쓰는 색 토큰을 늘어놓는다(개발용).
        var colors = ColorsPage(editor: false);
        // 「편집기 색」 — 퀘스트 편집기 · 모션 메이커 따위 도구 창의 붓만 따로 — 게임 화면 색과 섞이면 헷갈렸다.
        var toolColors = ColorsPage(editor: true);
        var pages = new List<(string, FrameworkElement)> { ("일반", rows), ("테스트", tests) };
        if (options.Discoveries is { } table)
        {
            pages.Add(("발견물", FindsPage(table, options.EventDone, options.UserDiscoveries)));
            Closed += (_, _) => { if (_findsChanged) options.DiscoveriesChanged?.Invoke(); };
        }
        if (options.HistoryEvents?.Invoke() is { Count: > 0 } events)
            pages.Add(("역사이벤트", HistoryPage(events)));
        if (options.Barmaids is { } maids)
            pages.Add(("여급", BarmaidPage(maids, options.CityName ?? (c => $"도시 {c}"))));
        pages.Add(("대항2", dh2));
        pages.Add(("애드온", addOns));
        pages.Add(("색", colors));
        pages.Add(("편집기 색", toolColors));
        stack.Children.Add(Tabs([.. pages]));
        foreach (var (_, page) in pages) stack.Children.Add(page);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Sync();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>탭 머리 — 누른 쪽 판만 보이고 머리는 밝게 선다(<see cref="ModDialog"/> 와 같은 모양).</summary>
    private static FrameworkElement Tabs(params (string Text, FrameworkElement Page)[] pages)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 0) };
        var heads = new List<(Border Head, FrameworkElement Page)>();

        void Select(FrameworkElement page)
        {
            foreach (var (head, p) in heads)
            {
                bool on = ReferenceEquals(p, page);
                p.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                head.Background = on ? new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                ((TextBlock)head.Child).Opacity = on ? 1 : 0.6;
            }
        }

        foreach (var (text, page) in pages)
        {
            var head = new Border
            {
                BorderBrush = GameUi.Edge,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(16, 4, 16, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15 },
            };
            head.MouseLeftButtonDown += (_, _) => Select(page);
            heads.Add((head, page));
            bar.Children.Add(head);
        }
        Select(pages[0].Page);
        return bar;
    }

    /// <summary>
    /// 「색」 탭 — 원본이 쓰는 색 토큰. <b>코드에서 그대로 읽어 온다</b>(리플렉션) — 색을 더하거나 바꾸면 저절로 따라온다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   UI 붓      CdsHelper.Game.UI.Views 의 형마다 정적 Brush 칸(공개 · 비공개) — GameUi.Back 따위
    ///   글꼴 색인  GameFont 의 *Color 상수 — 게임 비트맵 글꼴이 쓰는 공용 색표 자리
    ///   공용 색표  GamePalette 0~73 — 그 위는 그림마다 제 팔레트가 얹힌다
    /// </code>
    /// 줄을 누르면 #RRGGBB 를 클립보드에 넣는다.
    /// </remarks>
    /// <param name="editor">참이면 편집기 · 도구 창의 붓만, 거짓이면 게임 화면 쪽(붓 · 글꼴 색인 · 공용 색표)이다.</param>
    private FrameworkElement ColorsPage(bool editor)
    {
        var list = new StackPanel();

        TextBlock Head(string text) => new()
        {
            Text = text, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 14,
            Margin = new Thickness(0, 10, 0, 4),
        };

        UIElement Swatch(Color c, string name, string note)
        {
            string hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}" + (c.A < 255 ? $" (α {c.A})" : "");
            var line = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var chip = new Border
            {
                Width = 36, Height = 18, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(c), BorderBrush = GameUi.Edge, BorderThickness = new Thickness(1),
            };
            DockPanel.SetDock(chip, Dock.Left);
            line.Children.Add(chip);
            var code = new TextBlock { Text = hex, Width = 120, Foreground = GameUi.Text, FontFamily = new FontFamily("Consolas"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(code, Dock.Left);
            line.Children.Add(code);
            line.Children.Add(new TextBlock
            {
                Text = note.Length > 0 ? $"{name}   {note}" : name,
                Foreground = GameUi.Text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            line.ToolTip = "누르면 색 값을 복사합니다";
            line.MouseLeftButtonUp += (_, _) => { try { Clipboard.SetText($"#{c.R:X2}{c.G:X2}{c.B:X2}"); } catch { } };
            return line;
        }

        // UI 붓 — 이 어셈블리 UI.Views 의 형마다 정적 SolidColorBrush 칸. 편집기 쪽 형은 따로 낸다(IsEditorType).
        list.Children.Add(Head(editor ? "편집기 · 도구 창 붓 (형.이름)" : "UI 붓 (형.이름)"));
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                  | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        var brushes = typeof(GameUi).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(GameUi).Namespace && !t.IsGenericTypeDefinition)
            .Where(t => IsEditorType(t) == editor)
            .SelectMany(t =>
            {
                try { return t.GetFields(flags).Select(f => (Type: t, Field: f)).ToList(); }
                catch { return []; }
            })
            .Where(x => typeof(Brush).IsAssignableFrom(x.Field.FieldType))
            .Select(x =>
            {
                try { return (Name: $"{Short(x.Type)}.{x.Field.Name}", Brush: x.Field.GetValue(null) as SolidColorBrush); }
                catch { return (Name: "", Brush: (SolidColorBrush?)null); }
            })
            .Where(x => x.Brush != null && !x.Name.Contains('<'))
            .OrderBy(x => x.Name, StringComparer.Ordinal);
        foreach (var (name, brush) in brushes) list.Children.Add(Swatch(brush!.Color, name, ""));
        if (editor) return Wrap(list);

        // 글꼴 색인 — GameFont 의 *Color 상수.
        list.Children.Add(Head("게임 글꼴 색인 (GameFont)"));
        foreach (var f in typeof(GameFont).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                     .Where(f => f.IsLiteral && f.FieldType == typeof(byte) && f.Name.EndsWith("Color")))
        {
            byte index = (byte)f.GetRawConstantValue()!;
            uint argb = GameFont.TextArgb(index);
            list.Children.Add(Swatch(Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb),
                                     $"GameFont.{f.Name}", $"색인 {index}"));
        }

        // 공용 색표 0~73.
        list.Children.Add(Head($"공용 색표 (GamePalette 0~{GamePalette.OwnPaletteBase - 1})"));
        var grid = new WrapPanel { Width = 520 };
        for (int i = 0; i < GamePalette.OwnPaletteBase; i++)
        {
            var c = Color.FromRgb(GamePalette.Rgb[i * 3], GamePalette.Rgb[i * 3 + 1], GamePalette.Rgb[i * 3 + 2]);
            int at = i;
            var cell = new Border
            {
                Width = 64, Height = 34, Margin = new Thickness(1),
                Background = new SolidColorBrush(c), BorderBrush = GameUi.Edge, BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"{at}  #{c.R:X2}{c.G:X2}{c.B:X2}",
                Child = new TextBlock
                {
                    Text = $"{at}\n{c.R:X2}{c.G:X2}{c.B:X2}", FontSize = 10, FontFamily = new FontFamily("Consolas"),
                    Foreground = c.R * 0.3 + c.G * 0.59 + c.B * 0.11 > 128 ? Brushes.Black : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                },
            };
            cell.MouseLeftButtonUp += (_, _) => { try { Clipboard.SetText($"#{c.R:X2}{c.G:X2}{c.B:X2}"); } catch { } };
            grid.Children.Add(cell);
        }
        list.Children.Add(grid);
        return Wrap(list);

        static FrameworkElement Wrap(UIElement content) => new ScrollViewer
        {
            Width = 560, Height = 460, Margin = new Thickness(12, 10, 12, 4),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };

        static string Short(Type t) => t.IsNested ? $"{t.DeclaringType!.Name}.{t.Name}" : t.Name;
    }

    /// <summary>
    /// 편집기 · 도구 창 쪽 형인지 — 게임 화면에는 안 나오는 창(퀘스트 편집기 흐름도 · 모션 메이커 · 각종 편집 창)이다.
    /// 딸린 형(중첩)은 바깥 형을 따른다.
    /// </summary>
    private static bool IsEditorType(Type type)
    {
        var t = type;
        while (t.DeclaringType is { } outer) t = outer;
        string name = t.Name;
        return name.Contains("Edit") || name.Contains("Designer") || name.StartsWith("Disev")
               || name is "MotionMakerDialog" or "BuildingListDialog" or "CitySpriteDialog" or "DevDialog";
    }

    /// <summary>
    /// 「발견물」 탭 — 발견물을 죄다 늘어놓고, 체크하면 <b>찾아서 보고까지 한 것으로</b>, 풀면 안 찾은 것으로 한다.
    /// </summary>
    /// <remarks>
    /// 게임에는 없는 판이다. 발견물이 있어야 볼 수 있는 것(보고 · 연표 · 백과사전 · 발견으로 켜지는 교역품)을
    /// 시험하려고 둔다. 체크는 찾은 것으로 적고(<see cref="Player.Discover"/>) 그 자리 사건도 매듭짓는다
    /// (<see cref="Player.Settle"/>) — 지도에서 그 자리를 지나도 발견 장면이 다시 안 뜬다. 그리고 <b>보고(발표)한 것으로도</b>
    /// 적는다(<see cref="Player.Announce"/>) — 그래야 후원자 · 왕궁의 「제안 선택」에 안 뜬다. 보고로 드는 명성 · 보수 ·
    /// 아이템은 안 준다. 체크는 보고까지 된 것에만 서 있고, 찾기만 하고 보고 전인 것은 「(보고 전)」으로 적혀 비어 있다.
    /// 체크를 풀면 보고한 기록까지 지운다(<see cref="Player.Undiscover"/>).
    ///
    /// 인물(다 빈치 · 코페르니쿠스 · 아기랄 · 마리나 · 틀락스칼라족 · 토파크) · 비보는 원본 대본에 발견을 적는 명령
    /// (<c>01 0B</c>)이 없다 — 사건을 마쳐도 찾은 것으로 영영 안 적히고(<c>38 0D</c> 은 인물 칸 <c>+0xF8</c> 만 지운다,
    /// <c>0x004321B0</c>), 사건 매듭(<see cref="Player.Settle"/>)만 남는다. 그래서 찾은 것 · 보고한 것만 보면 늘 비어
    /// 있었다. 발견물 지도와 같이 <paramref name="eventDone"/> 이 참인 것도 체크해 「(사건)」으로 적는다.
    /// </remarks>
    private FrameworkElement FindsPage(DiscoveryTable table, Func<int, bool>? eventDone, Action? userDiscoveries)
    {
        var page = new StackPanel { Margin = new Thickness(12, 10, 12, 4), Width = 560 };
        var count = new TextBlock { Foreground = GameUi.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var find = Field();
        find.Width = 180;
        find.TextAlignment = TextAlignment.Left;
        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, DiscoveryTable.Record Row)>();
        bool bulk = false;

        // 목록 머리의 전체 체크 칸 — 「모두 체크」 · 「모두 해제」 단추 둘을 대신한다(단추가 자리를 먹어 「발견물 추가…」가 잘렸다).
        // 보이는 줄이 다 체크면 체크, 하나도 없으면 빈칸, 섞였으면 가운데 칸이다.
        var header = new CheckBox
        {
            Content = "전체",
            Foreground = GameUi.Text,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        // 체크된 줄을 센다 — 보고한 것에 사건만 마친 인물 · 비보까지 든다.
        void Count()
        {
            count.Text = $"  {boxes.Count(b => b.Box.IsChecked == true)} / {boxes.Count}";
            var shown = boxes.Where(b => b.Box.Visibility == Visibility.Visible).ToList();
            int on = shown.Count(b => b.Box.IsChecked == true);
            header.IsChecked = shown.Count > 0 && on == shown.Count ? true : on == 0 ? false : null;
        }

        foreach (var row in table.Discoveries)
        {
            if (row.Name.Length == 0) continue;
            int id = row.Id;
            bool byEvent = !_player.HasFound(id) && eventDone?.Invoke(id) == true;
            var box = new CheckBox
            {
                Content = $"{id,3}  {row.Name}" + (row.CategoryName.Length > 0 ? $"  [{row.CategoryName}]" : "")
                          + (_player.HasFound(id) && !_player.HasAnnounced(id) ? "  (보고 전)" : "")
                          + (byEvent ? "  (사건)" : ""),
                IsChecked = _player.HasAnnounced(id) || byEvent,
                Foreground = GameUi.Text,
                FontSize = 14,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.Checked += (_, _) =>
            {
                if (_player.Discover(id)) { _player.Settle(id); _findsChanged = true; }
                // 보고까지 한 것으로 — 행적에는 안 남긴다(게임에서 한 보고가 아니다).
                if (_player.Announce(id, trace: false)) _findsChanged = true;
                if (!bulk) Count();
            };
            box.Unchecked += (_, _) =>
            {
                if (_player.Undiscover(id)) _findsChanged = true;
                // 사건만 마친 것(인물 · 비보)은 찾은 기록이 없어 매듭만 푼다 — 그 자리 사건이 다시 뜬다.
                else if (_player.Unsettle(id)) _findsChanged = true;
                if (!bulk) Count();
            };
            boxes.Add((box, row));
            list.Children.Add(box);
        }

        void All(bool on)
        {
            bulk = true;
            // 찾기로 걸러 보이는 줄만 바꾼다 — 가려진 줄까지 건드리면 뭘 바꿨는지 알 수 없다.
            foreach (var (box, _) in boxes)
                if (box.Visibility == Visibility.Visible) box.IsChecked = on;
            bulk = false;
            Count();
        }

        find.TextChanged += (_, _) =>
        {
            string want = find.Text.Trim();
            foreach (var (box, row) in boxes)
                box.Visibility = want.Length == 0 || row.Name.Contains(want, StringComparison.OrdinalIgnoreCase)
                                 || row.CategoryName.Contains(want) || row.Id.ToString() == want
                    ? Visibility.Visible : Visibility.Collapsed;
            Count();
        };

        // 누르면 보이는 줄이 다 체크였으면 다 풀고, 아니면 다 체크한다. 칸의 모양은 Count 가 다시 맞춘다.
        header.Click += (_, _) =>
        {
            var shown = boxes.Where(b => b.Box.Visibility == Visibility.Visible).ToList();
            All(!(shown.Count > 0 && shown.All(b => b.Box.IsChecked == true)));
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(new TextBlock
        {
            Text = "찾기", Width = 40, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        top.Children.Add(find);
        top.Children.Add(count);
        // 사람이 지어 넣는 발견물 — 짓기 · 고치기 · 내보내기 · 불러오기(UserDiscoveryDialog).
        if (userDiscoveries is { } open)
            top.Children.Add(GameUi.PushButton("발견물 추가…", () => { Close(); open(); }, 140));
        page.Children.Add(top);
        var sheet = new DockPanel();
        var head = new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 4, 8, 4),
            Child = header,
        };
        DockPanel.SetDock(head, Dock.Top);
        sheet.Children.Add(head);
        sheet.Children.Add(new ScrollViewer
        {
            Height = 394,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(8, 4, 8, 4),
            Content = list,
        });
        page.Children.Add(new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = sheet,
        });
        page.Children.Add(new TextBlock
        {
            Text = "체크하면 발견한 것으로 적습니다(보고는 따로 해야 합니다). 풀면 보고한 기록까지 지웁니다.",
            Foreground = GameUi.Text, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Count();
        return page;
    }

    /// <summary>
    /// 「여급」 탭 — 여급 127명을 표 차례로 늘어놓는다. 보기만 한다.
    /// </summary>
    /// <remarks>
    /// 서는 해는 1480 년 나이(표 <c>+0x08</c>)로 15~40살인 해다(<c>0x004797B0</c>, <see cref="BarmaidTable.Barmaid.Works"/>).
    /// 취향은 성격(<c>+0x18</c>)이다 — 「이야기한다」에서 그녀가 말하는 이상형이고, 제독의 성미 칸이 맞으면 말이 통한다
    /// (<see cref="Engine.Town.Barmaids.Suits"/>). 언어(<c>+0x20</c> 비트)는 그녀가 하는 말이다 — 제독 혼자 그 말이 3 이상이어야 설득 · 선물 · 프로포즈 줄이 서고(<c>0x00466850</c>), 아내가 되면 자식에게 가르친다.
    /// </remarks>
    private FrameworkElement BarmaidPage(BarmaidTable table, Func<int, string> cityName)
    {
        var page = new StackPanel { Margin = new Thickness(12, 10, 12, 4), Width = 760 };
        var find = Field();
        find.Width = 220;
        find.TextAlignment = TextAlignment.Left;
        var count = new TextBlock { Foreground = GameUi.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var list = new StackPanel();
        var lines = new List<(FrameworkElement Line, string Text)>();

        int year = _player.Date.Year, mine = Engine.Town.Barmaids.FortuneOf(_player);
        foreach (var her in table.Barmaids)
        {
            int age = her.AgeIn(year);
            string state = her.Works(year) ? "서 있음" : age < BarmaidTable.WorkFrom ? "아직" : "물러남";
            string tongues = string.Join(" · ", Skill.Languages.Where((_, i) => (her.Tongues & (1 << i)) != 0));
            string zodiac = her.Zodiac >= 0 && her.Zodiac < Player.Zodiacs.Length ? Player.Zodiacs[her.Zodiac] : $"{her.Zodiac}";
            bool wife = _player.SpouseId == her.Id, destined = BarmaidTable.Destined(mine, her.Fortune);
            bool suits = Engine.Town.Barmaids.Suits(_player, her);
            int liking = _player.LikingOf(her.Id);

            string head = $"#{her.Id,3}  {her.Name}  ·  {cityName(her.City)}  ·  {her.Year}~{her.LastYear}년"
                        + $"  ·  지금 {age}살 [{state}]"
                        + (wife ? "  ♥ 아내" : destined ? "  ★ 운명의 반려자" : "");
            string body = $"언어 {(tongues.Length > 0 ? tongues : "없음")}  ·  취향 {her.PersonalityName}{(suits ? "(내 성미와 맞음)" : "")}"
                        + $"  ·  {her.BloodName}  ·  {zodiac}  ·  궁합 코드 {her.Fortune}  ·  얼굴 {her.Face}"
                        + (liking > 0 ? $"  ·  친밀도 {liking}" : "");
            string taste = Engine.Town.StrangerTalk.OwnTasteOf(her.Personality) is { } t ? $"「{t}」" : "";

            bool dim = state != "서 있음";
            var line = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
            line.Children.Add(new TextBlock
            {
                Text = head, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 13,
                Opacity = dim ? 0.6 : 1, TextWrapping = TextWrapping.Wrap,
            });
            line.Children.Add(new TextBlock
            {
                Text = "    " + body, Foreground = GameUi.Text, FontSize = 12,
                Opacity = dim ? 0.55 : 0.9, TextWrapping = TextWrapping.Wrap,
            });
            if (taste.Length > 0)
                line.Children.Add(new TextBlock
                {
                    Text = "    " + taste, Foreground = GameUi.Text, FontSize = 12,
                    Opacity = dim ? 0.45 : 0.7, TextWrapping = TextWrapping.Wrap,
                });
            lines.Add((line, $"{head} {body} {taste}"));
            list.Children.Add(line);
        }

        void Filter()
        {
            string want = find.Text.Trim();
            int shown = 0;
            foreach (var (line, text) in lines)
            {
                bool on = want.Length == 0 || text.Contains(want, StringComparison.OrdinalIgnoreCase);
                line.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) shown++;
            }
            count.Text = $"  {shown} / {lines.Count}  ({year}년에 서 있는 여급 {table.Barmaids.Count(b => b.Works(year))}명)";
        }
        find.TextChanged += (_, _) => Filter();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(new TextBlock
        {
            Text = "찾기", Width = 40, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        top.Children.Add(find);
        top.Children.Add(count);
        page.Children.Add(top);
        page.Children.Add(new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Height = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 4, 8, 4),
                Content = list,
            },
        });
        page.Children.Add(new TextBlock
        {
            Text = "서는 해는 15~40살인 해입니다(마흔이 되는 해까지). 언어는 그녀가 하는 말입니다(제독이 그 말을 3 이상 해야 설득 · 선물 · 프로포즈가 서고, 아내가 되면 자식에게 가르칩니다). 취향은 「이야기한다」에서 말하는 이상형(성격)입니다. "
                   + "이름 · 도시 · 언어 · 취향 · 해로 찾을 수 있습니다(「서 있음」으로 지금 서 있는 여급만).",
            Foreground = GameUi.Text, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Filter();
        return page;
    }

    /// <summary>
    /// 「역사이벤트」 탭 — 역사 대본(HIST_EV 79파트 · HISTCHR 역사 항해자 열넷)의 칸을 모두 풀어 늘어놓는다. 보기만 한다.
    /// </summary>
    /// <remarks>
    /// 상태는 지금 날짜로 칸 고르기(<c>0x00407390</c>)를 따져 본 것이다 — 「돎」은 한 번 도는 칸을 이미 돌았고,
    /// 「이번 달 참」은 다음 달이 바뀔 때 돌 칸, 「모름」은 우리가 못 읽는 조건이라 파트째 안 도는 칸이다.
    /// </remarks>
    private FrameworkElement HistoryPage(List<Engine.Market.CityHistory.Entry> events)
    {
        var page = new StackPanel { Margin = new Thickness(12, 10, 12, 4), Width = 760 };
        var find = Field();
        find.Width = 220;
        find.TextAlignment = TextAlignment.Left;
        var count = new TextBlock { Foreground = GameUi.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var list = new StackPanel();
        var lines = new List<(FrameworkElement Line, string Text)>();

        foreach (var e in events)
        {
            string head = $"{e.Source} {e.Part,2}-{e.Slot}  [{e.State}]";
            // 줄을 누르면 큰 글씨로 따로 띄운다 — 목록 글은 작고 한 덩이라 읽기 힘들다.
            var line = new StackPanel { Margin = new Thickness(0, 3, 0, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var entry = e;
            line.MouseLeftButtonUp += (_, _) => ShowHistoryEntry(this, entry);
            line.Children.Add(new TextBlock
            {
                Text = $"{head}  {e.When}",
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Opacity = e.State is "돎" or "지남" ? 0.55 : 1,
                TextWrapping = TextWrapping.Wrap,
            });
            line.Children.Add(new TextBlock
            {
                Text = "    " + e.What,
                Foreground = GameUi.Text,
                FontSize = 12,
                Opacity = e.State is "돎" or "지남" ? 0.5 : 0.85,
                TextWrapping = TextWrapping.Wrap,
            });
            lines.Add((line, $"{head} {e.When} {e.What}"));
            list.Children.Add(line);
        }

        void Filter()
        {
            string want = find.Text.Trim();
            int shown = 0;
            foreach (var (line, text) in lines)
            {
                bool on = want.Length == 0 || text.Contains(want, StringComparison.OrdinalIgnoreCase);
                line.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) shown++;
            }
            count.Text = $"  {shown} / {lines.Count}  (돎 {events.Count(e => e.State == "돎")} · 지남 {events.Count(e => e.State == "지남")})";
        }
        find.TextChanged += (_, _) => Filter();

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(new TextBlock
        {
            Text = "찾기", Width = 40, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        top.Children.Add(find);
        top.Children.Add(count);
        page.Children.Add(top);
        page.Children.Add(new Border
        {
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer
            {
                Height = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8, 4, 8, 4),
                Content = list,
            },
        });
        page.Children.Add(new TextBlock
        {
            Text = "[돎] 한 번 도는 칸을 이미 돌았습니다 · [지남] 그 해 그 달에만 도는 칸이고 그 달이 지났습니다 · [이번 달 참] 다음 달이 바뀔 때 돕니다 · [대기] 조건이 아직 아닙니다 · "
                   + "[모름] 못 읽는 조건이라 그 파트가 안 돕니다. 도시 이름 · 나라 · 해로 찾을 수 있고, 줄을 누르면 크게 봅니다.",
            Foreground = GameUi.Text, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Filter();
        return page;
    }

    /// <summary>역사이벤트 한 칸을 큰 글씨로 띄운다 — 조건 한 덩이, 하는 일은 한 줄에 하나씩.</summary>
    private static void ShowHistoryEntry(Window owner, Engine.Market.CityHistory.Entry e)
    {
        var box = new Window
        {
            Owner = owner,
            Title = "역사이벤트",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = GameUi.Back,
        };
        TextBlock Text(string text, double size, bool bold = false, double opacity = 1) => new()
        {
            Text = text, Foreground = GameUi.Text, FontSize = size, Opacity = opacity,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
        };

        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 8), Width = 720 };
        body.Children.Add(Text($"{e.Source} {e.Part}-{e.Slot}  [{e.State}]", 20, bold: true));
        body.Children.Add(Text("조건", 15, bold: true, opacity: 0.7));
        body.Children.Add(Text(e.When, 18));
        body.Children.Add(Text("하는 일", 15, bold: true, opacity: 0.7));
        var acts = new StackPanel();
        foreach (string act in e.What.Split(" / "))
            acts.Children.Add(Text("· " + act, 18));
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = acts,
        });

        var title = GameUi.TitleBar("역사이벤트", box.Close);
        GameUi.EnableDrag(box, title);
        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
            Children = { GameUi.PushButton("닫기", box.Close, 96) },
        });
        box.Content = new Border
        {
            Background = GameUi.Back, BorderBrush = GameUi.Edge, BorderThickness = new Thickness(2),
            Margin = new Thickness(4), Child = stack,
        };
        box.KeyDown += (_, k) => { if (k.Key is Key.Escape) box.Close(); };
        box.ShowDialog();
    }

    /// <summary>다운로드한 CDSX 에셋이 있는 실행 폴더를 탐색기로 연다.</summary>
    private void OpenCdsxDirectory()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                CdsAssetPath.DownloadDirectory)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            NoticeDialog.Show(this, $"CDSX 폴더를 열지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>다운로드한 BGM 파일이 있는 폴더를 탐색기로 연다.</summary>
    private void OpenBgmDirectory()
    {
        try
        {
            Directory.CreateDirectory(BgmAssetDownloader.CacheDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                BgmAssetDownloader.CacheDirectory)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            NoticeDialog.Show(this, $"BGM 폴더를 열지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>
    /// 아내를 붙였다 뗀다 — 자택 "후손을 남긴다" 줄이 아내가 있어야 눌린다.
    /// </summary>
    /// <remarks>
    /// 게임에는 없는 줄이다. 게임에서 아내를 맞으려면 술집 여급과 친밀도를 90 까지
    /// 올려야 해서(<see cref="Engine.Town.Barmaids"/>) 시험할 때는 여기서 붙여 준다.
    /// </remarks>
    private UIElement SpouseRow()
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };

        var shown = new TextBlock
        {
            Width = 120,
            Foreground = GameUi.Text,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        };
        void Paint() => shown.Text = _player.Spouse.Length > 0 ? _player.Spouse : "— 없음";
        Paint();

        line.Children.Add(new TextBlock
        {
            Text = "아내",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(GameUi.PushButton("맞는다", () =>
        {
            _player.Marry("카타리나");
            Paint();
        }, 96));
        line.Children.Add(GameUi.PushButton("없앤다", () => { _player.Marry(""); Paint(); }, 96));
        line.Children.Add(shown);
        return line;
    }

    /// <summary>개발 창에서 고를 수 있는 해. 게임은 1480년에 열린다.</summary>
    private const int FirstYear = 1480, LastYear = 1600;

    /// <summary>
    /// 날짜를 바꾸는 줄 — 새 주인공의 나이처럼 <b>계산기</b>로 넣는다(<see cref="NumberPadDialog"/>).
    /// 해 · 달 · 날 단추가 따로다(요청 — 예전에는 해만 바꿨다). 그 달에 없는 날이면 그 달 끝날로 당긴다.
    /// </summary>
    private UIElement YearRow(Action<bool>? changed)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        line.Children.Add(new TextBlock
        {
            Text = "연도",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var buttons = new List<Border>();
        void Repaint()
        {
            var d = _player.Date;
            string[] labels = [$"{d.Year}년…", $"{d.Month}월…", $"{d.Day}일…"];
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].Child is TextBlock text) text.Text = labels[i];
        }
        void Move(Func<DateTime, int> get, int min, Func<DateTime, int> max, Func<DateTime, int, DateTime> with)
        {
            var was = _player.Date;
            if (NumberPadDialog.Ask(this, get(was), min, max(was)) is not { } value || value == get(was)) return;
            var to = with(was, value);
            _player.SetDate(to);
            changed?.Invoke(to < was);
            Repaint();
        }
        static DateTime Make(int y, int m, int d) => new(y, m, Math.Min(d, DateTime.DaysInMonth(y, m)));

        buttons.Add(GameUi.PushButton("", () => Move(d => d.Year, FirstYear, _ => LastYear,
                                                     (d, v) => Make(v, d.Month, d.Day)), 100));
        buttons.Add(GameUi.PushButton("", () => Move(d => d.Month, 1, _ => 12,
                                                     (d, v) => Make(d.Year, v, d.Day)), 80));
        buttons.Add(GameUi.PushButton("", () => Move(d => d.Day, 1, d => DateTime.DaysInMonth(d.Year, d.Month),
                                                     (d, v) => Make(d.Year, d.Month, v)), 80));
        foreach (var b in buttons) { b.Margin = new Thickness(0, 0, 4, 0); line.Children.Add(b); }
        Repaint();
        return line;
    }

    /// <summary>
    /// 배를 넣는 줄 — 선체 표에서 하나를 골라 값 없이 <b>함대에 곧장</b> 넣는다(요청). 함대가 찼으면(8척) 안 넣는다.
    /// 하드코어 판에는 「dev:ship」으로 적힌다.
    /// </summary>
    private UIElement ShipRow()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        line.Children.Add(new TextBlock
        {
            Text = "배",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var shown = new TextBlock { Foreground = GameUi.Text, FontSize = 14, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        void Paint() => shown.Text = $"함대 {_player.Ships.Count} / {Player.MaxShips}척";
        line.Children.Add(GameUi.PushButton("배 넣기…", () =>
        {
            if (_player.Ships.Count >= Player.MaxShips) { NoticeDialog.Show(this, "함대가 다 찼습니다."); return; }
            var hulls = Hull.All.OrderBy(h => h.Price).ToList();
            var names = hulls.Select(h => $"{h.Name}   내구 {h.Hp} · 속도 {h.Speed} · 용량 {h.Capacity} · 승원 {h.Crew} · {h.Price:N0}닢").ToList();
            int at = ListPickDialog.Pick(this, names, "배 넣기", "선체 표를 못 읽었습니다", listWidth: 640, preselect: 0);
            if (at < 0 || at >= hulls.Count) return;
            if (!_player.Enlist(new Ship(hulls[at], name: _player.SuggestShipName()))) return;
            Engine.HardcoreRules.Mark("dev:ship");
            Paint();
        }, 120));
        line.Children.Add(shown);
        Paint();
        return line;
    }

    /// <summary>줄 하나 — 이름, 적는 칸, 늘리고 줄이는 단추.</summary>
    private UIElement Row(string label, TextBox box, int step, Action<int> set)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };

        line.Children.Add(new TextBlock
        {
            Text = label,
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        void Move(int by)
        {
            set(Math.Max(0, Current(box) + by));
            Sync();
        }

        line.Children.Add(GameUi.PushButton($"-{step:N0}", () => Move(-step), 96));
        line.Children.Add(box);
        line.Children.Add(GameUi.PushButton($"+{step:N0}", () => Move(+step), 96));

        // 적어 넣은 값은 칸을 떠날 때(또는 엔터) 그대로 들어간다.
        void Apply()
        {
            set(Math.Max(0, Current(box)));
            Sync();
        }
        box.LostFocus += (_, _) => Apply();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Apply(); };

        return line;
    }

    /// <summary>고르는 줄 하나 — 이름과 펼침 상자. 고르면 곧바로 설정에 남긴다.</summary>
    private static UIElement Select(string label, IReadOnlyList<string> items, int selected,
                                    Action<int> set, string tip)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 2),
            ToolTip = tip,
        };
        line.Children.Add(new TextBlock
        {
            Text = label,
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var box = new ComboBox
        {
            Width = 160,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(6, 3, 6, 3),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (string item in items) box.Items.Add(item);
        box.SelectedIndex = Math.Clamp(selected, 0, items.Count - 1);
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(box.SelectedIndex); };

        line.Children.Add(box);
        return line;
    }

    /// <summary>애드온 켜고 끄기와 그 밑의 「애드온 폴더 열기」 · 「다시 읽기」 단추.</summary>
    private UIElement AddOnControls()
    {
        var box = Toggle("애드온", GameSettings.AddOns, on => GameSettings.AddOns = on,
            "Lua 애드온을 읽어 지도 위에 패널을 띄웁니다. 예시(SupplyHud · HintButton)와 설명서(README.md)가 애드온 폴더에 들어갑니다."
            + " 켜면 제목 줄 도시락에 「애드온」 목록이 서고, 하드코어 랭킹 기록에 모드처럼 적힙니다");

        var open = GameUi.PushButton("애드온 폴더 열기", OpenAddOnDirectory, 150);
        var reload = GameUi.PushButton("다시 읽기", UI.AddOns.AddOnHost.Reload, 90);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 2, 0, 2) };
        buttons.Children.Add(open);
        buttons.Children.Add(reload);
        reload.Margin = new Thickness(6, 0, 0, 0);
        void Sync(bool on) { buttons.IsEnabled = on; buttons.Opacity = on ? 1 : 0.4; }
        Sync(box.IsChecked == true);
        box.Checked += (_, _) => Sync(true);
        box.Unchecked += (_, _) => Sync(false);
        open.ToolTip = @"애드온 폴더(%APPDATA%\CdsHelper\AddOns)를 엽니다. 애드온마다 폴더 하나에 이름.toc 와 .lua 파일을 넣습니다.";
        reload.ToolTip = "애드온 파일을 고친 뒤 게임을 끄지 않고 다시 읽습니다. 오류는 폴더의 addons.log 에 적힙니다.";

        var group = new StackPanel();
        group.Children.Add(box);
        group.Children.Add(buttons);
        return group;
    }

    /// <summary>애드온 폴더를 연다 — 없으면 만든다.</summary>
    private void OpenAddOnDirectory()
    {
        try
        {
            System.IO.Directory.CreateDirectory(UI.AddOns.AddOnHost.Root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UI.AddOns.AddOnHost.Root)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            NoticeDialog.Show(this, $"애드온 폴더를 열지 못했습니다 — {ex.Message}");
        }
    }

    /// <summary>켜고 끄는 줄 하나.</summary>
    private static CheckBox Toggle(string label, bool on, Action<bool> set, string tip)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = on,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 8, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = tip,
        };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
    }

    /// <summary>도시 창이 열릴 때 줄 효과를 고르는 줄. 고른 값은 설정에 남는다.</summary>
    private UIElement EffectRow()
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 4),
        };

        line.Children.Add(new TextBlock
        {
            Text = "도시 열림",
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        (CityOpenEffect Effect, string Label)[] choices =
        [
            (CityOpenEffect.None, "없음"),
            (CityOpenEffect.Expand, "펼침 (가운데서 커짐)"),
            (CityOpenEffect.Slide, "넘김 (미끄러져 들어옴)"),
            (CityOpenEffect.Fade, "페이드인"),
            (CityOpenEffect.Zoom, "확대/축소 (PPT식 — 커지며 나타나고 살짝 넘침)"),
        ];

        var box = new ComboBox
        {
            Width = 300,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(6, 3, 6, 3),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (var (effect, label) in choices)
            box.Items.Add(new ComboBoxItem { Content = label, Tag = effect });

        box.SelectedIndex = Array.FindIndex(choices, c => c.Effect == GameSettings.CityOpenEffect);
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: CityOpenEffect picked })
                GameSettings.CityOpenEffect = picked;
        };

        line.Children.Add(box);
        return line;
    }

    /// <summary>칸에 적힌 수. 수가 아니면 0.</summary>
    private static int Current(TextBox box) =>
        int.TryParse(box.Text.Replace(",", "").Trim(), out int v) ? v : 0;

    /// <summary>지금 값을 칸에 다시 적는다.</summary>
    private void Sync()
    {
        _gold.Text = _player.Gold.ToString("N0");
        _fame.Text = _player.Fame.ToString("N0");
    }

    private static TextBox Field() => new()
    {
        Width = 110,
        Margin = new Thickness(6, 0, 6, 0),
        Padding = new Thickness(4, 2, 4, 2),
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        TextAlignment = TextAlignment.Right,
        Background = GameUi.ItemFill,
        Foreground = Brushes.Black,
        BorderBrush = GameUi.ItemEdge,
        BorderThickness = new Thickness(2),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public static void Show(Window owner, Player player, Options options) =>
        new DevDialog(player, options) { Owner = owner }.ShowDialog();
}
