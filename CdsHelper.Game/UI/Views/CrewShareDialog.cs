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
/// 창 짜임(<c>0x004AC339</c> ~ <c>0x004AC576</c>) — 너비 496, 높이 max(136, (배 수 + 5) * 16), <b>제목이 없다</b>
/// (<c>vt+0x70</c> 의 제목 인자 0). 글은 (8, 8) 머리글, 줄은 y 32 부터 16 씩, 칸은 게임 글자 칸(8점)이라
/// 선명 36 칸 = 288 · 「 %3d」 32 · 「   %3d」 48 · 48 이다. 올림·내림 화살표는 x 448 · 464.
/// 단추는 아래 띠에 <b>최적화(304, 64) · 결정(376, 48) · 중지(432, 48)</b> 차례로 선다(높이 24).
/// 모자란 배의 줄은 선명부터 최대까지 <b>한 줄 통째로</b> 색 0x38 로 찍는다(<c>0x004AC20E</c>, 여느 줄은 0x0A).
/// </remarks>
internal sealed class CrewShareDialog : InfoDialog
{
    /// <summary>칸 폭 — 게임 글자 칸(8점)으로 선명 36 · 「 %3d」 4 · 「   %3d」 6 · 6 칸이다(<c>0x0057B444</c>).</summary>
    private const double NameWidth = 288, CrewWidth = 32, NumberWidth = 48;

    /// <summary>숫자 끝(424)에서 첫 화살표(448)까지의 틈. 두 화살표는 붙어 선다(448 · 464).</summary>
    private const double ArrowLead = 24, ArrowGap = 0;

    /// <summary>필요 승원보다 적은 줄의 글자색(<c>0x004AC216</c>). 여느 줄은 <see cref="GameFont.WhiteColor"/>(0x0A)다.</summary>
    private const byte ShortColor = 0x38;

    /// <summary>창 너비 496 에 맞춘 판 둘레 — 글이 왼쪽 8 에서 시작한다.</summary>
    protected override Thickness BoardPad => new(8, 8, 8, 2);
    protected override Thickness ButtonPad => new(0, 0, 8, 8);

    private readonly Player _player;
    private readonly int[] _shares;
    private readonly GameUi.GameLabel[] _counts;
    private readonly GameUi.GameLabel[] _names;
    private readonly GameUi.GameLabel[] _needs;
    private readonly GameUi.GameLabel[] _maxes;
    private readonly GameUi.GameLabel _pool;
    private int _free;
    private bool _ok;

    private CrewShareDialog(Player player)
    {
        _player = player;
        _shares = [.. player.CrewShares];
        _counts = new GameUi.GameLabel[_shares.Length];
        _names = new GameUi.GameLabel[_shares.Length];
        _needs = new GameUi.GameLabel[_shares.Length];
        _maxes = new GameUi.GameLabel[_shares.Length];
        _pool = Label("");

        var rows = new StackPanel();
        // 머리글은 한 줄 글 그대로다(0x0053AB10) — 「명칭」 뒤 빈칸으로 36 칸을 채운다.
        rows.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Child = Label("명칭                                승무원 필요 최대"),
        });

        for (int i = 0; i < _shares.Length; i++)
        {
            int at = i;
            var ship = player.Ships[i];
            _names[i] = Label(ship.Name);
            _counts[i] = Label("");
            _needs[i] = Label($"   {Player.NeedCrewOf(ship),3}");
            _maxes[i] = Label($"   {Player.MaxCrewOf(ship),3}");
            var arrows = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(ArrowLead, 0, 0, 0) };
            arrows.Children.Add(Arrow(UiSprites.IconUp, () => Bump(at, +1)));
            arrows.Children.Add(Arrow(UiSprites.IconDown, () => Bump(at, -1)));
            rows.Children.Add(Row(_names[i], _counts[i], _needs[i], _maxes[i], arrows));
        }

        var poolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        poolRow.Children.Add(Label("배치되지 않은 승무원  "));
        poolRow.Children.Add(_pool);
        rows.Children.Add(poolRow);

        double height = 22 * (_shares.Length + 1) + 40;
        // 제목 없이, 단추는 원본 자리 차례대로 — 최적화 · 결정 · 중지(사이 8).
        static GameButton Band(string text, Action run, double width) =>
            new(text, run, width: width) { Margin = new Thickness(8, 0, 0, 0) };
        Build("", rows, 480, height,
              Band("최적화", Optimize, 64),
              Band("결정", Decide, 48),
              Band("중지", Close, 48));

        Sync();
    }

    private static UIElement Row(UIElement name, UIElement crew, UIElement need, UIElement max, UIElement? tail)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CrewWidth) });
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
            _counts[i].Text = $" {_shares[i],3}";
            // 필요 승원보다 적은 줄은 한 줄 통째로 색 0x38 이다(0x004AC20E) — 흐리게 하지 않는다.
            bool short_ = _shares[i] < Player.NeedCrewOf(_player.Ships[i]);
            byte color = short_ ? ShortColor : GameFont.WhiteColor;
            foreach (var label in (GameUi.GameLabel[])[_names[i], _counts[i], _needs[i], _maxes[i]])
                label.TextColor = color;
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
