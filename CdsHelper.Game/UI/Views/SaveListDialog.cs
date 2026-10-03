using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 세이브 고르기 표(모드 폰트 「게임 로드 창」) — 리디바탕 글씨로 칸을 나눠 그린다.
/// </summary>
/// <remarks>
/// 원본 꼴(<see cref="HintListDialog"/>)은 게임 비트맵 글꼴이 고정폭이라 빈칸을 채워 칸을 맞춘다. 리디바탕은 글자 폭이 제각각이라
/// 그렇게는 칸이 어긋나서, 칸마다 따로 세운 표로 그린다. 틀 · 바탕 · 고른 줄 색은 공용 목록 창과 같다.
/// </remarks>
public sealed class SaveListDialog : GameWindow
{
    private static readonly FontFamily Ridi =
        new(new Uri("pack://application:,,,/CdsHelper.Game;component/"), "./Fonts/#RIDIBatang");

    private static readonly Brush Ink = Frozen(Color.FromRgb(0x10, 0x0A, 0x08));
    private static readonly Brush PickInk = Frozen(Color.FromRgb(0xF4, 0xE8, 0xE0));
    private static readonly Brush PickFill = Frozen(Color.FromRgb(0x43, 0x56, 0x7A));
    private static readonly Brush PickEdge = Frozen(Color.FromRgb(0x05, 0x06, 0x09));

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>칸 폭 — 캐릭터 · 도시 · 저장한 시각 · 발견물.</summary>
    private static readonly double[] Widths = [230, 120, 170, 70];

    private readonly List<Border> _rows = [];
    private readonly List<List<TextBlock>> _cells = [];
    private readonly GameButton _decide;
    private int _picked = -1;

    private SaveListDialog(string caption, IReadOnlyList<string> header, IReadOnlyList<string[]> rows, int preselect)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var list = new StackPanel();
        list.Children.Add(Line(header, bold: true, out _));
        for (int i = 0; i < rows.Count; i++)
        {
            int at = i;
            var row = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(1),
                Cursor = Cursors.Hand,
                Child = Line(rows[i], bold: false, out var cells),
            };
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; Select(at); };
            row.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { e.Handled = true; Select(at); Decide(); } };
            _rows.Add(row);
            _cells.Add(cells);
            list.Children.Add(row);
        }

        var page = new Border
        {
            Background = GameUi.PageFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3, 3, 3, 0),
            Padding = new Thickness(4, 2, 4, 2),
            Child = GameUi.Scroller(list, 520),
        };

        _decide = new GameButton("결정", Decide, BandStyle.Button, 106)
        {
            Height = UiSprites.BandHeight,
            Margin = new Thickness(0, 0, 6, 0),
            On = false,
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 5),
            Children =
            {
                _decide,
                new GameButton("중단", Cancel, BandStyle.Button, 106)
                {
                    Height = UiSprites.BandHeight,
                    Margin = new Thickness(6, 0, 0, 0),
                },
            },
        };

        var title = GameUi.TitleBar(caption, Cancel);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(page);
        stack.Children.Add(buttons);
        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Background = GameUi.MenuBack,
                BorderBrush = GameUi.MenuEdge,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(4),
                Child = stack,
            },
        };

        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: Cancel(); break;
                case Key.Enter: Decide(); break;
                case Key.Up: Select(Math.Max(0, _picked - 1)); break;
                case Key.Down: Select(Math.Min(_rows.Count - 1, _picked + 1)); break;
            }
        };
        MouseRightButtonUp += (_, _) => Cancel();
        if (preselect >= 0 && preselect < _rows.Count) Select(preselect);
    }

    /// <summary>한 줄 — 칸마다 리디바탕 글씨. 마지막 칸(발견물)은 오른쪽으로 붙인다.</summary>
    private static Grid Line(IReadOnlyList<string> texts, bool bold, out List<TextBlock> cells)
    {
        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        cells = [];
        for (int c = 0; c < Widths.Length; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Widths[c]) });
            var block = new TextBlock
            {
                Text = c < texts.Count ? texts[c] : "",
                FontFamily = Ridi,
                FontSize = 16,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = c == Widths.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 4, 0),
            };
            Grid.SetColumn(block, c);
            grid.Children.Add(block);
            cells.Add(block);
        }
        return grid;
    }

    private void Select(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        _picked = index;
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = i == index;
            _rows[i].Background = on ? PickFill : Brushes.Transparent;
            _rows[i].BorderBrush = on ? PickEdge : Brushes.Transparent;
            foreach (var cell in _cells[i]) cell.Foreground = on ? PickInk : Ink;
        }
        _rows[index].BringIntoView();
        _decide.On = true;
    }

    private bool _decided;

    private void Decide()
    {
        if (_picked < 0) return;
        _decided = true;
        Close();
    }

    private void Cancel()
    {
        _decided = false;
        Close();
    }

    /// <summary>표를 띄워 한 줄을 고르게 한다. 고른 줄 번호, 중단하면 −1.</summary>
    public static int Pick(Window owner, string caption, IReadOnlyList<string> header,
                           IReadOnlyList<string[]> rows, int preselect = 0)
    {
        var dialog = new SaveListDialog(caption, header, rows, preselect) { Owner = owner };
        dialog.ShowDialog();
        return dialog._decided ? dialog._picked : -1;
    }
}
