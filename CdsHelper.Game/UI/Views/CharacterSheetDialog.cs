using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// NEW GAME 마지막 걸음 — 지금까지 정한 것을 한 번 더 보여 주고 "결정" 을 받는다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045E260</c> 이다. 화면 글은 신상 걸음의 서식을 그대로 쓴다.
/// <code>
///   0x00571B08  "%s·%s"                                  ; 명·성
///   0x00571B10  "%2d월%2d일생(%2d세)  %-6s  %s형"
///   0x00571B30  "국적:%-10s  직업:%-8s"
/// </code>
/// "결정" 을 누르면 새 놀이가 시작되고, 고른 국적의 <b>자택이 열린다</b> —
/// 포르투갈이면 리스본, 에스파니아면 세빌리아다.
/// </remarks>
internal sealed class CharacterSheetDialog : InfoDialog
{
    /// <summary>게임 갈무리에 이 화면들에는 닫기(X)가 없다.</summary>
    protected override bool ShowClose => false;

    /// <summary>판과 단추 줄의 여백. 게임 것이 훨씬 촘촘하다.</summary>
    protected override Thickness BoardPad => new(8, 6, 8, 2);

    protected override Thickness ButtonPad => new(0, 2, 8, 6);

    /// <summary>아래 단추의 폭과 사이. 게임 것은 마구리 둘에 가운데 두 칸(48)이다.</summary>
    private const double FootWidth = 48;

    private static readonly Thickness FootGap = new(3, 0, 0, 0);

    /// <summary>
    /// 판 크기(그림 점). 잰 값이 <b>1.75배로 늘어난 화면</b>에서 나온 것이라 도로 나눴다.
    /// </summary>
    private const double BoardWidth = 300, BoardHeight = 172;

    /// <summary>기술 칸의 양피지 바탕. 게임 화면에서 뽑았다.</summary>
    private static readonly Brush PageFill = Frozen(Color.FromRgb(0xFF, 0xEF, 0xD6));

    /// <summary>기술 칸에 비워 두는 높이.</summary>
    private const double ListHeight = 90;

    private bool _ok;

    private CharacterSheetDialog(Player player)
    {
        var rows = new StackPanel();
        rows.Children.Add(Label($"  {player.Name}"));
        rows.Children.Add(Label($"   {player.BirthMonth,2}월{player.BirthDay,2}일생" +
                                $"({player.Age,2}세)   {player.Zodiac}  {player.BloodName}형"));
        rows.Children.Add(Label($"  국적: {GameUi.Pad(player.NationName, 16)}직업: {player.Work.Name}"));
        rows.Children.Add(Gap(6));

        // 왼쪽에 능력치 다섯, 오른쪽에 기술·언어 목록.
        var left = new StackPanel { Width = 108 };
        for (int i = 0; i < Ability.Shown; i++)
            left.Children.Add(Label($"  {GameUi.Pad(Ability.Names[i], 8)}{Ability.Display(player.AbilityOf(i)),3}"));

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(left);
        body.Children.Add(new Border { Width = 184, Child = List(Learned(player), ListHeight, PageFill) });
        rows.Children.Add(body);

        Build("", rows, BoardWidth, BoardHeight,
              new GameButton("취소", Close, width: FootWidth) { Margin = FootGap }, new GameButton("결정", Decide, width: FootWidth) { Margin = FootGap });
    }

    /// <summary>배운 기술과 언어를 한 줄씩. 자리가 0 인 것도 게임처럼 다 적는다.</summary>
    private static IEnumerable<string> Learned(Player player)
    {
        foreach (string name in Skill.Names)
            yield return $"{GameUi.Pad(name, 18)}{player.LevelOf(name),2}";
        foreach (string name in Skill.Languages)
            yield return $"{GameUi.Pad(name, 18)}{player.TongueOf(name),2}";
    }

    private void Decide()
    {
        _ok = true;
        Close();
    }

    /// <summary>확인 판을 띄운다. "결정" 을 누르면 true.</summary>
    public static bool Show(Window owner, Player player)
    {
        var dialog = new CharacterSheetDialog(player) { Owner = owner };
        dialog.ShowDialog();
        return dialog._ok;
    }
}
