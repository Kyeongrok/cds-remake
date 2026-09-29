using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 바다 커맨드 「대열」 — 「함대배치도」에서 해전에 들어설 때의 대열 여덟 가운데 하나를 고른다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x00433F30</c> 이다(볼트 84).
/// <code>
///   창      400 x 384 · 실행(0x0056A1D8) · 취소(0x0056A1E0)
///   칸 L    x (L/4)*192 + 12 ,  y (L%4)*80 + 0x28      ; 2열 x 4줄
///   기함    ●(0x0056A1C4) · 호위 ○(0x0056A1C0) — 배 수−1 척까지
///   실행    0x00475A50(L) → 함대 +0xDC
/// </code>
/// 점은 게임 글꼴의 「●」「○」 글자로 찍고, 자리는 원본 표를 그대로 옮겼다(<c>0x00433E8D</c> ~ <c>0x00433F1C</c>).
/// <code>
///   호위 k 번째 · 대열 L   0x004FB418 + (k*8 + L)*8 = (x, y)   → 「○」 를 (x+4, y+8) 에
///   기함 · 대열 L          x = (L/4)*192 + 100,  y = 0x004FB5D8[L] + 8  → 「●」
/// </code>
/// 좌표는 창 속 기준이라 판에 놓을 때 판의 원점(8, 32)을 뺀다.
/// </remarks>
internal sealed class FormationDialog : InfoDialog
{
    private const double BoardWidth = 384, BoardHeight = 344;
    private const double CellWidth = 180, CellHeight = 76;

    private readonly Border[] _cells = new Border[SeaBattle.FormationCount];
    private int _pick;
    private bool _ok;

    private FormationDialog(Player player)
    {
        _pick = player.Formation;
        int escorts = Math.Clamp(player.Ships.Count - 1, 0, 7);

        var board = new Canvas { Width = BoardWidth, Height = BoardHeight };
        for (int formation = 0; formation < SeaBattle.FormationCount; formation++)
        {
            int at = formation;
            double left = (formation / 4) * 192 + 12 - 8;
            double top = (formation % 4) * 80 + 0x28 - 32;

            var cell = new Border
            {
                Width = CellWidth,
                Height = CellHeight,
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(at); };
            cell.MouseLeftButtonDown += (_, e) => e.Handled = true;
            Canvas.SetLeft(cell, left);
            Canvas.SetTop(cell, top);
            board.Children.Add(cell);
            _cells[formation] = cell;
        }

        // 호위를 먼저, 기함을 나중에 찍는다 — 게임 차례 그대로다.
        for (int k = 0; k < escorts; k++)
            for (int formation = 0; formation < SeaBattle.FormationCount; formation++)
            {
                var (x, y) = EscortSpots[k * SeaBattle.FormationCount + formation];
                Dot(board, x + 4, y + 8, flagship: false);
            }
        for (int formation = 0; formation < SeaBattle.FormationCount; formation++)
            Dot(board, (formation / 4) * 192 + 100, FlagshipY[formation] + 8, flagship: true);

        Build("함대배치도", board, BoardWidth, BoardHeight,
              new GameButton("실행", Run, width: 64),
              new GameButton("취소", Close, width: 64));

        // 글쇠(0x00433AD0) — 위·아래(0x4800·0x5000, 키패드 8·2)는 한 열 네 줄 안에서 돌아 감기고,
        // 왼·오른쪽(0x4B00·0x4D00, 키패드 4·6)은 두 열을 오간다. Enter·Space 는 실행(+0x9C = 1),
        // Esc 는 취소다. 판 기본 처리(Enter = 닫기)보다 먼저 잡는다.
        PreviewKeyDown += (_, e) =>
        {
            int row = _pick % 4;
            int? next = e.Key switch
            {
                Key.Up or Key.NumPad8 => row == 0 ? _pick + 3 : _pick - 1,
                Key.Down or Key.NumPad2 => row == 3 ? _pick - 3 : _pick + 1,
                Key.Left or Key.Right or Key.NumPad4 or Key.NumPad6 => _pick < 4 ? _pick + 4 : _pick - 4,
                _ => null,
            };
            if (next is { } to)
            {
                e.Handled = true;
                Pick(to);
                return;
            }
            if (e.Key is Key.Enter or Key.Space) { e.Handled = true; Run(); }
            else if (e.Key is Key.Escape) { e.Handled = true; Close(); }
        };

        Sync();
    }

    /// <summary>호위 점 자리(<c>0x004FB418</c>) — 호위 일곱 x 대열 여덟, 창 속 좌표.</summary>
    private static readonly (int X, int Y)[] EscortSpots =
    [
        (80, 56), (96, 120), (96, 200), (80, 280), (272, 88), (304, 136), (272, 216), (288, 280),
        (112, 56), (80, 136), (112, 216), (112, 280), (304, 72), (272, 168), (304, 248), (272, 312),
        (64, 56), (112, 136), (80, 216), (64, 312), (256, 72), (320, 136), (256, 216), (304, 312),
        (128, 56), (64, 152), (128, 232), (128, 312), (320, 56), (256, 168), (320, 248), (256, 296),
        (48, 56), (128, 152), (64, 232), (48, 296), (240, 56), (336, 136), (240, 216), (320, 296),
        (144, 56), (80, 168), (144, 248), (144, 296), (336, 40), (240, 168), (336, 248), (256, 328),
        (32, 56), (112, 168), (48, 248), (96, 328), (224, 40), (352, 136), (224, 216), (320, 328),
    ];

    /// <summary>기함 점의 y(<c>0x004FB5D8</c>) — 대열 여덟, 창 속 좌표.</summary>
    private static readonly int[] FlagshipY = [56, 152, 232, 296, 88, 152, 232, 312];

    /// <summary>판의 원점이 창 속에서 떨어진 만큼 — 칸 자리(<c>(L/4)*192 + 12</c>, <c>(L%4)*80 + 0x28</c>)에서 뺀 값과 같다.</summary>
    private const double OriginX = 8, OriginY = 32;

    /// <summary>「●」(<c>0x0056A1C4</c>) · 「○」(<c>0x0056A1C0</c>)를 게임 글꼴로 창 속 (x, y) 에 찍는다.</summary>
    private static void Dot(Canvas board, double x, double y, bool flagship)
    {
        var mark = new GameUi.GameLabel(Local.Helpers.GameFont.WhiteColor)
        {
            Text = flagship ? "●" : "○",
            Bold = false,
            FallbackBrush = Brushes.White,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(mark, x - OriginX);
        Canvas.SetTop(mark, y - OriginY);
        board.Children.Add(mark);
    }

    private void Pick(int formation)
    {
        _pick = formation;
        Sync();
    }

    private void Sync()
    {
        for (int i = 0; i < _cells.Length; i++)
            _cells[i].BorderBrush = i == _pick ? Brushes.Gold : Brushes.Transparent;
    }

    private void Run()
    {
        _ok = true;
        Close();
    }

    /// <summary>함대배치도를 연다. 실행했으면 대열을 적고 true.</summary>
    public static bool Show(Window owner, Player player)
    {
        var dialog = new FormationDialog(player) { Owner = owner };
        dialog.ShowDialog();
        if (dialog._ok) player.SetFormation(dialog._pick);
        return dialog._ok;
    }
}
