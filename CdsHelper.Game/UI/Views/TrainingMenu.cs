using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 수련하는 곳 — 조합 · 교회 · 학자 저택.
/// </summary>
/// <remarks>
/// 자리가 아니라 <b>건물 표의 가르침 비트</b>가 정한다. 비트가 선 건물이면 어디든
/// "수련" 줄이 붙는다(<see cref="Engine.Town.TownWorks"/>).
///
/// 게임의 수련 차림은 <c>0x00491390</c>, 한 자리 배우기는 <c>0x00491110</c> 이다. <b>건물마다 사람도 말도 값도
/// 다르다</b> — 문구는 <c>0x00490D90</c> 이 건물 종류(vtbl+0x48)로 셋 중 하나를 집는다: 3 교회 · 9 조합 ·
/// 그 밖(12~15 학자 저택 따위). 말은 모두 <b>가르치는 사람 얼굴</b>로 뜬다(시설 객체 <c>+0x88</c>).
/// <code>
///                 교회                          조합                         학자 저택
///   가르칠 것 없음 0x55A918 죄송하지만…불가능      0x55A948 우리집에서는…       0x55A968 미안하지만…
///   인사          0x55A7E8 주의 배움의 터전에…    0x55A830 기술을 습득하고 싶나? 0x55A848 한가지 밖에 / 0x55A878 무엇을
///   숙달          0x55A588 당신은 벌써 숙달해…    0x55A5D0 자네에게 가르쳐…    0x55A5F8 내가 가르쳐…
///   값 물음       0x55A620 기부금으로 %d닢…       0x55A670 배우고 싶다면…      0x55A6E0 수업료로…
///   돈 부족       0x55A750 안됐지만 기부금이…     0x55A788 돈도 없는 녀석…     0x55A7B8 수업료를 내지…
///   종료          0x55A8A0 용건이 있을 경우에는…  0x55A8D0 용건이 없다면…      0x55A8F0 배울 마음이 없다면…
///   값(0x490FD0)  (Lv+1)x100                    (Lv+1)x120                  (Lv+1)x150
///   달(0x491060)  3 · 6 · 12                    3 · 6 · 12                  2 · 5 · 10
/// </code>
/// 배우면(0x0049121B) 값을 치르고 화면을 어둡게 했다 밝히며(0x004A59F0 · 0x004A5AA0) <b>(200 − 지력) x 달 x 30 / 100</b> 일이
/// 지나고, 「%s%s 습득했다!」(얼굴 없음)를 낸 뒤 <b>차림을 끝낸다</b>(0x00491436). 물림 · 숙달 · 돈 부족은 목록으로 돌아간다.
/// </remarks>
/// <param name="view">이 건물을 낸 도시 창. 대사 창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 화자 얼굴이 여기서 온다.</param>
/// <param name="buildingCode">건물 코드(교회 3 · 조합 9 · 학자 저택 12~15).</param>
/// <param name="culture">이 마을 문화권. 가르치는 사람 얼굴이 여기 따라 갈린다.</param>
/// <param name="buildings">건물 표. 가르침 비트를 기술 이름으로 푼다.</param>
/// <param name="cityId">이 마을 — 가르치는 사람이 쓰는 말(그 나라 말)을 여기서 찾는다.</param>
/// <param name="patronFace">그 건물에 앉은 후원자 얼굴. 있으면 시설 사람 대신 이 얼굴로 말한다.</param>
internal sealed class TrainingMenu(Window view, Engine.Game game, int buildingCode, int culture,
                                   CityBuildingTable buildings, int cityId, uint[]? patronFace = null)
{
    private const int Church = 3, Guild = 9;

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly int _buildingCode = buildingCode;
    private readonly int _culture = culture;
    private readonly CityBuildingTable _buildings = buildings;
    private readonly int _cityId = cityId;

    private Player Player => _game.Player;

    /// <summary>가르치는 사람의 얼굴. 후원자가 앉은 건물이면 그 후원자, 아니면 화자표가 건물과 문화권으로 정한다.</summary>
    private uint[]? Face => patronFace ?? _game.SpeakerFace(_buildingCode, _culture);

    /// <summary>건물 갈래로 셋 중 하나를 고른다(<c>0x00490D90</c>).</summary>
    private T Pick<T>(T church, T guild, T scholar) =>
        _buildingCode == Church ? church : _buildingCode == Guild ? guild : scholar;

    private void Say(string text) => ConfirmDialog.Tell(_view, text, face: Face);

    /// <summary>"수련" — 가르칠 것을 늘어놓고, 하나 배우면 끝나고, 종료면 배웅한다.</summary>
    /// <remarks>
    /// 차림을 열기 전에 두 문을 본다(<c>0x004914C8</c> · <c>0x004914D3</c>). 둘 다 얼굴 없는 알림이다.
    /// <list type="number">
    /// <item><c>0x00490DF0</c> — 기능도 언어도 더 못 배우면 「더 이상 기능을 습득할 수 없습니다」(<see cref="CanLearnMore"/>).</item>
    /// <item><c>0x00490E60</c> — 가르치는 사람과 <b>통하는 말이 하나도 없으면</b> 「말이 통하지 않는 상대로부터 배울 수는 없습니다」.
    ///       <c>0x00478050</c> 이 언어 열넷마다 min(내 것, 그 사람 것)의 가장 큰 값을 보고 0 이면 막는다.
    ///       그 사람 말은 <c>0x00477EE0</c> — <b>마을 나라의 말</b>과 <b>그 건물이 가르치는 언어</b>가 3, 나머지 0 이다.</item>
    /// </list>
    /// </remarks>
    public void Teach(uint teachMask)
    {
        if (!CanLearnMore(tongues: false) && !CanLearnMore(tongues: true))
        {
            NoticeDialog.Show(_view, "더 이상 기능을 습득할 수 없습니다");
            return;
        }
        if (!SharesTongue(teachMask))
        {
            NoticeDialog.Show(_view, "말이 통하지 않는 상대로부터 배울 수는 없습니다");
            return;
        }

        var skills = _buildings.Teaches(teachMask);
        if (skills.Count == 0)
        {
            Say(Pick("죄송하지만, 여기서는 수련이 불가능합니다.", "우리집에서는 수련할 수 없네.",
                     "미안하지만, 나는 아무것도 가르쳐 줄 수 없네."));
            return;
        }

        Say(Pick("주의 배움의 터전에 잘 오셨습니다. 어떤 학문, 기능을 배우고 싶습니까?", "기술을 습득하고 싶나?",
                 skills.Count == 1 ? "가르쳐 드릴 것은 한가지 밖에 없습니다만." : "무엇을 배우고 싶은가?"));

        if (SkillLearnDialog.Show(_view, skills, LevelOf, Learn)) return;

        Say(Pick("용건이 있을 경우에는 언제든지 와 주십시오.", "용건이 없다면 오지 말게!", "배울 마음이 없다면 돌아가게."));
    }

    private static bool IsTongue(string name) => Skill.Languages.Contains(name);

    private int LevelOf(string name) => IsTongue(name) ? Player.TongueOf(name) : Player.LevelOf(name);

    /// <summary>
    /// 기능(또는 언어)을 한 자리 더 배울 수 있는지(<c>0x004696E0</c> · <c>0x00469750</c>).
    /// </summary>
    /// <remarks>
    /// 열셋(언어는 열넷) 자리마다 <b>1 이면 1 · 2 면 3 · 3 이면 6</b> 을 더한 값이
    /// <b>(지력 x 3 + 3) / 5</b> 보다 작아야 한다. 지력 73 이면 44 까지다.
    /// </remarks>
    private bool CanLearnMore(bool tongues)
    {
        var names = tongues ? Skill.Languages : Skill.Names;
        int weight = 0;
        foreach (string name in names)
            weight += (tongues ? Player.TongueOf(name) : Player.LevelOf(name)) switch { 1 => 1, 2 => 3, 3 => 6, _ => 0 };
        return weight < (Player.AbilityOf(Ability.Mind) * 3 + 3) / 5;
    }

    /// <summary>가르치는 사람과 통하는 말이 하나라도 있는지(<c>0x00490E30</c>).</summary>
    private bool SharesTongue(uint teachMask)
    {
        var spoken = new HashSet<string>(_buildings.Teaches(teachMask).Where(IsTongue));
        int nation = _game.CityRows?.NationOf(_cityId) ?? -1;
        if (_game.Nations?.Find(nation) is { } row && row.Language >= 0 && row.Language < Skill.Languages.Length)
            spoken.Add(Skill.Languages[row.Language]);
        // 나라 말을 모르는 판(표를 못 읽음)이면 막지 않는다 — 막으면 수련이 통째로 닫힌다.
        if (spoken.Count == 0) return true;
        return spoken.Any(name => Player.TongueOf(name) > 0);
    }

    /// <summary>한 자리 배운다(<c>0x00491110</c>). 배웠으면 true — 목록이 닫힌다.</summary>
    private bool Learn(string name)
    {
        // 더 배울 자리가 없으면 먼저 막는다(0x00491122 — 숙달 여부보다 앞이다).
        if (!CanLearnMore(IsTongue(name)))
        {
            NoticeDialog.Show(_view, IsTongue(name) ? "더 이상 언어를 습득할 수 없습니다!" : "더 이상 기능을 습득할 수 없습니다!");
            return false;
        }

        int level = LevelOf(name);
        if (level >= Skill.MaxLevel)
        {
            Say(Pick("당신은 벌써 숙달해 있습니다. 제가 가르쳐 드릴 것은 아무것도 없습니다.",
                     "자네에게 가르쳐 줄 것은 아무것도 없네.", "내가 가르쳐 줄 것은 아무것도 없네."));
            return false;
        }

        int cost = (level + 1) * Pick(100, 120, 150);
        int months = Pick<int[]>([3, 6, 12], [3, 6, 12], [2, 5, 10])[level];
        string ask = Pick(
            $"기부금으로 {cost}닢 받겠습니다. 습득하는데는 {months}개월 정도 걸립니다. 좋습니까?",
            $"배우고 싶다면 금화 {cost}닢 필요하네. 습득하는데는, {months}개월 정도 필요하네. 그래도 좋다면 가르쳐 주지. 괜찮은가?",
            $"수업료로 금화 {cost}닢 받겠네. 습득하는데는 {months}개월 정도 필요하네만, 괜찮은가?");
        if (!ConfirmDialog.Ask(_view, ask, face: Face)) return false;

        if (cost > Player.Gold)
        {
            Say(Pick("안됐지만 기부금이 모자랍니다. 다음 기회에 와 주십시오.", "돈도 없는 녀석에게는 볼일없다. 빨리 돌아가게!",
                     "수업료를 내지 못한다면 가르쳐 드릴 수 없습니다."));
            return false;
        }

        Player.SetGold(Player.Gold - cost);
        int days = (200 - Player.AbilityOf(Ability.Mind)) * months * 30 / 100;

        Blackout(() => Player.AdvanceDays(days));
        // 배우는 동안 쉰 셈으로 HP 가 지난 날의 10분의 1 만큼 찬다(0x00491270).
        Player.SetCondition(Player.Condition + days / 10);

        if (IsTongue(name)) Player.SetTongue(name, level + 1);
        else Player.SetSkill(name, level + 1);

        NoticeDialog.Show(_view, $"{name}{GameUi.Josa(name, "을", "를")} 습득했다!");
        RaiseByMastery(name, level + 1);
        return true;
    }

    /// <summary>숙달했으면 능력을 올리고 오른 만큼 알린다(<see cref="Engine.Town.Mastery"/>).</summary>
    private void RaiseByMastery(string name, int newLevel)
    {
        bool tongue = IsTongue(name);
        var gains = Engine.Town.Mastery.Gains(tongue ? -1 : Array.IndexOf(Skill.Names, name), tongue, newLevel,
                                              _game.Random);
        for (int k = 0; k < gains.Length; k++)
        {
            if (gains[k] <= 0) continue;
            int was = Player.AbilityOf(k);
            Player.AdjustAbility(k, gains[k]);
            int up = Math.Min(Player.AbilityOf(k), Ability.Max) - was;
            if (up > 0)
                NoticeDialog.Show(_view, $"{Ability.Names[k]}{GameUi.Josa(Ability.Names[k], "이", "가")} {up} 올라갔다!");
        }
    }

    /// <summary>배우는 동안 화면을 잠깐 어둡게 했다 밝힌다(<see cref="DayPass.Blackout"/>).</summary>
    private void Blackout(Action during) => DayPass.Blackout(_view, during);
}
