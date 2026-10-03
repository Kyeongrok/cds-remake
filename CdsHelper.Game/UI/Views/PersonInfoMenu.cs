using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 인물정보 — <b>부하가 하나라도 있으면</b> 게임처럼 누구를 볼지 먼저 묻고,
/// 아무도 없으면 곧바로 제독의 판을 낸다.
/// </summary>
/// <remarks>
/// 바다에서도 도시에서도 같은 판이라 한 벌만 둔다. 예전에는 도시 창만 묻고 지도 창은
/// 곧바로 제독 판을 냈다.
/// </remarks>
internal static class PersonInfoMenu
{
    /// <param name="owner">판을 띄울 창.</param>
    /// <param name="game">이 판.</param>
    /// <param name="menu">이 줄을 낸 커맨드 창. 판을 띄우는 동안 감춰 두고, 물을 때는 그 위에 한 겹 쌓는다.</param>
    /// <param name="hold">
    /// 판이 떠 있는 동안 <b>붙잡아 달라</b>고 부르는 쪽에 알리는 손. 지도 창은 이것으로 멈춤을
    /// 쥐고 있는다 — 창을 접으면 그 알림이 멈춤을 풀어 버려, 인물정보를 보는 사이에 배가
    /// 계속 나아갔다.
    /// </param>
    public static void Show(Window owner, Engine.Game game, GameMenuHost menu,
                            Action<bool>? hold = null)
    {
        if (game.Player.MateCount == 0)
        {
            Held(hold, menu, () => PersonInfoDialog.Show(owner, game.Player, game.Directory, game));
            return;
        }
        // 모드 「인물정보 목록」 — 스폰서 일람처럼 초상화와 기능을 늘어놓고 고르게 한다.
        if (Local.Settings.GameSettings.PersonInfoEnhanced)
        {
            Held(hold, menu, () => ShowList(owner, game));
            return;
        }
        menu.Push(() => Build(owner, game, menu, hold));
    }

    /// <summary>
    /// 단축키(기본 X)로 연다 — 커맨드 창 없이 지금 창 위에 띄운다. 부하가 없으면 곧바로 제독 판, 모드 「인물정보 목록」이면
    /// 그 목록, 아니면 자리 이름을 고르는 창(플레이어 · 부관 …)을 취소할 때까지 되풀이한다.
    /// </summary>
    public static void ShowByKey(Window owner, Engine.Game game)
    {
        var player = game.Player;
        if (player.MateCount == 0) { PersonInfoDialog.Show(owner, player, game.Directory, game); return; }
        if (Local.Settings.GameSettings.PersonInfoEnhanced) { ShowList(owner, game); return; }

        var slots = new List<int> { -1 };
        var labels = new List<string> { "플레이어" };
        for (int slot = 0; slot < Player.MaxMates; slot++)
            if (player.MateAt(slot).Length > 0) { slots.Add(slot); labels.Add(Player.MateRoles[slot]); }

        while (true)
        {
            int pick = ChoiceDialog.Pick(owner, "", labels);
            if (pick < 0 || pick >= slots.Count) return;
            if (slots[pick] < 0) { PersonInfoDialog.Show(owner, player, game.Directory, game); continue; }

            string name = player.MateAt(slots[pick]);
            if (game.MateInfo(name) is { } mate)
                PersonInfoDialog.ShowMate(owner, mate, Engine.GameInfo.SheetOf(game, mate), game.Directory);
            else
                NoticeDialog.Show(owner, $"{name}의 자료를 찾지 못했다");
        }
    }

    /// <summary>
    /// 누구를 볼지 고르는 목록(모드 「인물정보 목록」) — 줄마다 왼쪽에 초상화, 이름 밑에 지닌 기능, 오른쪽에 자리.
    /// 고르면 그 사람의 인물정보 판을 띄우고, 닫으면 목록으로 돌아온다. 취소하면 끝난다.
    /// </summary>
    private static void ShowList(Window owner, Engine.Game game)
    {
        var player = game.Player;
        var portraits = Portraits.Open(game.Directory);
        var names = new List<string>();
        var faces = new List<uint[]?>();
        var skills = new List<string>();
        var roles = new List<string>();
        var open = new List<Action>();
        var notes = new List<string>();

        names.Add(player.Name);
        faces.Add(portraits?.TryGetBgra(PortraitAges.At(player.Face, player.Age, false, portraits), female: false));
        skills.Add(SkillLine(i => player.LevelOf(Skill.Names[i]), _ => true) + "\n"
                   + LanguageLine(i => player.TongueOf(Skill.Languages[i]), true));
        roles.Add("플레이어");
        open.Add(() => PersonInfoDialog.Show(owner, player, game.Directory, game));
        notes.Add(Explain(-1, player.Name, i => player.LevelOf(Skill.Names[i]), i => player.TongueOf(Skill.Languages[i])));

        for (int slot = 0; slot < Player.MaxMates; slot++)
        {
            string name = player.MateAt(slot);
            if (name.Length == 0) continue;
            var info = game.MateInfo(name);
            var row = game.World?.People.FirstOrDefault(r => r.Name == name);
            names.Add(name);
            faces.Add(info is { } who ? game.MateFace(who) : null);
            int at = slot;
            skills.Add(row is { } r
                ? SkillLine(i => i < r.Skills.Length ? r.Skills[i] : 0, i => ActiveSkill(at, i)) + "\n"
                  + LanguageLine(i => i < r.Languages.Length ? r.Languages[i] : 0, at is FirstMateSlot or InterpreterSlot)
                : "");
            roles.Add(Player.MateRoles[slot]);
            notes.Add(Explain(slot, name,
                              i => row is { } sr && i < sr.Skills.Length ? sr.Skills[i] : 0,
                              i => row is { } lr && i < lr.Languages.Length ? lr.Languages[i] : 0));
            open.Add(() =>
            {
                if (info is { } mate)
                    PersonInfoDialog.ShowMate(owner, mate, Engine.GameInfo.SheetOf(game, mate), game.Directory);
                else
                    NoticeDialog.Show(owner, $"{name}의 자료를 찾지 못했다");
            });
        }

        // 향상된 인물정보 목록 — 리디바탕 글씨의 전용 창.
        PersonListDialog.Show(owner, [.. Enumerable.Range(0, names.Count).Select(i =>
            new PersonListDialog.Entry(names[i], faces[i], skills[i], roles[i], notes[i], open[i]))]);
    }

    /// <summary>부하 자리.</summary>
    private const int FirstMateSlot = 0, NavigatorSlot = 1, SurveyorSlot = 2, InterpreterSlot = 3;

    /// <summary>
    /// 그 자리에서 <b>실제로 쓰이는 기능</b>인지 — 게임은 기능마다 제독과 정해진 한 자리를 견준다(<c>0x0047CCA0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   부관   검술 · 포술 · 사격술(해전 · 육상전) · 의학 · 과학(괴혈병 · 전염병) · 운용술(쥐) · 조선기술(뭍 수리)
    ///   항해사 항해술(바다의 하루) · 운용술(뭍의 하루)
    ///   측량사 측량(지도 · 도시 알아보기)
    ///   통역   기능은 없다 — 언어만 쓴다
    /// </code>
    /// 도서관 책 읽기는 네 자리를 다 보지만(<c>0x00463E00</c>) 여기서는 자리 몫만 친다.
    /// </remarks>
    private static bool ActiveSkill(int slot, int skill) => slot switch
    {
        FirstMateSlot => skill is Skill.Sword or Skill.Gunnery or Skill.Shooting or Skill.Medicine or Skill.Science
                                 or Skill.Handling or Skill.Shipwright,
        NavigatorSlot => skill is Skill.Sailing or Skill.Handling,
        SurveyorSlot => skill == Skill.Survey,
        _ => false,
    };

    /// <summary>
    /// 오른쪽 설명 칸 — 그 사람이 그 자리에서 <b>맡고 있는</b> 기능 · 언어가 무슨 효과를 주는지.
    /// </summary>
    /// <remarks>
    /// 효과는 엔진이 실제로 그 자리를 보는 곳에서 모았다(SeaEvents · LandBattle · SeaCombat · ShipMapWindow 의 측량 · 운용술).
    /// 도서관은 네 자리 모두의 기능을 보므로 끝에 따로 적는다.
    /// </remarks>
    /// <param name="slot">부하 자리. −1 이면 제독이다.</param>
    private static string Explain(int slot, string name, Func<int, int> skillOf, Func<int, int> tongueOf)
    {
        var lines = new List<string> { slot < 0 ? $"{name} (제독)" : $"{name} ({Player.MateRoles[slot]})", "" };
        bool any = false;
        for (int i = 0; i < Skill.Names.Length; i++)
        {
            int level = skillOf(i);
            if (level <= 0 || (slot >= 0 && !ActiveSkill(slot, i))) continue;
            if (EffectOf(slot, i) is not { Length: > 0 } effect) continue;
            lines.Add($"● {Skill.Names[i]}{level} : {effect}");
            any = true;
        }

        bool speaks = slot < 0 || slot is FirstMateSlot or InterpreterSlot;
        var tongues = Enumerable.Range(0, Skill.Languages.Length).Where(i => tongueOf(i) > 0).ToList();
        if (speaks && tongues.Count > 0)
        {
            lines.Add($"● 언어 : {string.Join(" · ", tongues.Select(i => $"{Skill.Languages[i]}{tongueOf(i)}"))}");
            lines.Add(slot < 0 ? "    그 말을 쓰는 도시 사람과 말이 통한다." : "    제독보다 잘 알아들으면 통역해 준다.");
            any = true;
        }
        if (!any) lines.Add(slot < 0 ? "지닌 기능이 없다." : "이 자리에서 쓰이는 기능이 없다.");

        // 「누구 값을 쓰는가」는 줄마다 되풀이하지 않고 끝에 한 번만 적는다.
        lines.Add("");
        lines.Add(slot < 0
            ? "※ 제독과 맡은 부하 가운데 높은 값을 쓴다."
            : "※ 제독과 견주어 높은 값을 쓴다. 흐린 기능은 이 자리에서 안 쓰인다.");
        return string.Join("\n", lines);
    }

    /// <summary>기능 하나의 효과 — 자리마다 쓰이는 몫으로 적는다. 제독(−1)은 모든 몫이다.</summary>
    private static string EffectOf(int slot, int skill) => skill switch
    {
        Skill.Sailing => "바다에서 폭풍 · 괴혈병 같은 재해를 덜 만난다.",
        Skill.Handling => slot switch
        {
            FirstMateSlot => "배에 쥐가 생기기 전에 미리 퇴치할 확률이 오른다.",
            NavigatorSlot => "뭍을 걸을 때 여행비가 준다.",
            _ => "뭍을 걸을 때 여행비가 줄고(항해사 몫), 쥐를 미리 퇴치한다(부관 몫).",
        },
        Skill.Sword => "해전 백병전 · 육상전 근접 공격 · 일기토.",
        Skill.Gunnery => "해전 포격 · 육상전 포병.",
        Skill.Shooting => "해전 · 육상전 사격과 일기토.",
        Skill.Medicine => "괴혈병 · 전염병을 다스려 선원이 덜 죽는다.",
        Skill.Science => "3 이면 괴혈병을 보리로 눌러 앉힌다.",
        Skill.Shipwright => "뭍에서 배를 고칠 수 있다.",
        Skill.Survey => "지도에서 도시를 알아보는 거리가 는다(측량 + 2칸), 도시 좌표도 보인다.",
        Skill.Rhetoric => slot < 0 ? "부하를 고용 · 재계약할 때 계약금이 준다." : "",
        _ => slot < 0 ? "도서관에서 이 기능이 필요한 힌트를 알아듣는다." : "",
    };

    /// <summary>할 줄 아는 말을 한 줄로 — 「스페인어3 아랍어2」. 하나도 없으면 「언어 없음」.</summary>
    /// <remarks>통역을 맡는 자리(제독 · 부관 · 통역)가 아니면 다 흐리게(<c>~</c>) 낸다.</remarks>
    private static string LanguageLine(Func<int, int> levelOf, bool active)
    {
        var parts = new List<string>();
        for (int i = 0; i < Skill.Languages.Length; i++)
            if (levelOf(i) is var level && level > 0) parts.Add($"{(active ? "" : "~")}{Skill.Languages[i]}{level}");
        return parts.Count > 0 ? Wrapped(parts) : "언어 없음";
    }

    /// <summary>낱말을 띄어 잇는다 — 줄 접기는 목록 창(리디바탕 글씨)이 폭에 맞춰 한다. 흐린 표시 <c>~</c> 는 그대로 둔다.</summary>
    private static string Wrapped(IReadOnlyList<string> parts) => string.Join(" ", parts);

    /// <summary>지닌 기능을 한 줄로 — 「항해술3 측량2 역사학3」. 하나도 없으면 「기능 없음」.</summary>
    /// <remarks>그 자리에서 안 쓰이는 기능은 앞에 <c>~</c> 를 붙인다 — 목록 창이 흐린 색으로 찍는다.</remarks>
    private static string SkillLine(Func<int, int> levelOf, Func<int, bool> active)
    {
        var parts = new List<string>();
        for (int i = 0; i < Skill.Names.Length; i++)
            if (levelOf(i) is var level && level > 0) parts.Add($"{(active(i) ? "" : "~")}{Skill.Names[i]}{level}");
        return parts.Count > 0 ? Wrapped(parts) : "기능 없음";
    }

    /// <summary>
    /// 붙잡아 달라고 알리고 판을 띄운다. 판이 떠 있는 동안 커맨드 창은 <b>감춰 둘 뿐</b>이다 — 판을 닫으면
    /// 고르던 창으로 되돌아온다(<c>0x0046E0BA</c> 가 고르기로 되뛴다).
    /// </summary>
    private static void Held(Action<bool>? hold, GameMenuHost menu, Action show)
    {
        hold?.Invoke(true);
        var window = menu.Window;
        if (window != null) window.Visibility = Visibility.Hidden;
        try { show(); }
        finally
        {
            if (window != null && window.IsLoaded)
            {
                window.Visibility = Visibility.Visible;
                window.Activate();
            }
            hold?.Invoke(false);
        }
    }

    /// <summary>
    /// 누구의 인물정보를 볼지 고르는 창 — 제독과 부하 네 자리다.
    /// </summary>
    /// <remarks>
    /// <b>사람이 앉은 자리만 낸다.</b> 예전에는 넷을 다 내고 빈 자리를 흐려 두었는데,
    /// 없는 자리를 굳이 보일 까닭이 없다.
    /// </remarks>
    private static GameMenu Build(Window owner, Engine.Game game, GameMenuHost menu,
                                  Action<bool>? hold = null)
    {
        var rows = new List<(string, Action?)>
        {
            ("플레이어", () => Held(hold, menu, () => PersonInfoDialog.Show(owner, game.Player, game.Directory, game))),
        };

        for (int i = 0; i < Player.MaxMates; i++)
        {
            int slot = i;
            string name = game.Player.MateAt(slot);
            if (name.Length == 0) continue;
            rows.Add((Player.MateRoles[slot], () => ShowMate(owner, game, menu, slot, hold)));
        }

        // 「취소」면 정보 차림표로 되돌아간다 — 고르기는 취소할 때까지 되풀이한다(0x0046E0BA).
        rows.Add(("취소", menu.Pop));
        return new GameMenu("", null, [.. rows]);
    }

    /// <summary>
    /// 그 자리에 앉은 부하의 인물정보 판.
    /// </summary>
    /// <remarks>
    /// 신상은 판이 찾아 준다(<see cref="Engine.Game.MateInfo"/>) — 우리 세이브를 먼저 보고,
    /// 없으면 게임 세이브의 인물표에서 채운다. 채울 데가 없으면 못 찾았다고 알린다.
    /// </remarks>
    private static void ShowMate(Window owner, Engine.Game game, GameMenuHost menu, int slot,
                                 Action<bool>? hold = null)
    {
        string name = game.Player.MateAt(slot);
        var who = game.MateInfo(name);

        Held(hold, menu, () =>
        {
            if (who is { } mate)
                PersonInfoDialog.ShowMate(owner, mate, Engine.GameInfo.SheetOf(game, mate), game.Directory);
            else
                NoticeDialog.Show(owner, $"{name}의 자료를 찾지 못했다");
        });
    }
}
