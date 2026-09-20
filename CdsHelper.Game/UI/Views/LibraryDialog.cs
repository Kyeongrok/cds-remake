using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도서관 → 열람 의 책장 화면. 그 도시에 놓인 책을 책등으로 꽂아 두고, 누르면 읽는다.
/// </summary>
/// <remarks>
/// 책등 색은 게임 규칙 그대로다(볼트 <c>20.분석-도서관 책과 책등 색</c>).
/// <list type="bullet">
///   <item><b>초록</b> — 이 책이 나에게 더 줄 힌트가 없다(안 읽었어도 초록일 수 있다).</item>
///   <item><b>파랑</b> — 지금 읽으면 새 힌트가 들어온다.</item>
///   <item><b>빨강</b> — 줄 힌트는 남았는데 조건(책의 언어 3 · 힌트의 필요 기능)이 모자란다.</item>
/// </list>
/// 칸은 선반 세 줄에 열일곱 자리씩이다 — 게임 화면에서 재어 맞췄다. 책이 51권을 넘으면
/// 뒤는 안 꽂는데, <b>원본 데이터로는 그런 도시가 없다</b> — 책 표(<c>0x004C4748</c>)의
/// 놓인 도시 여덟 칸을 다 세어도 가장 많은 도시가 서른넷이다. 그래서 넘기는 장치도 없다.
/// </remarks>
public sealed class LibraryDialog : GameWindow
{
    /// <summary>
    /// 선반 세 줄. 책등 그림의 <b>위</b>가 놓이는 높이다(책장 그림 384x320 기준).
    /// </summary>
    /// <remarks>
    /// 게임 화면에 책장 그림을 맞춰 끼워(1.74배로 맞았다) 책등 자리를 되돌린 값이다 —
    /// 첫 칸 x 30, 간격 16.1, 줄 사이 80.5. 간격이 책등 너비(32)의 절반이라 책이 반쯤씩
    /// 겹쳐 꽂힌다. 게임도 그렇다.
    /// </remarks>
    private static readonly double[] ShelfTops = [66, 146.5, 227];

    /// <summary>한 줄에 꽂히는 자리 수와 첫 자리·간격.</summary>
    private const int SlotsPerShelf = 17;
    private const double FirstSlotX = 30, SlotStep = 16.1;

    private readonly Player _player;
    private readonly BookTable _books;
    private readonly CityBuildingTable _names;
    private readonly Func<int, string> _hintName;
    private readonly Canvas _layer = new();
    private readonly Border _tag;
    private readonly GameUi.GameLabel _tagText;
    private readonly int _scale;

    /// <summary>
    /// 못 읽는 책이 어느 말로 적혔는지 이르는 곳 — <b>게임 화면 맨 아래 띠</b>다.
    /// </summary>
    /// <remarks>
    /// 왕궁에서 "명성치가 모자랍니다." 가 뜨는 그 자리다(<see cref="ShipMapWindow.Say"/>).
    /// 책장 밑에 따로 띠를 두는 것이 아니다.
    /// </remarks>
    private readonly Action<string>? _say;

    /// <summary>닫기 조각의 크기와 양피지 모서리에서 떨어진 거리. 게임 갈무리에서 잰 값이다.</summary>
    private const double CloseSize = 16, CloseInset = 10;
    private readonly OpenBookArt? _book;
    private readonly Func<int, string>? _hintText;

    /// <summary>띠 말에 딸린 소리(<see cref="SoundBank.BandNoticePart"/>)를 낼 효과음 묶음.</summary>
    private readonly SoundBank? _sfx;

    /// <summary>힌트가 가리키는 발견물을 찾아서 보고까지 했는지 — 펼친 책의 종이 색을 가른다.</summary>
    private readonly Func<int, bool>? _reported;

    private LibraryDialog(string cityName, BookShelf art, IReadOnlyList<Library.Slot> shelved,
                          Player player, BookTable table, CityBuildingTable names,
                          Func<int, string> hintName, int scale,
                          OpenBookArt? bookArt, Func<int, string>? hintText, Action<string>? say,
                          SoundBank? sfx, Func<int, bool>? reported)
    {
        _player = player;
        _books = table;
        _names = names;
        _hintName = hintName;
        _scale = scale;
        _book = bookArt;
        _say = say;
        _hintText = hintText;
        _sfx = sfx;
        _reported = reported;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var shelf = new Image
        {
            Source = ToBitmap(art.Shelf, BookShelf.ShelfWidth, BookShelf.ShelfHeight),
            Width = BookShelf.ShelfWidth * scale,
            Height = BookShelf.ShelfHeight * scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(shelf, GameUi.SpriteScaling);

        var box = new Grid
        {
            Width = shelf.Width,
            Height = shelf.Height,
            Children = { shelf, _layer },
        };

        // 책 이름표도 <b>게임 글꼴</b>이다. 글씨는 <b>흰색</b>이고 굵히지 않는다 —
        // 굵히면 한 점 겹쳐 찍혀서 획에 그림자가 진 것처럼 보인다.
        // 게임 서가의 이름표는 짙은 판에 밝은 한 점 테, 흰 글씨다 — 술집 손님 이름표와 같은 꼴이다.
        (_tag, _tagText) = GameUi.HoverTag();
        _layer.Children.Add(_tag);
        Panel.SetZIndex(_tag, 20);

        var spines = new[]
        {
            ToBitmap(art.Spines[0], BookShelf.SpineWidth, BookShelf.SpineHeight),
            ToBitmap(art.Spines[1], BookShelf.SpineWidth, BookShelf.SpineHeight),
            ToBitmap(art.Spines[2], BookShelf.SpineWidth, BookShelf.SpineHeight),
        };
        for (int i = 0; i < shelved.Count && i < ShelfTops.Length * SlotsPerShelf; i++)
        {
            if (shelved[i].Book is { } book) AddBook(book, i, spines);
            else if (shelved[i].Filler) AddFiller(i, spines);
        }

        // 게임에는 제목 띠가 없다 — 양피지 오른쪽 위에 닫기 조각만 얹혀 있다.
        var close = GameUi.CloseBox(Close, _scale);
        Canvas.SetLeft(close, (BookShelf.ShelfWidth - CloseSize - CloseInset) * _scale);
        Canvas.SetTop(close, CloseInset * _scale);
        Panel.SetZIndex(close, 30);
        _layer.Children.Add(close);

        // 서가는 <b>테두리를 두르지 않는다</b> — 게임은 양피지 그림 그대로 띄운다.
        // 여느 창처럼 밝은 줄(<see cref="GameUi.Edge"/>)을 두르면 그림 밖에 액자가 하나
        // 더 생겨 원본과 다르게 보인다.
        Content = new Border { Background = GameUi.Back, Child = box };
        GameUi.EnableDrag(this, box);
        Closed += (_, _) => _say?.Invoke("");

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>
    /// 읽을 수 없는 책 한 권. 게임이 책 번호 -1 로 끼워 넣는 것이라 늘 초록이고,
    /// 이름표도 없고 눌러도 열리지 않는다 — 서가를 채우는 것이 하는 일의 전부다.
    /// </summary>
    private void AddFiller(int slot, BitmapSource[] spines)
    {
        int shelfRow = slot / SlotsPerShelf, column = slot % SlotsPerShelf;

        var image = new Image
        {
            Source = spines[0],                     // 0 = 초록
            Width = BookShelf.SpineWidth * _scale,
            Height = BookShelf.SpineHeight * _scale,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, (FirstSlotX + column * SlotStep) * _scale);
        Canvas.SetTop(image, ShelfTops[shelfRow] * _scale);
        _layer.Children.Add(image);
    }

    /// <summary>책 한 권을 서가에 꽂는다.</summary>
    private void AddBook(BookTable.Book book, int slot, BitmapSource[] spines)
    {
        int shelfRow = slot / SlotsPerShelf, column = slot % SlotsPerShelf;
        double x = FirstSlotX + column * SlotStep;
        double y = ShelfTops[shelfRow];

        var image = new Image
        {
            Source = spines[SpineColor(book)],
            Width = BookShelf.SpineWidth * _scale,
            Height = BookShelf.SpineHeight * _scale,
            Stretch = Stretch.Fill,
            Cursor = Cursors.Hand,
            Tag = book,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        Canvas.SetLeft(image, x * _scale);
        Canvas.SetTop(image, y * _scale);

        image.MouseEnter += (_, _) => ShowTag(book, x, y);
        image.MouseLeave += (_, _) =>
        {
            _tag.Visibility = Visibility.Collapsed;
            // 책을 펴는 동안에는 띠를 안 지운다 — 책 창이 뜨면 책등에서 쥐가 벗어나
            // 이 줄이 바로 돌아, 「무슨 말인지 잘 모르겠습니다」가 스쳐 지나가 버렸다.
            if (!_hovering) return;
            _hovering = false;
            _say?.Invoke("");
        };
        image.MouseLeftButtonDown += (_, e) => e.Handled = true;
        image.MouseLeftButtonUp += (_, e) => { e.Handled = true; Read(book, image, spines); };
        _layer.Children.Add(image);
    }

    /// <summary>
    /// 지금 띠에 뜬 글이 <b>책등에 쥐를 올려서</b> 난 것인지.
    /// </summary>
    /// <remarks>
    /// 책을 펴면 <see cref="Shown"/> 이 띠에 말을 넣는데, 그 순간 책 창이 책등을 덮어
    /// <c>MouseLeave</c> 가 돌아 방금 넣은 말을 지워 버렸다. 그래서 띠를 지우는 것은
    /// <b>올려서 난 글일 때뿐</b>으로 좁힌다.
    /// </remarks>
    private bool _hovering;

    /// <summary>책등 밑에 제목·저자를 띄운다.</summary>
    private void ShowTag(BookTable.Book book, double x, double y)
    {
        _hovering = true;
        // 읽을 수 없는 책은 이름이 안 보인다 — 글자마다 x 로 가린다.
        bool readable = CanRead(book);
        string title = readable ? book.Title : Masked(book.Title);
        string author = readable ? book.Author : Masked(book.Author);
        _tagText.Text = $"「{title}」{author}";
        _say?.Invoke(readable
            ? ""
            : $"{LanguageOf(book)}{GameUi.Josa(LanguageOf(book), "으로", "로")} 표기되어 있습니다");
        _tag.Visibility = Visibility.Visible;
        _tag.UpdateLayout();
        double w = _tag.ActualWidth > 0 ? _tag.ActualWidth : 160;
        double left = (x + BookShelf.SpineWidth / 2.0) * _scale - w / 2;
        Canvas.SetLeft(_tag, Math.Clamp(left, 0, Math.Max(0, BookShelf.ShelfWidth * _scale - w)));
        Canvas.SetTop(_tag, Math.Min((y + BookShelf.SpineHeight + 2) * _scale,
                                     BookShelf.ShelfHeight * _scale - 24));
    }

    private string LanguageOf(BookTable.Book book) =>
        book.Language >= 0 && book.Language < _names.LanguageNames.Count
            ? _names.LanguageNames[book.Language]
            : $"언어 {book.Language}";

    /// <summary>
    /// 책등 색을 고른다 — 게임 <c>0x4716A0</c> 의 규칙을 그대로 옮겼다.
    /// 이미 얻은 힌트는 세지 않으므로, 한 번도 안 편 책이 곧장 초록일 수 있다.
    /// </summary>
    private int SpineColor(BookTable.Book book)
    {
        bool left = false;      // 아직 못 얻은 힌트가 남았나
        foreach (int hint in book.Hints)
        {
            if (_player.HasHint(hint)) continue;
            left = true;
            if (CanRead(book) && Unlocked(hint) && KnowsSkill(hint)) return 1;   // 파랑
        }
        return left ? 2 : 0;                                    // 빨강 / 초록
    }

    /// <summary>책을 읽으려면 그 언어가 있어야 하는 자리 — 게임도 <c>3</c> 이다.</summary>
    /// <remarks><c>0x00463E41</c> 의 <c>cmp eax,3 / jl</c> 이다.</remarks>
    private const int ReadLevel = 3;

    /// <summary>
    /// 책을 읽을 수 있는지 — <b>그 언어를 3 자리까지</b> 배워야 한다(<c>0x00463DB0</c>).
    /// </summary>
    /// <remarks>
    /// 언어는 기술과 <b>딴 칸</b>에 적힌다(<see cref="Player.TongueOf"/>). 예전에는 여기서
    /// <c>LevelOf</c>(기술 칸)를 봐서 <b>언어 자리가 늘 0 으로 읽혔고</b>, 그래서 파란 책이
    /// 한 권도 안 나오고 아무 책도 못 읽었다.
    ///
    /// 게임은 <b>함대에 탄 사람 전부</b>를 훑어 그 언어를 가장 잘 아는 이를 찾는다
    /// (<c>0x0047CD20</c>) — 부하가 대신 읽어 준다. 그래서 로드리고·데·에스코베토 같은
    /// 말 잘하는 부하를 들이면 읽을 수 있는 책이 확 는다. 부하의 언어는 인물 표에서 이름으로 찾는다.
    /// </remarks>
    private bool CanRead(BookTable.Book book)
    {
        int best = _player.TongueOf(LanguageOf(book));
        if (book.Language >= 0)
            foreach (var mate in MateRows())
                if (book.Language < mate.Languages.Length)
                    best = Math.Max(best, mate.Languages[book.Language]);
        return best >= ReadLevel;
    }

    private List<PersonTable.Row>? _mateRows;

    /// <summary>함대에 탄 부하들의 인물 표 줄. 한 번만 찾는다.</summary>
    private List<PersonTable.Row> MateRows()
    {
        if (_mateRows != null) return _mateRows;
        var people = PersonTable.Open().People;
        _mateRows = [.. _player.Mates.Where(n => n.Length > 0)
                                     .Select(n => people.FirstOrDefault(r => r.Name == n))
                                     .OfType<PersonTable.Row>()];
        return _mateRows;
    }

    /// <summary>
    /// 힌트가 <b>열려 있는지</b> — 기능·언어를 보기 전의 관문이다(<c>0x0042CC90</c>).
    /// </summary>
    /// <remarks>
    /// 여기서 물리면 펼친 책에 삽화도 안 얹히고 띠에 「무슨 말인지 잘 모르겠습니다」 가 뜬다.
    /// <code>
    ///   0042CC93  [힌트+0x04] &amp; 0x08   개방 비트 — 놀이가 켜 준다
    ///   0042CCA4  힌트 번호 != 184
    ///   0042CCC0  선행 발견물 여덟 칸이 다 발견되었나
    /// </code>
    /// 톨레도 도서관의 <b>카파도키아</b>(힌트 52)가 그렇다 — 신학 3 만으로는 안 되고
    /// <b>산티아고 대성당</b>(발견물 50)을 먼저 봐야 한다. 성지순례를 다녀와야 읽힌다는
    /// 것이 이것이다. 「성스러운 유물상자」(힌트 101)도 성 마르틴 교회(62)가 앞선다.
    ///
    /// <b>개방 비트는 옮길 것이 없다</b> — 힌트 표 <c>+0x2C</c> 가 120줄 모두 1 이라 판을 열 때
    /// <c>0x0042CB57</c> 가 전부 세워 두고(<c>0x0042CB60</c>), 지우는 곳은 그 초기화뿐이다.
    /// 대본 명령 <c>26 0E</c>(<c>0x0040A0F8</c>)와 세터 <c>0x004AE020</c> 은 이미 선 비트를 다시 세울 뿐이라
    /// 눈에 보이는 차이가 없다. 그래서 늘 열린 것으로 둔다.
    /// </remarks>
    private bool Unlocked(int hint)
    {
        if (hint == SealedHint) return false;

        // 먼저 찾아 두어야 할 것이 남아 있으면 아무리 배워도 안 들어온다.
        if (_books.NeedFor(hint).Parents is { } parents)
            foreach (int id in parents)
                if (!_player.HasFound(id)) return false;
        return true;
    }

    /// <summary>늘 닫혀 있는 힌트 번호(<c>0x0042CCA4</c> 의 <c>cmp 184</c>).</summary>
    private const int SealedHint = 184;

    /// <summary>
    /// 힌트의 <b>필요 기능</b>을 채웠는지(<c>0x00463E50</c>) — 없으면 된 것으로 친다.
    /// </summary>
    /// <remarks>필요 기능은 힌트 줄 <c>+0x20</c>, 그 자리는 <c>+0x28</c> 이다(<c>0x00463E72</c>).</remarks>
    private bool KnowsSkill(int hint)
    {
        var need = _books.NeedFor(hint);
        if (need.Skill < 0 || need.Skill >= _names.SkillNames.Count) return true;
        return _player.LevelOf(_names.SkillNames[need.Skill]) >= need.Level;
    }

    /// <summary>힌트가 요구하는 기능 이름(기능 이름표 <c>0x00560A10</c>).</summary>
    private string SkillOf(int hint)
    {
        int skill = _books.NeedFor(hint).Skill;
        return skill >= 0 && skill < _names.SkillNames.Count ? _names.SkillNames[skill] : $"기능 {skill}";
    }

    /// <summary>
    /// 책을 편다(<c>0x00471EA0</c>). 시간도 돈도 들지 않는다.
    /// </summary>
    /// <remarks>
    /// <b>책은 언제나 열린다</b> — 언어가 모자라도 창은 똑같이 뜨고, 모자란 것은 창 안의 띠 말로만
    /// 이른다. 힌트는 책을 펼 때 한꺼번에 들어오는 것이 아니라 <b>그 펼침면이 화면에 나올 때</b>
    /// 칸 하나씩 들어온다(<see cref="Shown"/>). 한 번도 안 넘긴 면의 힌트는 안 들어온다.
    /// </remarks>
    private void Read(BookTable.Book book, Image image, BitmapSource[] spines)
    {
        int count = Math.Min(book.Hints.Count, OpenBookDialog.MaxSpreads);
        _hovering = false;      // 펴는 동안 난 띠 말은 책등에서 쥐가 벗어나도 안 지운다

        // 그림을 못 읽으면 첫 면만 편 셈 치고 힌트 주기와 띠 말만 낸다.
        if (!OpenBookDialog.Read(this, _book, count, i => SpreadAt(book, i), i => Shown(book, i)))
            Shown(book, 0);

        image.Source = spines[SpineColor(book)];   // 읽고 나면 색이 바뀐다
    }

    /// <summary>
    /// 펼침면 <c>i</c> 에 그릴 것 — 게임 <c>0x00464C50</c> 의 규칙이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   삽화   개방·184·선행 발견물(0x0042CC90)만 보고 얹는다 — 기능·언어가 모자라도 나온다
    ///   종이   찾아서 보고까지 한 힌트면 누런 벌, 아니면 흰 벌
    ///   글     개방 &amp;&amp; 언어 3(0x00463E30) &amp;&amp; 기능(0x00463E50) 일 때만 — 아니면 오른쪽이 빈 종이
    /// </code>
    /// </remarks>
    private OpenBookDialog.Spread SpreadAt(BookTable.Book book, int i)
    {
        if (i >= book.Hints.Count) return new OpenBookDialog.Spread(false, -1, false, "", "");

        int hint = book.Hints[i];
        bool open = Unlocked(hint);
        int picture = _books.NeedFor(hint).Picture;
        int illustration = open && picture >= 0 && picture < OpenBookArt.IllustrationCount
            ? OpenBookArt.FirstIllustration + picture
            : -1;
        bool readable = open && CanRead(book) && KnowsSkill(hint);

        return new OpenBookDialog.Spread(true, illustration, _reported?.Invoke(hint) == true,
                                         readable ? _hintName(hint) : "",
                                         readable ? _hintText?.Invoke(hint) ?? "" : "");
    }

    /// <summary>
    /// 펼침면 <c>i</c> 가 화면에 나왔다 — 힌트를 주고 아래 띠에 말을 낸다(<c>0x00464A30</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   칸이 -1                       「모험에 도움이 될 것 같지 않습니다」   0x0055CAB0
    ///   개방·184·선행 발견물 실패     「무슨 말인지 잘 모르겠습니다」         0x0055CA90
    ///   언어 &amp;&amp; 기능                 힌트가 들어오고 띠를 비운다(소리 없음, 0x0040E0C0)
    ///   기능 부족                     「%s의 지식이 필요합니다」 기능 이름     0x0055CA60
    ///   기능은 되고 언어 부족         같은 꼴에 언어 이름                     0x0055CA78
    /// </code>
    /// <b>기능 부족이 언어 부족보다 먼저다</b> — 둘 다 모자라면 기능 이름이 나온다. 이미 얻은
    /// 힌트라고 따로 이르는 말은 없다.
    /// </remarks>
    private void Shown(BookTable.Book book, int i)
    {
        if (i >= book.Hints.Count) { Band("모험에 도움이 될 것 같지 않습니다"); return; }

        int hint = book.Hints[i];
        if (!Unlocked(hint)) { Band("무슨 말인지 잘 모르겠습니다"); return; }

        bool tongue = CanRead(book), skill = KnowsSkill(hint);
        if (tongue && skill) _player.GainHint(hint);   // 띠 검사보다 앞이라 늘 실행된다

        if (!skill) Band($"{SkillOf(hint)}의 지식이 필요합니다");
        else if (!tongue) Band($"{LanguageOf(book)}의 지식이 필요합니다");
        else _say?.Invoke("");
    }

    /// <summary>아래 띠에 말을 넣는다 — 소리 <c>0x1D</c> 가 같이 난다(<c>0x0040E0A0</c>).</summary>
    private void Band(string text)
    {
        _say?.Invoke(text);
        _sfx?.Play(SoundBank.BandNoticePart);
    }

    /// <summary>글자마다 <c>x</c> 로 가린다. 띄어쓰기는 그대로 둔다.</summary>
    /// <summary>
    /// 못 읽는 책의 이름 — 게임은 글자마다 영문 X 가 아니라 <b>온각 ×</b>(CP949 <c>A1BF</c>, 두 칸 폭)로 가린다.
    /// </summary>
    private static string Masked(string text) =>
        new([.. text.Select(c => c == ' ' ? ' ' : '×')]);

    private static BitmapSource ToBitmap(uint[] bgra, int width, int height)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
                                         bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// 책장을 띄운다. 그림이나 표를 못 읽으면 그 까닭을 알리고 만다.
    /// </summary>
    /// <param name="book">펼친 책 그림. 없으면 알림 창으로만 이른다.</param>
    /// <param name="hintText">그 힌트의 설명 — 펼친 책 오른쪽 면에 적힌다.</param>
    /// <param name="sfx">띠 말 소리를 낼 효과음 묶음. 없으면 소리 없이 말만 낸다.</param>
    /// <param name="reported">힌트의 발견물을 찾아서 보고까지 했는지 — 펼친 책 종이 색을 가른다.</param>
    public static void Show(Window owner, string gameDirectory, string cityName, int cityId,
                            Player player, BookTable table, CityBuildingTable names,
                            Func<int, string> hintName,
                            OpenBookArt? book = null, Func<int, string>? hintText = null,
                            Action<string>? say = null, SoundBank? sfx = null,
                            Func<int, bool>? reported = null)
    {
        var art = BookShelf.Open(gameDirectory);
        if (art == null)
        {
            NoticeDialog.Show(owner, $"책장을 열지 못했다 — {BookShelf.LastError}");
            return;
        }

        var books = table.InLibrary(cityId, player.Date.Year);
        if (books.Count == 0)
        {
            NoticeDialog.Show(owner, "서가가 비어 있다.");
            return;
        }

        // 진짜 책 사이사이에 읽을 수 없는 초록 책이 끼인다 — 그 마을 그 해면 늘 같은 모양이다.
        var shelved = Library.Shelve(books, Library.RandomFor(cityId, player.Date.Year));

        // 창 크기에 맞춰 정수배로 키운다(책장이 384x320 이라 두 배면 넉넉하다).
        int scale = owner.ActualHeight > 800 ? 2 : 1;
        new LibraryDialog(cityName, art, shelved, player, table, names, hintName, scale,
                          book, hintText, say, sfx, reported)
        {
            Owner = owner,
        }.ShowDialog();
    }
}
