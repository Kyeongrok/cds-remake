using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 개발 → <b>운명 자리</b> — 여급 127명의 궁합 코드를 보고 고친다.
/// </summary>
/// <remarks>
/// 운명 코드는 <b>0~31 한 덩어리</b>다. 아래 열여섯이 젊은 제독, 위 열여섯이 그 중년
/// 몫이라 초상화가 <c>얼굴 + 16</c> 인 것과 똑같은 짜임이다 — 그래서 자리를 0~16 으로
/// 늘릴 수는 없다. 16 은 이미 「자리 0 의 중년」이다.
///
/// 주인공의 자리는 새 놀이가 정한다 — 앞 열여섯 얼굴은 얼굴 번호가 곧 자리고, 더 넣은
/// 얼굴은 <b>열여섯 가운데 하나를 굴려</b> 준다. 그러니 여기서 할 일은 <b>어느 자리에
/// 여급이 몇이나 있는지</b> 보고, 쏠린 데를 손보는 것이다.
///
/// 고친 것은 <see cref="BarmaidEdits"/> 가 적어 두고 원본 여급표는 안 건드린다.
/// </remarks>
public sealed class FortuneDialog : GameWindow
{
    private readonly ListBox _girls = new()
    {
        Background = GameUi.PageFill,
        BorderBrush = GameUi.Edge,
        BorderThickness = new Thickness(1),
    };

    private readonly NumericSpinner _code = new()
    {
        Minimum = 0,
        Maximum = FortuneCodes.Slots * 2 - 1,
        Step = 1,
        DecimalPlaces = 0,
        Width = 90,
        IsEnabled = false,
    };

    private readonly TextBlock _note = new()
    {
        Foreground = GameUi.Text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private BarmaidTable? _table;

    private FortuneDialog()
    {
        Title = "운명 자리";
        Width = 720;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = GameUi.Back;

        _table = BarmaidTable.Open(Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "");

        Content = Build();
        _girls.SelectionChanged += (_, _) => Pick();
        Refill();
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner)
    {
        var window = new FortuneDialog();
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }

    /// <summary>목록 한 줄.</summary>
    private sealed record Row(int Id, string Name, int City, int Code, int Slot, bool Aged,
                              bool Edited)
    {
        public override string ToString() =>
            $"{Id,3}  {Name,-10}  코드 {Code,2}  =  자리 {Slot}{(Aged ? " · 중년" : " · 젊음")}"
            + (Edited ? "   ●" : "");
    }

    private void Refill()
    {
        int at = _girls.SelectedIndex;
        _girls.Items.Clear();

        int step = FortuneCodes.Slots;
        var empty = new List<int>();
        for (int slot = 0; slot < step; slot++)
            if (FortuneCodes.Empty(slot, _table)) empty.Add(slot);

        foreach (var her in _table?.Barmaids ?? [])
        {
            bool aged = her.Fortune >= step;
            _girls.Items.Add(new Row(her.Id, her.Name, her.City, her.Fortune,
                                     aged ? her.Fortune - step : her.Fortune, aged,
                                     BarmaidEdits.Of(her.Id) >= 0));
        }

        _note.Text = $"운명 코드는 0~{step * 2 - 1} 한 덩어리다 — 아래 {step} 이 젊은 제독, "
                     + $"위 {step} 이 그 중년 몫이다. 자리를 늘리면 여급 코드는 새 걸음으로 "
                     + "저절로 옮겨 앉는다(뜻은 안 바뀐다)."
                     + (empty.Count > 0
                        ? $"\n● 여급이 하나도 없는 자리: {string.Join(", ", empty)} — "
                          + "그 자리를 진 제독은 아무와도 궁합이 안 맞는다."
                        : "\n● 모든 자리에 여급이 하나씩은 있다.");

        _girls.SelectedIndex = Math.Clamp(at, 0, Math.Max(0, _girls.Items.Count - 1));
    }

    private void Pick()
    {
        if (_girls.SelectedItem is not Row row) { _code.IsEnabled = false; return; }
        _code.IsEnabled = true;
        _code.Value = row.Code;
    }

    private UIElement Build()
    {
        var keep = GameUi.PushButton("이 코드로", () =>
        {
            if (_girls.SelectedItem is not Row row) return;
            BarmaidEdits.Set(row.Id, (int)_code.Value);
            Reopen();
        }, 100);

        var back = GameUi.PushButton("되돌린다", () =>
        {
            if (_girls.SelectedItem is not Row row) return;
            BarmaidEdits.Set(row.Id, -1);
            Reopen();
        }, 100);

        var all = GameUi.PushButton("모두 되돌린다", () => { BarmaidEdits.ResetAll(); Reopen(); }, 120);

        var close = GameUi.PushButton("닫기", Close, 90);
        close.Margin = new Thickness(20, 0, 0, 0);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { keep, back, all, close },
        };

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { Label("고른 여급의 궁합 코드"), _code },
        };

        var page = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(_note, Dock.Top);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        page.Children.Add(_note);
        page.Children.Add(top);
        page.Children.Add(buttons);
        page.Children.Add(_girls);

        var shell = new DockPanel();
        var title = GameUi.TitleBar("운명 자리", Close);
        DockPanel.SetDock(title, Dock.Top);
        shell.Children.Add(title);
        shell.Children.Add(page);
        return shell;
    }

    /// <summary>표를 다시 읽어 목록을 새로 편다.</summary>
    private void Reopen()
    {
        _table = BarmaidTable.Open(Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "");
        Refill();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = GameUi.Text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0),
    };
}
