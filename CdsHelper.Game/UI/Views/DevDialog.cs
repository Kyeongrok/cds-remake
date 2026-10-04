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
/// 놀이에는 없는 창이다. 명성이 오르는 길(발견물 발표)이나 돈이 도는 길(교역)을 아직
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

        /// <summary>자동항해 — 목적지 도시를 골라 손을 놓고 몬다. 개발 창을 닫은 뒤 부른다.</summary>
        public Action? AutoSail { get; init; }

        /// <summary>도구 앱(Editor.exe) 띄우기 — 창을 닫은 뒤 부른다.</summary>
        public Action? HelperApp { get; init; }

        /// <summary>묘책 확률 표(<c>0x00549B80</c>) 보고 고치기 — 창을 닫은 뒤 부른다.</summary>
        public Action? RuseTable { get; init; }

        /// <summary>싸움 셈을 돌려 보는 세 가지 — 일기토 · 육상전 모의전 · 모의해전. 창을 닫은 뒤 부른다.</summary>
        public Action? Duel { get; init; }
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
        rows.Children.Add(Row("소지금", _gold, GoldStep, v => _player.SetGold(v)));
        rows.Children.Add(Row("명성", _fame, FameStep, v => _player.Fame = v));
        rows.Children.Add(YearRow(options.YearChanged));
        rows.Children.Add(EffectRow());
        rows.Children.Add(SpouseRow());

        // 좌표 겹쳐 보기 — 배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄운다.
        // 놀이에는 없는 것이라 이 창으로 옮겨 두었다.
        rows.Children.Add(Toggle("좌표 겹쳐 보기", options.CoordsOn(), options.SetCoords,
            "배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄웁니다"));

        // 일기토 불사 — 베타 테스트용. 내 체력이 안 떨어지고, 판은 오른쪽 단추 「승리 · 항복」으로 끝낸다.
        rows.Children.Add(Toggle("일기토 불사", GameSettings.DuelImmortal, on => GameSettings.DuelImmortal = on,
            "일기토에서 죽지 않습니다. 명령을 고를 차례에 오른쪽 단추를 누르면 「승리 · 항복」으로 판을 끝냅니다"));

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
            rows.Children.Add(line);
        }

        // 자동항해 — 해상 커맨드에 있던 것을 옮겼다. 창을 닫고 나서 목적지를 고른다.
        if (options.AutoSail is { } autoSail)
        {
            var sail = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            sail.Children.Add(new TextBlock
            {
                Text = "항해",
                Width = 64,
                Foreground = GameUi.Text,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                VerticalAlignment = VerticalAlignment.Center,
            });
            sail.Children.Add(GameUi.PushButton("자동항해…", () => { Close(); autoSail(); }, 180));
            rows.Children.Add(sail);
        }

        // 모의전 셋 — 게임에 없는 줄이라 미니 게임 차림표에서 이 창으로 옮겼다.
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
            line.Children.Add(GameUi.PushButton("이야기 상태…", () => { Close(); quest(); }, 180));
            rows.Children.Add(line);
        }

        // 묘책 확률 표 — 게임 표를 그대로 보여 주고 고치면 놀이에도 바로 든다.
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
            line.Children.Add(GameUi.PushButton("묘책 확률…", () => { Close(); ruses(); }, 180));
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
        // 「색」 탭 — 게임이 쓰는 색 토큰을 늘어놓는다(개발용).
        var colors = ColorsPage(editor: false);
        // 「편집기 색」 — 퀘스트 편집기 · 모션 메이커 따위 도구 창의 붓만 따로 — 게임 화면 색과 섞이면 헷갈렸다.
        var toolColors = ColorsPage(editor: true);
        var pages = new List<(string, FrameworkElement)> { ("일반", rows) };
        if (options.Discoveries is { } table)
        {
            pages.Add(("발견물", FindsPage(table)));
            Closed += (_, _) => { if (_findsChanged) options.DiscoveriesChanged?.Invoke(); };
        }
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
    /// 「색」 탭 — 게임이 쓰는 색 토큰. <b>코드에서 그대로 읽어 온다</b>(리플렉션) — 색을 더하거나 바꾸면 저절로 따라온다.
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
    /// 편집기 · 도구 창 쪽 형인지 — 놀이 화면에는 안 나오는 창(퀘스트 편집기 흐름도 · 모션 메이커 · 각종 편집 창)이다.
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
    /// 놀이에는 없는 판이다. 발견물이 있어야 볼 수 있는 것(보고 · 연표 · 백과사전 · 발견으로 켜지는 교역품)을
    /// 시험하려고 둔다. 체크는 찾은 것으로 적고(<see cref="Player.Discover"/>) 그 자리 사건도 매듭짓는다
    /// (<see cref="Player.Settle"/>) — 지도에서 그 자리를 지나도 발견 장면이 다시 안 뜬다. 그리고 <b>보고(발표)한 것으로도</b>
    /// 적는다(<see cref="Player.Announce"/>) — 그래야 후원자 · 왕궁의 「제안 선택」에 안 뜬다. 보고로 드는 명성 · 보수 ·
    /// 아이템은 안 준다. 체크는 보고까지 된 것에만 서 있고, 찾기만 하고 보고 전인 것은 「(보고 전)」으로 적혀 비어 있다.
    /// 체크를 풀면 보고한 기록까지 지운다(<see cref="Player.Undiscover"/>).
    /// </remarks>
    private FrameworkElement FindsPage(DiscoveryTable table)
    {
        var page = new StackPanel { Margin = new Thickness(12, 10, 12, 4), Width = 560 };
        var count = new TextBlock { Foreground = GameUi.Text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var find = Field();
        find.Width = 180;
        find.TextAlignment = TextAlignment.Left;
        var list = new StackPanel();
        var boxes = new List<(CheckBox Box, DiscoveryTable.Record Row)>();
        bool bulk = false;

        void Count() => count.Text = $"  {_player.Announced.Count} / {boxes.Count}";

        foreach (var row in table.Discoveries)
        {
            if (row.Name.Length == 0) continue;
            int id = row.Id;
            var box = new CheckBox
            {
                Content = $"{id,3}  {row.Name}" + (row.CategoryName.Length > 0 ? $"  [{row.CategoryName}]" : "")
                          + (_player.HasFound(id) && !_player.HasAnnounced(id) ? "  (보고 전)" : ""),
                IsChecked = _player.HasAnnounced(id),
                Foreground = GameUi.Text,
                FontSize = 14,
                Margin = new Thickness(0, 2, 0, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.Checked += (_, _) =>
            {
                if (_player.Discover(id)) { _player.Settle(id); _findsChanged = true; }
                // 보고까지 한 것으로 — 행적에는 안 남긴다(놀이에서 한 보고가 아니다).
                if (_player.Announce(id, trace: false)) _findsChanged = true;
                if (!bulk) Count();
            };
            box.Unchecked += (_, _) =>
            {
                if (_player.Undiscover(id)) _findsChanged = true;
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
        };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(new TextBlock
        {
            Text = "찾기", Width = 40, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });
        top.Children.Add(find);
        top.Children.Add(GameUi.PushButton("모두 체크", () => All(true), 100));
        top.Children.Add(GameUi.PushButton("모두 해제", () => All(false), 100));
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
            Text = "체크하면 발견한 것으로 적습니다(보고는 따로 해야 합니다). 풀면 보고한 기록까지 지웁니다.",
            Foreground = GameUi.Text, Opacity = 0.75, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });
        Count();
        return page;
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
    /// 놀이에는 없는 줄이다. 게임에서 아내를 맞으려면 술집 여급과 친밀도를 90 까지
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

    /// <summary>개발 창에서 고를 수 있는 해. 놀이는 1480년에 열린다.</summary>
    private const int FirstYear = 1480, LastYear = 1600;

    /// <summary>
    /// 해를 바꾸는 줄 — 새 주인공의 나이처럼 <b>계산기</b>로 넣는다(<see cref="NumberPadDialog"/>).
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

        Border? pick = null;
        string Label() => $"{_player.Date.Year}년 {_player.Date.Month}월 {_player.Date.Day}일…";
        pick = GameUi.PushButton(Label(), () =>
        {
            int was = _player.Date.Year;
            if (NumberPadDialog.Ask(this, was, FirstYear, LastYear) is not { } year || year == was) return;
            _player.SetYear(year);
            changed?.Invoke(year < was);
            if (pick!.Child is TextBlock text) text.Text = Label();
        }, 180);
        line.Children.Add(pick);
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
