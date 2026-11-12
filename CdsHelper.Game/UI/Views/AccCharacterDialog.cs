using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 누적 캐릭터 창 — 올라 있는 은퇴 제독을 늘어놓고 한 사람씩 <b>지우거나</b> 「등장 안 함」을 켠다.
/// </summary>
/// <remarks>
/// 원본에 없는 창이다(모드 창 「캐릭터」 탭의 「누적 캐릭터…」). 원본은 한 사람씩 지울 길이 없고, NEW GAME 에서
/// 「등장시키지 않는다」를 고른 주인공이 은퇴할 때 다섯 자리를 통째로 비울 뿐이다(<c>0x0041AD55</c>).
///
/// 지우면 누적캐릭터.json 에서 빠지고, 지금 게임에 앉아 있으면(인물 276~280) 그 자리를 세상에서 내린다 —
/// 판이 앉힌 자리를 표지로 기억하므로(<see cref="Player.AccSeats"/>) 세이브했다 불러와도 되살아나지 않고,
/// 뒷사람이 그 자리로 당겨 앉지도 않는다. 「등장 안 함」은 지우지 않고 다음 게임부터 안 내보낸다.
/// </remarks>
public sealed class AccCharacterDialog : GameWindow
{
    /// <summary>지금 게임과 잇는 손잡이. 타이틀처럼 게임이 없으면 다 null 이다.</summary>
    public sealed class Hooks
    {
        /// <summary>얼굴 그림.</summary>
        public Portraits? Faces { get; init; }

        /// <summary>그 사람이 지금 게임에 앉아 있는지(표지).</summary>
        public Func<string, bool>? Seated { get; init; }

        /// <summary>지운 뒤 부른다 — 지금 게임에서 그 자리를 내린다.</summary>
        public Action<string>? Removed { get; init; }
    }

    private const double ListWidth = 560;
    private const double ListHeight = 470;
    private const double FaceScale = 0.75;

    private readonly Hooks _hooks;
    private readonly StackPanel _rows = new() { Width = ListWidth };
    private readonly TextBlock _count = new()
    {
        Foreground = GameUi.Text,
        FontSize = 13,
        Margin = new Thickness(12, 0, 12, 4),
    };

    private AccCharacterDialog(Hooks hooks)
    {
        _hooks = hooks;
        Title = "누적 캐릭터";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var intro = new TextBlock
        {
            Text = "원본에 없는 기능입니다. 은퇴해 누적 캐릭터로 올라간 제독을 한 사람씩 지우거나,"
                 + " 지우지 않고 다음 게임부터 등장시키지 않을 수 있습니다."
                 + " 지금 게임에 나와 있는 사람을 지우면 지금 게임에서도 사라집니다.",
            Foreground = GameUi.Text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Width = ListWidth + GameUi.ScrollWidth,
            Margin = new Thickness(12, 8, 12, 8),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("누적 캐릭터", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(intro);
        stack.Children.Add(new Border
        {
            Margin = new Thickness(12, 0, 12, 4),
            Child = GameUi.Scroller(_rows, ListHeight),
        });
        stack.Children.Add(_count);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
        Fill();
    }

    /// <summary>목록을 새로 짓는다 — 지우거나 깃발을 바꾸면 다시 부른다.</summary>
    private void Fill()
    {
        _rows.Children.Clear();
        var all = AccData.Load();
        _count.Text = $"등록 {all.Count} / {AccData.Slots}명";
        if (all.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = "등록된 누적 캐릭터가 없습니다. 주인공을 은퇴시키면 여기에 올라갑니다.",
                Foreground = GameUi.Text,
                FontSize = 14,
                Margin = new Thickness(4, 12, 4, 12),
            });
            return;
        }
        foreach (var who in all) _rows.Children.Add(Row(who));
    }

    /// <summary>한 사람 — 얼굴 · 이름 · 나라 · 은퇴 날짜 · 명성, 그리고 「등장 안 함」 · 「삭제」.</summary>
    private UIElement Row(AccData.Character who)
    {
        string id = who.Id ?? "";
        bool seated = id.Length > 0 && _hooks.Seated?.Invoke(id) == true;

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var face = Face(who.Face);
        Grid.SetColumn(face, 0);
        grid.Children.Add(face);

        var info = new StackPanel { Margin = new Thickness(10, 2, 6, 0), VerticalAlignment = VerticalAlignment.Top };
        info.Children.Add(new TextBlock
        {
            Text = who.Name,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 16,
        });
        info.Children.Add(Line(Lang.T(NationOf(who.Nation))));
        info.Children.Add(Line($"은퇴 {who.RetiredOn.Year}년 {who.RetiredOn.Month}월 {who.RetiredOn.Day}일"));
        info.Children.Add(Line($"명성 {who.Fame:N0}"));
        if (seated) info.Children.Add(Line("지금 게임에 등장해 있습니다", Brushes.Khaki));
        if (who.Hidden) info.Children.Add(Line("다음 게임부터 등장하지 않습니다", Brushes.Silver));
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var hide = new CheckBox
        {
            Content = "등장 안 함",
            IsChecked = who.Hidden,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(10, 0, 10, 8),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "지우지 않고 다음 게임부터 등장시키지 않습니다. 지금 게임에는 그대로 남습니다.",
        };
        hide.Checked += (_, _) => SetHidden(id, true);
        hide.Unchecked += (_, _) => SetHidden(id, false);
        side.Children.Add(hide);
        side.Children.Add(GameUi.PushButton("삭제", () => Delete(id, ask: true), 96));
        Grid.SetColumn(side, 2);
        grid.Children.Add(side);

        return new Border
        {
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(2, 2, 2, 6),
            Child = grid,
        };
    }

    /// <summary>「등장 안 함」을 켜고 끈다 — 다음 게임부터 든다. 지금 게임에 앉은 사람은 그대로 남는다.</summary>
    internal void SetHidden(string id, bool hidden)
    {
        AccData.SetHidden(id, hidden);
        // 체크 상자의 이벤트 안에서 불린다 — 목록을 갈아 끼우는 것은 한 박자 뒤로 미룬다.
        Dispatcher.BeginInvoke(Fill);
    }

    /// <summary>
    /// 그 사람을 지운다 — 한 번 묻고(<paramref name="ask"/>), 누적캐릭터.json 에서 빼고, 지금 게임에서도 내린다.
    /// </summary>
    /// <returns>지웠으면 true.</returns>
    internal bool Delete(string id, bool ask)
    {
        var who = AccData.Load().FirstOrDefault(c => c.Id == id);
        if (who == null) return false;
        bool seated = _hooks.Seated?.Invoke(id) == true;
        if (ask)
        {
            string name = who.Name;
            string text = seated
                ? $"{name}{GameUi.Josa(name, "을", "를")} 누적 캐릭터에서 지우겠습니까?\n지금 게임에서도 사라지고, 되돌릴 수 없습니다."
                : $"{name}{GameUi.Josa(name, "을", "를")} 누적 캐릭터에서 지우겠습니까?\n되돌릴 수 없습니다.";
            if (!ConfirmDialog.Ask(this, text)) return false;
        }
        if (!AccData.Remove(id))
        {
            NoticeDialog.Show(this, "누적 캐릭터 파일을 고치지 못했습니다.");
            return false;
        }
        _hooks.Removed?.Invoke(id);
        Fill();
        return true;
    }

    private static string NationOf(int nation) =>
        nation >= 0 && nation < Player.Nations.Length ? Player.Nations[nation]
        : Player.NationNameOf?.Invoke(nation) ?? "";

    private static TextBlock Line(string text, Brush? color = null) => new()
    {
        Text = text,
        Foreground = color ?? GameUi.Text,
        FontSize = 13,
        Margin = new Thickness(0, 2, 0, 0),
    };

    /// <summary>얼굴 한 장. 못 읽으면 빈 칸으로 자리만 지킨다.</summary>
    private UIElement Face(int face)
    {
        var box = new Border
        {
            Width = Portraits.Width * FaceScale + 2,
            Height = Portraits.Height * FaceScale + 2,
            Background = Brushes.Black,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
        };
        if (_hooks.Faces?.TryGetBgra(face, female: false) is not { } bgra) return box;

        var bmp = Portraits.Bitmap(bgra);
        bmp.Freeze();
        var image = new Image
        {
            Source = bmp,
            Width = Portraits.Width * FaceScale,
            Height = Portraits.Height * FaceScale,
        };
        RenderOptions.SetBitmapScalingMode(image, Portraits.Scaling(image.Source));
        box.Child = image;
        return box;
    }

    /// <summary>창을 띄운다. 닫힐 때까지 돌아오지 않는다.</summary>
    /// <param name="opened">띄운 직후(그리기 전에) 부른다 — 화면 밖 시험이 창을 잡는 데 쓴다.</param>
    public static void Show(Window owner, Hooks hooks, Action<AccCharacterDialog>? opened = null)
    {
        var dialog = new AccCharacterDialog(hooks) { Owner = owner };
        if (opened != null) dialog.Loaded += (_, _) => opened(dialog);
        dialog.ShowDialog();
    }
}
