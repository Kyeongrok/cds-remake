using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「에디터」 탭 — 지금 게임의 주인공 능력치 · 기능 · 언어를 보고 고친다(버그 리포트 #35, 원본에 없는 것이다).
/// </summary>
/// <remarks>
/// 인물 <b>표</b>를 고치는 <see cref="PersonEditDialog"/> 와 달리 세이브에 담기는 주인공(<see cref="Player"/>)을 바로 고친다 —
/// 값은 곧장 게임에 들고 다음 저장 때 세이브(GameSave 의 Abilities · Skills · Tongues)에 남는다.
/// <code>
///   능력치   보이는 값 1~100 — 원본이 올리고 내릴 때 자르는 폭(0x00432C50, Player.AdjustAbility). 담는 값은 보이는 값 − 1
///   기능     자리 0~3(Skill.MaxLevel). 언어도 같다
///   올리기   수련(TrainingMenu.CanLearnMore, 0x00491122)과 같이 자리 무게(1 · 3 · 6) 합이 (지력 x 3 + 3) / 5 보다 작을 때만
///            한 자리 올린다. 내리기는 늘 된다
/// </code>
/// 다른 개발 기능처럼 값을 고치면 개발 기록(<see cref="Engine.HardcoreRules.Mark"/> → <see cref="Player.DevMarks"/>)에
/// 「dev:player」로 남고, 하드코어 게임이면 랭킹 기록에도 더한다 — 막지는 않는다.
/// </remarks>
public sealed partial class DevDialog
{
    /// <summary>이 탭이 고친 것을 적는 개발 기록 열쇠.</summary>
    private const string PlayerEditMark = "dev:player";

    /// <summary>「에디터」 탭 판.</summary>
    private FrameworkElement PlayerEditorPage()
    {
        var page = new StackPanel { Margin = new Thickness(12, 10, 12, 4), Width = 760 };
        var refreshers = new List<Action>();
        void RefreshAll() { foreach (var r in refreshers) r(); }

        TextBlock Label(string text, double width = 0, double size = 15, bool bold = true)
        {
            var t = new TextBlock
            {
                Text = text, Foreground = GameUi.Text, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            };
            if (width > 0) t.Width = width;
            return t;
        }

        Border Small(string text, Action run)
        {
            var b = GameUi.PushButton(text, run, 30);
            b.Margin = new Thickness(2, 0, 2, 0);
            return b;
        }

        TextBox Box()
        {
            var box = Field();
            box.Width = 52;
            box.Margin = new Thickness(2, 0, 2, 0);
            return box;
        }

        // 이름 줄.
        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        who.Children.Add(Label("주인공", 80));
        who.Children.Add(Label(_player.Name, 0, 15, bold: false));
        page.Children.Add(who);
        page.Children.Add(Label("지금 게임의 주인공을 고칩니다. 고친 값은 바로 들고 다음 저장 때 세이브에 남습니다. " +
                                "고치면 개발 기록에 남고, 하드코어 게임이면 랭킹 기록에도 남습니다.", 0, 13, bold: false));

        // ── 능력치 ──
        page.Children.Add(Section("능력치"));
        var stats = new WrapPanel();
        for (int i = 0; i < Ability.Names.Length; i++)
        {
            int which = i;
            var box = Box();
            var bonus = Label("", 70, 13, bold: false);
            void Show()
            {
                box.Text = Ability.Display(_player.Abilities[which]).ToString();
                int extra = _player.AbilityOf(which) - _player.Abilities[which];
                bonus.Text = extra != 0 ? $"({extra:+0;-0})" : "";
                bonus.ToolTip = extra != 0 ? "작위 보정이 얹힌 값입니다" : null;
            }
            void Set(int shown)
            {
                SetAbility(_player, which, shown);
                Engine.HardcoreRules.Mark(PlayerEditMark, _player);
                RefreshAll();
            }
            Hook(box, () => Set(Current(box)));
            refreshers.Add(Show);

            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 6, 3), Width = 245 };
            line.Children.Add(Label(Ability.Names[which], 60));
            line.Children.Add(Small("-", () => Set(Ability.Display(_player.Abilities[which]) - 1)));
            line.Children.Add(box);
            line.Children.Add(Small("+", () => Set(Ability.Display(_player.Abilities[which]) + 1)));
            line.Children.Add(bonus);
            stats.Children.Add(line);
        }
        page.Children.Add(stats);

        // ── 기능 · 언어 ──
        foreach (bool tongues in new[] { false, true })
        {
            var names = tongues ? Skill.Languages : Skill.Names;
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(Section(tongues ? "언어" : "기능"));
            var weightText = Label("", 0, 13, bold: false);
            weightText.Margin = new Thickness(16, 8, 0, 0);
            weightText.ToolTip = "자리 무게(1 · 3 · 6) 합 / 상한 (지력 x 3 + 3) / 5 — 무게 합이 상한보다 작아야 한 자리 더 올릴 수 있습니다";
            head.Children.Add(weightText);
            refreshers.Add(() => weightText.Text = $"{Weight(_player, tongues)} / {LearnCap(_player)}");
            page.Children.Add(head);

            var grid = new WrapPanel();
            foreach (string name in names)
            {
                string key = name;
                var box = Box();
                box.Width = 36;
                void Show() => box.Text = LevelOf(_player, key, tongues).ToString();
                void Set(int level)
                {
                    if (!SetLevel(_player, key, tongues, level)) { Show(); return; }
                    Engine.HardcoreRules.Mark(PlayerEditMark, _player);
                    RefreshAll();
                }
                Hook(box, () => Set(Current(box)));
                refreshers.Add(Show);

                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 6, 3), Width = 245 };
                line.Children.Add(Label(key, 130, 14));
                line.Children.Add(Small("-", () => Set(LevelOf(_player, key, tongues) - 1)));
                line.Children.Add(box);
                line.Children.Add(Small("+", () => Set(LevelOf(_player, key, tongues) + 1)));
                grid.Children.Add(line);
            }
            page.Children.Add(grid);
        }

        var rule = Label("능력치는 1~100, 기능 · 언어는 0~3 입니다. 기능 · 언어를 올릴 때는 수련처럼 무게 합이 상한보다 작아야 합니다. " +
                         "내리는 것은 언제나 됩니다.", 0, 13, bold: false);
        rule.Margin = new Thickness(0, 8, 0, 4);
        page.Children.Add(rule);

        RefreshAll();
        return page;

        TextBlock Section(string text)
        {
            var t = Label(text);
            t.Margin = new Thickness(0, 10, 0, 2);
            return t;
        }

        // 적은 값은 칸을 떠날 때(또는 엔터) 든다 — 「일반」 탭 소지금 칸과 같다.
        static void Hook(TextBox box, Action apply)
        {
            box.LostFocus += (_, _) => apply();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) apply(); };
        }
    }

    /// <summary>능력치 한 칸을 보이는 값으로 박는다 — 1~100 으로 자른다(<c>0x00432C50</c>).</summary>
    internal static void SetAbility(Player player, int which, int shown)
    {
        if (which < 0 || which >= player.Abilities.Length) return;
        var next = player.Abilities.ToArray();
        next[which] = Ability.Store(Math.Clamp(shown, 1, Ability.Max));
        player.SetAbilities(next);
    }

    /// <summary>그 기능(또는 언어)의 지금 자리.</summary>
    internal static int LevelOf(Player player, string name, bool tongue) =>
        tongue ? player.TongueOf(name) : player.LevelOf(name);

    /// <summary>자리 무게 합 — 1 이면 1 · 2 면 3 · 3 이면 6(TrainingMenu.CanLearnMore).</summary>
    internal static int Weight(Player player, bool tongues) =>
        (tongues ? Skill.Languages : Skill.Names)
        .Sum(name => LevelOf(player, name, tongues) switch { 1 => 1, 2 => 3, 3 => 6, _ => 0 });

    /// <summary>무게 합이 이보다 작아야 한 자리 더 배운다 — (지력 x 3 + 3) / 5(<c>0x00491122</c>).</summary>
    internal static int LearnCap(Player player) => (player.AbilityOf(Ability.Mind) * 3 + 3) / 5;

    /// <summary>
    /// 기능(또는 언어) 자리를 박는다 — 0~3 으로 자르고, 올리는 쪽은 한 자리씩 수련 규칙(무게 합 &lt; 상한)을 따진다.
    /// 하나도 못 올렸으면 false.
    /// </summary>
    internal static bool SetLevel(Player player, string name, bool tongue, int level)
    {
        int now = LevelOf(player, name, tongue);
        level = Math.Clamp(level, 0, Skill.MaxLevel);
        if (level == now) return false;
        if (level > now)
        {
            int reached = now;
            while (reached < level && Weight(player, tongue) < LearnCap(player))
                Put(++reached);
            return reached > now;
        }
        Put(level);
        return true;

        void Put(int v)
        {
            if (tongue) player.SetTongue(name, v);
            else player.SetSkill(name, v);
        }
    }
}
