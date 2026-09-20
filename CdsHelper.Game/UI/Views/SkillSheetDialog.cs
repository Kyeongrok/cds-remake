using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물정보의 「특기」 — 기술 열셋과 어학 열넷을 <b>두 칸으로</b> 늘어놓는다.
/// </summary>
/// <remarks>
/// 게임 화면 그대로다. 인물정보와 같은 강청색 판이고, 묶음마다 머리에
/// <c>━━━━━━━━기술━━━━━━━━</c> 을 두른다. 줄은 <b>왼쪽 칸을 다 채우고 오른쪽 칸으로</b>
/// 넘어간다 — 기술은 일곱·여섯, 어학은 일곱·일곱이다.
///
/// 예전에는 힌트 일람 창을 빌려 한 줄로 스물일곱 개를 세로로 쌓았는데, 게임은 두 칸이라
/// 모양이 아주 달랐다.
/// </remarks>
internal sealed class SkillSheetDialog : InfoDialog
{
    /// <summary>
    /// 판 크기. 폭은 두 칸 줄(44칸 = 352점)에 맞춘다 — 게임 갈무리는 오른쪽 여백이 왼쪽만큼만
    /// 남는다. 424 로 두었더니 값 칸 오른쪽이 휑하게 비었다.
    /// </summary>
    private const double BoardWidth = 356, BoardHeight = 322;

    /// <summary>
    /// 이름을 채우는 칸 수와 자릿수. 이름이 길어도 값이 세로로 맞게 못 박는다.
    /// </summary>
    /// <remarks>
    /// 가장 긴 이름이 「동남아시아토착어」 여덟 자, 곧 <b>열여섯 칸</b>이다. 스물까지
    /// 채우면 오른쪽이 휑하게 남아 열여덟로 줄였다 — 이름 뒤에 두 칸이 남는다.
    /// </remarks>
    private const int NameCells = 18, LevelCells = 3;

    /// <summary>두 칸 사이 틈.</summary>
    private const int GapCells = 2;

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    /// <param name="skillAt">기술 번호로 익힘새를 내는 손.</param>
    /// <param name="tongueAt">어학 번호로 익힘새를 내는 손.</param>
    private SkillSheetDialog(Func<int, int> skillAt, Func<int, int> tongueAt)
    {
        var rows = new StackPanel();
        rows.Children.Add(Divider("기술", GameFont.BlackColor));
        rows.Children.Add(Gap(8));
        AddPairs(rows, Skill.Names, skillAt);

        rows.Children.Add(Gap(22));
        rows.Children.Add(Divider("어학", GameFont.BlackColor));
        rows.Children.Add(Gap(8));
        AddPairs(rows, Skill.Languages, tongueAt);

        Build("", rows, BoardWidth, BoardHeight, new GameButton("취소", Close));
    }

    /// <summary>
    /// 목록을 반으로 갈라 왼쪽·오른쪽 칸에 나란히 적는다. 홀수면 왼쪽이 하나 더 갖는다 —
    /// 게임도 기술 열셋을 일곱·여섯으로 가른다.
    /// </summary>
    private static void AddPairs(StackPanel rows, IReadOnlyList<string> names,
                                 Func<int, int> levelAt)
    {
        int half = (names.Count + 1) / 2;
        for (int i = 0; i < half; i++)
        {
            string line = Cell(names[i], levelAt(i)) + new string(' ', GapCells);
            int right = i + half;
            if (right < names.Count) line += Cell(names[right], levelAt(right));
            rows.Children.Add(Label(line, GameFont.BlackColor));
        }
    }

    /// <summary>칸 하나 — 이름을 채우고 값을 오른쪽에 붙인다.</summary>
    private static string Cell(string name, int level) =>
        $"{GameUi.Pad(name, NameCells)}{level,LevelCells}";

    /// <summary>제독의 특기 판을 연다.</summary>
    public static void Show(Window owner, Player player) =>
        new SkillSheetDialog(i => player.LevelOf(Skill.Names[i]),
                             i => player.TongueOf(Skill.Languages[i]))
        { Owner = owner }.ShowDialog();

    /// <summary>
    /// 부하의 특기 판을 연다. 그 이름이 인물 표에 없으면 안 연다.
    /// </summary>
    /// <remarks>
    /// 부하 신상(<see cref="Player.MateInfo"/>)에는 검술·사격술·포술 셋만 베껴 두었다 —
    /// 싸움에 드는 것만 쓰면 되기 때문이다. 특기 판은 열셋과 열넷을 다 내야 하므로
    /// 인물 표에서 그 사람 줄을 찾아 그대로 편다.
    /// </remarks>
    public static bool Show(Window owner, string name)
    {
        var row = PersonTable.Open().People.FirstOrDefault(r => r.Name == name);
        if (row == null) return false;

        new SkillSheetDialog(i => At(row.Skills, i), i => At(row.Languages, i))
        { Owner = owner }.ShowDialog();
        return true;
    }

    /// <summary>표에 든 익힘새 하나. 칸이 모자라면 0.</summary>
    private static int At(int[] levels, int i) => i >= 0 && i < levels.Length ? levels[i] : 0;
}
