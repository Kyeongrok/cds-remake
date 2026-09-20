using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// NEW GAME 의 첫 걸음 — 성·명·연령·생일·혈액형·국적을 받는다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045BF80</c> 이다(만들기 본체는 <c>0x0045EBE0</c>).
/// <code>
///   0x00571AAC  명   0x00571AB0 성   0x00571AB8 연령   0x00571AC0 생일
///   0x00571AC8  월   0x00571ACC 일   0x00571AD0 혈액형
///   0x00571B08  "%s·%s"                                  ; 명·성
///   0x00571500  "&lt;&lt;"  "&gt;&gt;"  "일람"  "이름 일람"  "입력 에러"
///   0x005609D8  별자리 열둘 — 목양좌부터
/// </code>
/// <b>자리는 화면을 재어 박았다.</b> 쌓기가 아니라 <see cref="Canvas"/> 로 놓는다 —
/// 행 피치가 일정하지 않고 칸 하나가 어긋나 있어 쌓기로는 그 모양이 안 난다.
/// <code>
///   속 384 x 206 · 테 밝은 줄 2
///   가로  8 빈칸 · 초상화 80 · 이름표 104 · 컨트롤 128 · 오른끝 378
///   세로  8 성 · 24 명 · 24 연령 · 24 생일 · 27 혈액형 · 29 국적 · 40 단추
///   단추  높이는 게임 띠 그대로 24 · 일람·취소·다음 64 · 국가 152 · 사이는 4
/// </code>
/// 잰 값이 <b>1.75배로 늘어난 화면</b>에서 나온 것이라 그 배로 도로 나눴다. 생일 줄이 밀린
/// <b>16</b>이 곧 한글 한 글자 폭인 것도 그제야 맞아떨어진다(늘어난 화면에서는 28이었다).
///
/// 처음에 잰 판(368 x 224)은 <b>단추 폭을 잘못 잡아</b> 오른쪽 끝이 잘렸다 — 띠는 마구리
/// 둘(32) 안에 글자가 들어야 해서 두 자짜리도 64 가 있어야 한다. 단추를 제 폭으로 키우고
/// 속을 384 로 넓혔다. 테도 검은 베벨 다섯 겹에서 <b>밝은 줄 둘</b>로 줄였다.
///
/// <b>어긋난 데 둘을 그대로 뒀다.</b> 생일 줄의 첫 숫자칸이 연령 줄보다 <b>16(글자 한 칸)</b>
/// 오른쪽에 있고, 행 피치가 생일부터 27 · 29 로 벌어진다. 재어 보니 게임이 그렇다.
///
/// 이름 칸 오른쪽 작은 단추는 <see cref="TextInputDialog"/> 를, 숫자 칸 것은
/// <see cref="NumberPadDialog"/> 를 연다. "일람" 은 미리 갖춰 둔 이름을 늘어놓는다.
///
/// <b>이름 일람은 게임 것이 아니다.</b> 게임은 그 목록을 파일에서 읽어 오는데
/// (<c>0x0045C9DD</c> 가 클래스 <c>0x004FD0D8</c> 을 세운다) 그 파일을 아직 안 짚었다.
/// 그래서 EXE 의 <b>후원자 이름 여든하나</b>(<see cref="SponsorTable"/>)를 가운뎃점에서
/// 갈라 명·성 목록으로 쓴다 — 같은 시대의 진짜 이름들이다.
/// </remarks>
internal sealed class CharacterMakeDialog : GameWindow
{
    // ── 화면에서 잰 자리 ──────────────────────────────────────────────────────

    /// <summary>속 크기. 판은 여기에 테 둘을 두른 388 x 234 가 된다.</summary>
    private const double ContentWidth = 384, ContentHeight = 230;

    /// <summary>테 — 밝은 줄 한 겹이다. 안쪽 줄과 빈칸은 걷어냈다.</summary>
    private const double Bevel = 2;

    /// <summary>물리는 창의 제목 줄(<c>0x00571538</c>).</summary>
    private const string InputError = "입력 에러";

    /// <summary>아직 안 고른 칸(<c>0x0045CD8E</c> 의 <c>cmp …, -1</c>).</summary>
    private const int Unpicked = -1;

    /// <summary>
    /// 가로 자리(속 왼쪽에서). <b>줄마다 칸이 시작하는 자리가 다르다</b> — 이름표는 늘
    /// 같은 자리(116)인데, 이름표가 길수록 칸이 그만큼 오른쪽으로 밀린다.
    /// </summary>
    /// <remarks>
    /// 갈무리를 점 단위로 재어 옮겼다.
    /// <code>
    ///   이름표 116 ─ 성·명 140 · 연령 158 · 생일 175 · 혈액형 178
    /// </code>
    /// 예전에는 칸을 모두 128 에 세워 두어, 두 자·세 자짜리 이름표가 칸에 먹혔다
    /// ("연령" 이 "연" 으로, "혈액형" 이 "혈액" 으로 잘려 보였다).
    /// </remarks>
    private const double PortraitX = 17, LabelX = 116, RightEdge = 368;
    private const double NameX = 140, AgeX = 158, BirthX = 175, BloodX = 178;

    /// <summary>초상화 크기. 얼굴 조각 그대로 놓는다 — 늘리지 않는다.</summary>
    private const double FaceWidth = Portraits.Width, FaceHeight = Portraits.Height;

    /// <summary>세로 자리(속 위에서). 갈무리에서 잰 값이다.</summary>
    private const double RowFamily = 14, RowGiven = 39, RowAge = 64, RowBirth = 90,
                         RowBlood = 115, RowNation = 153, RowFooter = 195;

    /// <summary>칸 크기. 글 한 줄(16)에 위아래 한 점씩이다.</summary>
    private const double FieldWidth = 158, FieldHeight = 18, NumWidth = 26, SpinSize = 16;

    /// <summary>
    /// 단추 크기. 높이는 게임 띠 높이 그대로고, 폭은 띠가 늘어나는 8점 칸에 맞춘다
    /// (<c>16 + 8*n + 16</c>).
    /// </summary>
    /// <remarks>
    /// 갈무리를 재어 보니 "일람"·"취소"·"다음" 이 <b>48</b>(마구리 32 + 가운데 두 칸)이다 —
    /// 한글 두 자(32점)가 마구리를 조금 물고 앉는다. 64 로 두면 눈에 띄게 길쭉했다.
    /// 나라 이름은 여덟 자라 128 이다.
    /// </remarks>
    private const double SmallWidth = 48, SmallHeight = UiSprites.BandHeight,
                         PickWidth = 40, NationWidth = 128;

    /// <summary>단추 사이. 화면 어디서나 3~4 다.</summary>
    private const double Gap = 3;

    /// <summary>칸과 그 옆 계산기 단추 사이.</summary>
    private const double SpinGap = 4;

    /// <summary>"월"·"일" 과 그 다음 것 사이 — 한 칸(반각 한 자) 띈다.</summary>
    private const double WordGap = 8;

    /// <summary>생일 줄이 연령 줄보다 밀려 있는 만큼 — 한글 한 글자 폭이다.</summary>
    private const double BirthShift = 17;

    /// <summary>이름 한 칸에 들어갈 수 있는 길이.</summary>
    private const int NameLimit = 16;

    /// <summary>
    /// 게임이 새 놀이에서 고르게 하는 초상화 수 — <b>앞의 열여섯</b>이다.
    /// </summary>
    /// <remarks>
    /// 여기서는 <b>더 넣은 얼굴까지 다 고르게 한다</b>. 게임이 열여섯으로 묶어 둔 까닭은
    /// 뒤 열여섯이 그 중년 얼굴이라서인데(<c>얼굴 + 16</c>), 그 짝을
    /// <see cref="PortraitAges"/> 가 또렷하게 들고 있으므로 고르는 쪽을 묶을 까닭이 없다.
    /// 중년 얼굴이 없는 얼굴은 <b>나이가 들어도 안 바뀔 뿐</b>이다.
    /// </remarks>
    private const int GameFaceChoices = 16;

    // ── 색 ────────────────────────────────────────────────────────────────────

    private static readonly Brush Back = Frozen(Color.FromRgb(0x31, 0x18, 0x18));
    private static readonly Brush Line = GameUi.Edge;
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xCB, 0xC5, 0xC5));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Portraits? _faces;
    /// <summary>
    /// 이름표. <b>명은 고른 국적에 따라 표기가 갈리므로</b> 목록을 미리 굳히지 않고
    /// 표를 들고 있다가 "일람" 을 누를 때 그 국적 것으로 낸다(조안/후안 · 디오고/디에고).
    /// </summary>
    private readonly PlayerNameTable? _names;

    private readonly IReadOnlyList<string> _givenNames, _familyNames;
    private readonly Canvas _board = new() { Width = ContentWidth, Height = ContentHeight };

    private readonly Image _portrait = new();
    private readonly GameUi.GameLabel _family = Field(), _given = Field();
    private readonly GameUi.GameLabel _age = Field(), _month = Field(), _day = Field();
    private readonly GameUi.GameLabel _zodiac = Glyph("");

    private readonly List<GameButton> _bloods = [], _nations = [];

    private int _face, _blood, _nation;
    private bool _ok;

    private CharacterMakeDialog(Player player, Portraits? faces, PlayerNameTable? names,
                                IReadOnlyList<string> given, IReadOnlyList<string> family)
    {
        _faces = faces;
        _names = names;
        _givenNames = given;
        _familyNames = family;

        _face = player.Face;

        // <b>혈액형과 국적은 처음에 안 골라져 있다</b> — 게임은 신상 칸을 -1 로 깔아 두고
        // (<c>0x0045CD8E</c> 가 -1 인지 본다) 안 고르면 「입력 에러」로 물린다. 우리 쪽 신상
        // 값은 0 이 A형·포르투갈이라 새 사람일 때만 -1 로 두고 시작한다.
        bool fresh = player.Family.Length == 0 && player.Given.Length == 0;
        _blood = fresh ? Unpicked : player.Blood;
        _nation = fresh ? Unpicked : player.Nation;
        _family.Text = player.Family;
        _given.Text = player.Given;
        _age.Text = $"{player.Age}";
        _month.Text = $"{player.BirthMonth}";
        _day.Text = $"{player.BirthDay}";

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Back;

        Portrait();
        NameRow(RowFamily, "성", _family, () => _familyNames);
        NameRow(RowGiven, "명", _given, () => _names?.GivenFor(Math.Max(0, _nation)) ?? _givenNames);
        AgeRow();
        BirthRow();
        BloodRow();
        NationRow();
        Footer();

        Content = Framed(_board);
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();

        ShowFace();
        Mark();
    }

    /// <summary>
    /// 바깥 베벨 한 겹. 안쪽 줄과 빈칸은 걷어냈다 — 그만큼 창이 넓어져 오른쪽 끝의
    /// 일람·다음 단추가 잘렸다.
    /// </summary>
    private UIElement Framed(UIElement content)
    {
        var outer = new Border
        {
            Background = Back,
            BorderBrush = Line,
            BorderThickness = new Thickness(Bevel),
            Child = content,
        };
        GameUi.EnableDrag(this, outer);
        return outer;
    }

    // ── 조각 ──────────────────────────────────────────────────────────────────

    /// <summary>글이나 숫자가 적히는 칸의 속. 양피지 위라 글씨는 짙은 갈색이다.</summary>
    private static GameUi.GameLabel Field() => new(GameFont.ButtonColor)
    {
        // 게임 글씨에는 그림자가 없다 — Bold 는 오른아래로 한 점 겹쳐 찍는 것이라
        // 이 크기에서는 그림자처럼 보인다.
        Bold = false,
        FallbackBrush = Brushes.Black,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(3, 0, 3, 0),
    };

    /// <summary>판 위에 그냥 얹는 글자 — 이름표와 "월"·"일"·별자리가 그렇다.</summary>
    private static GameUi.GameLabel Glyph(string text) => new(GameFont.WhiteColor)
    {
        Text = text,
        Bold = false,
        FallbackBrush = Ink,
    };

    /// <summary>속에 무엇 하나를 그 자리에 놓는다.</summary>
    private T Put<T>(T what, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(what, x);
        Canvas.SetTop(what, y);
        _board.Children.Add(what);
        return what;
    }

    /// <summary>이름표 한 자리.</summary>
    private void Label(string text, double y) => Put(Glyph(text), LabelX, y + 1);

    /// <summary>글이나 숫자를 적는 칸.</summary>
    private void Boxed(UIElement child, double x, double y, double width) =>
        Put(new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Width = width,
            Height = FieldHeight,
            Child = child,
        }, x, y);

    /// <summary>칸 옆의 작은 계산기 단추. 원본 아이콘(MISC.CDS 파트 3)이다.</summary>
    private void Spinner(double x, double y, Action run) =>
        Put(GameUi.CalcButton(run, SpinSize), x, y);

    /// <summary>띠 단추 하나.</summary>
    private GameButton Band(string text, double x, double y, double width, Action run)
    {
        var button = new GameButton(text, run, BandStyle.Button, width);
        button.Height = SmallHeight;
        button.Margin = default;
        return Put(button, x, y);
    }

    // ── 줄 ────────────────────────────────────────────────────────────────────

    private void Portrait()
    {
        Put(new Border
        {
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Width = FaceWidth,
            Height = FaceHeight,
            Child = _portrait,
        }, PortraitX, RowFamily);

        // 화살표 둘을 얼굴 밑에 붙여 놓는다 — 띠 가장 좁은 폭이 40 이라 둘이 얼굴 폭 80 과
        // 딱 떨어진다. 사이를 벌리면 얼굴 밖으로 비어져 나간다.
        double y = RowFamily + FaceHeight + Gap;
        Band("<<", PortraitX, y, PickWidth, () => Turn(-1));
        Band(">>", PortraitX + PickWidth, y, PickWidth, () => Turn(+1));
    }

    private void NameRow(double y, string label, GameUi.GameLabel box, Func<IReadOnlyList<string>> list)
    {
        Label(label, y);
        Boxed(box, NameX, y, FieldWidth);
        Spinner(NameX + FieldWidth + SpinGap, y, () =>
        {
            if (TextInputDialog.Ask(this, box.Text, NameLimit) is { } typed) box.Text = typed;
        });
        Band("일람", RightEdge - SmallWidth, y - 1, SmallWidth, () =>
        {
            if (NameListDialog.Ask(this, list(), box.Text) is { } got) box.Text = got;
        });
    }

    private void AgeRow()
    {
        Label("연령", RowAge);
        Boxed(_age, AgeX, RowAge, NumWidth);
        Spinner(AgeX + NumWidth + SpinGap, RowAge, () =>
        {
            if (NumberPadDialog.Ask(this, Number(_age, 25), Player.MinAge, Player.MaxAge) is { } n)
                _age.Text = $"{n}";
        });
    }

    /// <summary>
    /// 생일 줄. 첫 숫자칸이 연령 줄보다 <b>글자 한 칸(28)</b> 오른쪽이다 — 게임이 그렇다.
    /// </summary>
    private void BirthRow()
    {
        double x = BirthX;
        double after = NumWidth + SpinGap + SpinSize;

        Label("생일", RowBirth);
        Boxed(_month, x, RowBirth, NumWidth);
        Spinner(x + NumWidth + SpinGap, RowBirth, () =>
        {
            if (NumberPadDialog.Ask(this, Number(_month, 1), 1, 12) is { } n)
            { _month.Text = $"{n}"; Mark(); }
        });
        Put(Glyph("월"), x + after + 2, RowBirth + 1);

        // "월" 한 자(16) 뒤로 한 칸 띄고 다음 칸이 선다.
        double x2 = x + after + 2 + 16 + WordGap;
        Boxed(_day, x2, RowBirth, NumWidth);
        Spinner(x2 + NumWidth + SpinGap, RowBirth, () =>
        {
            if (NumberPadDialog.Ask(this, Number(_day, 1), 1, 31) is { } n)
            { _day.Text = $"{n}"; Mark(); }
        });
        Put(Glyph("일"), x2 + after + 2, RowBirth + 1);
        // 별자리는 "일" 에서 한 칸 띄고 선다 — 붙여 놓으면 "일산양좌" 로 읽힌다.
        Put(_zodiac, x2 + after + 2 + 16 + WordGap, RowBirth + 1);
    }

    private void BloodRow()
    {
        Label("혈액형", RowBlood);
        for (int i = 0; i < Player.BloodTypes.Length; i++)
        {
            int pick = i;
            _bloods.Add(Band(Player.BloodTypes[i], BloodX + i * (PickWidth + Gap), RowBlood,
                             PickWidth, () => { _blood = pick; Mark(); }));
        }
    }

    private void NationRow()
    {
        // 두 단추 묶음을 속 한가운데에 놓는다.
        double x = (ContentWidth - (NationWidth * 2 + Gap)) / 2;
        for (int i = 0; i < Player.Nations.Length; i++)
        {
            int pick = i;
            _nations.Add(Band(Player.Nations[i], x + i * (NationWidth + Gap), RowNation,
                              NationWidth, () => { _nation = pick; Mark(); }));
        }
    }

    private void Footer()
    {
        Band("다음", RightEdge - SmallWidth, RowFooter, SmallWidth, Next);
        Band("취소", RightEdge - SmallWidth * 2 - Gap, RowFooter, SmallWidth, Close);
    }

    // ── 손 ────────────────────────────────────────────────────────────────────

    /// <summary>고른 것을 도드라지게 하고 별자리를 다시 적는다.</summary>
    private void Mark()
    {
        // 게임은 <b>고른 것이 밝은 베이지</b>고 안 고른 것이 어두운 쪽이다 —
        // 직업 단추와 같은 규칙이다(AbilityMakeDialog).
        for (int i = 0; i < _bloods.Count; i++)
            _bloods[i].Band = i == _blood ? BandStyle.Button : BandStyle.Alt;
        for (int i = 0; i < _nations.Count; i++)
            _nations[i].Band = i == _nation ? BandStyle.Button : BandStyle.Alt;
        _zodiac.Text = Player.ZodiacOf(Number(_month, 1), Number(_day, 1));
    }

    /// <summary>
    /// 주인공이 고를 수 있는 얼굴들.
    /// </summary>
    /// <remarks>
    /// 게임은 <b>앞의 열여섯</b>(<see cref="GameFaceChoices"/>)만 내준다 — 뒤 열여섯이
    /// 그 중년 얼굴이라 짝이 어긋나면 안 되기 때문이다. 나머지 사백 남짓은 인물표가
    /// 쓰는 남의 얼굴이라 주인공이 쓸 것이 아니다.
    ///
    /// 다만 <b>우리가 더 넣은 얼굴</b>(<see cref="Portraits.GameMaleCount"/> 뒤)은 함께
    /// 낸다 — 초상화 넣기로 제 얼굴을 넣은 사람이 그것을 못 고르면 넣을 까닭이 없다.
    /// 중년 짝은 <see cref="PortraitAges"/> 가 따로 들고 있어 어긋날 일이 없다.
    /// </remarks>
    private int[] Choices()
    {
        int count = _faces?.MaleCount ?? 0;
        var made = new List<int>();

        for (int i = 0; i < GameFaceChoices && i < count; i++) made.Add(i);
        for (int i = Portraits.GameMaleCount; i < count; i++) made.Add(i);
        return [.. made];
    }

    /// <summary>초상화를 하나 옆으로 넘긴다.</summary>
    private void Turn(int by)
    {
        var choices = Choices();
        if (choices.Length == 0) return;

        int at = Array.IndexOf(choices, _face);
        if (at < 0) at = 0;

        _face = choices[(at + by % choices.Length + choices.Length) % choices.Length];
        ShowFace();
    }

    private void ShowFace()
    {
        var px = _faces?.TryGetBgra(_face, female: false);
        if (px == null) { _portrait.Source = null; return; }

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, Portraits.Width * 4);
        bmp.Freeze();
        _portrait.Source = bmp;
        _portrait.Stretch = Stretch.Fill;
        RenderOptions.SetBitmapScalingMode(_portrait, GameUi.SpriteScaling);
    }

    /// <summary>
    /// 그 얼굴이 지고 나올 <b>운명 자리</b>(0~15).
    /// </summary>
    /// <remarks>
    /// 게임은 앞의 열여섯만 고르게 해서 <b>얼굴 번호가 곧 자리</b>였다 — 그 열여섯은
    /// 게임 그대로 둔다. 더 넣은 얼굴에는 매인 자리가 없으므로 <b>열여섯 가운데 하나를
    /// 굴려</b> 준다. 자리는 얼굴에 딸린 값이 아니라 여급 궁합에만 쓰이므로
    /// (<see cref="FortuneCodes"/>) 굴려도 어긋날 데가 없다.
    /// </remarks>
    private static int Destiny(int face) =>
        face < FortuneCodes.Slots
            ? face
            : new Engine.GameRandom(Environment.TickCount).Next(FortuneCodes.Slots);

    private static int Number(GameUi.GameLabel box, int fallback) =>
        int.TryParse(box.Text, out int n) ? n : fallback;

    /// <summary>
    /// "다음" — 게임처럼 빈 칸을 먼저 따진다.
    /// </summary>
    /// <remarks>
    /// 물리는 창에는 <b>제목 줄이 붙는다</b> — "입력 에러" 다(<c>0x00571538</c> 벌).
    /// 성과 명을 따로 따지는 것도 게임 그대로다 — 같은 문구가 두 벌 있다
    /// (<c>0x0045D1D6</c> · <c>0x0045D1EF</c>).
    /// </remarks>
    private void Next()
    {
        // 따지는 차례도 게임 그대로다(0x0045CC74 ~ 0x0045D01F) — 명 · 성 · 달 · 날 · 나이 · 혈액형 · 국적 · 겹치는 이름.
        if (_given.Text.Trim().Length == 0 || _family.Text.Trim().Length == 0)
        {
            NoticeDialog.Show(this, "이름을 정확히 입력해 주십시오", InputError);
            return;
        }

        // 날은 그 달의 날수까지다 — 달 길이 표(0x004FF940)에 윤년은 없어 2월은 28일까지다(0x0045CD1C).
        int month = Number(_month, 0), day = Number(_day, 0);
        if (month is < 1 or > 12 || day < 1 || day > DaysIn[month])
        {
            NoticeDialog.Show(this, "생일을 정확히 입력해 주십시오", InputError);
            return;
        }

        // 나이는 18 ~ 49 로 따진다(0x0045CD45 · 0x0045CD4E) — 숫자판이 40 까지만 내므로 걸릴 일은 드물다.
        int age = Number(_age, 0);
        if (age < Player.MinAge || age > AgeCheckMax)
        {
            NoticeDialog.Show(this, "연령을 정확히 입력해 주십시오", InputError);
            return;
        }

        if (_blood == Unpicked)
        {
            NoticeDialog.Show(this, "혈핵형이 선택되지 않았습니다", InputError);
            return;
        }
        if (_nation == Unpicked)
        {
            NoticeDialog.Show(this, "국적이 선택되지 않았습니다", InputError);
            return;
        }

        string full = $"{_given.Text.Trim()}·{_family.Text.Trim()}";

        // 판에 있는 사람과 이름이 겹쳐도 물린다 — 인물 281명(0x0045CE77), 후원자 81명(0x004AD810),
        // 여급 127명(0x005B3C60) 차례로 훑고 문구는 셋 다 같다(0x00571698 · 0x005716D8 · 0x00571718).
        if (PersonTable.Open().People.Any(r => r.Name == full)
            || (_directory.Length > 0 && SponsorTable.Open(_directory)?.Sponsors.Any(s => s.Name == full) == true)
            || (_directory.Length > 0 && BarmaidTable.Open(_directory)?.Barmaids.Any(b => b.Name == full) == true))
        {
            NoticeDialog.Show(this, "같은 성명을 쓰는 사람이 게임중에 있습니다", InputError);
            return;
        }

        // 누적 캐릭터와 이름이 겹치면 물린다(0x0045CF4A — 자리 다섯의 IDX 를 열어 견준다).
        if (Engine.AccData.NameTaken(full))
        {
            NoticeDialog.Show(this, "같은 성명을 쓰는 누적 캐릭터가 있습니다", InputError);
            return;
        }

        _ok = true;
        Close();
    }

    /// <summary>달마다의 날수(<c>0x004FF940</c>, 0 번은 빈 칸) — 윤년이 없다.</summary>
    private static readonly int[] DaysIn = [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>나이 검사의 위 끝(<c>0x0045CD4E</c> 의 <c>0x31</c>).</summary>
    private const int AgeCheckMax = 49;

    /// <summary>게임 폴더 — 후원자·여급 이름을 견줄 때 쓴다.</summary>
    private string _directory = "";

    /// <summary>
    /// 신상 화면을 띄운다. "다음" 을 누르면 <paramref name="player"/> 에 적고 true.
    /// </summary>
    public static bool Show(Window owner, Player player, string gameDirectory)
    {
        var faces = Portraits.Open(gameDirectory);
        var names = gameDirectory.Length > 0 ? PlayerNameTable.Open(gameDirectory) : null;
        var (given, family) = NamePool(names);

        var dialog = new CharacterMakeDialog(player, faces, names, given, family)
        {
            Owner = owner,
            _directory = gameDirectory,
        };
        dialog.ShowDialog();
        if (!dialog._ok) return false;

        player.SetProfile(dialog._family.Text, dialog._given.Text,
                          Number(dialog._age, 25), Number(dialog._month, 1), Number(dialog._day, 1),
                          dialog._blood, dialog._nation, dialog._face, Destiny(dialog._face));
        return true;
    }

    /// <summary>
    /// 고를 수 있는 명·성. 후원자 여든하나의 이름을 가운뎃점에서 가른 것이다.
    /// </summary>
    /// <summary>
    /// 고를 수 있는 이름들. <b>EXE 에 박힌 표</b>다(<see cref="PlayerNameTable"/>) —
    /// 성 마흔여덟, 명 서른일곱이고 명은 국적에 따라 표기가 갈린다.
    /// </summary>
    /// <remarks>
    /// 예전에는 이 표를 못 짚어 후원자 여든한 명을 가운뎃점에서 갈라 썼는데, 그러면 목록이
    /// 원본보다 훨씬 길고 사람도 다르다. 표를 못 읽으면 미리 만든 주인공 이름만 낸다.
    /// </remarks>
    private static (List<string> Given, List<string> Family) NamePool(PlayerNameTable? table) =>
        table != null
            ? ([.. table.GivenFor(0)], [.. table.Family])
            : (["라몬", "에밀리오", "에르네스토"], ["데·마르시아스", "알발레스"]);
}
