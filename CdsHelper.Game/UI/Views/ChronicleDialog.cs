using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 자택의 「연표를 본다」와 커맨드의 「항해일지를 본다」 — 발견·보고를 날짜 차례로 늘어놓는 한 창이다.
/// </summary>
/// <remarks>
/// 게임도 창 하나를 두 갈래로 쓴다(<c>0x00424590</c> 연표 · <c>0x004246C0</c> 항해일지, 그림 <c>0x00423ED0</c>).
/// <code>
///   판     592 x 448, MISC.CDS 파트 8 을 통째로 깐다(asset/ui/misc-08.png)
///   줄     한 쪽에 19 줄, 줄 높이 20, 날짜 칸 x=8 · 본문 x=104 폭 472
///   날짜   "%4d년%2d월" — 해가 앞 줄과 같으면 달만, 달까지 같으면 빈칸
///   본문   연표   "%s%s %s%s %s했다"   (사람 이름 + 조사, 발견물 + 조사, 발견/보고)
///          항해일지 "%s%s %s했다"       (사람 이름 없이)
///   단추   앞장(360,416) · 다음장(448,416) · 취소(536,416)
/// </code>
/// <b>줄을 고르는 규칙이 갈래마다 다르다</b>(<c>0x00424590</c> · <c>0x004246C0</c>).
/// 연표는 <b>보고까지 끝낸</b> 발견물만 싣고 「발견」과 「보고」 두 줄을 내며, 항해일지는 발견한 것을 다 싣고
/// 보고한 것에 「보고」 줄을 더한다. 읽고 넘기는 것뿐이라 날짜도 소지금도 움직이지 않는다.
/// </remarks>
public sealed class ChronicleDialog : GameWindow
{
    /// <summary>판 크기와 줄 자리(<c>0x00423ED0</c>).</summary>
    private const double PanelW = 592, PanelH = 448, RowH = 20, FirstY = 8,
                         DateX = 8, TextX = 104, TextW = 472;

    /// <summary>한 쪽에 들어가는 줄 수.</summary>
    private const int Lines = 19;

    /// <summary>한 줄 — 무엇을, 언제, 어떻게 했는가.</summary>
    /// <param name="Who">한 사람 이름. 항해일지면 빈 글이다.</param>
    /// <param name="What">발견물 이름.</param>
    /// <param name="Reported">보고 줄인지(<c>보고했다</c>). 아니면 <c>발견했다</c>.</param>
    public readonly record struct Row(DateTime When, string Who, string What, bool Reported);

    private readonly List<Row> _rows;
    private readonly Canvas _sheet = new() { Width = PanelW, Height = PanelH };
    private int _page;

    private ChronicleDialog(IEnumerable<Row> rows, string caption)
    {
        // 날짜 차례다 — 게임도 그린 뒤가 아니라 목록을 세울 때 정렬한다(0x004241D0).
        _rows = [.. rows.OrderBy(r => r.When.Year * 12 + r.When.Month)];

        Title = caption;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        if (Backdrop() is { } paper)
        {
            var image = new Image { Source = paper, Width = PanelW, Height = PanelH };
            RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
            _sheet.Children.Add(image);
        }

        Content = _sheet;
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape) Close();
            else if (e.Key is Key.Left or Key.PageUp) Turn(-1);
            else if (e.Key is Key.Right or Key.PageDown) Turn(1);
        };
        Paint();
    }

    /// <summary>파트 8 바탕. 없으면 null 이라 종이 없이 글만 앉는다.</summary>
    private static BitmapSource? Backdrop()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "asset", "ui", "misc-08.png");
            if (!File.Exists(path)) return null;
            var made = new BitmapImage();
            made.BeginInit();
            made.CacheOption = BitmapCacheOption.OnLoad;
            made.UriSource = new Uri(path);
            made.EndInit();
            made.Freeze();
            return made;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>이 쪽의 첫 줄 번호.</summary>
    private int Start => _page * Lines;

    private void Turn(int by)
    {
        int page = Math.Clamp(_page + by, 0, Math.Max(0, (_rows.Count - 1) / Lines));
        if (page == _page) return;
        _page = page;
        Paint();
    }

    private void Paint()
    {
        // 바탕 그림(첫 아이)만 남기고 다시 앉힌다.
        while (_sheet.Children.Count > 1) _sheet.Children.RemoveAt(1);

        int year = -1, month = -1;
        for (int i = 0; i < Lines && Start + i < _rows.Count; i++)
        {
            var row = _rows[Start + i];
            double y = FirstY + i * RowH;

            // 해가 같으면 달만, 달까지 같으면 날짜 칸을 비운다(0x00537AD8 · 0x00537AC8).
            string when = row.When.Year != year ? $"{row.When.Year,4}년{row.When.Month,2}월"
                        : row.When.Month != month ? $"{row.When.Month,8}월" : "";
            year = row.When.Year;
            month = row.When.Month;

            Put(when, DateX, y, 90);
            Put(Words(row), TextX, y, TextW);
        }

        Put($"{_page + 1} / {Math.Max(1, (_rows.Count + Lines - 1) / Lines)}", DateX, 418, 90);
        Button("앞장", 360, 416, 80, () => Turn(-1));
        Button("다음장", 448, 416, 80, () => Turn(1));
        Button("취소", 536, 416, 48, Close);
    }

    /// <summary>줄 글 — 항해일지는 사람 이름이 없다.</summary>
    private static string Words(in Row row)
    {
        string what = $"{row.What}{GameUi.Josa(row.What, "을", "를")} {(row.Reported ? "보고" : "발견")}했다";
        return row.Who.Length == 0 ? what : $"{row.Who}{GameUi.Josa(row.Who, "이", "가")} {what}";
    }

    private void Put(string text, double x, double y, double width)
    {
        if (text.Length == 0) return;
        var label = new GameUi.GameLabel(GameFont.BlackColor, GameUi.ItemTextHeight) { Text = text };
        var box = new Border { Width = width, Child = label, HorizontalAlignment = HorizontalAlignment.Left };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        _sheet.Children.Add(box);
    }

    private void Button(string text, double x, double y, double width, Action run)
    {
        var button = GameUi.PushButton(text, run, width);
        Canvas.SetLeft(button, x);
        Canvas.SetTop(button, y);
        _sheet.Children.Add(button);
    }

    /// <summary>
    /// 자택의 <b>연표</b> — 보고까지 끝낸 발견물만, 발견 줄과 보고 줄을 함께 낸다(<c>0x00424590</c>).
    /// </summary>
    public static void ShowChronicle(Window owner, Player player, DiscoveryTable? table)
    {
        var rows = new List<Row>();
        foreach (int id in player.Announced)
        {
            string name = NameOf(table, id);
            if (player.AnnouncedDateOf(id) is not { } told) continue;
            if (player.FoundDateOf(id) is { } found)
                rows.Add(new Row(found, player.Name, name, Reported: false));
            rows.Add(new Row(told, player.Name, name, Reported: true));
        }
        Open(owner, rows, "연표", "아직 연표에 적을 것이 없다.");
    }

    /// <summary>
    /// 커맨드의 <b>항해일지</b> — 발견한 것을 다 싣고, 보고한 것에 보고 줄을 더한다(<c>0x004246C0</c>).
    /// </summary>
    public static void ShowLogbook(Window owner, Player player, DiscoveryTable? table)
    {
        var rows = new List<Row>();
        foreach (int id in player.Discoveries)
        {
            string name = NameOf(table, id);
            if (player.FoundDateOf(id) is { } found) rows.Add(new Row(found, "", name, Reported: false));
            if (player.AnnouncedDateOf(id) is { } told) rows.Add(new Row(told, "", name, Reported: true));
        }
        Open(owner, rows, "항해일지", "항해일지에 아직 적은 것이 없다.");
    }

    private static string NameOf(DiscoveryTable? table, int id) => table?.NameOf(id) ?? $"발견물 {id}";

    private static void Open(Window owner, List<Row> rows, string caption, string whenEmpty)
    {
        if (rows.Count == 0)
        {
            NoticeDialog.Show(owner, whenEmpty);
            return;
        }
        new ChronicleDialog(rows, caption) { Owner = owner }.ShowDialog();
    }
}
