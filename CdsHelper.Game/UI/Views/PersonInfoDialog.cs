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
/// 게임의 <c>0x0046DF70</c> 이다. 줄 글은 그쪽 서식 그대로다(<c>0x00570EE8</c> 벌).
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
    /// 판 크기(그림 점). 게임 갈무리를 재어 맞췄다 — 판 바탕이 <b>430 x 270</b> 이고,
    /// 좌우 여백 14 씩과 아래 단추 줄을 빼면 속이 이만큼이다.
    /// </summary>
    private const double BoardWidth = 402, BoardHeight = 224;

    /// <summary>
    /// 초상화를 몇 배로 그릴지. 게임은 <b>조각 그대로</b> 80x96 이다 —
    /// 두 배로 걸면 얼굴만 커져 글자와 어긋난다.
    /// </summary>
    private const int FaceScale = 1;

    /// <summary>이 판은 글씨가 검정이다. 밤색 판들과 다른 자리다.</summary>
    private const byte BlackInk = GameFont.BlackColor;

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private PersonInfoDialog(Player player, Portraits? faces)
    {
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
        rows.Children.Add(BlackLine($"  별자리/{GameUi.Pad(player.Zodiac, 12)}혈액형  /{player.BloodName}"));
        rows.Children.Add(BlackLine($"  국적  /{player.NationName}"));
        rows.Children.Add(BlackLine($"  소지금/{player.Gold,10}닢"));
        rows.Children.Add(BlackLine($"  저금  /{player.Savings,10}닢"));
        rows.Children.Add(BlackLine($"  빚    /{player.Debt,10}닢"));

        Build("", rows, BoardWidth, BoardHeight,
              new GameButton("특기", () => ShowSkills(player)), new GameButton("취소", Close));
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
        var head = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        head.Children.Add(BlackLine($"  {who.Name}"));
        // 부하도 같은 판이라 능력치는 담은 값 + 1 이다(0x0046D76C).
        head.Children.Add(BlackLine($"  체  력/{Ability.Display(who.Body),4}"));
        head.Children.Add(BlackLine($"  지  력/{Ability.Display(who.Mind),4}"));
        head.Children.Add(BlackLine($"  무  력/{Ability.Display(who.Might),4}    직업  /{sheet.Job}"));
        head.Children.Add(BlackLine($"  매  력/{Ability.Display(who.Charm),4}"));

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        if (Face(faces?.TryGetBgra(who.Face, female: false)) is { } portrait)
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
    /// 게임의 <c>0x0046DBC0(인물, 1)</c> 이다. 결정(id 0)만 참을 내고, 특기(id 2)는 특기 창을
    /// 띄운 뒤 이 판으로 돌아온다.
    /// </remarks>
    private PersonInfoDialog(in HireSheet who, uint[]? face)
    {
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

        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96,
                                      PixelFormats.Bgra32, null, px, Portraits.Width * 4);
        bmp.Freeze();

        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width * FaceScale,
            Height = Portraits.Height * FaceScale,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);

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
    public static void Show(Window owner, Player player, string gameDirectory = "")
    {
        var faces = Portraits.Open(gameDirectory);
        new PersonInfoDialog(player, faces) { Owner = owner }.ShowDialog();
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
