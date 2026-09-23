using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Disev;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 개발용 퀘스트 창 — 개인 이야기의 진행값과, 그 값이 가리키는 파트의 발동 조건을 풀어 보인다.
/// </summary>
/// <remarks>
/// 게임은 이야기 책마다 진행 카운터 하나만 두고, 그 값이 곧 <b>다음 사건 때 열어 볼 파트 번호</b>다
/// (<see cref="Engine.Discovery.StoryLog"/>). 그래서 「왜 안 뜨는지」는 그 파트의 슬롯 조건을 지금 값으로
/// 재 보면 안다. 슬롯은 앞에서부터 처음 통과하는 것 하나만 돈다.
///
/// 건물 조건은 건물에 들어선 사건에서만 뜻이 있어 「…」로 둔다 — 그 건물에 들어가면 맞는다는 뜻이다.
/// </remarks>
public sealed class QuestDialog : GameWindow
{
    private readonly Engine.Game _game;
    private readonly StackPanel _body = new() { Margin = new Thickness(12, 8, 12, 4) };

    private QuestDialog(Engine.Game game)
    {
        _game = game;

        Title = "퀘스트";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12, 6, 12, 10),
        };
        buttons.Children.Add(GameUi.PushButton("진행값 바꾸기…", SetStep, 150));
        buttons.Children.Add(GameUi.PushButton("다시 보기", Paint, 110));
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("퀘스트", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(new ScrollViewer
        {
            Content = _body,
            MaxHeight = 560,
            MinWidth = 520,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        Paint();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>지금 책 이름. 개인 이야기가 없는 캐릭터면 null.</summary>
    private string? Book => _game.Player.ActiveStoryBook;

    /// <summary>진행값 — <see cref="Engine.Discovery.StoryLog"/> 와 같게, 없으면 옛 열쇠(「책:0」)로 물러선다.</summary>
    private int Step(string book) =>
        _game.Player.StoryProgress.TryGetValue(book, out int step) ? step : _game.Player.StoryStepOf($"{book}:0");

    private void Paint()
    {
        _body.Children.Clear();
        var player = _game.Player;

        if (Book is not { } book)
        {
            Line("개인 이야기가 없는 캐릭터입니다.");
            return;
        }

        string name = DisevBook.Books.FirstOrDefault(b => b.Cache == book).Title ?? book;
        int step = Step(book);
        int count = DisevRunner.Open(_game.Directory, book)?.Count ?? 0;
        bool closed = player.IsStoryArcClosed(book);

        Line($"책  {book} — {name} (파트 {count}개)", bold: true);
        Line($"진행값  {step}" + (closed ? "   · 책이 닫혔다(04) — 더는 사건이 안 난다" : ""), bold: true);
        Line(player.StoryQuestDeadline is { } due
            ? $"의뢰 기한  {due:yyyy-MM-dd} (남은 {player.StoryQuestDaysLeft}일)"
            : "의뢰 기한  없음");
        Line($"지금  {player.Date:yyyy-MM-dd} · 명성 {player.Fame} · 도시 {(player.CityId >= 0 ? player.CityName : "바다")}");

        if (DisevRunner.Inspect(_game, book, step) is not { } slots)
        {
            Line($"파트 {step} 이 책에 없습니다.", top: 10);
            return;
        }

        Line($"파트 {step} — 슬롯 {slots.Count}개 (앞에서부터 처음 통과하는 것 하나가 돈다)", bold: true, top: 10);
        bool picked = false;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            string mark = slot.Passes ? picked ? "통과(앞 슬롯이 먼저 돈다)" : "통과 — 이것이 돈다" : "안 됨";
            picked |= slot.Passes;
            Line($"슬롯 {i}  [{mark}]", bold: true, top: 6);

            if (slot.Checks.Count == 0) Line("    조건 없음 — 늘 통과");
            foreach (var check in slot.Checks)
            {
                string value = check.Text == "또는" ? "" : check.Value switch
                {
                    true => "  ○",
                    false => "  ×",
                    null => "  …(그 건물에 들어가야 안다)",
                };
                Line($"    {check.Text}{value}", color: check.Value == false ? Brushes.IndianRed : null);
            }

            Line("    본문:");
            foreach (string text in slot.Body.Take(12))
                Line($"      {Short(text)}", color: Brushes.Gray);
            if (slot.Body.Count > 12) Line($"      … {slot.Body.Count - 12}줄 더", color: Brushes.Gray);
        }
    }

    /// <summary>진행값을 계산기로 박는다 — 내려가기도 하고, 닫힌 책도 다시 연다.</summary>
    private void SetStep()
    {
        if (Book is not { } book) return;
        int count = DisevRunner.Open(_game.Directory, book)?.Count ?? 0;
        if (count <= 0) return;
        if (NumberPadDialog.Ask(this, Math.Clamp(Step(book), 0, count - 1), 0, count - 1) is not { } step) return;
        _game.Player.ForceStoryStep(book, step);
        Paint();
    }

    private static string Short(string text) => text.Length > 70 ? text[..70] + "…" : text;

    private void Line(string text, bool bold = false, double top = 0, Brush? color = null) =>
        _body.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = color ?? GameUi.Text,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 14,
            Margin = new Thickness(0, top, 0, 1),
        });

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window owner, Engine.Game game) =>
        new QuestDialog(game) { Owner = owner }.ShowDialog();
}
