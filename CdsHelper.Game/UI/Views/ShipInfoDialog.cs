using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「선박정보」 — 배 한 척의 판. 함대정보에서 이름 단추를 누르면 열린다.
/// </summary>
/// <remarks>
/// 함대정보(<see cref="FleetInfoDialog"/>)에는 이름만 서고 자세한 것은 여기서 본다 —
/// 게임도 목록이 먼저고 배가 그 다음이다. 판 색은 함대정보와 같은 강청색이다.
/// </remarks>
internal sealed class ShipInfoDialog : InfoDialog
{
    /// <summary>판 크기. 함대정보 판과 폭을 맞췄다.</summary>
    private const double BoardWidth = 375, BoardHeight = 190;

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private ShipInfoDialog(Player player, int at, ItemTable? items)
    {
        var ship = player.Ships[at];
        var rows = new StackPanel();

        rows.Children.Add(Label($"  {ship.Name}호{(at == player.Flagship ? "  ★기함" : "")}"));
        rows.Children.Add(Gap(6));
        rows.Children.Add(Label($"  선종  {ship.Hull.Name}"));
        rows.Children.Add(Label($"  내구  {ship.Hp,4}/{ship.MaxHp,-4}추진  {ship.Speed,4}"));
        rows.Children.Add(Label($"  승원  {ship.Crew,4}    적재  {ship.Capacity,4}"));
        rows.Children.Add(Label($"  포    {ship.Guns,2}/{ship.Turrets,-2}    " +
                                $"{Cannon.Of(ship.Gun)?.Name ?? "—"}"));
        rows.Children.Add(Gap(6));
        rows.Children.Add(Label($"  돛    {Sails(ship)}"));
        rows.Children.Add(Label($"  선수상 {Carved(ship, items)}"));

        Build("선박정보", rows, BoardWidth, BoardHeight, new GameButton("취소", Close));
    }

    /// <summary>
    /// 뱃머리에 단 선수상. 안 달았으면 "—" 다.
    /// </summary>
    /// <remarks>
    /// 무엇을 막아 주는지도 함께 낸다 — 조각마다 막는 재앙이 다르고
    /// (<see cref="Figureheads.GuardOf"/>) 그것이 곧 이 조각을 다는 뜻이다.
    /// </remarks>
    private static string Carved(Ship ship, ItemTable? items)
    {
        int carved = ship.Figurehead;
        if (!Figureheads.Known(carved)) return "—";

        string name = items?.Find(Figureheads.ToItem(carved))?.Name ?? $"선수상 {carved}";
        string guards = Figureheads.GuardOf(carved) switch
        {
            Figureheads.GuardsRats => "쥐",
            Figureheads.GuardsSickness => "병",
            Figureheads.GuardsMutiny => "반란",
            _ => "폭풍",
        };
        int block = Figureheads.BlockPercent(carved);
        return block > 0 ? $"{name}  ({guards} {block}%)" : $"{name}  (저주)";
    }

    /// <summary>마스트에 달린 돛을 한 줄로. 안 선 자리는 안 적는다.</summary>
    private static string Sails(Ship ship)
    {
        var parts = new List<string>();
        for (int i = 0; i < Ship.MastSlots; i++)
            if (ship.Sails[i] != Ship.NoSail)
                parts.Add($"{Ship.MastNames[i]} {Ship.SailNames[ship.Sails[i]]}");
        return parts.Count == 0 ? "마스트 없음" : string.Join(" · ", parts);
    }

    /// <summary>그 배의 판을 연다.</summary>
    /// <param name="items">아이템 표. 선수상 이름을 여기서 낸다 — 없으면 번호로 물러선다.</param>
    public static void Show(Window owner, Player player, int at, ItemTable? items = null)
    {
        if (at < 0 || at >= player.Ships.Count) return;
        new ShipInfoDialog(player, at, items) { Owner = owner }.ShowDialog();
    }
}
