using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 <b>부대배치</b> 화면 — 여섯 자리에 부대를 놓고 「결정」을 누른다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0049E000</c>~<c>0x004A0500</c> 이다(볼트 <c>65.분석-육상전</c> 4절).
/// 자리와 크기를 손으로 짐작하지 않았다 — 게임이 그림을 어디에 얹는지 그대로 옮겼다.
/// <code>
///   0049ec00  자리 여섯   (16,16) (128,16) (240,16) (16,160) (128,160) (240,160)
///   0049ecd1  고르는 넉 칸 (368,16) (480,16) (368,160) (480,160)
///   0049fefb  판 그림      0x004B5CB9(0, 0, 592, 320, 파트 6)
///   0049ff37  부대 한 칸   96 x 96
///   0049f8b5  「결정」 (536,292) 48x24 · 0049f947 「전회」 (478,292) 48x24
/// </code>
///
/// <b>낼 수 있는 병종은 넷</b>이다 — 대장 · 근접 · 사격 · 포. 무엇이 되는지는 제 기능이
/// 정하고, 갈래마다 몇 부대까지인지도 기능이 정한다. 셈은 <see cref="LandRoster"/> 에 있다.
///
/// <b>「전회」는 물러서는 단추가 아니라 지난번 배치를 다시 펴는 단추</b>다
/// (<c>0x0049F540</c> 이 <c>0x0056EAB8</c> 여섯 칸을 그대로 되놓는다). 새 놀이에서 그 여섯이
/// 죄다 −1 인 것도 그래서다 — 아직 한 번도 안 싸운 것이다.
/// </remarks>
internal sealed class LandDeployDialog : GameWindow
{
    /// <summary>판 위 자리 여섯(<c>0x0049EC00</c>). 앞 셋이 윗줄, 뒤 셋이 아랫줄이다.</summary>
    private static readonly (int X, int Y)[] SlotAt =
    [
        (16, 16), (128, 16), (240, 16),
        (16, 160), (128, 160), (240, 160),
    ];

    /// <summary>고르는 넉 칸(<c>0x0049ECD1</c>) — 대장 · 근접 · 사격 · 포 차례다.</summary>
    private static readonly (int X, int Y)[] PickAt =
    [
        (368, 16), (480, 16),
        (368, 160), (480, 160),
    ];

    /// <summary>부대 한 칸의 한 변.</summary>
    private const int Tile = LandArt.DeploySide;

    /// <summary>
    /// 부대 그림은 <b>세로로 두 배 늘려</b> 칸을 꽉 채운다.
    /// </summary>
    /// <remarks>
    /// 조각은 96x48 이 맞다 — LANDDATA 파트 8~50 이 192x192 에 2열 4행으로 여덟 장씩
    /// 들어 있고 한 칸이 96x48 이다. 그런데 <b>게임은 그걸 96x96 으로 늘려 찍는다</b>
    /// (<c>0x004B6963(0x60, 0x60)</c>). 실제 화면에서 말 세 마리 무리를 재면 121x120 으로
    /// <b>거의 정사각</b>인데, 조각 그대로 찍으면 2:1 로 납작해진다.
    /// </remarks>
    private const int ArtZoomY = Tile / LandArt.DeployHeight;

    /// <summary>숫자 한 자의 한 변.</summary>
    private const int Digit = LandArt.DigitSide;

    /// <summary>병사수를 넉 자리로 보고 가운데로 몬다(<c>0x0049FD14</c>).</summary>
    private const int DigitSlots = 4;

    /// <summary>병종 이름을 얹는 자리 — 칸 밑 <c>0x70</c> 이다(<c>0x0049FDDE</c>).</summary>
    private const int NameDrop = 0x70;

    /// <summary>「×」와 남은 수를 얹는 자리(<c>0x0049FE21</c> · <c>0x0049FE7E</c>).</summary>
    private const int CrossX = 0x20, CrossY = 0x70, CountX = 0x30, CountY = 0x68;

    /// <summary>단추 둘(<c>0x0049F8B5</c> · <c>0x0049F947</c>).</summary>
    private const int ButtonY = 292, ButtonW = 48, ButtonH = 24,
                      BackX = 478, DecideX = 536;

    /// <summary>글자 한 자가 차지하는 폭의 절반 — 이름을 가운데로 몰 때 쓴다.</summary>
    private const int HalfCell = 4, NameCells = 12;

    /// <summary>
    /// 지난번 배치 — 게임의 <c>0x0056EAB8</c> 여섯 칸이다.
    /// </summary>
    /// <remarks>
    /// 게임도 정적 자리에 들고 있어 한 판 안에서만 살아 있다. 병종 번호가 아니라
    /// <b>고르는 넉 칸의 번호</b>로 적어 둔다 — 기능이 오르면 같은 자리라도 병종이
    /// 달라지는데, 게임의 <c>0x0049F370</c> 이 되놓을 때 바로 그 옮김을 한다.
    /// </remarks>
    private static readonly int[] LastPicked = [-1, -1, -1, -1, -1, -1];

    private readonly LandArt? _art;
    private readonly LandRoster _roster;
    private readonly SoundBank? _sfx;

    /// <summary>자리마다 놓인 것 — 고르는 넉 칸의 번호, 비었으면 −1.</summary>
    private readonly int[] _picked = [-1, -1, -1, -1, -1, -1];

    /// <summary>자리마다 나뉜 병사수.</summary>
    private readonly int[] _men = new int[LandRoster.SlotCount];

    private readonly Canvas _board = new()
    {
        Width = LandArt.BoardWidth,
        Height = LandArt.BoardHeight,
    };

    /// <summary>
    /// 부대 그림과 병사수를 얹는 층. <b>손을 안 받는다.</b>
    /// </summary>
    /// <remarks>
    /// 이 층은 누르는 네모(<see cref="Spot"/>)보다 <b>위</b>에 얹힌다. 손을 받게 두면
    /// 그림이 깔린 칸에서 누름이 그림에 먹혀 <see cref="Grab"/> 이 안 불린다 — 부대
    /// 그림을 칸만 하게 늘리고 나서 끌어다 놓기가 죽은 것이 이 때문이었다.
    /// </remarks>
    private readonly Canvas _layer = new()
    {
        Width = LandArt.BoardWidth,
        Height = LandArt.BoardHeight,
        IsHitTestVisible = false,
    };

    private LandDeployDialog(Engine.Game game, string cityName, LandRoster roster, double scale)
    {
        _roster = roster;
        _art = game.Directory.Length > 0 ? LandArt.Open(game.Directory) : null;
        _sfx = game.Sfx;

        Title = $"{cityName} — 부대배치";
        Width = LandArt.BoardWidth * scale;
        Height = LandArt.BoardHeight * scale;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;

        Content = Build(scale);
        Redraw();
    }

    /// <summary>
    /// 창을 띄운다. 「결정」을 눌렀으면 참.
    /// </summary>
    /// <remarks>
    /// 문화권을 안 받는다 — 낼 수 있는 여덟 병종은 <see cref="LandUnitArt.PartOf"/> 에서
    /// 죄다 아군·적으로만 갈리고 문화권으로는 안 갈린다.
    /// </remarks>
    /// <param name="borrowedMen">대본이 빌려 준 병력. −1 이면 함대 선원으로 짓는다.</param>
    public static int[]? Show(Window? owner, Engine.Game game, string cityName, int borrowedMen = -1)
    {
        var roster = LandRoster.For(game.Player, Aide(game), borrowedMen);

        // 판을 <b>화면 점</b>에 딱 떨어지게 앉힌다 — 배율이 175%인 화면에서 그냥
        // DIP 로 재면 곱이 1 로 깎이고, 그 1배 그림을 창이 다시 1.75배로 늘리면서
        // 점이 고르지 않게 겹쳐 그림이 찌그러진다.
        double scale = GameUi.PixelFitDip(owner, LandArt.BoardWidth, LandArt.BoardHeight);

        var window = new LandDeployDialog(game, cityName, roster, scale);
        if (owner != null) window.Owner = owner;
        if (window.ShowDialog() != true) return null;

        // 자리마다의 <b>병종 번호</b>를 낸다 — 빈 자리는 −1 이다.
        var kinds = new int[LandRoster.SlotCount];
        for (int i = 0; i < kinds.Length; i++)
            kinds[i] = window._picked[i] < 0 ? -1 : roster.KindAt(window._picked[i]);
        return kinds;
    }

    /// <summary>부관 신상. 없으면 null — 그때는 제독의 기능만 본다.</summary>
    private static Support.Local.Models.Player.MateInfo? Aide(Engine.Game game)
    {
        var mates = game.Player.Mates;
        if (mates.Count == 0) return null;

        string name = mates[0];
        return name.Length > 0 ? game.Player.MateInfoOf(name) : null;
    }

    // ── 화면 ───────────────────────────────────────────────────────────────────

    private UIElement Build(double scale)
    {
        if (Picture() is { } board) _board.Children.Add(Place(board, 0, 0));
        else _board.Background = new SolidColorBrush(Color.FromRgb(0x6B, 0x5E, 0x55));

        // 판 위에서 자리를 누르는 것이 배치다. 칸에 그림이 없어도 누를 수 있어야 하므로
        // 자리마다 눈에 안 띄는 네모를 하나씩 깔아 둔다.
        for (int i = 0; i < LandRoster.SlotCount; i++) _board.Children.Add(Spot(i, SlotAt[i]));

        // 고르는 넉 칸도 같은 네모를 깐다 — 여기서 집어 판으로 끌어다 놓는다.
        for (int i = 0; i < LandRoster.ChoiceCount; i++)
            _board.Children.Add(Spot(SlotCount + i, PickAt[i]));

        _board.Children.Add(_layer);
        Canvas.SetLeft(_layer, 0);
        Canvas.SetTop(_layer, 0);

        RenderOptions.SetBitmapScalingMode(_ghost, BitmapScalingMode.Fant);
        Panel.SetZIndex(_ghost, 100);
        _board.Children.Add(_ghost);

        // 집는 순간 판이 손을 잡으므로(CaptureMouse) 뗌은 <b>늘 판에</b> 온다 —
        // 칸에 손을 달아 두면 아예 안 불린다. 그래서 놓는 자리는 좌표로 짚는다.
        _board.MouseMove += Move;
        _board.MouseLeftButtonUp += (_, e) => Land(e);

        _board.Children.Add(Place(Button("전회", Previous), BackX, ButtonY));
        _board.Children.Add(Place(Button("결정", Decide), DecideX, ButtonY));

        var box = new Canvas
        {
            Width = LandArt.BoardWidth * scale,
            Height = LandArt.BoardHeight * scale,
            Children = { _board },
        };
        _board.RenderTransform = new ScaleTransform(scale, scale);
        return box;
    }

    /// <summary>
    /// 손을 받는 네모 하나. <paramref name="at"/> 이 자리고, 번호가 여섯 밑이면 판의
    /// 자리, 그 위면 고르는 칸이다.
    /// </summary>
    private Border Spot(int at, (int X, int Y) where)
    {
        var spot = new Border
        {
            Width = Tile,
            Height = Tile,
            Background = Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = at,
        };
        spot.MouseLeftButtonDown += (_, e) => Grab(at, e);
        return (Border)Place(spot, where.X, where.Y);
    }

    /// <summary>판의 자리 수. 그 위 번호는 고르는 칸이다.</summary>
    private const int SlotCount = LandRoster.SlotCount;

    private static GameButton Button(string text, Action run) =>
        new(text, run, BandStyle.Button, ButtonW)
        {
            Margin = default,
            Height = ButtonH,
        };

    private static FrameworkElement Place(FrameworkElement what, int x, int y)
    {
        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        return what;
    }

    /// <summary>배치 판 그림. 못 읽으면 null.</summary>
    private Image? Picture()
    {
        if (_art?.TryGetBoard() is not { } bgra) return null;
        return Put(bgra, LandArt.BoardWidth, LandArt.BoardHeight);
    }

    /// <param name="drawW">화면에 걸 너비. 안 주면 그림 그대로다.</param>
    /// <param name="drawH">화면에 걸 높이. 부대 그림은 두 배로 늘려 건다.</param>
    private static Image Put(uint[] bgra, int w, int h, int drawW = 0, int drawH = 0)
    {
        // 곧은 알파로 두고 보간하면 비치는 자리의 검정이 배어 나와 둘레에 어두운 테가 낀다.
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, bgra, w * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = drawW > 0 ? drawW : w,
            Height = drawH > 0 ? drawH : h,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Fant);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    // ── 다시 그리기 ────────────────────────────────────────────────────────────

    private void Redraw()
    {
        Split();
        _layer.Children.Clear();

        for (int i = 0; i < LandRoster.SlotCount; i++)
        {
            if (_picked[i] < 0) continue;
            var (x, y) = SlotAt[i];

            if (Unit(_roster.KindAt(_picked[i])) is { } art)
                _layer.Children.Add(Place(art, x, y));

            // 병사수는 넉 자리 폭에 가운데로 몬다(0x0049FD14).
            string men = _men[i].ToString();
            int left = x + (DigitSlots - men.Length) * Digit / 2;
            foreach (char c in men)
            {
                if (Number(c - '0') is { } glyph) _layer.Children.Add(Place(glyph, left, y));
                left += Digit;
            }

            // 병종 이름은 칸 밑에 붙는다(0x0049FDDE).
            string name = _roster.NameAt(_picked[i]);
            if (Word(name) is { } label)
                _layer.Children.Add(Place(label, x + (NameCells - Bytes(name)) * HalfCell,
                                          y + NameDrop));
        }

        for (int choice = 0; choice < LandRoster.ChoiceCount; choice++)
        {
            var (x, y) = PickAt[choice];
            if (Unit(_roster.KindAt(choice)) is { } art)
                _layer.Children.Add(Place(art, x, y));


            int left = Remaining(choice);
            if (Word("×") is { } cross) _layer.Children.Add(Place(cross, x + CrossX, y + CrossY));
            if (Number(Math.Min(left, 9)) is { } glyph)
                _layer.Children.Add(Place(glyph, x + CountX, y + CountY));
        }
    }

    /// <summary>그 병종의 배치 화면 그림. 못 구하면 null.</summary>
    private Image? Unit(int kind)
    {
        if (_art == null) return null;

        var bgra = _art.TryGetDeployUnit(kind, out int w, out int h);
        return bgra == null ? null : Put(bgra, w, h, w, h * ArtZoomY);
    }

    /// <summary>숫자 한 자. 못 구하면 게임 글꼴로 물러선다.</summary>
    private FrameworkElement? Number(int digit)
    {
        if (_art?.TryGetDigit(digit) is { } bgra) return Put(bgra, Digit, Digit);
        return Word(digit.ToString());
    }

    /// <summary>
    /// 게임 글꼴 한 마디 — <b>흰 글씨에 그림자 없이</b> 찍는다.
    /// </summary>
    /// <remarks>
    /// 배치 판은 돌빛 바탕이라 단추색(17)으로 두면 글자가 바탕에 어둡게 묻힌다. 게임도
    /// 이 자리는 흰 글씨를 그림자 없이 얹는다.
    /// </remarks>
    private static FrameworkElement? Word(string text) =>
        GameUi.GameFontLabel(text, GameFont.WhiteColor, 1, GameUi.ItemTextHeight, shadow: false);

    /// <summary>게임이 이름 길이를 재는 방식 — 바이트 수다(<c>0x0049FDC3</c>).</summary>
    private static int Bytes(string text) =>
        System.Text.Encoding.GetEncoding(949).GetByteCount(text);

    // ── 셈 ─────────────────────────────────────────────────────────────────────

    /// <summary>판에 선 부대 수.</summary>
    private int Standing() => _picked.Count(p => p >= 0);

    /// <summary>그 갈래를 몇 부대 더 낼 수 있는지(<c>0x0049F6E0</c>).</summary>
    private int Remaining(int choice)
    {
        int placed = _picked.Count(p => p == choice);
        return Math.Max(0, _roster.CapAt(choice) - placed);
    }

    /// <summary>
    /// 인원을 다시 나눈다 — <b>고르게 나누고 나머지는 대장 부대</b>가 갖는다.
    /// </summary>
    /// <remarks><c>0x0049F640</c> 이 부대를 놓거나 걷을 때마다 이것을 다시 돌린다.</remarks>
    private void Split()
    {
        Array.Clear(_men);
        int units = Standing();
        if (units == 0) return;

        int each = _roster.Men / units, over = _roster.Men % units;
        for (int i = 0; i < LandRoster.SlotCount; i++)
        {
            if (_picked[i] < 0) continue;
            _men[i] = each + (_picked[i] == LandRoster.Leader ? over : 0);
        }
    }

    // ── 누르기 ─────────────────────────────────────────────────────────────────

    // ── 끌어다 놓기 ────────────────────────────────────────────────────────────

    /// <summary>집은 데. 판이면 0~5, 고르는 칸이면 6~9. 안 집었으면 −1.</summary>
    private int _from = -1;

    /// <summary>집을 때 누른 자리 — 끌었는지 딸깍했는지를 가른다.</summary>
    private Point _grabbed;

    /// <summary>끌고 다니는 그림.</summary>
    /// <summary>끌고 다니는 동안 손끝에 붙어 다니는 부대 그림.</summary>
    /// <remarks>
    /// <b>늘려 채운다.</b> 조각이 96x48 인데 칸은 96x96 이라, 기본값(<c>Uniform</c>)으로
    /// 두면 96x48 그대로 가운데에 놓여 놓기 전과 놓은 뒤의 크기가 달라 보인다.
    /// </remarks>
    private readonly Image _ghost = new()
    {
        IsHitTestVisible = false,
        Opacity = 0.85,
        Visibility = Visibility.Collapsed,
        Stretch = Stretch.Fill,
    };

    /// <summary>이만큼 넘게 끌어야 끈 것으로 본다.</summary>
    private const double DragSlop = 6;

    private void Grab(int at, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (at >= SlotCount)
        {
            // 고르는 칸은 남은 수가 있어야 집힌다.
            if (Remaining(at - SlotCount) <= 0) return;
        }
        else if (_picked[at] < 0) return;              // 빈 자리는 집을 것이 없다

        _from = at;
        _grabbed = e.GetPosition(_board);
        _board.CaptureMouse();
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (_from < 0) return;

        var now = e.GetPosition(_board);
        if (Math.Abs(now.X - _grabbed.X) < DragSlop && Math.Abs(now.Y - _grabbed.Y) < DragSlop)
            return;

        int choice = _from >= SlotCount ? _from - SlotCount : _picked[_from];
        if (choice < 0) return;

        if (_ghost.Source == null || _ghostOf != choice)
        {
            _ghostOf = choice;
            _ghost.Source = Unit(_roster.KindAt(choice))?.Source;
            _ghost.Width = LandArt.DeployWidth;
            _ghost.Height = LandArt.DeployHeight * ArtZoomY;
        }
        _ghost.Visibility = Visibility.Visible;
        Canvas.SetLeft(_ghost, now.X - LandArt.DeployWidth / 2.0);
        Canvas.SetTop(_ghost, now.Y - LandArt.DeployHeight * ArtZoomY / 2.0);
    }

    private int _ghostOf = -1;

    /// <summary>그 자리에 놓인 칸 번호. 판 위 여섯이 0~5, 고르는 넉 칸이 6~9. 밖이면 −1.</summary>
    private static int SpotAt(Point at)
    {
        for (int i = 0; i < SlotCount; i++)
            if (In(at, SlotAt[i])) return i;
        for (int i = 0; i < LandRoster.ChoiceCount; i++)
            if (In(at, PickAt[i])) return SlotCount + i;
        return -1;
    }

    private static bool In(Point at, (int X, int Y) box) =>
        at.X >= box.X && at.X < box.X + Tile && at.Y >= box.Y && at.Y < box.Y + Tile;

    private void Land(MouseButtonEventArgs e)
    {
        if (_from < 0) { Release(); return; }
        e.Handled = true;

        int from = _from;
        var now = e.GetPosition(_board);
        int at = SpotAt(now);
        bool dragged = Math.Abs(now.X - _grabbed.X) >= DragSlop
                       || Math.Abs(now.Y - _grabbed.Y) >= DragSlop;
        Release();

        // 끌지 않고 딸깍했으면 게임의 「선택」 차림표를 편다(0x0049F010).
        if (!dragged)
        {
            if (from < SlotCount) Choose(from);
            return;
        }
        if (at < 0 || at == from) return;      // 판 밖에 놓으면 없던 일이다

        // 판 밖(고르는 칸)에 놓으면 걷는 것이다.
        if (at >= SlotCount)
        {
            if (from < SlotCount) Put(from, -1);
            return;
        }

        int choice = from >= SlotCount ? from - SlotCount : _picked[from];
        if (choice < 0) return;

        // 판에서 판으로 옮기면 자리를 맞바꾼다. 고르는 칸에서 왔으면 그냥 놓는다.
        if (from < SlotCount)
        {
            (_picked[at], _picked[from]) = (_picked[from], _picked[at]);
            _sfx?.Play(SoundBank.DeployPlacePart);
            Redraw();
            return;
        }
        if (_picked[at] != choice && Remaining(choice) <= 0) return;
        Put(at, choice);
    }

    /// <summary>손을 떼고 끌던 그림을 걷는다.</summary>
    private void Release()
    {
        _from = -1;
        _board.ReleaseMouseCapture();
        _ghost.Visibility = Visibility.Collapsed;
    }

    /// <summary>그 자리에 놓거나(−1 이면) 걷는다.</summary>
    private void Put(int slot, int choice)
    {
        _picked[slot] = choice;
        _sfx?.Play(choice < 0 ? SoundBank.DeployLiftPart : SoundBank.DeployPlacePart);
        Redraw();
    }

    /// <summary>자리를 누르면 「선택」 차림표가 뜬다(<c>0x0049F010</c>).</summary>
    private void Choose(int slot)
    {
        // 빈 자리에 하나 더 세우는 것이면 사람이 남았는지부터 본다(0x004A00A0).
        if (_picked[slot] < 0 && Standing() >= _roster.Men)
        {
            NoticeDialog.Show(this, LandRoster.NoMoreWord, LandRoster.WarnTitle);
            return;
        }

        var rows = new (string, bool)[LandRoster.ChoiceCount + 1];
        for (int i = 0; i < LandRoster.ChoiceCount; i++)
            rows[i] = (_roster.NameAt(i), Remaining(i) > 0);
        rows[LandRoster.None] = (LandRoster.NoneWord, true);

        int pick = ChoiceDialog.Pick(this, LandRoster.PickTitle, rows);
        if (pick < 0) return;

        // 놓는 소리와 걷는 소리가 다르다(0x0049F23E · 0x0049F205).
        bool clearing = pick == LandRoster.None;
        _sfx?.Play(clearing ? SoundBank.DeployLiftPart : SoundBank.DeployPlacePart);

        _picked[slot] = clearing ? -1 : pick;
        Redraw();
    }

    /// <summary>
    /// 「전회」 — 지난번 배치를 그대로 되편다(<c>0x0049F4F0</c>).
    /// </summary>
    /// <remarks>
    /// 되펴기 전에 두 가지를 본다(<c>0x0049F3E0</c>). 갈래마다 지금 낼 수 있는 수를 넘으면
    /// 「현재의 부대배치가능수 보다 많아…」고, 사람보다 부대가 많으면 「현재 인원수로는
    /// 나눌 수 없습니다」다. 지난번이 없으면(죄다 −1) 아무 일도 안 일어난다.
    /// </remarks>
    private void Previous()
    {
        var counts = new int[LandRoster.ChoiceCount];
        foreach (int choice in LastPicked)
            if (choice >= 0 && choice < LandRoster.ChoiceCount) counts[choice]++;

        for (int i = 0; i < LandRoster.ChoiceCount; i++)
            if (counts[i] > _roster.CapAt(i))
            {
                NoticeDialog.Show(this, LandRoster.TooManyWord, LandRoster.WarnTitle);
                return;
            }

        // 게임이 세는 것은 대장을 뺀 셋뿐이다 — 갈래 셈에서 대장 부대를 빼고(0x0049F49F)
        // 다시 하나를 더하기 때문에(0x0049F4B5) 대장 한 자리가 셈에서 상쇄된다.
        int units = counts[LandRoster.Melee] + counts[LandRoster.Shot] + counts[LandRoster.Cannon];
        if (units > _roster.Men)
        {
            NoticeDialog.Show(this, LandRoster.CannotSplitWord, LandRoster.WarnTitle);
            return;
        }

        Array.Copy(LastPicked, _picked, LandRoster.SlotCount);
        Redraw();
    }

    /// <summary>
    /// 「결정」 — 대장 부대가 판에 없으면 물린다(<c>0x004473B0</c>).
    /// </summary>
    private void Decide()
    {
        if (!_picked.Contains(LandRoster.Leader))
        {
            NoticeDialog.Show(this, LandRoster.NeedLeaderWord, "");
            return;
        }

        Array.Copy(_picked, LastPicked, LandRoster.SlotCount);
        DialogResult = true;
        Close();
    }
}
