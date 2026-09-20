using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 해전 판에서 배를 우클릭하면 뜨는 「해전전황정보(선박)」 — 아군·적 가리지 않는다.
/// </summary>
/// <remarks>
/// 창 이름은 <c>0x0056A388</c>, 줄은 <c>0x0056A258</c>~<c>0x0056A360</c> 이다(볼트 47 2절).
/// <code>
///   (오른쪽) 배 이름
///   선박종류 · 내구력 · 승원수 · 대포종류 · 대포수 · 이동력 · 추진력 · 적재량
/// </code>
/// 이름은 왼쪽, 값은 오른쪽으로 붙는다. 대포가 없으면 대포종류가 「없음」이다.
/// </remarks>
internal sealed class SeaShipInfoDialog : InfoDialog
{
    private const double BoardWidth = 280, RowHeight = 20;

    private SeaShipInfoDialog(SeaBattle.Ship ship)
    {
        var rows = new StackPanel();

        // 이름 칸은 <b>내 배만</b> 배 이름이다 — 적은 기함이면 「적기함」, 그 밖은 「적함」이다
        // (0x0043ECDA 의 0x0056B6C0 · 0x0056B6C8).
        var name = Label(ship.Mine ? ship.Name : ship.Flagship ? "적기함" : "적함");
        name.HorizontalAlignment = HorizontalAlignment.Right;
        rows.Children.Add(name);
        rows.Children.Add(Gap(8));

        string gun = ship.Gun >= 0 && Cannon.Of(ship.Gun) is { } cannon ? cannon.Name : "없음";
        Row(rows, "선박종류", ship.HullName);
        Row(rows, "내구력", $"{ship.Hp}");
        Row(rows, "승원수", $"{ship.Crew}");
        Row(rows, "대포종류", gun);
        Row(rows, "대포수", $"{ship.Guns}");
        Row(rows, "이동력", $"{ship.Power}");
        Row(rows, "추진력", $"{ship.Speed}");
        Row(rows, "적재량", $"{ship.Cargo}");

        Build("해전전황정보(선박)", rows, BoardWidth, RowHeight * 9 + 16);
    }

    private static void Row(StackPanel rows, string label, string value)
    {
        var line = new DockPanel { LastChildFill = false, Height = RowHeight };
        var left = Label(label);
        var right = Label(value);
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        line.Children.Add(left);
        line.Children.Add(right);
        rows.Children.Add(line);
    }

    /// <summary>배 한 척의 전황 정보를 띄운다.</summary>
    public static void Show(Window owner, SeaBattle.Ship ship) =>
        new SeaShipInfoDialog(ship) { Owner = owner }.ShowDialog();
}
