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
/// 연표는 <b>세상에 알려진</b>(사람 칸 2 가 찬) 발견물을 다 싣는다 — 알린 사람이 제독이면 「발견」과 「보고」
/// 두 줄, 남(누적 캐릭터)이면 그 사람 이름으로 「보고」 한 줄이고 글색이 다르다. 항해일지는 발견한 것을 다 싣고
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
    /// <param name="Mine">제독 제 줄인지. 남(누적 캐릭터)이 보고한 줄이면 글색이 다르다.</param>
    public readonly record struct Row(DateTime When, string Who, string What, bool Reported, bool Mine = true);

    /// <summary>
    /// 줄 글색(공용 색표) — 사람 이름이 제독이면 <c>0x49</c>, 남이면 <c>0x3B</c> 다
    /// (<c>0x00423F79</c> 의 strcmp → <c>sbb/and 0xE/add 0x3B</c>).
    /// </summary>
    private const byte MineColor = 0x49, OthersColor = 0x3B;

    private readonly List<Row> _rows;
    private readonly Canvas _sheet = new() { Width = PanelW, Height = PanelH };
    private int _page;

    /// <summary>바탕 그림(파트 8)이 판의 첫 아이로 깔렸는지.</summary>
    private bool _hasBackdrop;

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
            _hasBackdrop = true;
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
        // 바탕 그림만 남기고 다시 앉힌다. 그림을 못 읽었으면 남길 것이 없다 — 예전에는 늘 첫 아이를 남겨
        // 그림이 없을 때 앞 쪽의 첫 글이 쪽을 넘겨도 그대로 남았다.
        int keep = _hasBackdrop ? 1 : 0;
        while (_sheet.Children.Count > keep) _sheet.Children.RemoveAt(keep);

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

            byte color = row.Mine ? MineColor : OthersColor;
            Put(when, DateX, y, 90, color);
            Put(Words(row), TextX, y, TextW, color);
        }

        // 앞장은 첫 쪽이 아닐 때만, 다음장은 뒤에 쪽이 남았을 때만 눌린다(0x00424437 ~ 0x0042446A 의 켜짐 비트 4).
        Button("앞장", 360, 416, 80, () => Turn(-1), _page > 0);
        Button("다음장", 448, 416, 80, () => Turn(1), Start + Lines < _rows.Count);
        Button("취소", 536, 416, 48, Close, true);
    }

    /// <summary>줄 글 — 항해일지는 사람 이름이 없다.</summary>
    private static string Words(in Row row)
    {
        string what = $"{row.What}{GameUi.Josa(row.What, "을", "를")} {(row.Reported ? "보고" : "발견")}했다";
        return row.Who.Length == 0 ? what : $"{row.Who}{GameUi.Josa(row.Who, "이", "가")} {what}";
    }

    private void Put(string text, double x, double y, double width, byte color)
    {
        if (text.Length == 0) return;
        var label = new GameUi.GameLabel(color, GameUi.ItemTextHeight) { Text = text };
        var box = new Border { Width = width, Child = label, HorizontalAlignment = HorizontalAlignment.Left };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        _sheet.Children.Add(box);
    }

    /// <summary>
    /// 창 아래 단추 — 게임 띠 단추다(<c>0x00413450</c>, <c>0x00424372</c> ~ <c>0x0042440A</c>). 높이는 띠 그대로 24.
    /// </summary>
    private void Button(string text, double x, double y, double width, Action run, bool on)
    {
        var button = new GameButton(text, run, width: width) { Margin = new Thickness(0), On = on };
        Canvas.SetLeft(button, x);
        Canvas.SetTop(button, y);
        _sheet.Children.Add(button);
    }

    /// <summary>
    /// 자택의 <b>연표</b> — 세상에 알려진 발견물을 싣는다(<c>0x00424590</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 발견물마다 사람 칸 2(알린 사람)가 차 있으면 싣는다. 그 이름이 제독이면 「발견」 줄을 먼저 넣고,
    /// 「보고」 줄은 누가 알렸든 넣는다(<c>0x004245FA</c> ~ <c>0x0042466E</c>). 남이 알린 것은
    /// <see cref="Player.Scooped"/> 다 — 그 날짜를 안 적던 세이브에서 온 것은 줄을 세울 수 없어 뺀다.
    /// </remarks>
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
        foreach (var (id, who) in player.Scooped)
        {
            if (player.HasAnnounced(id) || player.ScoopedOn(id) is not { } told) continue;
            rows.Add(new Row(told, who, NameOf(table, id), Reported: true, Mine: false));
        }
        Open(owner, rows, "연표");
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
        Open(owner, rows, "항해일지");
    }

    private static string NameOf(DiscoveryTable? table, int id) => table?.NameOf(id) ?? $"발견물 {id}";

    /// <summary>
    /// 창을 연다. <b>줄이 없어도 빈 책을 그대로 연다</b> — 게임은 줄 수를 안 보고 창을 세운다
    /// (<c>0x00424590</c> · <c>0x004246C0</c> → <c>0x004241D0</c>). 따로 알리는 말도 없다.
    /// </summary>
    private static void Open(Window owner, List<Row> rows, string caption)
    {
        new ChronicleDialog(rows, caption) { Owner = owner }.ShowDialog();
    }
}
