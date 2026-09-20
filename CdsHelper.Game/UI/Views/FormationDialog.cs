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
/// 칸 안의 점 자리는 원본 표(<c>0x004FB418</c>·<c>0x004FB5D4</c>)를 아직 안 뜯어, 해전 배치에 쓰는
/// 호위 (ΔX, ΔY) 를 그대로 줄여 그린다.
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

            var inside = new Canvas { Width = CellWidth, Height = CellHeight };
            Dot(inside, CellWidth / 2, CellHeight / 2, flagship: true);
            var offsets = SeaBattle.Formations[formation];
            for (int k = 0; k < escorts; k++)
                Dot(inside, CellWidth / 2 + offsets[k].Dx * 10, CellHeight / 2 + offsets[k].Dy * 8, flagship: false);

            var cell = new Border
            {
                Width = CellWidth,
                Height = CellHeight,
                BorderThickness = new Thickness(2),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = inside,
            };
            cell.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(at); };
            cell.MouseLeftButtonDown += (_, e) => e.Handled = true;
            Canvas.SetLeft(cell, left);
            Canvas.SetTop(cell, top);
            board.Children.Add(cell);
            _cells[formation] = cell;
        }

        Build("함대배치도", board, BoardWidth, BoardHeight,
              new GameButton("실행", Run, width: 64),
              new GameButton("취소", Close, width: 64));

        KeyDown += (_, e) =>
        {
            int next = e.Key switch
            {
                Key.Left => _pick - 4,
                Key.Right => _pick + 4,
                Key.Up => _pick - 1,
                Key.Down => _pick + 1,
                _ => _pick,
            };
            if (next != _pick && next is >= 0 and < SeaBattle.FormationCount) Pick(next);
        };

        Sync();
    }

    private static void Dot(Canvas canvas, double x, double y, bool flagship)
    {
        var mark = new TextBlock
        {
            Text = flagship ? "●" : "○",
            Foreground = Brushes.White,
            FontSize = 12,
        };
        Canvas.SetLeft(mark, x - 6);
        Canvas.SetTop(mark, y - 8);
        canvas.Children.Add(mark);
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
