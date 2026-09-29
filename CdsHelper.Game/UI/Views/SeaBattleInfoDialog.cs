using System.Windows;
using System.Windows.Controls;

using CdsHelper.Game.Engine.Sea;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 해전 중에 뜨는 「해전전황정보(제독·함대수)」(<c>0x00434430</c>, 제목 <c>0x0056A238</c>).
/// </summary>
/// <remarks>
/// 304x128 표에 <b>프레이어측 · 적측</b> 두 칸으로 여섯 줄을 낸다 — 무력 · 지력 · 검술 ·
/// 사격술 · 포술 · 총함대수(<c>0x0056A200</c>~<c>0x0056A228</c>).
/// <code>
///   00434349  줄 여섯을 16 픽셀씩 내려가며      0043434b  칸 둘을 0x98(152) 씩 옮겨가며
///   00434367  이름은 칸 왼쪽 +8                 004343a0  값은 칸 왼쪽 +0x78(120)
///   004343b6  앞 다섯 줄 · 첫 칸만 색 0x29 로 도드라진다(총함대수 줄은 안 도드라진다)
/// </code>
/// 값은 판의 싸움 값 그대로다(<c>0x0043F895</c> 가 <c>[+0x904]</c>~ 아군 · <c>[+0x924]</c>~ 적을 넘긴다) — 아군은
/// 제독·부관 가운데 큰 값이고 무력·지력은 능력+1 이다.
/// <b>도드라지는 것은 부관 값이 쓰인 줄</b>이다 — 적과 견주지 않고 판 값이 제독 제 값과 다른지를 본다(<c>0x004342C3</c>~).
/// <code>
///   무력   [0x005B60C8] + 1 − 판 무력  ≠ 0      지력   [0x005B60C4] + 1 − 판 지력 ≠ 0
///   검술   판 검술 − [0x005B60E8] ≠ 0          사격술 판 사격술 − [0x005B60F0] ≠ 0     포술 판 포술 − [0x005B60EC] ≠ 0
/// </code>
///
/// 뜨는 자리는 둘이다 — <c>PgUp</c>(<c>0x0043F895</c>)과, 괴물이 잠수한 판에서 적 칸을
/// 누를 때(<c>0x0043EBE2</c>)다.
/// </remarks>
internal sealed class SeaBattleInfoDialog : InfoDialog
{
    private const double BoardWidth = 304, RowHeight = 20, ColumnWidth = 140;

    /// <summary>줄 이름(<c>0x0056A200</c>~<c>0x0056A228</c>).</summary>
    private static readonly string[] Labels =
        ["무력", "지력", "검술", "사격술", "포술", "총함대수"];

    /// <summary>값 글자색 — 앞선 쪽은 0x29, 여느 때는 0x0A 다.</summary>
    private const byte AheadColor = 0x29, PlainColor = 0x0A;

    private SeaBattleInfoDialog(string admiral, string foe, int[] mine, int[] theirs, int[] own)
    {
        var rows = new StackPanel();
        // 맨 윗줄은 두 제독 이름이다 — 왼쪽 칸에 내 이름(0x005B60A0 가상 함수 0), 0x98 오른쪽에 적장 이름
        // (0x004319D0([+0x1008]) 가상 함수 0)을 「%s」로 찍고, 여섯 줄은 그 아래 +0x18 부터다(0x0043421C~0x00434363).
        var head = new StackPanel { Orientation = Orientation.Horizontal, Height = RowHeight };
        foreach (string who in new[] { admiral, foe })
        {
            var name = Label(who);
            name.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(new Grid { Width = ColumnWidth, Children = { name } });
        }
        rows.Children.Add(head);
        for (int i = 0; i < Labels.Length; i++)
        {
            // 마지막 줄(총함대수)은 도드라지지 않는다(0x004343B4 의 je).
            bool ahead = i < Labels.Length - 1 && mine[i] != own[i];
            rows.Children.Add(Row(Labels[i], mine[i], theirs[i], ahead));
        }
        Build("해전전황정보(제독·함대수)", rows, BoardWidth, RowHeight * (Labels.Length + 1) + 16);
    }

    /// <summary>한 줄 — 같은 이름을 칸마다 적고 그 오른쪽에 값을 놓는다.</summary>
    private static UIElement Row(string label, int mine, int theirs, bool ahead)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = RowHeight };
        row.Children.Add(Cell(label, mine, ahead));
        row.Children.Add(Cell(label, theirs, false));
        return row;
    }

    private static UIElement Cell(string label, int value, bool ahead)
    {
        var cell = new Grid { Width = ColumnWidth };
        var name = Label(label);
        name.HorizontalAlignment = HorizontalAlignment.Left;
        // 도드라지는 색은 0x29 고, 여느 값은 0x0A 다(0x004343CE · 0x004343FC).
        var number = Label($"{value}", ahead ? AheadColor : PlainColor);
        number.HorizontalAlignment = HorizontalAlignment.Right;
        cell.Children.Add(name);
        cell.Children.Add(number);
        return cell;
    }

    /// <summary>
    /// 그 판의 값을 모아 창을 띄운다 — 판의 싸움 값(<see cref="SeaBattle.MineSide"/> · <see cref="SeaBattle.EnemySide"/>)이다.
    /// </summary>
    public static void Show(Window owner, SeaBattle battle, Player? player, Captain? leader, string foeName = "")
    {
        var me = battle.MineSide;
        var them = battle.EnemySide;
        int[] mine = [me.Might, me.Mind, me.Sword, me.Shooting, me.Gunnery, battle.Ships.Count(s => s.Mine)];
        int[] theirs = [them.Might, them.Mind, them.Sword, them.Shooting, them.Gunnery, battle.Ships.Count(s => !s.Mine)];
        // 제독 제 값 — 이것과 다르면 부관 값이 쓰인 줄이라 도드라진다.
        int[] own =
        [
            (player?.AbilityOf(Ability.Might) ?? 0) + 1,
            (player?.AbilityOf(Ability.Mind) ?? 0) + 1,
            player?.LevelOf("검술") ?? 0,
            player?.LevelOf("사격술") ?? 0,
            player?.LevelOf("포술") ?? 0,
            0,
        ];
        new SeaBattleInfoDialog(player?.Name ?? "", foeName, mine, theirs, own) { Owner = owner }.ShowDialog();
    }
}
