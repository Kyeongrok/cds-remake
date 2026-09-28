using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 「기능명령 → 정보」가 여는 「육상전 전황정보」 창(<c>0x004469C0</c>, 제목 <c>0x0056CB88</c>).
/// </summary>
/// <remarks>
/// 248x196 창을 화면 가운데(8 점에 맞춰 내린 자리)에 편다. 속(<c>0x004447B0</c>)은 이렇다.
/// <code>
///   004447F0  가로줄 (8,32)~(239,32)          0044481A  세로줄 (124,32)~(124,187)
///   00444838  칸 둘 — 왼쪽 x 16 이 아군, 오른쪽 x 136 이 적(ebp 0 · 6)
///   줄 여섯   y 38 부터 24 씩 — 무력 · 지력 · 검술 · 사격술 · 포술 · 병사수(0x0056CAD0~0x0056CB20)
/// </code>
/// 값은 싸움이 쓰는 그대로다 — 무력·지력은 <c>0x00446FF0</c>(능력 + 1), 기능은 <c>0x00446F70</c>,
/// 병사수는 살아 있는 부대의 합(<c>0x004474E0</c>)이다. 「아군부대 적부대」 머리글(<c>0x0056CB98</c>)은
/// 참조가 없어 안 찍는다.
///
/// 창은 안 멈춘다 — 떠 있는 동안에는 기능명령의 「정보」 줄이 죽는다(<c>0x0044920A</c>).
/// </remarks>
internal sealed class LandBattleInfoDialog : InfoDialog
{
    /// <summary>창 너비·높이(<c>0x004469CD</c> 의 <c>0xF8</c>·<c>0xC4</c>).</summary>
    private const double BoardWidth = 248 - 28, BoardHeight = 196 - 12;

    /// <summary>줄 사이(<c>0x62B2D4</c> 에 24 씩 더한다).</summary>
    private const double RowHeight = 24;

    public LandBattleInfoDialog(LandBattle battle)
    {
        var columns = new Grid { Margin = new Thickness(-6, 0, -6, 0) };
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        columns.ColumnDefinitions.Add(new ColumnDefinition());

        var mine = Column(battle, 0);
        var foe = Column(battle, LandBattle.FirstFoe);
        // 세로줄(0x0044481A) — 가로줄 아래로 판 끝까지 내려간다.
        var split = new Border { Width = 1, Background = Ink };
        Grid.SetColumn(split, 1);
        Grid.SetColumn(foe, 2);
        columns.Children.Add(mine);
        columns.Children.Add(split);
        columns.Children.Add(foe);

        var body = new StackPanel();
        // 제목 아래 가로줄(0x004447F0).
        body.Children.Add(new Border { Height = 1, Background = Ink, Margin = new Thickness(-6, 0, -6, 0) });
        body.Children.Add(columns);
        Build("육상전 전황정보", body, BoardWidth, BoardHeight);
    }

    /// <summary>한 편의 여섯 줄. 글 모양은 원본 서식 그대로다 — 이름 뒤 칸 수로 값 자리를 맞춘다.</summary>
    private static StackPanel Column(LandBattle battle, int slot)
    {
        bool foe = slot >= LandBattle.FirstFoe;
        string[] lines =
        [
            $"무력    {battle.MightAt(slot),4}",
            $"지력    {battle.MindAt(slot),4}",
            $"검술      {battle.SkillAt(slot, Skill.Sword),2}",
            $"사격술    {battle.SkillAt(slot, Skill.Shooting),2}",
            $"포술      {battle.SkillAt(slot, Skill.Gunnery),2}",
            $"병사수  {battle.MenOn(foe),4}",
        ];

        // 첫 줄이 가로줄에서 6 점 아래(y 38), 칸 왼쪽에서 8 점 안(x 16 · 136)이다.
        var column = new StackPanel { Margin = new Thickness(8, 6 - (RowHeight - 16) / 2, 0, 0) };
        foreach (string line in lines)
        {
            var label = Label(line);
            label.Height = RowHeight;
            column.Children.Add(label);
        }
        return column;
    }
}
