using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 제독의 모든 것을 한 장에 늘어놓는 창. 제목 줄 햄버거에서 연다.
/// </summary>
/// <remarks>
/// <b>읽기만 한다.</b> 놀이 화면 곳곳에 흩어져 있는 값(신상 · 능력 · 기능 · 언어 · 형편 ·
/// 함대 · 부하 · 짐 · 계약 · 가족 · 나라 사이)을 한자리에 모아 놓은 것이라, 무엇이 왜
/// 그렇게 나오는지 대 볼 데가 필요할 때 쓴다. 고치는 것은 <see cref="DevDialog"/> 다.
///
/// 게임에는 없는 창이라 게임 꼴을 흉내내지 않는다 — 게임데이터 창과 같은 결이다.
/// </remarks>
public sealed class PlayerInfoDialog : GameWindow
{
    private readonly TextBox _body = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas, D2Coding, Courier New"),
        FontSize = 13,
        Background = GameUi.PageFill,
        Foreground = Brushes.Black,
        BorderBrush = GameUi.Edge,
        BorderThickness = new Thickness(1),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(8, 6, 8, 6),
    };

    private PlayerInfoDialog(Engine.Game game)
    {
        Title = "제독 정보";
        Width = 760;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = GameUi.Back;

        var copy = new Button
        {
            Content = "글로 복사",
            Padding = new Thickness(10, 3, 10, 3),
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(_body.Text); } catch (System.Runtime.InteropServices.COMException) { }
        };

        var page = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(copy, Dock.Bottom);
        page.Children.Add(copy);
        page.Children.Add(_body);
        Content = page;

        _body.Text = Sheet(game);
    }

    /// <summary>창을 띄운다.</summary>
    public static void Show(Window? owner, Engine.Game game)
    {
        var window = new PlayerInfoDialog(game);
        if (owner != null) window.Owner = owner;
        window.ShowDialog();
    }

    // ── 한 장 짓기 ─────────────────────────────────────────────────────────────

    private static string Sheet(Engine.Game game)
    {
        var p = game.Player;
        var text = new StringBuilder();

        void Head(string title) => text.Append('\n').Append("── ").Append(title)
                                       .Append(' ').Append('─', Math.Max(0, 46 - title.Length))
                                       .Append('\n');
        void Row(string label, object? value) =>
            text.Append("  ").Append(label.PadRight(12)).Append(value).Append('\n');

        Head("신상");
        Row("이름", p.Name.Length > 0 ? p.Name : $"{p.Given}·{p.Family}");
        Row("나이", $"{p.Age}세 ({p.BirthYear}년 {p.BirthMonth}월 {p.BirthDay}일생)");
        Row("별자리", p.Zodiac);
        Row("혈액형", p.Blood >= 0 && p.Blood < Player.BloodTypes.Length
                       ? Player.BloodTypes[p.Blood] + "형" : "-");
        Row("국적", p.Nation >= 0 && p.Nation < Player.Nations.Length
                     ? Player.Nations[p.Nation] : "-");
        Row("직업", p.Work.Name);
        Row("얼굴", p.Face);
        Row("행운", $"{p.Fortune} / {Player.MaxFortune}");

        Head("능력 여섯");
        for (int i = 0; i < Ability.Names.Length && i < p.Abilities.Length; i++)
            Row(Ability.Names[i], Bar(p.Abilities[i], 100));

        Head("기능 열셋");
        foreach (string name in Skill.Names)
            Row(name, Dots(p.Skills.GetValueOrDefault(name), Skill.MaxLevel));

        Head("언어 열넷");
        foreach (string name in Skill.Languages)
            Row(name, Dots(p.Tongues.GetValueOrDefault(name), Skill.MaxLevel));

        Head("형편");
        Row("날짜", $"{p.Date:yyyy년 M월 d일}");
        Row("있는 곳", p.CityId >= 0 ? $"{p.CityName} ({p.CityId}번)" : "바다 위");
        Row("소지금", $"{p.Gold:N0} 닢");
        Row("저금", $"{p.Savings:N0} 닢");
        Row("빚", $"{p.Debt:N0} 닢");
        Row("명성", $"{p.Fame:N0}");
        Row("악명", $"{p.Infamy:N0}");
        Row("피로", Bar(p.Fatigue, Player.MaxFatigue));
        Row("사기", Bar(p.Morale, Player.MaxMorale));
        Row("항해 일수", $"{p.DaysAtSea}일");
        Row("선원", $"{p.Crew}명 (정원 {p.MinCrew}~{p.MaxCrew})");

        Head($"함대 — 배 {p.Ships.Count}척");
        for (int i = 0; i < p.Ships.Count; i++)
        {
            var ship = p.Ships[i];
            Row(i == p.Flagship ? "★ " + ship.Name : "  " + ship.Name,
                $"{ship.Hull.Name} · 내구 {ship.Hp}/{ship.MaxHp} · 추진 {ship.Speed}"
                + $" · 적재 {ship.Capacity} · 선원 {ship.Crew} · 돛 {ship.Masts}"
                + (ship.Guns > 0 ? $" · 대포 {ship.Guns}" : ""));
        }
        if (p.Ships.Count == 0) text.Append("  (없다)\n");

        Head("부하 넷");
        for (int i = 0; i < Player.MateRoles.Length; i++)
        {
            string who = i < p.Mates.Count ? p.Mates[i] : "";
            Row(Player.MateRoles[i], who.Length > 0 ? Mate(p, who) : "(빈자리)");
        }
        Row("낯 튼 사람", $"{p.Met.Count}명");

        Head("짐");
        Row("가진 것", Items(game, p.Items, p.Items.Count == 0 ? "(없다)" : ""));
        Row("맡긴 것", Items(game, p.Stored, p.Stored.Count == 0 ? "(없다)" : ""));
        Row("보급", string.Join(" · ", p.Supplies));

        Head("발견과 계약");
        Row("얻은 힌트", $"{p.Hints.Count}개");
        Row("발견", $"{p.Discoveries.Count}개");
        Row("보고", $"{p.Announced.Count}개");
        Row("계약", p.Contract is { } deal
                     ? $"{deal.Sponsor} · {deal.City} · {deal.Amount:N0}닢 · "
                       + $"{deal.SignedOn:yyyy-MM-dd} 부터 {deal.Years}년"
                     : "(없다)");

        Head("가족");
        Row("배우자", p.Spouse.Length > 0 ? p.Spouse : "(없다)");
        Row("후손", p.Heirs.Count > 0 ? string.Join(" · ", p.Heirs) : "(없다)");

        Head("나라 사이");
        Row("적대도", p.Hostility.Count == 0
                       ? "(다 좋다)"
                       : string.Join(" · ", p.Hostility.Where(h => h.Value != 0)
                                             .Select(h => $"{Nation(game, h.Key)} {h.Value:+#;-#;0}")));
        Row("연 성문", p.OpenedGates.Count == 0
                        ? "(없다)"
                        : string.Join(" · ", p.OpenedGates.Select(game.CityName)));

        Head("후원자 친밀도");
        var close = p.Closeness.Where(c => c.Value != 0).OrderByDescending(c => c.Value).ToList();
        if (close.Count == 0) text.Append("  (아직 없다)\n");
        foreach (var (who, value) in close) Row(who, Bar(value, Player.MaxCloseness));

        return text.ToString().TrimStart('\n');
    }

    // ── 잔손 ───────────────────────────────────────────────────────────────────

    /// <summary>0~<paramref name="max"/> 를 스무 칸 막대로.</summary>
    private static string Bar(int value, int max)
    {
        int filled = max <= 0 ? 0 : Math.Clamp(value * 20 / max, 0, 20);
        return $"{value,4}  {new string('█', filled)}{new string('·', 20 - filled)}";
    }

    /// <summary>기능·언어 자리를 점으로.</summary>
    private static string Dots(int level, int max) =>
        level <= 0 ? "-" : $"{level}  {new string('●', level)}{new string('○', Math.Max(0, max - level))}";

    private static string Mate(Player p, string who) =>
        p.MateInfoOf(who) is { } info
            ? $"{info.Name} ({info.Age}세 · 명성 {info.Fame})"
            : who;

    private static string Items(Engine.Game game, IReadOnlyList<int> ids, string ifEmpty)
    {
        if (ids.Count == 0) return ifEmpty;
        return string.Join(" · ", ids.Select(id => game.Items?.Find(id)?.Name ?? $"#{id}"));
    }

    private static string Nation(Engine.Game game, int id) =>
        game.Nations?.Find(id)?.Name ?? $"나라 {id}";
}
