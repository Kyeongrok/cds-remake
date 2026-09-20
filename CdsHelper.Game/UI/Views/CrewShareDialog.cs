using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 바다 커맨드 「편성」 — 함대가 태운 선원을 배마다 몇 명씩 둘지 나눈다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004265C0</c> → <c>0x004AC310</c> 이다(볼트 84). 항구의 「함대편성」과는 다른 창이다.
/// <code>
///   머리   「명칭　　　　승무원 필요 최대」                    0x0053AB10
///   줄     %-36s %3d   %3d   %3d  — 필요보다 적으면 붉게       0x0057B444
///   아래   「배치되지 않은 승무원  %3d」                       0x0053AB48
///   올림   풀 ≥ 1 이고 승원 &lt; 최대  ·  내림  승원 &gt; 0
///   결정   풀이 남으면 「선원의 분배가 끝나지 않았습니다」     0x0053AB98
///   최적화 승원*100/필요 가 가장 작은 배에 한 명씩             0x004744F0
/// </code>
/// </remarks>
internal sealed class CrewShareDialog : InfoDialog
{
    private const double NameWidth = 220, NumberWidth = 44, ArrowGap = 2;

    private readonly Player _player;
    private readonly int[] _shares;
    private readonly GameUi.GameLabel[] _counts;
    private readonly GameUi.GameLabel[] _names;
    private readonly GameUi.GameLabel _pool;
    private int _free;
    private bool _ok;

    private CrewShareDialog(Player player)
    {
        _player = player;
        _shares = [.. player.CrewShares];
        _counts = new GameUi.GameLabel[_shares.Length];
        _names = new GameUi.GameLabel[_shares.Length];
        _pool = Label("");

        var rows = new StackPanel();
        rows.Children.Add(Row(Label("명칭"), Label("승무원"), Label("필요"), Label("최대"), null));

        for (int i = 0; i < _shares.Length; i++)
        {
            int at = i;
            var ship = player.Ships[i];
            _names[i] = Label(ship.Name);
            _counts[i] = Label("");
            var arrows = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            arrows.Children.Add(Arrow(UiSprites.IconUp, () => Bump(at, +1)));
            arrows.Children.Add(Arrow(UiSprites.IconDown, () => Bump(at, -1)));
            rows.Children.Add(Row(_names[i], _counts[i],
                                  Label($"{Player.NeedCrewOf(ship),3}"), Label($"{Player.MaxCrewOf(ship),3}"),
                                  arrows));
        }

        var poolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        poolRow.Children.Add(Label("배치되지 않은 승무원  "));
        poolRow.Children.Add(_pool);
        rows.Children.Add(poolRow);

        double height = 22 * (_shares.Length + 1) + 40;
        Build("편성", rows, NameWidth + NumberWidth * 3 + 60, height,
              new GameButton("결정", Decide, width: 64),
              new GameButton("중지", Close, width: 64),
              new GameButton("최적화", Optimize, width: 72));

        Sync();
    }

    private static UIElement Row(UIElement name, UIElement crew, UIElement need, UIElement max, UIElement? tail)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NumberWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NumberWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NumberWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Put(grid, name, 0);
        Put(grid, crew, 1);
        Put(grid, need, 2);
        Put(grid, max, 3);
        if (tail != null) Put(grid, tail, 4);
        return grid;
    }

    private static void Put(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private FrameworkElement Arrow(int icon, Action run)
    {
        FrameworkElement box = GameUi.GameIcon(icon) ?? (FrameworkElement)new TextBlock
        {
            Text = icon == UiSprites.IconUp ? "↑" : "↓",
            Foreground = Brushes.White,
        };
        box.Margin = new Thickness(ArrowGap, 0, 0, 0);
        box.Cursor = Cursors.Hand;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.MouseLeftButtonDown += (_, e) => e.Handled = true;
        box.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return box;
    }

    private void Bump(int at, int by)
    {
        var ship = _player.Ships[at];
        if (by > 0)
        {
            if (_free < 1 || _shares[at] >= Player.MaxCrewOf(ship)) return;
            _shares[at]++;
            _free--;
        }
        else
        {
            if (_shares[at] <= 0) return;
            _shares[at]--;
            _free++;
        }
        Sync();
    }

    private void Optimize()
    {
        var fresh = Player.OptimizeCrew(_player.Ships, _shares.Sum() + _free);
        for (int i = 0; i < _shares.Length; i++) _shares[i] = fresh[i];
        _free = 0;
        Sync();
    }

    private void Decide()
    {
        if (_free != 0)
        {
            NoticeDialog.Show(this, "선원의 분배가 끝나지 않았습니다");
            return;
        }
        _ok = _player.SetCrewShares(_shares);
        Close();
    }

    private void Sync()
    {
        for (int i = 0; i < _shares.Length; i++)
        {
            _counts[i].Text = $"{_shares[i],3}";
            // 필요 승원보다 적은 줄은 붉게(색 0x38) — 글꼴 색표 대신 줄 전체를 흐리게 표시한다.
            bool short_ = _shares[i] < Player.NeedCrewOf(_player.Ships[i]);
            _names[i].Opacity = short_ ? 0.6 : 1.0;
            _counts[i].Opacity = short_ ? 0.6 : 1.0;
        }
        _pool.Text = $"{_free,3}";
    }

    /// <summary>편성 창을 연다. 결정했으면 true.</summary>
    public static bool Show(Window owner, Player player)
    {
        if (player.Ships.Count == 0) return false;
        var dialog = new CrewShareDialog(player) { Owner = owner };
        dialog.ShowDialog();
        return dialog._ok;
    }
}
