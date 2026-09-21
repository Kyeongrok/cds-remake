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

    }

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
        rows.Children.Add(EffectRow());
        rows.Children.Add(SpouseRow());

        // 좌표 겹쳐 보기 — 배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄운다.
        // 놀이에는 없는 것이라 이 창으로 옮겨 두었다.
        rows.Children.Add(Toggle("좌표 겹쳐 보기", options.CoordsOn(), options.SetCoords,
            "배가 선 자리를 WORLD.CDS 의 칸·파일 오프셋까지 지도 위에 띄웁니다"));

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
        stack.Children.Add(rows);
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
