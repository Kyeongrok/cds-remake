using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 배 한 척을 고르는 표 — 「개조선박의 선택」 같은 창. <b>머리글을 누르면 칸 묶음이 바뀐다.</b>
/// </summary>
/// <remarks>
/// 원본 배 목록 창(<c>0x0046C3E0</c> → <c>0x0046BBE0</c>)은 칸 표 <c>0x00560D68</c>(칸마다 머리글 · 서식 · 폭)와
/// 묶음 표 <c>0x00560E10</c>(묶음마다 칸 번호 열넷, −1 로 끝)를 쓴다.
/// <code>
///   칸   0 선명 %99s · 1 승원수 %3d/%3d · 2 내구력 %3d/%3d · 3 중량 %5d · 4 용적 %5d · 5 선체타입 %12s
///        6 선두상 %6s · 7 추진력 %3d/%3d · 8 대포명  포문수 %12s %2d/%2d문 · 9 대포수 %6d · 10 포탑수 %6d
///        11 소유 %4s · 12 돛종류 %6s · 13 함대 %4s
///   묶음 0 선명 승원수 내구력 중량 용적 함대
///        1 선명 추진력 대포명·포문수 돛종류
///        2 선명 선체타입 추진력 내구력 선두상
///        3 선명 선체타입 내구력 중량 용적 소유
///        4 선명 대포명·포문수 선두상 소유 함대
/// </code>
/// 머리글을 누르면 <c>0x0046C300</c> 이 묶음을 하나 넘긴다 — 인자가 0 이면 +1, 아니면 −1, 0~4 에서 돈다.
/// 지금 묶음은 <b>부른 쪽이 넘긴 칸</b>에 적혀 창을 닫아도 남는다 — 개조는 <c>0x0056E290</c>(처음 1),
/// 수리 <c>0x00549D60</c>(2), 기함 변경 <c>0x005602C8</c>(3), 선박삭제·파기 4 따위다.
/// 우리는 좌클릭을 +1, 우클릭을 −1 로 둔다(어느 단추가 어느 쪽인지는 원본 인자를 끝까지 못 따랐다).
///
/// 선수상 칸 머리글은 EXE 글이 「선두상」이지만 화면에 「선수상」으로 보여 그렇게 적는다
/// (<see cref="ShipyardMenu"/> 의 선수상 목록과 같은 까닭).
/// </remarks>
public sealed class ShipPickDialog : GameWindow
{
    /// <summary>칸 — 머리글과 폭(점), 숫자인지.</summary>
    private readonly record struct Column(string Head, double Width, bool Number);

    private static readonly Column[] Columns =
    [
        new("선명", 104, false), new("승원수", 72, true), new("내구력", 72, true), new("중량", 56, true),
        new("용적", 56, true), new("선체타입", 100, true), new("선수상", 88, false), new("추진력", 72, true),
        new("대포명  포문수", 176, false), new("대포수", 60, true), new("포탑수", 60, true), new("소유", 48, false),
        new("돛종류", 64, false), new("함대", 48, false),
    ];

    /// <summary>묶음 표(<c>0x00560E10</c>).</summary>
    private static readonly int[][] Sets =
    [
        [0, 1, 2, 3, 4, 13],
        [0, 7, 8, 12],
        [0, 5, 7, 2, 6],
        [0, 5, 2, 3, 4, 11],
        [0, 8, 6, 11, 13],
    ];

    /// <summary>창마다 지금 묶음 — 게임처럼 닫아도 남는다. 열쇠는 창 제목이다.</summary>
    private static readonly Dictionary<string, int> Remembered = [];

    private static readonly Brush HeadFill = Frozen(Color.FromRgb(0xDE, 0xC6, 0xAD));
    private static readonly Brush RowFill = Frozen(Color.FromRgb(0xFF, 0xEF, 0xD6));
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0xC4, 0xA8, 0x8C));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private readonly Player _player;
    private readonly ItemTable? _items;
    private readonly string _title;
    private readonly StackPanel _table = new();
    private readonly GameButton _decide;
    private readonly List<Border> _rows = [];

    private int _picked = -1;

    /// <summary>고른 배 번호. 안 골랐으면 −1.</summary>
    public int Chosen { get; private set; } = -1;

    private ShipPickDialog(Player player, ItemTable? items, string title)
    {
        _player = player;
        _items = items;
        _title = title;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _decide = new GameButton("결정", Decide) { On = false };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 8),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("중단", Close));

        var head = GameUi.TitleBar(title, Close);
        GameUi.EnableDrag(this, head);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(new Border { Margin = new Thickness(8, 4, 8, 0), Child = _table });
        stack.Children.Add(buttons);
        Content = GameUi.DialogEdge(stack);

        Paint();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    private int Set => Remembered.TryGetValue(_title, out int set) ? set : 0;

    /// <summary>묶음을 하나 넘긴다(<c>0x0046C300</c>) — 0~4 에서 돈다.</summary>
    private void Turn(int by)
    {
        Remembered[_title] = ((Set + by) % Sets.Length + Sets.Length) % Sets.Length;
        Paint();
    }

    private void Paint()
    {
        _table.Children.Clear();
        _rows.Clear();
        var set = Sets[Set];

        var header = Row(set, set.Select(c => Columns[c].Head).ToArray(), header: true);
        header.Cursor = Cursors.Hand;
        header.MouseLeftButtonUp += (_, e) => { e.Handled = true; Turn(+1); };
        header.MouseRightButtonUp += (_, e) => { e.Handled = true; Turn(-1); };
        _table.Children.Add(header);

        for (int i = 0; i < _player.Ships.Count; i++)
        {
            int at = i;
            var row = Row(set, set.Select(c => CellOf(c, at)).ToArray(), header: false);
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Pick(at); };
            if (at == _picked) row.Background = PickFill;
            _rows.Add(row);
            _table.Children.Add(row);
        }
    }

    /// <summary>칸 하나의 글 — 서식은 칸 표(<c>0x00560D68</c>) 그대로다.</summary>
    private string CellOf(int column, int at)
    {
        var ship = _player.Ships[at];
        return column switch
        {
            0 => ship.Name,
            1 => $"{_player.CrewShares.ElementAtOrDefault(at),3}/{ship.Crew,3}",
            2 => $"{ship.Hp,3}/{ship.MaxHp,3}",
            3 => $"{ship.Tonnage,5}",
            4 => $"{ship.UsableCapacity,5}",
            5 => ship.Hull.Name,
            6 => ship.Figurehead >= 0
                ? _items?.Find(Figureheads.ToItem(ship.Figurehead))?.Name ?? $"선수상 {ship.Figurehead}"
                : "---",
            7 => $"{ship.MaxSpeed,3}/{ship.Hull.SpeedCeiling,3}",
            8 => ship.Gun >= 0 && ship.Gun < Cannon.Count
                ? $"{Cannon.All[ship.Gun].Name} {ship.Guns,2}/{ship.Turrets,2}문"
                : "",
            9 => $"{ship.Guns,6}",
            10 => $"{ship.Turrets,6}",
            11 => ship.Lent ? "대출" : "소유",
            12 => string.Concat(ship.Sails.Select(ShipyardMenu.SailMark)),
            13 => at == _player.Flagship ? "기함" : "",
            _ => "",
        };
    }

    /// <summary>표 한 줄 — 이름은 가운데, 숫자는 오른쪽이다(구입 표와 같은 모양).</summary>
    private static Border Row(int[] set, string[] cells, bool header)
    {
        var grid = new Grid();
        for (int c = 0; c < set.Length; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Columns[set[c]].Width) });

        for (int c = 0; c < cells.Length; c++)
        {
            var label = new GameUi.GameLabel(GameFont.BlackColor)
            {
                Text = cells[c],
                Bold = false,
                FallbackBrush = Brushes.Black,
                Margin = new Thickness(3, 1, 3, 1),
                HorizontalAlignment = header || !Columns[set[c]].Number
                    ? HorizontalAlignment.Center
                    : HorizontalAlignment.Right,
            };
            Grid.SetColumn(label, c);
            grid.Children.Add(label);
        }
        return new Border { Background = header ? HeadFill : RowFill, Child = grid };
    }

    private void Pick(int at)
    {
        _picked = at;
        for (int i = 0; i < _rows.Count; i++) _rows[i].Background = i == at ? PickFill : RowFill;
        _decide.On = true;
    }

    private void Decide()
    {
        if (_picked < 0) return;
        Chosen = _picked;
        Close();
    }

    /// <summary>
    /// 배를 고르게 한다. 고른 번호를 낸다(중단이면 −1).
    /// </summary>
    /// <param name="startSet">이 창을 처음 열 때의 묶음 — 원본이 부른 쪽마다 들고 있는 처음 값이다.</param>
    public static int Pick(Window owner, Player player, ItemTable? items, string title, int startSet)
    {
        if (!Remembered.ContainsKey(title)) Remembered[title] = Math.Clamp(startSet, 0, Sets.Length - 1);
        var dialog = new ShipPickDialog(player, items, title) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Chosen;
    }
}
