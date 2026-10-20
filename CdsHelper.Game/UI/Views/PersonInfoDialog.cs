using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「인물정보」 — 제독 자신을 보여 주는 판.
/// </summary>
/// <remarks>
/// 원본의 <c>0x0046DF70</c> 이다. 줄 글은 그쪽 서식 그대로다(<c>0x00570EE8</c> 벌).
/// <code>
///   "체  력/%4d"      "    명성치/%8d"
///   "지  력/%4d"      "    악명치/%8d"
///   "무  력/%4d    직업  /%s"
///   "매  력/%4d"
///   "연령  /%2d세"    "          생년월일/%4d년%2d월%2d일"
///   "별자리/%-6s        혈액형  /%s"
///   "국적  /%s"
///   "소지금/%10ld닢"  "저금  /%10ld닢"  "빚    /%10ld닢"
///   "특기"  "취소"
/// </code>
/// <b>판이 밤색이 아니라 강청색이다</b> — 화면에서 뽑은 값이 바탕
/// <c>(92, 111, 147)</c>, 테 <c>(54, 65, 86)</c> 다. 정보 판 가운데 이것만 색이 다르다.
///
/// 왼쪽 위에 초상화가 서고, 능력치는 <b>넷만</b> 뜬다(운·신앙심은 안 보인다).
/// "특기" 를 누르면 기술과 어학이 따로 열린다.
/// </remarks>
internal sealed class PersonInfoDialog : InfoDialog
{
    /// <summary>
    /// 판 크기(그림 점). 원본 갈무리를 재어 맞췄다 — 판 바탕이 <b>430 x 270</b> 이고,
    /// 좌우 여백 14 씩과 아래 단추 줄을 빼면 속이 이만큼이다.
    /// </summary>
    private const double BoardWidth = 402, BoardHeight = 224;

    /// <summary>
    /// 초상화를 몇 배로 그릴지. 원본은 <b>조각 그대로</b> 80x96 이다 —
    /// 두 배로 걸면 얼굴만 커져 글자와 어긋난다.
    /// </summary>
    private const int FaceScale = 1;

    /// <summary>이 판은 글씨가 검정이다. 밤색 판들과 다른 자리다.</summary>
    private const byte BlackInk = GameFont.BlackColor;

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private PersonInfoDialog(Player player, Portraits? faces, Engine.Game? game = null)
    {
        if (Local.Settings.GameSettings.Dh2PersonInfo)
        {
            int Show(int which) => Ability.Display(player.AbilityOf(which));
            bool nobleOn = Engine.Town.Nobility.Enabled && game != null;
            var lines = new List<string>
            {
                $"생년월일 {player.BirthYear}년 {player.BirthMonth}월 {player.BirthDay}일",
                $"소지금 {player.Gold}닢   저금 {player.Savings}닢   빚 {player.Debt}닢",
            };
            if (nobleOn)
                lines.Add($"작위 {Engine.Town.Nobility.NameOf(player.NobleRank)}   공적 {Engine.Town.Nobility.MeritOf(game!)}");
            var acts = new List<(string, Action)> { ("특기", () => ShowSkills(player)) };
            if (nobleOn) acts.Add(("작위", () => ShowNobility(this, game!)));
            acts.Add(("취소", Close));
            BuildDh2(faces?.TryGetBgra(PortraitAges.At(player.Face, player.Age, false, faces), female: false), player.Name,
                [("연령", $"{player.Age}세"), ("체력", $"{Show(Ability.Body)}"), ("명성", $"{player.Fame}"),
                 ("국적", player.NationName), ("지력", $"{Show(Ability.Mind)}"), ("악명", $"{player.Infamy}"),
                 ("직업", player.Work.Name), ("무력", $"{Show(Ability.Might)}"), ("별자리", player.Zodiac),
                 ("혈액형", player.BloodName), ("매력", $"{Show(Ability.Charm)}")],
                lines, acts);
            return;
        }

        var head = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        head.Children.Add(BlackLine($"  {player.Name}"));
        // 능력치는 <b>담은 값 + 1</b> 로 보인다(0x0046D76C 의 inc eax).
        int Shows(int which) => Ability.Display(player.AbilityOf(which));
        head.Children.Add(BlackLine($"  체  력/{Shows(Ability.Body),4}" +
                                $"    명성치/{player.Fame,8}"));
        head.Children.Add(BlackLine($"  지  력/{Shows(Ability.Mind),4}" +
                                $"    악명치/{player.Infamy,8}"));
        head.Children.Add(BlackLine($"  무  력/{Shows(Ability.Might),4}" +
                                $"    직업  /{player.Work.Name}"));
        head.Children.Add(BlackLine($"  매  력/{Shows(Ability.Charm),4}"));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (Face(player, faces) is { } portrait) top.Children.Add(portrait);
        top.Children.Add(head);

        var rows = new StackPanel();
        rows.Children.Add(top);
        rows.Children.Add(Gap(14));
        rows.Children.Add(BlackLine($"  연령  /{player.Age,2}세          " +
                                $"생년월일/{player.BirthYear,4}년{player.BirthMonth,2}월{player.BirthDay,2}일"));
        rows.Children.Add(BlackLine($"  별자리/{GameUi.Pad(player.Zodiac, 14)}혈액형  /{player.BloodName}"));
        rows.Children.Add(BlackLine($"  국적  /{player.NationName}"));
        rows.Children.Add(BlackLine($"  소지금/{player.Gold,10}닢"));
        rows.Children.Add(BlackLine($"  저금  /{player.Savings,10}닢"));
        rows.Children.Add(BlackLine($"  빚    /{player.Debt,10}닢"));

        // 모드 「작위」 — 작위와 공적 한 줄, 그리고 혜택을 보는 단추.
        bool noble = Engine.Town.Nobility.Enabled && game != null;
        if (noble)
        {
            int merit = Engine.Town.Nobility.MeritOf(game!);
            int next = Engine.Town.Nobility.ToNext(player.NobleRank, merit);
            rows.Children.Add(BlackLine($"  작위  /{GameUi.Pad(Engine.Town.Nobility.NameOf(player.NobleRank), 8)}" +
                                        $"공적/{merit,5}" + (next > 0 ? $"  (다음까지 {next})" : "")));
        }

        var buttons = new List<GameButton> { new("특기", () => ShowSkills(player)) };
        if (noble) buttons.Add(new GameButton("작위", () => ShowNobility(this, game!)));
        buttons.Add(new GameButton("취소", Close));
        Build("", rows, BoardWidth, noble ? BoardHeight + 20 : BoardHeight, [.. buttons]);
    }

    /// <summary>
    /// 작위 혜택 — 작위마다 무엇이 붙는지, 지금 가진 것과 아직인 것을 늘어놓는다(모드 「작위」).
    /// </summary>
    /// <remarks>인물정보의 「작위」 단추와 도시 커맨드의 「작위」 줄이 같이 쓴다.</remarks>
    public static void ShowNobility(Window owner, Engine.Game game)
    {
        var player = game.Player;
        int merit = Engine.Town.Nobility.MeritOf(game);
        int next = Engine.Town.Nobility.ToNext(player.NobleRank, merit);
        var lines = new List<string>
        {
            // 조각마다 번역한다 — 이은 글 통째로는 번역표에 없다.
            Lang.T($"지금 작위: {Engine.Town.Nobility.NameOf(player.NobleRank)}   공적: {merit}")
                + (next > 0 ? Lang.T($"   다음 작위까지: {next}") : ""),
            Lang.T($"  (발견물 보고 {Engine.Town.Nobility.DiscoveryMeritOf(game)} · 교역소 투자 {Engine.Town.Nobility.InvestMeritOf(player.TotalInvested)}"
                + $", 투자 {player.TotalInvested}닢)"),
            "",
        };
        for (int rank = 1; rank <= Engine.Town.Nobility.MaxRank; rank++)
        {
            var perks = Engine.Town.Passives.All.Where(p => p.Rank == rank).ToList();
            string mark = player.NobleRank >= rank ? "●" : "○";
            string what = perks.Count > 0 ? string.Join(" · ", perks.Select(p => Lang.T(p.Name))) : "-";
            lines.Add($"{mark} {Lang.T(Engine.Town.Nobility.Names[rank])}({Engine.Town.Nobility.Thresholds[rank]}) : {what}");
        }
        NoticeDialog.Show(owner, string.Join(Environment.NewLine, lines), "작위");
    }

    /// <summary>
    /// 부하 하나의 판 — 술집 인물 판과 같은 줄이다(<c>0x0046DBC0</c>).
    /// </summary>
    /// <remarks>
    /// 명성치·악명치·생년월일·소지금·저금·빚만 제독 판에 있다(<c>[객체+4] == 0</c> 일 때만 그린다).
    /// 직업(<c>0x00570F28</c>) · 별자리/혈액형(<c>0x00570FA8</c> — 인물 객체의 가상 <c>+0x1C</c> · <c>+0x28</c>) ·
    /// 국적(<c>0x00570FC8</c>)은 누구에게나 그린다. 부하에게 「자리」 줄은 없다.
    /// </remarks>
    private PersonInfoDialog(Player.MateInfo who, in HireSheet sheet, Portraits? faces)
    {
        if (Local.Settings.GameSettings.Dh2PersonInfo)
        {
            bool woman = PersonTable.Open().People.Any(r => r.Female && r.Name == who.Name);
            string mateName = who.Name;
            BuildDh2(faces?.TryGetBgra(who.Face, woman), who.Name,
                [("연령", $"{who.Age}세"), ("체력", $"{Ability.Display(who.Body)}"), ("직업", sheet.Job),
                 ("국적", sheet.Nation), ("지력", $"{Ability.Display(who.Mind)}"), ("별자리", sheet.Zodiac),
                 ("", ""), ("무력", $"{Ability.Display(who.Might)}"), ("혈액형", sheet.Blood),
                 ("", ""), ("매력", $"{Ability.Display(who.Charm)}")],
                [], [("특기", () => ShowSkills(mateName)), ("취소", Close)]);
            return;
        }

        var head = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        head.Children.Add(BlackLine($"  {who.Name}"));
        // 부하도 같은 판이라 능력치는 담은 값 + 1 이다(0x0046D76C).
        head.Children.Add(BlackLine($"  체  력/{Ability.Display(who.Body),4}"));
        head.Children.Add(BlackLine($"  지  력/{Ability.Display(who.Mind),4}"));
        head.Children.Add(BlackLine($"  무  력/{Ability.Display(who.Might),4}    직업  /{sheet.Job}"));
        head.Children.Add(BlackLine($"  매  력/{Ability.Display(who.Charm),4}"));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        // 신상에는 이름과 얼굴 번호뿐이라 이름으로 인물을 되짚어 여자 얼굴인지 가른다.
        bool female = PersonTable.Open().People.Any(r => r.Female && r.Name == who.Name);
        if (Face(faces?.TryGetBgra(who.Face, female)) is { } portrait)
            top.Children.Add(portrait);
        top.Children.Add(head);

        var rows = new StackPanel();
        rows.Children.Add(top);
        rows.Children.Add(Gap(14));
        rows.Children.Add(BlackLine($"  연령  /{who.Age,2}세"));
        rows.Children.Add(BlackLine($"  별자리/{GameUi.Pad(sheet.Zodiac, 12)}혈액형  /{sheet.Blood}"));
        rows.Children.Add(BlackLine($"  국적  /{sheet.Nation}"));

        Build("", rows, BoardWidth, BoardHeight,
              new GameButton("특기", () => ShowSkills(who.Name)), new GameButton("취소", Close));
    }

    /// <summary>
    /// 술집에서 「부하로 고용한다」를 눌렀을 때의 판 — 단추가 <b>특기 · 결정 · 중단</b>이다.
    /// </summary>
    /// <remarks>
    /// 원본의 <c>0x0046DBC0(인물, 1)</c> 이다. 결정(id 0)만 참을 내고, 특기(id 2)는 특기 창을
    /// 띄운 뒤 이 판으로 돌아온다.
    /// </remarks>
    private PersonInfoDialog(in HireSheet who, uint[]? face)
    {
        if (Local.Settings.GameSettings.Dh2PersonInfo)
        {
            string hireName = who.Name;
            BuildDh2(face, who.Name,
                [("연령", $"{who.Age}세"), ("체력", $"{Ability.Display(who.Body)}"), ("직업", who.Job),
                 ("국적", who.Nation), ("지력", $"{Ability.Display(who.Mind)}"), ("별자리", who.Zodiac),
                 ("", ""), ("무력", $"{Ability.Display(who.Might)}"), ("혈액형", who.Blood),
                 ("", ""), ("매력", $"{Ability.Display(who.Charm)}")],
                [], [("특기", () => ShowSkills(hireName)), ("결정", () => { _decided = true; Close(); }), ("중단", Close)]);
            return;
        }

        var head = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        head.Children.Add(BlackLine($"  {who.Name}"));
        head.Children.Add(BlackLine($"  체  력/{Ability.Display(who.Body),4}"));
        head.Children.Add(BlackLine($"  지  력/{Ability.Display(who.Mind),4}"));
        head.Children.Add(BlackLine($"  무  력/{Ability.Display(who.Might),4}    직업  /{who.Job}"));
        head.Children.Add(BlackLine($"  매  력/{Ability.Display(who.Charm),4}"));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (Face(face) is { } portrait) top.Children.Add(portrait);
        top.Children.Add(head);

        var rows = new StackPanel();
        rows.Children.Add(top);
        rows.Children.Add(Gap(14));
        rows.Children.Add(BlackLine($"  연령  /{who.Age,2}세"));
        rows.Children.Add(BlackLine($"  별자리/{GameUi.Pad(who.Zodiac, 12)}혈액형  /{who.Blood}"));
        rows.Children.Add(BlackLine($"  국적  /{who.Nation}"));

        string name = who.Name;
        Build("", rows, BoardWidth, BoardHeight,
              new GameButton("특기", () => ShowSkills(name)),
              new GameButton("결정", () => { _decided = true; Close(); }),
              new GameButton("중단", Close));
    }

    private bool _decided;

    /// <summary>술집 인물 판에 적는 것.</summary>
    public readonly record struct HireSheet(string Name, int Body, int Mind, int Might, int Charm,
                                            int Age, string Job, string Zodiac, string Blood,
                                            string Nation);

    /// <summary>술집 인물 판을 열고 <b>결정</b>을 눌렀는지 낸다.</summary>
    public static bool AskHire(Window owner, in HireSheet who, uint[]? face)
    {
        var dialog = new PersonInfoDialog(who, face) { Owner = owner };
        dialog.ShowDialog();
        return dialog._decided;
    }

    /// <summary>
    /// 모드 「대항해시대2 스타일 인물정보」 — 대항해시대2 인물 판 모양으로 짓는다. 베이지 바탕에 줄무늬 테, 왼쪽 위에 테 없는
    /// 얼굴, 그 옆에 이름과 <b>항목 이름(빨강) · 값(검정)</b> 짝을 세 벌씩 늘어놓고, 밑에 덧붙일 줄과 왕관 꼴 단추 판을 둔다.
    /// </summary>
    /// <param name="cells">항목 이름 · 값 짝 — 세 짝이 한 줄이다. 이름이 비면 그 칸을 비워 둔다.</param>
    private void BuildDh2(uint[]? face, string name, (string Label, string Value)[] cells, IReadOnlyList<string> lines,
                          IReadOnlyList<(string Text, Action Run)> acts)
    {
        GameUi.GameLabel Text(string text, byte color) => new(color, GameUi.ItemTextHeight)
        {
            Text = text,
            Bold = false,
            FallbackBrush = color == Dh2Style.LabelColor ? Brushes.Firebrick : Brushes.Black,
            Margin = new Thickness(0, 1, 0, 1),
        };

        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        // 칸 폭은 밑값만 두고 글을 따라 넓힌다 — 96 으로 묶었더니 국적 「에스파니아 왕국」이 잘렸다(제보).
        for (int c = 0; c < 6; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = c % 2 == 0 || c == 5 ? 64 : 96 });
        int rowsNeeded = (cells.Length + 2) / 3;
        for (int r = 0; r < rowsNeeded; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].Label.Length == 0) continue;
            int row = i / 3, col = i % 3 * 2;
            var label = Text(cells[i].Label, Dh2Style.LabelColor);
            var value = Text(cells[i].Value, BlackInk);
            value.Margin = new Thickness(0, 1, 10, 1);   // 긴 값이 옆 칸 이름에 붙지 않게
            Grid.SetRow(label, row); Grid.SetColumn(label, col);
            Grid.SetRow(value, row); Grid.SetColumn(value, col + 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        var right = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(Text(name, BlackInk));
        right.Children.Add(grid);

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (face != null)
        {
            var bmp = Portraits.Bitmap(face);
            bmp.Freeze();
            var image = new Image { Source = bmp, Width = Portraits.Width, Height = Portraits.Height, VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapScalingMode(image, Portraits.Scaling(image.Source));
            top.Children.Add(image);
        }
        top.Children.Add(right);

        var body = new StackPanel();
        body.Children.Add(top);
        if (lines.Count > 0)
        {
            var more = new StackPanel { Margin = new Thickness(16, 12, 0, 0) };
            foreach (string line in lines) more.Children.Add(Text(line, BlackInk));
            body.Children.Add(more);
        }

        // 단추 판 — 마우스를 올린 판이 고른 모양(분홍)이 된다.
        var plaques = new List<FrameworkElement>();
        foreach (var (text, run) in acts)
        {
            var plaque = Dh2Style.Plaque(text, run, out var focused);
            plaque.MouseEnter += (_, _) => focused(true);
            plaque.MouseLeave += (_, _) => focused(false);
            plaques.Add(plaque);
        }
        var bar = Dh2Style.PlaqueBar(plaques);
        bar.HorizontalAlignment = HorizontalAlignment.Right;
        bar.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(bar);

        var paper = new Border { Background = Dh2Style.Paper, Padding = new Thickness(14, 12, 14, 12), Child = body };
        var frame = Dh2Style.Frame(paper);
        Background = Dh2Style.Paper;
        Content = frame;
        GameUi.EnableDrag(this, (FrameworkElement)frame);
        KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>이 판의 글 한 줄 — 검정 글씨다.</summary>
    private static GameUi.GameLabel BlackLine(string text) => Label(text, BlackInk);

    /// <summary>왼쪽 위 초상화. 얼굴을 못 읽었으면 안 세운다.</summary>
    private static UIElement? Face(Player player, Portraits? faces) =>
        // 서른여섯부터는 중년 얼굴로 바뀐다 — 다만 그 짝이 있을 때만이다
        // (PortraitAges). 더 넣은 얼굴처럼 짝이 없으면 젊은 얼굴 그대로 늙는다.
        Face(faces?.TryGetBgra(PortraitAges.At(player.Face, player.Age, false, faces),
                               female: false));

    /// <summary>이미 꺼내 둔 얼굴 점으로 초상화를 세운다.</summary>
    private static UIElement? Face(uint[]? px)
    {
        if (px == null) return null;

        var bmp = Portraits.Bitmap(px);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width * FaceScale,
            Height = Portraits.Height * FaceScale,
        };
        RenderOptions.SetBitmapScalingMode(image, Portraits.Scaling(image.Source));

        return new Border
        {
            BorderBrush = SteelEdge,
            BorderThickness = new Thickness(2),
            VerticalAlignment = VerticalAlignment.Top,
            Child = image,
        };
    }

    /// <summary>「특기」 — 기술 열셋과 어학 열넷을 두 칸으로 늘어놓는다.</summary>
    private void ShowSkills(Player player) => SkillSheetDialog.Show(this, player);

    /// <summary>부하의 「특기」. 인물 표에 그 사람이 없으면 못 찾았다고 이른다.</summary>
    private void ShowSkills(string name)
    {
        if (!SkillSheetDialog.Show(this, name))
            NoticeDialog.Show(this, $"{name}의 특기를 찾지 못했다", "인물정보");
    }

    /// <summary>인물정보 판을 연다.</summary>
    /// <param name="gameDirectory">초상화를 읽을 게임 폴더. 없으면 얼굴 없이 뜬다.</param>
    public static void Show(Window owner, Player player, string gameDirectory = "", Engine.Game? game = null)
    {
        var faces = Portraits.Open(gameDirectory);
        new PersonInfoDialog(player, faces, game) { Owner = owner }.ShowDialog();
    }

    /// <summary>부하 하나의 인물정보 판을 연다.</summary>
    /// <param name="role">그가 앉은 자리("부관" 따위). 판에 한 줄로 적는다.</param>
    /// <param name="sheet">직업·별자리·혈액형·국적 — 인물 밑표에서 온다(<see cref="Engine.GameInfo.SheetOf"/>).</param>
    public static void ShowMate(Window owner, Player.MateInfo who, in HireSheet sheet,
                                string gameDirectory = "")
    {
        var faces = Portraits.Open(gameDirectory);
        new PersonInfoDialog(who, sheet, faces) { Owner = owner }.ShowDialog();
    }
}
