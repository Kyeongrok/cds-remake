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
/// 도드라지는 조건은 <b>내 값이 적 값 이상</b>일 때다. (원본은 줄마다 견주는 차례가 서로
/// 뒤바뀌어 있어 뒤 세 줄은 거꾸로 걸릴 수 있다 — 불확실이라 한 가지로 맞춰 둔다.)
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

    private SeaBattleInfoDialog(int[] mine, int[] theirs)
    {
        var rows = new StackPanel();
        for (int i = 0; i < Labels.Length; i++)
        {
            // 마지막 줄(총함대수)은 도드라지지 않는다(0x004343B4 의 je).
            bool ahead = i < Labels.Length - 1 && mine[i] >= theirs[i];
            rows.Children.Add(Row(Labels[i], mine[i], theirs[i], ahead));
        }
        Build("해전전황정보(제독·함대수)", rows, BoardWidth, RowHeight * Labels.Length + 16);
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
    /// 그 판의 값을 모아 창을 띄운다. 적장을 모르면 적 쪽 능력은 0 이다.
    /// </summary>
    public static void Show(Window owner, SeaBattle battle, Player? player, Captain? leader)
    {
        int[] mine =
        [
            player?.AbilityOf(Ability.Might) ?? 0,
            player?.AbilityOf(Ability.Mind) ?? 0,
            player?.LevelOf("검술") ?? 0,
            player?.LevelOf("사격술") ?? 0,
            player?.LevelOf("포술") ?? 0,
            battle.Ships.Count(s => s.Mine),
        ];
        int[] theirs =
        [
            leader?.Might ?? 0,
            leader?.Mind ?? 0,
            leader?.Sword ?? 0,
            leader?.Shooting ?? 0,
            leader?.Gunnery ?? 0,
            battle.Ships.Count(s => !s.Mine),
        ];
        new SeaBattleInfoDialog(mine, theirs) { Owner = owner }.ShowDialog();
    }
}
