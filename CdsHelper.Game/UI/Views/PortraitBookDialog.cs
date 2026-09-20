using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// MALE.CDS · FEMALE.CDS 에 든 초상화를 번호와 함께 늘어놓는 창.
/// </summary>
/// <remarks>
/// 게임 자료가 사람을 <b>얼굴 번호</b>로 가리키므로(인물표 · 후원자표 · 시설 화자표
/// <c>0x0056823C</c>) 그 번호로 얼굴을 찾아볼 데가 필요하다. 이를테면 조선소에서 말을
/// 거는 늙은 목수는 <c>402</c> 다.
///
/// 그림은 <b>1배</b>로 건다 — 조각 그대로 80x96 이라야 게임 화면과 눈으로 맞댈 수 있다.
/// 크게 볼 때만 두 배로 건다.
/// </remarks>
public sealed class PortraitBookDialog : GameWindow
{
    /// <summary>한 줄에 몇 장을 놓을지. 창 폭에 맞춰 저절로 접힌다.</summary>
    private const double CellPad = 6;

    private readonly WrapPanel _sheet = new() { Margin = new Thickness(8) };
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 6, 10, 8) };
    /// <summary>보러 갈 얼굴 번호. 열씩 뛴다 — 한 장씩은 손으로 고친다.</summary>
    private readonly NumericSpinner _find = new()
    {
        Minimum = 0,
        Maximum = 9999,
        Step = 10,
        DecimalPlaces = 0,
        Width = 90,
    };
    private Portraits? _faces;
    private bool _female;
    private int _scale = 1;

    public PortraitBookDialog()
    {
        Title = "초상화 (MALE.CDS · FEMALE.CDS)";
        Width = 860;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var male = new RadioButton { Content = "남자", IsChecked = true, Margin = new Thickness(0, 0, 10, 0) };
        var female = new RadioButton { Content = "여자", Margin = new Thickness(0, 0, 16, 0) };
        male.Checked += (_, _) => { _female = false; Fill(); };
        female.Checked += (_, _) => { _female = true; Fill(); };

        var big = new CheckBox { Content = "두 배로", Margin = new Thickness(0, 0, 16, 0) };
        big.Checked += (_, _) => { _scale = 2; Fill(); };
        big.Unchecked += (_, _) => { _scale = 1; Fill(); };

        var go = new Button { Content = "번호로 가기", Padding = new Thickness(10, 2, 10, 2) };
        go.Click += (_, _) => ScrollTo();

        // 바깥 그림을 게임 얼굴로 넣는다 — 넣고 나면 이 목록도 새로 편다.
        var add = new Button
        {
            Content = "초상화 넣기…",
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(16, 0, 0, 0),
        };
        add.Click += (_, _) =>
        {
            int put = PortraitAddDialog.Show(this);
            if (put < 0) return;

            Load();
            _find.Value = put;
            ScrollTo();
        };
        // 얼굴 하나를 비운다. 맨 뒤라야 아주 들어내고, 가운데는 「없음」으로 덮는다.
        var drop = new Button
        {
            Content = "지우기",
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(6, 0, 0, 0),
        };
        drop.Click += (_, _) => Erase();

        // 넣은 얼굴을 죄다 걷고 처음 벌로 돌린다. 원본은 Support 안에 박혀 있으므로
        // 지금 벌을 지우고 다시 꺼내 놓기만 하면 된다(PortraitStore).
        var undo = new Button
        {
            Content = "원래대로",
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(6, 0, 0, 0),
        };
        undo.Click += (_, _) => Reset();

        // 숫자를 굴리는 대로 따라간다 — 단추를 또 누르지 않아도 된다.
        _find.ValueChanged += (_, _) => ScrollTo();

        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 4),
            Children = { male, female, big, _find, go, add, drop, undo },
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _sheet,
        };

        var page = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        page.Children.Add(bar);
        page.Children.Add(_status);
        page.Children.Add(scroll);
        Content = page;

        Loaded += (_, _) => Load();
    }

    /// <summary>
    /// <see cref="_find"/> 가 가리키는 얼굴을 지운다.
    /// </summary>
    /// <remarks>
    /// <b>번호는 절대로 안 밀린다.</b> 게임 자료는 사람을 얼굴 번호로 가리키므로
    /// (인물표 · 후원자표 · 시설 화자표 <c>0x0056823C</c>) 가운데를 들어내 뒤를 당기면
    /// 엉뚱한 사람들 얼굴이 한꺼번에 바뀐다. 그래서 둘로 나눈다 —
    /// <b>맨 뒤</b>는 아주 들어내고(장수가 하나 준다), <b>가운데</b>는 「없음」 그림으로
    /// 덮어 자리만 비운다.
    /// </remarks>
    private void Erase()
    {
        if (_faces == null) return;

        int face = (int)_find.Value;
        int count = _female ? _faces.FemaleCount : _faces.MaleCount;
        if (face < 0 || face >= count)
        {
            MessageBox.Show(this, $"{face}번은 없습니다. 지금 0~{count - 1}번이 들어 있습니다.",
                            "지우기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool last = face == count - 1;
        string ask = last
            ? $"맨 뒤인 {face}번을 아주 지웁니다. {count - 1}장이 됩니다."
            : $"{face}번을 「없음」 그림으로 비웁니다.\n"
              + "번호가 밀리면 다른 사람들 얼굴까지 바뀌므로 자리는 그대로 둡니다.";

        if (MessageBox.Show(this, ask, "지우기", MessageBoxButton.OKCancel,
                            MessageBoxImage.Question) != MessageBoxResult.OK) return;

        bool done = last
            ? PortraitImport.Remove(_female, face)
            : PortraitImport.Blank(_female, face);
        if (!done)
        {
            MessageBox.Show(this, PortraitImport.LastError, "지우기",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Load();
        ScrollTo();
    }

    /// <summary>지금 보고 있는 벌을 처음 것으로 돌린다.</summary>
    private void Reset()
    {
        string which = _female ? "여자" : "남자";
        if (MessageBox.Show(this,
                $"{which} 초상화에 넣은 얼굴이 죄다 사라지고 처음 벌로 돌아갑니다. 하시겠습니까?",
                "원래대로", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK) return;

        if (!PortraitStore.Reset(_female))
        {
            MessageBox.Show(this, PortraitStore.LastError, "원래대로",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        Load();
    }

    /// <summary>세이브를 연 자리에서 게임 폴더를 찾는다. 못 찾아도 우리 벌로 열린다.</summary>
    private void Load()
    {
        string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        _faces = Portraits.Open(dir);
        if (_faces == null)
        {
            _status.Text = $"초상화를 못 읽었습니다 — {Portraits.LastError}";
            return;
        }
        Fill();
    }

    /// <summary>고른 성별의 얼굴을 죽 늘어놓는다.</summary>
    private void Fill()
    {
        _sheet.Children.Clear();
        if (_faces is not { } faces) return;

        int count = _female ? faces.FemaleCount : faces.MaleCount;
        for (int face = 0; face < count; face++)
        {
            var px = faces.TryGetBgra(face, _female);
            if (px == null) continue;
            _sheet.Children.Add(Cell(face, px));
        }

        _status.Text = $"{(_female ? "FEMALE.CDS" : "MALE.CDS")} · 얼굴 {count}장" +
                       "   —  번호는 인물표 · 후원자표 · 시설 화자표가 가리키는 그 번호다";
    }

    /// <summary>얼굴 한 장과 그 번호.</summary>
    private UIElement Cell(int face, uint[] px)
    {
        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width * _scale,
            Height = Portraits.Height * _scale,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);

        var box = new StackPanel { Margin = new Thickness(CellPad), Tag = face };
        box.Children.Add(image);
        box.Children.Add(new TextBlock
        {
            Text = face.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });
        return box;
    }

    /// <summary>적어 넣은 번호의 얼굴로 굴려 간다.</summary>
    private void ScrollTo()
    {
        int want = (int)_find.Value;

        foreach (var child in _sheet.Children)
            if (child is FrameworkElement { Tag: int face } cell && face == want)
            {
                cell.BringIntoView();
                return;
            }
        _status.Text = $"{want} 번 얼굴은 이 쪽에 없습니다";
    }
}
