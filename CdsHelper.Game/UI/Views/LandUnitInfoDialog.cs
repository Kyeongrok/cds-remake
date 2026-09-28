using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Land;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 판에서 부대를 누르면 뜨는 부대 정보 창(<c>0x00446900</c>).
/// </summary>
/// <remarks>
/// 176x132 창을 <b>그 부대가 선 칸의 왼위</b>에 편다 — 자리표(<c>+0xB4</c>·<c>+0xB8</c>)에서 부대 자리
/// (<c>+0x0C</c>, 적이면 6 을 더한다)의 좌표를 그대로 쓴다. 제목은 병종 이름(<c>0x00444750</c> →
/// 이름표 <c>0x00549AB8</c>)이고, 속은 y 32 에 가로줄 하나를 긋고(<c>0x00444670</c>) 그 아래 (16, 40)에
/// 병종 설명(<c>0x00549B20</c>)을 찍는 것뿐이다. 병사수 같은 값은 안 낸다.
///
/// 창은 <b>안 멈춘다</b> — 차림표가 떠 있는 채로 열리고, 하나가 떠 있으면 다시 눌러도 안 연다
/// (<c>0x00446A83</c> 이 창의 보임 비트를 본다).
/// </remarks>
internal sealed class LandUnitInfoDialog : InfoDialog
{
    /// <summary>창 너비·높이(<c>0x0044691F</c> 의 <c>0xB0</c>·<c>0x84</c>).</summary>
    private const double BoardWidth = 176 - 28, BoardHeight = 132 - 12;

    public LandUnitInfoDialog(int kind)
    {
        var rows = new StackPanel();
        // 제목 아래 가로줄(0x004446AF — (8,32)~(168,32)).
        rows.Children.Add(new Border { Height = 1, Background = Ink, Margin = new Thickness(-6, 0, -6, 6) });
        string note = kind is >= 0 and < LandUnits.Count ? LandUnits.Notes[kind] : "";
        foreach (string line in note.Split('\n')) rows.Children.Add(Label(line));

        string name = kind is >= 0 and < LandUnits.Count ? LandUnits.Names[kind] : "";
        Build(name, rows, BoardWidth, BoardHeight);
    }
}
