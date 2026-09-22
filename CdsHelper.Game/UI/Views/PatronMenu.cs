using System.IO;
using System.Windows;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 후원자 — 설득 · 보고 · 계약중단, 그리고 스폰서 일람.
/// </summary>
/// <remarks>
/// 왕궁만의 일이 아니다. 후원자는 총독부·상관·학자 저택 어디든 앉고, <b>앉은 자리</b>에
/// 이 줄들이 붙는다(<see cref="TownWorks"/>). 게임도 같은 자리를 계약 상태로 갈아 끼운다
/// (<c>0x0044E630</c>).
///
/// 값과 판정은 <see cref="Palace"/> 가 알고(사례 · 눈감아 주기 · 보고할 것 고르기),
/// 여기서는 묻고 알리는 차례만 맡는다.
/// </remarks>
/// <param name="view">이 건물을 낸 도시 창. 대사 창의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 주사위, 후원자 표가 여기서 온다.</param>
/// <param name="cityName">이 마을 이름. 후원자는 마을과 해로 자리가 정해진다.</param>
/// <param name="menu">그 건물의 명령 창 — 설득·보고·계약중단이 이 줄에서 뻗는다.</param>
/// <param name="cityMenu">도시 커맨드 창 — 스폰서 일람이 이 줄에서 뻗는다.</param>
/// <param name="cityTrack">이 마을 곡. 알현이 끝나면 이 곡으로 되돌린다.</param>
internal sealed class PatronMenu(Window view, Engine.Game game, string cityName,
                                 GameMenuHost menu, GameMenuHost cityMenu, int cityTrack,
                                 int culture, int cityId)
{
    private readonly int _cityTrack = cityTrack;
    private readonly int _culture = culture;
    private readonly int _cityId = cityId;
    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly string _cityName = cityName;
    private readonly GameMenuHost _menu = menu;
    private readonly GameMenuHost _cityMenu = cityMenu;

    private Player _player => _game.Player;
    private Random _random => _game.Random;

    /// <summary>물음창을 얹을 창 — 명령 창이 떠 있으면 그 위다.</summary>
    private Window Owner => _menu.Window is { Visibility: Visibility.Visible } window
        ? window : _view;

    /// <summary>
    /// 후원자와 이야기하는 동안 <b>그 건물의 명령 창을 접는다</b>.
    /// </summary>
    /// <remarks>
    /// 게임은 알현이 시작되면 명령 창을 지우고 화면 가득 대사만 낸다. 우리 명령 창은
    /// 제 창(HWND)이라 도시 그림 위에 그대로 남아, 대사 창 옆에 얹힌 채로 보였다 —
    /// 애니메이션(하트·설득)까지 그 창에 가렸다.
    ///
    /// 대사 창들은 이미 도시 그림(<c>_view</c>)을 주인으로 삼으므로 접어도 탈이 없다.
    /// 어떻게 끝나든 도로 펴 준다.
    /// </remarks>
    private void Alone(Action run)
    {
        var window = _menu.Window;
        bool shown = window is { Visibility: Visibility.Visible };
        if (shown) window!.Visibility = Visibility.Hidden;
        try { run(); }
        finally { if (shown && window!.IsLoaded) window.Visibility = Visibility.Visible; }
    }

    /// <summary>후원자 자료. 한 번만 읽어 둔다.</summary>
    private static List<Patron>? _patrons;

    /// <summary>이 건물에 앉아 있는 후원자. 없으면 null.</summary>
    public Patron? At(string kind, HashSet<string> kindsHere) =>
        new PatronService().SeatedAt(LoadPatrons(), _cityName, _player.Date.Year, kind, kindsHere);

    /// <summary>
    /// 왕궁의 "설득" — 후원자에게 힌트를 내밀어 자금을 받아 낸다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004AEF50</c> 을 따라간다. 차례가 이렇다.
    /// <list type="number">
    /// <item>후원자를 찾는다. 없으면 "이 마을에는 아는 스폰서가 없습니다".</item>
    /// <item>힌트를 고르게 한다(<c>0x004AE8E0</c>). 안 고르면 "용건이 없는가".</item>
    /// <item>안목이 힌트 등급에 못 미치면 물린다 — "이야기가 막연하네".
    ///       게임 판정은 <c>안목/20 + (등급==5 ? 1 : 2) &gt;= 등급</c> 이다(<c>0x004AF0F0</c>).</item>
    /// <item>재력이 낼 돈에 못 미치면 물린다 — "돈이…".</item>
    /// <item>통과하면 선금·기한·사례금을 걸고 승낙을 묻는다.</item>
    /// </list>
    /// 대사는 게임 EXE 에 있는 말을 그대로 옮겼다(<c>0x00545A80</c>~<c>0x00546D80</c>).
    /// <b>말투 세 벌</b>(<see cref="StyleOf"/> — 반말 · 존댓말 · 상인 반말)을 다 갈아 쓴다.
    /// </remarks>
    /// <param name="church">교회에 앉은 후원자인지 — 들머리 관문(<c>0x004AE1F0</c>)이 교회(건물 코드 3)에만 선다.</param>
    public void Persuade(Patron patron, bool church = false) => Alone(() => PersuadeNow(patron, church));

    private void PersuadeNow(Patron patron, bool church = false)
    {
        // 내밀 것이 없으면 줄부터 안 서지만, 그 사이 힌트가 사라졌으면 말없이 물린다(0x004AE8E0 의 −1).
        if (LiveHints.Count == 0) return;

        // 딴 후원자와 계약 중이면 <b>감찰관이 막는다</b>(0x0044EB70 case 0 → 0x0044FC80). 그 후원자와의 계약이면
        // 이 줄 대신 「계약중단」이 뜨므로 여기 올 일이 없다.
        string? punished = null;
        if (_player.Contract is { } deal && !Contracted(patron))
        {
            if (!InspectorLetsGo(deal)) return;
            punished = deal.Sponsor;
        }

        try
        {
            PersuadeBody(patron, church);
        }
        finally
        {
            // 감찰관을 처벌했으면 건물을 나설 때 부관이 걱정한다(0x0044E6C0, +0xBC == 2) —
            // 새 계약을 맺었는지에 따라 말이 갈린다.
            if (punished != null)
                TalkDialog.Say(_view, _game.AideFace, "",
                    _player.Contract is { } now && now.Sponsor == patron.Name
                        ? "처벌한 것이 안 좋았던 것 같습니다. 일단, 전 스폰서에게는 접근하지 않는 편이 좋겠군요."
                        : "제독, 곤란하게 되었습니다... 위험하니 일단 스폰서와는 가까이 하지 않는 것이 좋을 것 같군요.");
        }
    }

    /// <summary>
    /// 계약 중에 딴 후원자를 설득하려 할 때 감찰관이 막는다(<c>0x0044FC80</c>). 처벌하고 넘어가면 true.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   맡은 발견물을 이미 찾았으면   감찰관 「농담이지요! … 보고하지 않으면 안됩니다!」 → 끝 (0x005324E0 · 0x00532538)
    ///   부관   「제독, 지금 스폰서와의 계약을 백지화할 작정이십니까?」 (0x0054C3D8)
    ///   감찰관 「노, 농담을! 그러면 제 입장이 곤란해집니다. 생각을 바꿔 주십시오.」
    ///   [감찰관을 처벌한다 / 생각을 바꾼다]
    ///     생각을 바꾼다 → 「농담이시겠지요.」 → 끝
    ///     처벌한다     → 「예에엣! 요, 용서를~!!」 「자, 얌전히 이쪽으로 오게!」
    ///                    → 옛 후원자 친밀도 −50 · 배신 표시(비트 13) · 계약 해제(0x0044EE30(2)) → 설득을 잇는다
    /// </code>
    /// 계약금을 돌려 달라는 말은 없고, 옛 후원자의 남은 기한도 안 지운다 — 그 기한이 다 흐르면 추격이 시작된다.
    /// </remarks>
    private bool InspectorLetsGo(Contract deal)
    {
        var inspectorFace = _game.Faces?.TryGetBgra(Inspector.Face, female: false);
        void Inspector_(string words) => TalkDialog.Say(_view, inspectorFace, "", words);

        var old = _game.Sponsors?.FindByName(deal.Sponsor);
        string oldName = $"{old?.Name ?? deal.Sponsor} {old?.Honorific ?? "각하"}";

        if (Palace.ReportTargets(_player, true, _game.Discoveries?.Table, _game.Hints).Count > 0)
        {
            Inspector_(deal.City == _cityName
                ? $"농담이지요! 빨리 {oldName}에게 보고하지 않으면 안됩니다!"
                : $"농담이지요! 지금은 한 시각이라도 빨리 {deal.City}에 돌아가 {oldName}에게 보고하지 않으면 안됩니다!");
            return false;
        }

        TalkDialog.Say(_view, _game.AideFace, "", "제독, 지금 스폰서와의 계약을 백지화할 작정이십니까?");
        Inspector_("노, 농담을! 그러면 제 입장이 곤란해집니다. 생각을 바꿔 주십시오.");
        if (ChoiceDialog.Pick(_view, "", ["감찰관을 처벌한다", "생각을 바꾼다"]) != 0)
        {
            Inspector_("농담이시겠지요.");
            return false;
        }

        Inspector_("예에엣! 요, 용서를~!!");
        GameDialog.Show(_view, "자, 얌전히 이쪽으로 오게!");

        _player.Endear(deal.Sponsor, -50);
        _player.Betray(deal.Sponsor, deal.City, deal.DueOn);
        // 나설 때 부하 재계약이 먼저고 빌린 배 돌려주기가 뒤다(0x0044E6D5 · 0x0044E6DC) — 감찰관을 처벌한
        // 자리(+0xBC == 2)에서도 그대로 돈다.
        _player.EndContract();
        RecontractMates();
        MutinousLentShips(deal.Sponsor);
        return true;
    }

    /// <summary>
    /// 후원자와 말이 통하는지 — 게임의 <c>0x00468F70</c>(제독·부관·통역 가운데 가장 잘 통하는 수준)이 3 이상인지.
    /// </summary>
    /// <remarks>
    /// 후원자는 표 <c>+0x3A</c> 에 비트가 선 말을 수준 3 으로 한다(<c>0x004AD7B0</c>). 두 사람의 통하는 수준은
    /// 말마다 낮은 쪽을 잡아 그 가운데 큰 값이다(<c>0x00478050</c>). 표를 못 읽었으면 막지 않는다.
    /// </remarks>
    private bool SpeaksWith(SponsorTable.Sponsor? sponsor)
    {
        if (sponsor is not { Languages: not 0 } s) return true;
        var people = _game.World?.People;
        for (int i = 0; i < Skill.Languages.Length; i++)
        {
            if ((s.Languages & (1 << i)) == 0) continue;
            if (_player.TongueOf(Skill.Languages[i]) >= SponsorTongue) return true;
            foreach (int slot in (int[])[0, 3])
            {
                string name = _player.MateAt(slot);
                if (name.Length > 0 && people?.FirstOrDefault(r => r.Name == name) is { } row
                    && i < row.Languages.Length && row.Languages[i] >= SponsorTongue) return true;
            }
        }
        return false;
    }

    /// <summary>교회 들머리 관문의 안목 배수(<c>0x004AE1FD</c> 의 <c>push 0x50</c>).</summary>
    private const int ChurchEye = 80;

    /// <summary>교회의 건물 코드 — 돌려보내는 사람의 얼굴이 이 자리 화자다(<c>0x004AE21C</c> 의 <c>+0x84</c>).</summary>
    private const int ChurchCode = 3;

    /// <summary>후원자가 하는 말의 수준(<c>0x004AD7D3</c> 의 <c>and eax, 3</c>) — 이만큼 통해야 설득한다.</summary>
    private const int SponsorTongue = 3;

    private void PersuadeBody(Patron patron, bool church = false)
    {

        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        string shown = sponsor?.Name ?? patron.Name;             // 게임 이름은 가운뎃점이 들어간다
        string sir = sponsor?.Honorific ?? "각하";
        string me = _player.Name;

        var face = FaceOf(patron);
        void Say(string words) => TalkDialog.Say(_view, face, "", words);
        void Steward(string words) => TalkDialog.Say(_view, StewardFace(), "", words);

        // 교회는 들머리에 관문이 하나 더 있다(0x004AE1F0 — 건물 코드 3 일 때만). 아직 못 만난 후원자
        // (비트 15)면 안목 x 80 을 명성과 견주고(0x0044E740(0x50)), 모자라면 교회 사람이 돌려보낸다.
        if (church && !_player.HasMet(patron.Name) && !(sponsor is { } met && _player.HasMet(met.Name))
            && (sponsor?.Eye ?? patron.Fame / 70) * ChurchEye > _player.Fame)
        {
            TalkDialog.Say(_view, _game.SpeakerFace(ChurchCode, _culture), "",
                           $"{shown}님은 바쁘셔서 만나실 수 없습니다.");
            return;
        }

        // 기분이 상한 후원자는 문간에서 돌려보낸다(0x004AEFC1, 후원자 비트 14) — 설득을 물렸거나
        // 계약 결판을 치른 뒤 30일 동안이다(0x004A2AD0 이 푼다).
        if (_player.IsSulking(patron.Name))
        {
            Steward($"{shown} {sir}께서는 꽤 기분이 안좋은 상태이니 여기서 일단 돌아가 주십시오.");
            return;
        }

        // 말이 통해야 설득한다(0x004AEFE5 → 0x004AE0B0) — 후원자가 하는 말(표 +0x3A 비트, 수준 3) 가운데
        // 제독·부관·통역 누군가 수준 3 이상이어야 한다(0x00468F70). 모자라면 두 말 가운데 하나다(0x00469680).
        if (!SpeaksWith(sponsor))
        {
            if (_player.MateAt(0).Length > 0)
                TalkDialog.Say(_view, _game.AideFace, "", "말이 통하지 않는 것만은 어쩔 수가 없군요.");
            else
                GameDialog.Show(_view, "말이 통하지 않아서 상대해 주지 않았습니다.");
            return;
        }

        // 1494년부터 포르투갈·에스파니아 사이에는 불가침 조약이 있다(0x004AE0F0 → 0x004698C0) —
        // 남의 나라 후원자와 계약하면 배반자가 된다고 <b>알려만 주고</b> 막지는 않는다.
        int theirNation = Array.FindIndex(Player.Nations, n => n == patron.Nationality);
        if (Palace.TreatyWarning(_player.Nation, theirNation, _player.Date.Year))
        {
            string mine = Player.Nations[_player.Nation], theirs = patron.Nationality;
            string word = $"우리 {mine}{GameUi.Josa(mine, "과", "와")} {theirs}의 사이에는 "
                        + $"불가침 조약이 맺어져 있습니다. {theirs}의 스폰서와 계약하게 되면, "
                        + "배반자가 되어 모국에 돌아갈 수 없게 됩니다.";
            if (_game.AideFace is { } aide) TalkDialog.Say(_view, aide, "", $"제독, 알고 계시리라 생각합니다만, {word}");
            else GameDialog.Show(_view, $"현재 {word}");
        }

        // 명성 관문이다. 모자라면 집사가 문간에서 돌려보낸다(게임 0x004AE260 — 어느 건물이든 건다).
        // 교회에만 서는 들머리 관문(0x004AE1F0)은 위에서 따로 봤다.
        //
        // "명성치가 모자랍니다" 는 내지 않는다 — 게임에서도 그 줄은 디버그 깃발
        // (0x00580C6C 의 2비트)이 서 있을 때만 나오는 기록용이지 사람에게 보이는 말이 아니다.
        //
        // <b>식이 문간(0x0044E740)과 다르다.</b> 0x004AE260 은 후원자 안목(표 +0x20) x 100 을 명성 + 1500 과
        // 견준다 — 문간은 안목 x 70 을 명성 그대로와 견준다(patrons.json 의 fame 이 그 값이다). 예전에는 문간 식을
        // 여기에도 써서, 명성 1500 인 초심자 라몬이 안목 24 인 파브리스(2400 ≤ 3000)에게 막혔다.
        int eye = _game.Sponsors?.FindByName(patron.Name)?.Eye ?? patron.Fame / 70;
        if (!Palace.Admitted(eye, _player.Fame))
        {
            // 문 앞에서 돌려보낼 때 소리가 한 번 난다(닻 소리와 같은 파트다).
            _game.Sfx?.Play(SoundBank.TurnedAwayPart);
            Steward($"죄송하지만, {shown} {sir}께서는 바쁘셔서 만나실 수 없습니다. 다른 날에 와 주십시오.");

            // 명성이 오백만 더 있으면 <b>집사를 매수</b>해 뚫을 수 있다(0x004AE2E1).
            if (!Palace.BribeAdmits(eye, _player.Fame)) return;
            if (ChoiceDialog.Ask(_view, "", ["매수한다", "포기하고 돌아간다"]) != 0) return;
            if (!ConfirmDialog.Ask(_view, "집사에게 뇌물을 주겠습니다. 좋습니까?")) return;   // 0x00545A98

            int fee = Palace.StewardFee(eye);
            if (_player.Gold < fee)
            {
                NoticeDialog.Show(_view, "뇌물에 사용할 금화가 모자랍니다");
                return;
            }
            if (!Palace.StewardTalks(_player.LevelOf(Skill.Names[Skill.Rhetoric]),
                                     _player.AbilityOf(Ability.Charm), _random))
            {
                Steward("만날 수 없는 사람은 만날 수 없습니다. 돌아가 주십시오.");
                return;
            }

            Steward($"......어쩔 수 없군요. {shown}에게 교섭해 보지요. 무기는 여기서 보관하겠습니다.");
            _player.Pay(fee);
        }

        // 관문을 넘으면 집사가 맞고, 무기를 맡기고, 안에 들여보낸 뒤 주인에게 알린다.
        // 게임의 알현 순서 그대로다(문구도 EXE 0x005459B8~ 에서 옮겼다).
        //
        // 곡도 여기서 바뀐다 — 돌려보낼 때는 그대로고, 인사를 받고 들어갈 때부터 알현 곡이다.
        // 나갈 때 도시 곡으로 되돌리는 것은 아래 try/finally 가 맡는다.
        _game.Bgm.Play(BgmPlayer.SponsorTrack);
        try
        {
            Audience(patron, shown, sir, me, face, Say, Steward);
        }
        finally
        {
            _game.Bgm.Play(_cityTrack);   // 그 자리를 나오면 도시 곡으로 돌아간다
        }
    }

    /// <summary>알현 — 집사가 들여보낸 뒤부터 계약을 묻기까지.</summary>
    private void Audience(Patron patron, string shown, string sir, string me,
                          uint[]? face, Action<string> Say, Action<string> Steward)
    {
        // 설득 대사도 말투 세 벌이다(0x004694C0 이 반말 · 존댓말 · 상인 반말 셋을 받는다).
        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        Steward($"오래 기다리셨습니다. 제가 {shown} {sir}의 집사입니다.");
        Steward("무기는 여기서 보관하겠습니다. 그러면 안으로 들어가십시오.");

        // 주인이 용건을 묻는다(0x004AE490) — <b>처음 보는 사이인지</b>로 먼저 갈리고
        // (후원자 <c>+0x28</c> 의 비트 15), 그 다음 말투 셋으로 갈린다.
        // <code>
        //   처음    0x00545B98 집사 → 0x00545BD8 · 0x00545C00 · 0x00545C48
        //   구면    0x00545C78 집사 → 0x00545C98 · 0x00545CC0 · 0x00545CF8
        // </code>
        bool known = _player.HasMet(patron.Name);
        if (known)
        {
            Steward($"{sir}. {me}{Particle(me)} 데리고 왔습니다.");
            Say(Pick3($"흐음. {me}, 이번 모험 목적은 무엇인가?",
                      $"오래간만이군요. {me}, 이번은 어떤 모험을 할 작정입니까?",
                      $"오오, {me}인가. 잘 지냈는가. 또 모험 이야긴가?"));
        }
        else
        {
            // 처음 오면 집사가 나라까지 붙여 이른다(0x004AE53F 이 나라 이름표 0x00560AA8 을 읽는다).
            Steward($"{sir}. {_player.NationName}의 {me}{Particle(me)} 데리고 왔습니다. 모험의 지원을 신청하고 있습니다.");
            Say(Pick3("호오, 그렇다면 모험 목적을 말해 보게.",
                      "어떤 모험을 하고 싶습니까? 내용에 따라서 거기에 맞는 자금을 드리지요.",
                      "모험 지원인가. 그래, 무엇을 찾으러 갈건가?"));
        }

        // 이 자리에서 낯을 튼다. 게임도 이 물음 바로 뒤에 후원자의 "아직 못 만남" 표를
        // 지운다(0x004AE595 — 객체 +0x28 의 비트 15). 그래야 스폰서 일람에 뜬다.
        _player.Meet(patron.Name);

        // 얻었고 아직 보고 안 한 힌트만 내밀 수 있다 — 원본 힌트 상태로 13 인 것이다
        // (0x0044E7B0 이 상태가 맞는 것만 목록에 올린다). 보고를 마치면 0x004AACA0 이
        // bit1 을 켜서 여기서도 빠진다.
        var mine = LiveHints;
        var names = mine.Select(id => GameInfo.HintLabel(_game, id)).ToList();

        // 이 후원자가 좋아하는 갈래의 힌트는 갈색(#DEC6AD) 바탕으로 도드라지게 한다 — 설득이
        // 갈래 취향을 그대로 따지므로(후원자 정보의 「발견물의 취향」) 고르기 전에 보이는 편이 낫다.
        var liked = mine.Select(id => _game.Hints?.Find(id) is { } h && patron.Likes(h.Category)).ToList();
        int row = HintListDialog.Pick(_view, names, "제안 선택", marks: liked);
        if (row < 0)
        {
            // 0x004AF415 — 반말 쪽만 제독 이름을 부른다.
            Say(Pick3($"{me}, 사람을 방문해 놓고 꽤 무례하군. 그만 나가게!",
                      $"{me}, 용건도 없으면서 무턱대고 방문하는 것은 무례한 일입니다. 다음에 와 주십시오.",
                      "뭔가, 용건이 없는가? 이쪽은 바쁘네, 빨리 나가주게."));
            return;
        }

        var hint = _game.Hints?.Find(mine[row]);
        if (hint == null)
        {
            // 0x004AF14D — 이야기가 후원자 안목에 벅찰 때의 말이다.
            Say(Pick3("흠, 원조해 주고 싶은 마음은 많지만.",
                      "원조해 드리고 싶지만, 그렇게 큰 모험은, 저로서는 도저히...",
                      "가능한 한 원조해 주고 싶지만, 너무 이야기가 엄청나네."));
            return;
        }

        var it = hint.Value;

        // 받아 줄지는 게임 셈 그대로 가린다(Persuasion 참고) — 이야기 크기, 좋아하는
        // 갈래, 안목·웅변·매력 굴림 차례다.
        var verdict = Decide(it, patron, _game.Sponsors?.FindByName(patron.Name),
                             face, Say, mine.Count > 1);
        // 아주 물리면 후원자가 기분이 상한다(0x004AE72A) — 한 달 동안 문간에서 돌려보낸다.
        if (verdict is Persuasion.Verdict.Refused) _player.Sulk(patron.Name);
        if (verdict is Persuasion.Verdict.Refused or Persuasion.Verdict.TooBig
                    or Persuasion.Verdict.AskAnother) return;

        // 안목 관문(0x004AF0DE) — 자금까지 다 셈해 놓고 마지막에 <b>후원자의 눈</b>을 따진다.
        // 못 미치면 "이야기가 막연하다" 며 물리는데, 기분은 상하지 않아 다시 와도 된다.
        if (!Persuasion.Grasps(_game.Sponsors?.FindByName(patron.Name)?.Eye ?? patron.Discernment,
                               it.Grade))
        {
            Say(Pick3("흐음, 원조하고 싶은 마음은 많지만.",
                      "원조해 드리고 싶지만, 그렇게 큰 모험은 저로서는 도저히...",
                      "가능한 한 원조해 주고 싶지만, 너무나 이야기가 막연하네."));
            return;
        }

        int funds = Persuasion.Funds(
            it.Funds,
            _game.Sponsors?.FindByName(patron.Name)?.Closeness ?? DefaultCloseness,
            verdict);

        // 재력 판정(0x004AF169) — 견주는 것은 <b>지갑</b>이다. 낼 돈이 모자라도 스무 닢만
        // 넘으면 있는 만큼으로 깎아 내주고, 그마저 없으면 물린다(0x004AF264).
        int purse = _player.PurseOf(patron.Name, patron.Wealth);
        if (funds > purse)
        {
            if (purse <= Palace.PurseFloor)
            {
                Say(Pick3("흠, 원조 못 할 것은 없지만 요즘 지출이 많아서. 다른 이야기를 가지고 오는 것이 좋겠네.",
                          "자금을 대 드리고 싶지만..., 아무래도..., 안됐지만 힘이 되드릴 수 없군요.",
                          "원조는 해 주고 싶지만, 흐~음, 돈이... , 또 다음번이다."));
                return;
            }
            funds = purse;
        }

        // 계약금은 반으로 나뉜다 — 절반은 선금으로 그 자리에서 받고, 절반은 성공한 뒤에
        // 받는다. 제안 대사도 그 절반을 두 번 부른다(0x004AF1B6 이 계약금/2 를 두 번 넘기고,
        // 서식은 0x00546B80 "먼저 금화 %ld닢을 주겠다 … %ld닢의 사례" 다).
        int years = it.Deadline;
        int half = funds / 2;

        Say(string.Format(Pick3(
            "그러면, 자금으로 금화 {0}닢을 주겠다. {1}년 내에 목적을 달성하면, 거기다 {0}닢을 더 약속하겠네.",
            "그러면, 먼저 금화 {0}닢을 드리겠습니다. 또, {1}년 내에 달성했을 때에는, 거기다 금화 {0}닢을 더 약속하겠습니다.",
            "모험하는데 돈은 필요하겠지. 먼저 금화 {0}닢을 주겠다. {1}년 내에 성공하면 {0}닢의 사례를 약속하겠네. 이것으로 어떤가."),
            half, years));

        // 말과 고르기는 <b>따로 뜨는 창 둘</b>이다. 말은 얼굴을 단 알림창(0x004694C0)이고,
        // 고르기는 제목 띠에 기간·금화를 이고 승낙/교섭 두 줄만 놓인 창(0x00469A70,
        // 0x004AF22A 가 줄 수 2 를 넘긴다)이다. 두 줄이 같은 무늬라 Pick 을 쓴다.
        // <b>제목 띠에는 계약금 전부가 뜬다</b> — 절반이 아니다(서식은 0x00546C18
        // " 기간%d년 금화 %ld닢 "). 절반은 바로 위 대사가 이미 두 번 불렀다.
        int pick = ChoiceDialog.Pick(_view, $" 기간{years}년 금화 {funds}닢 ",
                                     ["승낙한다", "교섭한다"]);
        if (pick < 0) return;

        // 「교섭한다」를 골랐을 때만 한 번 더 묻는다 — <b>되풀이는 없다</b>.
        if (pick != 0 && !Bargain(patron, face, Say, ref funds, ref years)) return;

        // 계약을 적어 두고 선금을 받는다. 게임도 이 자리에서 소지금에 계약금의 절반을
        // 더한다(0x004ADF3E).
        // 감찰관은 계약마다 반드시 하나 붙는다 — 얼굴은 늘 같고 이름만 갈린다.
        string inspector = Inspector.Pick(_culture, _random);

        _player.Sign(new Contract(it.Id, patron.Name, _cityName, funds,
                                  _player.Date, years, inspector));

        // 선금은 <b>후원자 지갑에서</b> 나간다(0x004ADF4A) — 저절로 차지 않으므로
        // 같은 사람에게 잇달아 계약을 맺으면 점점 적게 받는다.
        _player.SpendPurse(patron.Name, -(funds / 2), patron.Wealth);

        // 맺고 나면 배 → 감찰관 → 배웅 차례다(게임 0x004AF2A3 · 0x004AF2B7 · 0x004AF3A4).
        LendShips(funds, Say, Pick3);
        SendInspector(inspector, me, Say, Pick3);

        // 배웅도 신분마다 세 벌이다(0x004AF3A8 이 0x00546D28 · 0x00546D48 · 0x00546DA8 을 넘긴다).
        Say(Pick3($"그러면, {me}, 기대하고 있겠네.",
                  $"그러면, {me}, 긴 여행이 되리라 생각되는데 조심하십시오. 여행의 성공을 기도하고 있겠습니다.",
                  "기대하고 있겠네. 훌륭히 성공을 거두고 돌아오게."));
    }

    /// <summary>
    /// 후원자가 감찰관을 딸려 보낸다 — 배를 대 준 바로 다음이다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x004AF2B7</c>(<c>0x004AF450</c> 이 사람을 짓는다) 다음에 오는 말 두 마디다.
    /// <code>
    ///   546CE0  "감찰관으로서 %s%s 따라가 주게. %s, 부탁하네."   후원자 얼굴
    ///   546D10  "하앗, 알겠습니다."                              감찰관 얼굴(232)
    /// </code>
    /// 앞의 것은 말투마다 세 벌이다(<c>0x00546C80</c>·<c>0x00546CB0</c>·<c>0x00546CE0</c>).
    /// </remarks>
    private void SendInspector(string inspector, string me, Action<string> Say,
                               Func<string, string, string, string> Pick3)
    {
        Say(Pick3($"{inspector}, 감찰관으로서 여기 있는 {me}{Particle(me)} 따라가게.",
                  $"{inspector}, 자네를 거기 있는 {me}의 감찰관에 임명하겠다.",
                  $"감찰관으로서 {me}{Particle(me)} 따라가 주게. {inspector}, 부탁하네."));

        var face = _game.Faces?.TryGetBgra(Inspector.Face, female: false);
        TalkDialog.Say(_view, face, "", "하앗, 알겠습니다.");
    }

    /// <summary>계약금이 이만큼 오를 때마다 배가 한 척씩 는다(<c>0x004105F4</c> 의 <c>0xEA60</c>).</summary>
    private const int GoldPerShip = 60000;

    /// <summary>
    /// 계약 직후 스폰서가 항구에 배를 대 준다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00410620</c> 이다. 설득이 끝나면 <c>0x004AF2A3</c> 이 <b>첫 인자에 1 을
    /// 넣어</b> 부르는데, 그 갈래는 묻지 않고 그냥 준다(그냥 찾아가 빌릴 때는 0 이고,
    /// 그때만 "배를 빌리겠습니까?" <c>0x0055C4D8</c> 를 묻는다).
    /// <code>
    ///   410677  척수 = 0x004105C0(빌려줄 수 있는 배, 계약금)
    ///     4105F4    계약금 / 60000 + 1
    ///     410605    min(그 값, 항구에 세워 둔 스폰서 배 수)
    ///     41060B    min(그 값, 8 - 내 함대 척수)
    ///   410718  0x00410800 — 배가 한 척이라도 있으면 1
    ///   410724  그러면 "배를 빌리겠습니까?"(0x55C4D8) 를 묻는다. 예(2) 라야 준다
    ///   41073A  아니라면 "그런가. 그렇다면, 좋을 대로 하게."(0x55C6C0 세 벌) 로 물린다
    ///   4106ED  "…배 %d척을 항구에 준비시켜 놓겠네" (문화권 3벌: 0x55C3F8·0x55C438·0x55C480)
    ///   410798  0x0040FA00 이 배 레코드(0x005A4E18) 의 <b>+0x64 에 1</b> 을 박는다 = 대출 표시
    ///   410763  줄 배가 없으면 "…배가 전부 나가고 없네" (0x55C718 세 벌)
    /// </code>
    /// 화면에서 본 것은 셋째 벌이라 그것을 쓴다 — 계약금 13,500닢에 1척이 나왔고
    /// <c>13500 / 60000 + 1 = 1</c> 로 셈이 맞는다.
    ///
    /// <b>못 옮긴 것 둘.</b> 스폰서마다 항구에 세워 둔 배 무리가 우리 쪽에 없어 가운데
    /// 상한을 뺐고, 선체도 고를 데가 없어 제일 싼 것으로 세운다.
    /// 계약이 끝나면 <see cref="ReturnLentShips"/> 가 거둬 간다(<c>0x0040FE40</c>) — 다만
    /// 후원자 나라가 멸망해 바다에서 계약이 깨질 때는 아직 안 거둔다(그 자리는 도시 밖이다).
    /// </remarks>
    private void LendShips(int funds, Action<string> Say, Func<string, string, string, string> Pick3)
    {
        // 모드에서 「배 빌림 묻기」를 끄면 배를 빌리는 대화와 지급 자체를 모두 건너뛴다.
        if (!Local.Settings.GameSettings.AskLendShips) return;

        // 함대가 이 도시에 없으면(걸어 들어온 마을) 한 척도 못 빌린다(0x004105D7 → 0x0040E1C0(도시, 0)).
        int ships = !_player.FleetHere(_cityId) ? 0
                  : Math.Min(funds / GoldPerShip + 1, Player.MaxShips - _player.Ships.Count);
        if (ships <= 0)
        {
            // 0x0055C718 · 0x0055C760 · 0x0055C7A8
            Say(Pick3("흐음, 빌려주고 싶은 마음은 굴뚝같지만 배가 전부 나가고 없네. 다시 오게.",
                      "안됐지만, 준비할 수 있는 배가 없습니다. 자신의 힘으로 해결해 주십시오.",
                      "흐~음, 때가 나쁘군. 지금은 가지고 있는 배가 없네. 다시 오게."));
            return;
        }

        // <b>후원자가 먼저 내주겠다고 말한 뒤에</b> 빌릴지 묻는다 — 물음창이 먼저 뜨면
        // 무엇을 빌리는지 모른 채 고르게 된다. 게임 차례가 그렇다.
        // 계약을 맺으며 내주는 벌이다(0x004106ED) — 계약 뒤에 따로 조를 때는 딴 말을 한다
        // (0x004106A4 「좋다. 배를 %d척…」 · 0x004106C7 「뭐라고? 또 배를 빌려 달라고…」).
        // 우리는 계약 자리에서만 빌려주므로 첫 벌만 쓴다.
        Say(string.Format(Pick3(
            "그리고, 배를 {0}척 항구에 준비시켜 놓겠네. 충분히 사용하게나.",
            "예예, 항구에 배를 {0}척 준비시켜 놓겠습니다. 모험에 도움이 될 것입니다.",
            "모험의 도움을 위해서 배 {0}척을 항구에 준비시켜 놓겠네. 마음대로 사용해도 상관없네."), ships));

        // <b>내 배</b>가 한 척이라도 있으면 빌릴지 묻는다. 한 척도 없으면 묻지 않고 그냥 준다
        // (0x00410718 이 0x00410800 으로 가려, 0 이면 물음창을 건너뛴다). 그 셈(0x0040E210 · 0x0040E320)은
        // 제독 것인 배만 센다 — <b>빌린 배는 안 친다</b>. 그래서 배를 안 산 초심자는 늘 묻지 않고 받는다.
        if ((_player.Ships.Any(s => !s.Lent) || _player.DockedAt(_cityId).Any(s => !s.Lent))
            && !ConfirmDialog.Ask(_view, "배를 빌리겠습니까?"))
        {
            // 0x0055C6C0 · 0x0055C6E8 · 0x0055C708
            Say(Pick3("그런가. 그렇다면, 좋을 대로 하게.",
                      "그렇습니까. 좋을 대로 하십시오.",
                      "그래, 괜찮겠나."));
            return;
        }

        if (Hull.All.MinBy(h => h.Price) is not { } hull) return;

        // 함대에 곧장 들어가지 않는다 — 항구에 「대출 · 계류」로 대 놓인다.
        int given = 0;
        for (int i = 0; i < ships; i++) if (_player.Give(hull, _cityId)) given++;
        if (given == 0) return;

        if (_player.Contract is { } deal) deal.ShipsLent = true;
    }

    /// <summary>
    /// 「교섭한다」 — 자금을 올리거나 기간을 늘린다(<c>0x004AEDA0</c>).
    /// </summary>
    /// <remarks>
    /// 세 줄짜리 고르기 창이 뜬다(<c>0x004AEDF7</c> 이 줄 수 3 을 넘긴다).
    /// <code>
    ///   자금 증가   자금 x 1.3, 기간 절반   — <b>기간이 2년 이상이라야 고를 수 있다</b>
    ///   기간 연장   기간 x 1.5, 자금 x 0.7  — 1년이면 2년으로
    ///   변경 없음   그대로
    /// </code>
    /// 두 셈 다 <b>10닢 단위로 내린다</b> — 게임이 <c>x13/10/10*5*2</c> 꼴로 셈해서다
    /// (<c>0x004AEE32</c> · <c>0x004AEEE4</c>).
    ///
    /// 자금을 올려 달랬는데 후원자의 재력이 못 미치면 쫓겨난다(<c>0x004AEE7D</c>) —
    /// "탐욕스러운 놈! 너 같은 녀석에게 볼일 없다. 썩 꺼져라!"(<c>0x00546430</c>).
    /// </remarks>
    /// <returns>계약으로 넘어가면 true, 물러났거나 쫓겨났으면 false.</returns>
    private bool Bargain(Patron patron, uint[]? face, Action<string> Say,
                         ref int funds, ref int years)
    {
        int at = ChoiceDialog.Pick(_view, " 교섭 ",
            [("자금 증가", years > 1), ("기간 연장", true), ("변경 없음", true)]);

        // <b>「변경 없음」은 바로 계약으로 간다</b> — 제안을 다시 묻지 않는다.
        // 게임도 그 갈래가 1 을 내고(0x004AEF40), 부르는 쪽은 1 이면 계약을 맺는다
        // (0x004AF249 가 0 이 아니면 0x004AF28A 로 간다).
        if (at < 0 || at == 2) return true;

        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        if (at == 0)
        {
            int raised = To10(funds * 13 / 10);
            // 정적 재력 상한과 <b>지금 지갑</b>을 둘 다 넘지 못한다(0x004AEE7B · 0x004AEE85).
            if (patron.Wealth < raised || _player.PurseOf(patron.Name, patron.Wealth) < raised
                || years <= 1)
            {
                Say(Pick3("탐욕스러운 놈! 너 같은 녀석에게 볼일 없다. 썩 꺼져라!",
                          "장래성이 있는 자라 생각했었는데 안됐습니다. 물러가 주십시오!",
                          "너 같이 욕심많은 녀석에게 원조할 수 없다! 썩 꺼져라!!"));
                return false;
            }

            funds = raised;
            years = years * 5 / 10;
            Say(string.Format(Pick3(
                "흐음, 좋다. 돈은 전부 {0}닢 주겠다. 그대신 기간은 {1}년으로 줄어드네. 이의없겠지.",
                "좋습니다. 금화는 전부 {0}닢 드리겠습니다. 그대신, 기간은 {1}년으로 하겠습니다. 좋지요.",
                "뭐, 돈을 더 달라고. 흐~음, 그렇다면 전부 금화 {0}닢을 주겠다. 그대신, 기간은 {1}년이네."), funds, years));
        }
        else
        {
            funds = To10(funds * 7 / 10);
            years = years > 1 ? years * 15 / 10 : years + 1;
            Say(string.Format(Pick3(
                "흐음, 좋다. 기간은 {0}년으로 늘려도 상관없네. 그대신 돈은 전부 {1}닢 이상 줄 수 없네. 이의없겠지.",
                "알겠습니다. 그러면, 기간을 {0}년으로 하지요. 그대신 금화는 전부 {1}닢으로 하겠습니다. 좋습니까?",
                "그것도 그렇군. 그러면, 기간을 {0}년으로 늘리지. 그렇다면 돈은 전부 {1}닢이 되겠군."), years, funds));
        }

        // <b>되묻지 않는다</b> — 게임은 새 값을 이르고 곧장 1 을 내 계약으로 간다(0x004AEE15).
        return true;
    }

    /// <summary>위약금을 <b>내고</b> 계약을 깼을 때 깎이는 친밀도(<c>0x0044F886</c>).</summary>
    private const int BreakPenaltyCloseness = 20;

    /// <summary>죄를 물을 때 먼저 깎는 친밀도(<c>0x0044F10E</c> 의 <c>push -0x14</c>).</summary>
    private const int ClosenessLost = 20;

    /// <summary>10닢 단위로 내린다 — 게임의 <c>/10*10</c> 꼴이다.</summary>
    private static int To10(int coins) => coins / 10 * 10;

    /// <summary>친밀도를 모를 때 쓰는 밑값. 표를 못 읽었을 때다.</summary>
    private const int DefaultCloseness = 60;

    /// <summary>
    /// 이야기를 받아 줄지 가린다 — 게임 <c>0x004AE5F0</c> 의 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 반응 대사는 <b>말투 세 벌</b>이다(<c>0x00469450</c> — <see cref="StyleOf"/>).
    /// <code>
    ///   좋아하는 갈래   0x00546008 · 0x00546018 · 0x00546038
    ///   말솜씨로 넘김   0x00546050 · 0x00546098 · 0x005460E8
    ///   다른 이야기를   0x00546120 · 0x00546158 · 0x00546198
    ///   아주 물림       0x005461F0 · 0x00546218 · 0x00546250
    /// </code>
    /// 아주 물리면 게임은 후원자의 기분을 상하게 해 한동안 안 만나 주는데, 그 자리는 아직 안 들고 있다.
    /// </remarks>
    private Persuasion.Verdict Decide(HintTable.Hint hint, Patron patron,
                                      SponsorTable.Sponsor? sponsor,
                                      uint[]? face, Action<string> Say, bool more)
    {
        var dice = new GameRandom(Environment.TickCount);
        var stage = _view as CityPicView;

        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        // 1. 이야기가 감당할 만한가. 게임은 여기서 <b>설득 애니메이션(5번)</b>을 돌린다
        //    (0x004AE68D) — 감당할 만하면 청을 들어주고 아니면 엎어진다.
        int weight = Persuasion.Weight(hint.Grade, _player.Fame);
        stage?.PlayFameCheck(weight == 0);

        if (weight != 0)
        {
            if (weight == 2)
            {
                // 아예 못 믿을 이야기다(0x004AE6A1).
                Say(Pick3("그런 이야기는 들어본 적도 없다. 자네에게는 짐이 너무 무거울 걸세.",
                          "진심으로 하는 말입니까? 당신에게 너무 어려울 거라 생각됩니다.",
                          "확실한 이야기인가? 미심쩍은 이야기에는 원조할 수 없네."));
            }
            // 무겁지만 우겨 볼 만하다 — 한 번 더 묻고 그래도 하겠다면 받아 준다(0x004AE6C3).
            else if (ConfirmDialog.Ask(_view, Pick3(
                         "자네에게는 짐이 너무 무거우리라 생각되는데... 꼭 하고 싶은가?",
                         "당신에게는 어려울 거라 생각됩니다. 그래도 가고 싶습니까?",
                         "터무니 없는 이야기라고 생각되는데... 책임질 수 있겠나?"), face: face))
                return Persuasion.Verdict.Reluctant;

            // <b>물러나도 한 마디가 더 붙는다</b>(0x004AE6ED 의 합류점) — 내밀 이야기가
            // 남았으면 그것을 묻고, 없으면 그 자리에서 기분이 상한다(0x004AE72A 의 비트 14).
            if (more)
            {
                Say(Pick3("좀더 발견할 수 있을만한 이야기는 없는가?",
                          "그밖에 흥미있는 이야기는 없는가?",
                          "어쩐지 썩 내키지 않는군. 좀더 흥미있는 얘기가 좋겠군."));
                return Persuasion.Verdict.AskAnother;
            }
            Say(Pick3("좀더 분수에 맞는 이야기를 찾아 오게.",
                      "흥미있는 이야기를 찾아 오십시오. 기다리고 있겠습니다.",
                      "그런 가치 없는 이야기에는 원조할 수 없다. 다음 기회로 하세."));
            return Persuasion.Verdict.Refused;
        }

        // 2. 좋아하는 갈래면 두말이 없다.
        if (Persuasion.Likes(sponsor?.Tastes ?? 0, hint.Category))
        {
            Say(Pick3("흐음, 흥미있군.",
                      "그거 흥미있는 이야기로군요.",
                      "음음, 흥미있을 것 같군."));
            return Persuasion.Verdict.Interested;
        }

        int eye = sponsor?.Eye ?? patron.Discernment;
        int rhetoric = _player.LevelOf(Skill.Names[Skill.Rhetoric]);
        int charm = _player.AbilityOf(Ability.Charm);

        // 3. 말솜씨로 넘긴다. 굴림 결과가 곧 <b>하트(3번)</b>다 — 이기면 커지고 지면 깨진다.
        bool talked = Persuasion.Talks(eye, rhetoric, charm, dice);
        stage?.PlayHeart(talked);

        if (talked)
        {
            Say(Pick3("흐음, 그다지 흥미가 없지만 자네의 부탁이라면 안 들어 줄 것도 없지.",
                      "그렇습니까? 그다지 내키지 않지만 다름 아닌 당신 부탁이니 원조하겠습니다.",
                      "썩 흥미롭지는 않지만 자네 부탁이니 거절할 수 없군."));
            return Persuasion.Verdict.Reluctant;
        }

        // 4. 못 넘겼다 — 다른 이야기라도 물어볼지, 아주 물릴지. 여기서도 하트가 돈다.
        bool softened = more && Persuasion.Softens(eye, rhetoric, charm, dice);
        if (more) stage?.PlayHeart(softened);

        if (softened)
        {
            Say(Pick3("흐음, 썩 내키지 않는군. 좀더 흥미있는 이야기는 없는가?",
                      "어쩐지 내키지 않는군요. 그밖에 흥미있는 이야기는 없습니까?",
                      "흐~음, 조금도 흥미가 일어나지 않는군. 좀더 호기심을 불러 일으킬 이야기를 찾아 오게."));
            return Persuasion.Verdict.AskAnother;
        }

        Say(Pick3("그런 쓸데없는 이야기에 버릴 돈은 없네.",
                  "그런 이야기에는 안됐지만 힘이 되어드릴 수 없습니다.",
                  "흐~음, 흥미없군. 이것으로는 원조할 기분이 안나는군."));
        return Persuasion.Verdict.Refused;
    }

    /// <summary>

    /// 그 후원자에게 <b>보고</b>할 수 있는지 — 계약을 맺은 그 자리이고 맡은 것을 찾아 왔는가.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044EA00</c> 이다.
    /// <code>
    ///   0x0044E9E0  계약이 있고 그 계약을 맺은 자리인가(0x0044E550 → 0x00493DB0)
    ///   0x0044E880  보고할 것이 하나 이상인가 — 계약의 유적 번호(0x00493E60)로 모은다
    /// </code>
    /// 사람이 아니라 <b>자리</b>다(<see cref="AtContractSeat"/>) — 은퇴한 후원자의 계약도
    /// 뒷사람에게 보고할 수 있다.
    /// </remarks>
    private bool CanReport(Patron patron) => ReportTargets(patron).Count > 0;

    /// <summary>
    /// 후원자가 앉은 건물의 첫 줄 — 계약 상태로 갈린다.
    /// </summary>
    /// <remarks>
    /// 게임도 셋을 같은 자리에 갈아 끼운다(<c>0x0044E630</c> 이 <c>+0xB0</c> 을 정한다).
    /// <code>
    ///   0x0044E9A0  설득     = 이 자리에 후원자가 있고 내밀 힌트가 있고 <b>계약 중이 아니다</b>
    ///   0x0044E9E0  계약중단 = 이 후원자와 <b>계약 중</b>이다
    ///   0x0044EA00  보고     = 계약중단 조건 + <b>보고할 발견물이 있다</b>
    /// </code>
    /// 그래서 계약을 맺어 두고 아무것도 못 찾은 채 찾아가면 "계약중단" 만 뜬다 —
    /// 그 자리에서 다시 설득할 수는 없다.
    /// </remarks>
    /// <returns>붙일 줄이 없으면 빈 문자열 — 그러면 후원자 줄이 아예 안 뜬다.</returns>
    public string PatronRow(Patron patron) =>
        // 감찰관을 처벌해 배신한 후원자면 「계약중단」 하나뿐이다(0x0044E630 이 +0xB0 = 3).
        _player.IsBetrayed(patron.Name) ? Facility.Break
      : CanReport(patron) ? Facility.Report
      : Contracted(patron) ? Facility.Break
      : CanPersuade ? Facility.Persuade
      : "";

    /// <summary>
    /// "설득" 줄은 <b>내밀 힌트가 있을 때만</b> 선다(<c>0x0044E663</c> → <c>0x0044E9A0</c> 이
    /// <c>0x0044E7B0(0) &gt; 0</c> 을 본다). 없으면 줄 상태가 −1 로 남아 줄이 아예 없다.
    /// </summary>
    /// <remarks>
    /// 「설득 가능한 힌트가 없습니다」(<c>0x0055E548</c>)는 설득이 아니라 정보 차림표의 힌트 일람
    /// (<c>0x004769A0</c> — <c>0x0042618B</c> · <c>0x004933E4</c> 에서 부른다)이 내는 말이다.
    /// </remarks>
    private bool CanPersuade => LiveHints.Count > 0;

    /// <summary>
    /// 아직 살아 있는 힌트 — 얻었고 아직 보고 안 한 것이다(원본 힌트 상태 13).
    /// </summary>
    /// <remarks>
    /// 보고까지 마친 힌트는 여기서 빠진다. 왜 «발견» 이 아니라 «보고» 인지는
    /// <see cref="DiscoveryLog.IsHintDone"/> 에 적어 두었다.
    /// </remarks>
    private List<int> LiveHints =>
        _game.Discoveries?.LiveHints(_player) ?? [.. _player.Hints.Order()];

    /// <summary>
    /// 이 <b>자리</b>에서 계약 중인지(<c>0x0044E550</c>) — 사람이 바뀌었어도 자리가 같으면 참이다.
    /// </summary>
    private bool Contracted(Patron patron) => AtContractSeat(patron);

    /// <summary>
    /// 그 후원자에게 보고할 발견물. 계약의 유적 번호를 가진 것 중 발견했고 아직 안 알린 것이다.
    /// </summary>
    /// <summary>
    /// 계약을 맺은 <b>그 자리</b>에 서 있는가(<c>0x0044E550</c> — 도시와 시설 종류를 본다).
    /// </summary>
    /// <remarks>
    /// 우리 계약은 후원자 이름과 마을만 들고 있으므로, 앉을 자리가 <b>직업으로만</b> 정해지는
    /// 것(<see cref="Patron.Seats"/>)을 써서 같은 마을·같은 직업이면 같은 자리로 본다 —
    /// 국왕 자리는 다음 국왕이 잇는다. 이름까지 같으면 물론 같은 자리다.
    /// </remarks>
    private bool AtContractSeat(Patron patron) =>
        _player.Contract is { } c && c.City == _cityName
        && (c.Sponsor == patron.Name
            || LoadPatrons().FirstOrDefault(p => p.Name == c.Sponsor)?.Occupation == patron.Occupation);

    private List<DiscoveryTable.Record> ReportTargets(Patron patron) =>
        Palace.ReportTargets(_player, AtContractSeat(patron),
                             _game.Discoveries?.Table, _game.Hints);

    /// <summary>
    /// 맡은 것을 찾아 왔다고 후원자에게 알린다 — 사례를 받고 계약이 끝난다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044ED9A</c> → <c>0x00412020</c> 이다. 사례를 셈하는 자리는
    /// <c>0x00411D10</c> 이고, 밑값이 <b>미불(계약금/2)</b> 이다.
    /// <code>
    ///   411d1f  push 0x1E ; call 0x4B7C0F      ; rand(30)
    ///   411d29  ecx = eax + 0x78               ; 기한 안이면 120 + rand(30) %
    ///   411d47  esi = 0x5A - rand(0x14)        ; 늦었으면  90 - rand(20) %
    ///   411d3b  eax = 계약금 / 2               ; 미불
    ///   411d3e  imul ; div 100                 ; 미불 x 비율 / 100
    /// </code>
    /// 100닢 단위로 내린다(<c>0x004117D0</c> — 100 이하면 그대로 둔다).
    /// 받은 돈은 <c>0x0041200E</c> 가 소지금에 더한다.
    ///
    /// 게임은 여기서 발견물의 사람 칸 2 를 채우고 깃발 <c>0x80</c> 을 세운다
    /// (<c>0x004AACA0</c>, 볼트 23) — 우리 쪽의 "알림" 과 같은 자리라 그렇게 적는다.
    /// 그래서 <b>계약으로 맡은 것은 항구에서 못 알리고 여기서만 매듭이 지어진다.</b>
    ///
    /// 발견물 하나마다의 셈은 <c>0x004111D0</c> 이고 <see cref="Palace"/> 에 옮겼다 —
    /// 명성 <see cref="Palace.FameFor"/>, 친밀도 <see cref="Palace.ClosenessFor"/>,
    /// 후원자에게 쌓이는 값 <see cref="Palace.CreditFor"/> 다.
    ///
    /// 모조품 갈래("이것은 모조품이네", <c>0x00530BC8</c>)는 <see cref="ReportCounterfeit"/> 로
    /// 옮겼고, 선대의 계약은 <see cref="HandOver"/> 다.
    /// </remarks>
    public void Report(Patron patron) => Alone(() => ReportNow(patron));

    /// <summary>
    /// <b>선대의 계약</b>으로 보고할 때의 인사(<c>0x00411620</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x0052FF10  「그런데, %s님이 모험을 하고 있는 사이에, %s%s%s 은퇴해,
    ///                지금은 %s%s%s 새로운 주인이 되었습니다. 일단 안내하지요.」   ← 집사
    ///   0x0052FF80  「%s. 선대가 계약한 %s의 %s%s 계약을 달성하고 돌아왔습니다.」   ← 집사
    ///   0x0052FFC0 · 0x00530008 · 0x00530060                                      ← 새 주인
    /// </code>
    /// 집사의 문간 인사(<c>0x004115CA</c>)는 이 앞에 이미 나왔고, 여기서는 후원자의 세 말투
    /// 인사를 <b>대신</b>한다 — 늦었어도 이 말만 한다(<c>0x0041171C</c> 에서 곧장 돌아선다).
    /// </remarks>
    private void HandOver(Patron patron, Contract contract, Func<string, string, string, string> Pick3)
    {
        var old = _game.Sponsors?.FindByName(contract.Sponsor);
        string oldName = $"{old?.Name ?? contract.Sponsor} {old?.Honorific ?? "각하"}";

        var now = _game.Sponsors?.FindByName(patron.Name);
        string sir = now?.Honorific ?? "각하";
        string newName = $"{now?.Name ?? patron.Name} {sir}";

        string me = _player.Name;
        void Steward(string words) => TalkDialog.Say(_view, StewardFace(), "", words);

        Steward($"그런데, {me}님이 모험을 하고 있는 사이에, {oldName}께서 은퇴해, "
              + $"지금은 {newName}께서 새로운 주인이 되었습니다. 일단 안내하지요.");
        Steward($"{sir}. 선대가 계약한 {_player.NationName}의 {me}{GameUi.Josa(me, "이", "가")} "
              + "계약을 달성하고 돌아왔습니다.");

        TalkDialog.Say(_view, FaceOf(patron), "", Pick3(
            "그런가... 선대와 계약이라면 어쩔 수 없다. 일단, 발견한 것을 보여 주게.",
            "그렇습니까... 선대의 계약이라면 어쩔 수 없군요. 그런데 발견한 물건은 어느 것입니까?",
            "호우, 선대와 계약이라, 그래, 무얼 발견했는가? 자 빨리 보여 주게."));
    }

    /// <summary>
    /// 발견물을 하나씩 보고한다 — 게임의 <c>0x00412020</c> 안쪽 차례 그대로다.
    /// </summary>
    /// <remarks>
    /// 발견물마다 <b>"…의 발견을 보고했다!!"</b>(<c>0x00530BA8</c>)를 내고, 동영상이나
    /// 그림이 있으면 그것을 튼 뒤 친밀도 · 아이템 · 명성 차례로 낸다(<c>0x004111D0</c>).
    /// 사례는 다 끝나고 한 번이다.
    /// </remarks>
    /// <returns>받은 사례(닢) · 후원자가 본 갈래 · 남이 먼저 발표해 버렸는지.</returns>
    private (int Paid, Palace.ReportGrade Grade, bool Scooped) ReportEach(
        Patron patron, Contract contract,
        IReadOnlyList<DiscoveryTable.Record> rows, bool inTime)
    {
        string me = _player.Name;
        int fame = 0, closer = 0;

        // 남이 앞질렀는지는 <b>보고를 시작하기 전에</b> 다 적어 둔다 — 원본도 사례 갈림길
        // (0x00412171)과 낱낱의 셈(0x004111F9)을 발표 깃발이 서기 전에 본다.
        var scoopedRows = rows.Where(KnownByOthers).Select(r => r.Id).ToHashSet();
        bool scoopedHead = Headline(rows) is { } head && scoopedRows.Contains(head.Id);

        void Credit(DiscoveryTable.Record row)
        {
            GameDialog.Show(_view,
                $"{me}{GameUi.Josa(me, "은", "는")} {row.Name}의 발견을 보고했다!!");

            // 그림은 동영상 → 움직이는 그림(DISCOVER.CDS) → 스틸 차례다(0x004AAF30) — 발표와 같은 함수다.
            if (row.Movie >= 0)
                MoviePlayer.Play(_view, DiscoveryDialog.MovieOf(_game.Directory, row.Movie));
            else if (row.Clip >= 0)
                DiscoveryClipPlayer.Play(_view, _game.Clips, row.Clip);
            else if (row.Picture >= 0)
                DiscoveryDialog.ShowPicture(_view, _game.Stills, row.Picture);   // 그림만 — 이름 창은 안 붙는다(0x004AD640)

            _player.Announce(row.Id);

            // 좋아하는 갈래를 물어다 주면 덤이 붙는다(0x004ADAE0 이 후원자 표 +0x38 을 본다).
            bool scooped = scoopedRows.Contains(row.Id);
            int by = Palace.ClosenessFor(row, inTime, scooped,
                                         patron.Likes(row.Category), _random);
            if (by != 0)
            {
                _player.Endear(patron.Name, by);
                GameDialog.Show(_view, by > 0 ? "친밀도가 올라갔다!" : "친밀도가 내려갔다!");
            }
            closer += by;

            // 그 발견물의 물건은 <b>서적(아이템 분류 7)만</b> 후원자가 돌려준다(0x004113E6~0x00411469).
            // 나머지는 후원자가 가져가 일람에서 사라진다 — 발견물 아이템은 보고 전까지 소지품에 없던 것이다.
            // 말투 셋(0x0052FBE0 · 0x0052FC00 · 0x0052FC60) 뒤에 「%s%s 손에 넣었다!」(0x0052FCB0)이고,
            // 넘치면 물릴 수 없는 버리기 창이다(0x004B1710).
            if (row.GivesItem && _game.Items?.Find(row.ItemId) is { } gift
                && gift.Category == Palace.KeepsakeCategory)
            {
                int s = StyleOf(patron);
                TalkDialog.Say(_view, FaceOf(patron), "", s == 1 ? "이것은 저에게는 필요없는 것입니다. 당신이 가지고 가 주십시오. 언젠가 필요할 때가 있을 것입니다."
                  : s == 2 ? "그것은 자네가 발견한 물건이네. 가지고 가도 좋네. 무언가 도움이 될지도 모르니."
                  : "이것은 자네가 가지고 가게.");
                GameDialog.Show(_view, $"{gift.Name}{GameUi.Josa(gift.Name, "을", "를")} 손에 넣었다!");
                ItemGain.AddForced(_view, _game, [row.ItemId]);
            }

            // 알린 것마다 명성이 오른다. 항구 발표(보수/70)와 셈이 다르다 — 보고는
            // 보수/50 이고 늦으면 그 반이다(0x004111D0).
            // 발견물의 보수가 후원자 지갑에 도로 쌓인다 — 재력 x 10000 을 못 넘는다(0x004113E4).
            _player.SpendPurse(patron.Name, Palace.CreditFor(row.Reward, inTime, scooped),
                               patron.Wealth);

            int up = Palace.FameFor(row, inTime, scooped);
            _player.Fame += up;
            if (up > 0) GameDialog.Show(_view, $"명성이 {up} 올라갔다!");
            fame += up;

            // 보고한 발견물마다 <b>늘</b> 딸려 온다 — 항구 발표와 같은 두 줄이고(0x0041156A ·
            // 0x00411576), 명성을 건너뛰는 가지도 이 앞으로 합쳐지므로 안 걸러진다.
            Harbor.Celebrate(_player);
        }

        foreach (var row in rows)
        {
            // 모조품(0x00412460)은 들키지 않으면 진짜와 똑같이 통과된다 — 들켰을 때만
            // 따로 간다(Report(Patron) 의 볼트 주석 참고).
            if (!row.IsCounterfeit) { Credit(row); continue; }
            if (!ReportCounterfeit(patron, row, inTime, out bool broke)) { Credit(row); continue; }

            // 못 봐주면 그 자리에서 <b>계약이 파기된다</b>(0x004123F1 이 0x0044EEA0 을 부른다).
            // 남은 보고도, 사례도, 가늠도 없다 — 봐주는 갈래만 0x0041243D 로 그냥 빠진다.
            if (!broke) continue;

            ReturnLentShips(broken: true);
            _player.EndContract();
            return (0, Palace.ReportGrade.Poor, scoopedHead);
        }

        // 다 보고하고 나면 후원자가 <b>성과를 가늠해</b> 한 마디 하고 사례를 친다(0x00411AA0).
        // 세계일주는 가늠 없이 딴 갈래로 빠진다(0x00411FC0 이 먼저 그것을 본다).
        bool world = rows.Any(r => r.Id == Palace.WorldRoute);
        var grade = world ? Palace.ReportGrade.Good : GradeOf(patron, contract, rows);
        // 세계일주도 남이 먼저 발표했으면 딴 말·딴 사례다(0x00411D9B 가 먼저 그것을 본다) — 기한 안이면
        // 계약금의 1/4 을 100닢 단위로 내려 주고(0x00411DAF → 0x004117D0), 늦었으면 한 푼도 없다(0x00411E77).
        int paid = world
            ? scoopedHead ? inTime ? Palace.To100(contract.Amount / 4) : 0
                          : Palace.WorldRouteRewardFor(contract.Unpaid, inTime, _random)
            : RewardFor(contract, grade, inTime, scoopedHead);
        if (world && scoopedHead && Headline(rows) is { } beatenWorld)
            WorldScoopedRemark(patron, beatenWorld, inTime, paid);
        else if (world) WorldRemark(patron, inTime);
        else if (scoopedHead && Headline(rows) is { } beaten)
            ScoopedRemark(patron, beaten, inTime, paid);
        else Remark(patron, grade, inTime, paid);

        _player.Earn(paid);
        // 계약이 끝나면 빌린 배를 거둬 간다(0x0040FE40).
        ReturnLentShips();
        _player.EndContract();
        return (paid, grade, scoopedHead);
    }

    /// <summary>
    /// 후원자가 성과를 어떻게 보았는지 굴린다(<c>0x00411AA0</c> · <see cref="Palace.GradeOf"/>).
    /// </summary>
    /// <remarks>
    /// 견주는 잣대는 <b>힌트 표의 자금</b>이다(<c>0x00412289</c>) — 흥정으로 고친 계약금이 아니다.
    /// 보고한 발견물의 보수를 다 더해 그보다 많으면 눈이 후해진다.
    /// </remarks>
    private Palace.ReportGrade GradeOf(Patron patron, Contract contract,
                                       IReadOnlyList<DiscoveryTable.Record> rows)
    {
        int total = rows.Sum(r => r.Reward);
        int funds = _game.Hints?.Find(contract.Hint)?.Funds ?? contract.Amount;
        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        return Palace.GradeOf(sponsor?.Closeness ?? DefaultCloseness,
                              SponsorFortune(sponsor)[7], funds < total, _random);
    }

    /// <summary>
    /// <b>세계일주</b>를 보고하고 나서의 마무리(<c>0x00411010</c> 의 <c>0x0046 94C0</c> 가지).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x0052F860 · 0x0052F8C0 · 0x0052F920   후원자의 치사(말투 셋)
    ///   0x0052F970  「%d년 %d월, %s%s 역사 최초로 세계일주를 달성했다!」
    ///   0x0052F9A8  「… 기한은 넘었지만 역사 최초로 …」
    /// </code>
    /// 그러고 나서 <b>엔딩 동영상</b>을 튼다(<c>0x00411133</c> → <c>0x0045B8F0</c> 이
    /// <c>AVI\END.AVI</c> 를 <c>0x0045B820(path, 5, 0)</c> 으로 튼다). 판은 <b>안 끝난다</b> —
    /// 동영상이 지나면 그대로 도시로 돌아간다(<c>0x004A2180</c> 은 끝내기가 아니라 화면 전환이다).
    /// 남이 먼저 발표했을 때의 짧은 벌(<c>0x0052F9F0</c> · <c>0x0052FA18</c>)은 우리 쪽에는 안 걸린다.
    /// </remarks>
    private void WorldFinale(Patron patron, bool inTime, Func<string, string, string, string> Pick3)
    {
        TalkDialog.Say(_view, FaceOf(patron), "", Pick3(
            $"{_player.Name}, 정말 잘 했네. 자네의 위업을 역사에 기리고 자자손손 전하겠네. 자네야 말로 최고의 모험가네.",
            "정말 잘 하셨습니다. 상상도 못할 고난을 넘어 오셨군요... 당신의 영광스러움을 나라안에 전합시다.",
            "정말로 대단하다. 아무도 성공 못한 모험을 잘 달성해 주었네. 자네야말로 영웅이네."));

        string me = _player.Name;
        var on = _player.Date;
        GameDialog.Show(_view, inTime
            ? $"{on.Year}년 {on.Month}월, {me}{GameUi.Josa(me, "은", "는")} 역사 최초로 세계일주를 달성했다!"
            : $"{on.Year}년 {on.Month}월, {me}{GameUi.Josa(me, "은", "는")} 기한은 넘었지만 역사 최초로 세계일주를 달성했다!");

        // 엔딩 동영상(0x0045B8F0). 파일이 없으면 조용히 넘어간다.
        MoviePlayer.Play(_view, MovieFiles.Resolve(_game.Directory, MovieFiles.EndingStem));
    }

    /// <summary>
    /// <b>세계일주</b>를 보고했을 때의 말(<c>0x00411D90</c>) — 기한 둘 x 말투 셋뿐이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기한 안  0x00530A00 · 0x00530A18 · 0x00530A50
    ///   늦음     0x00530A98 · 0x00530AC8 · 0x00530B08
    /// </code>
    /// </remarks>
    private void WorldRemark(Patron patron, bool inTime)
    {
        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        TalkDialog.Say(_view, FaceOf(patron), "", inTime
            ? Pick3("굉장하다! 잘 해주었다!!",
                    "이것은 상상 이상입니다!! 제 눈이 틀림없었던 것 같군요.",
                    "오오, 이거 굉장하군! 상상 이상의 것이다!! 기대를 져버리지 않았군.")
            : Pick3("훌륭하다! 잘 해내었다!! 늦은 것은 공제하겠다.",
                    "이것은... 상상 이상입니다!! 제 눈이 틀림없었던 것 같군요.",
                    "오오, 이건 굉장하다! 기대를 져버리지 않았군. 늦은 것은 없었던일로 하지."));
    }

    /// <summary>
    /// <b>세계일주</b>를 남이 먼저 발표한 뒤에 보고했을 때의 말(<c>0x00411D90</c> 의 앞 두 갈래) — 기한 둘 x 말투 셋이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기한 안  0x005307A8 · 0x00530800 · 0x00530850   ; 사례 닢수가 들어간다
    ///   늦음     0x005308B8 · 0x00530928 · 0x00530988   ; 한 푼도 없다
    /// </code>
    /// 조사는 발견물이 가/이(<c>0x00411DEE</c> · <c>0x00411E9C</c> 의 <c>0x004281B0(이름, 0)</c>), 발표한 사람은
    /// <see cref="ScoopedRemark"/> 와 같이 이/가 로 둔다.
    /// </remarks>
    private void WorldScoopedRemark(Patron patron, DiscoveryTable.Record row, bool inTime, int paid)
    {
        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        string me = _player.Name;
        string what = row.Name;
        string who = _player.ScoopedBy(row.Id) ?? "";
        string ga = GameUi.Josa(what, "이", "가");
        string iga = GameUi.Josa(who, "이", "가");

        TalkDialog.Say(_view, FaceOf(patron), "", inTime
            ? Pick3($"{me}, 안됐지만 {what}{ga}, 벌써 {who}{iga} 발표했네. 노력한 건 알겠지만, {paid}닢 밖에 지불할 수 없네.",
                    $"{me}, {what}{ga} {who}{iga} 벌써 발표했습니다. 안됐지만 {paid}닢 밖에 지불할 수 없습니다.",
                    $"늦었군, {me}. {what}{ga} {who}{iga} 발표했네. 사례는 {paid}닢이면 되겠지.")
            : Pick3($"{me}, 자네가 발견해 온 {what}{ga}, 벌써 {who}에 의해 발표되었다. 늦은데다 이것이라니 돈은 지불할 수 없다. 불만없겠지.",
                    $"{me}, 당신이 발견한 {what}{ga} {who}{iga} 먼저 발표했습니다. 계약기한이 넘었으니, 사례는 지불할 수 없습니다.",
                    $"늦었군, {me}. {what}{ga} 벌써 {who}{iga} 발표했네. 기한이 넘었으니 사례를 지불하라고는 하지 못하겠지."));
    }

    /// <summary>
    /// <b>남이 먼저 발표해 버렸을 때</b> 후원자가 하는 말(<c>0x004117F0</c>) — 갈래 가늠은
    /// 건너뛰고 이 말로 갈음한다(<c>0x00411FC0</c> 이 <see cref="Remark"/> 대신 이리 보낸다).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기한 안  0x005300A8 · 0x00530100 · 0x00530158   ; 사례 닢수가 들어간다
    ///   늦음     0x005301C8 · 0x00530238 · 0x005302A8   ; 한 푼도 없다
    /// </code>
    /// 견주는 발견물은 <see cref="Headline"/> 하나다. 조사는 발견물이 은/는
    /// (<c>0x004281B0(이름, 1)</c>), 발표한 사람이 이/가(<c>…, 0</c>)다.
    /// 기한 안 말은 얼굴 상자 하나로 내고(<c>0x004119AC</c>), 늦은 말은 말투 셋
    /// (<c>0x004694C0</c>)으로 낸다 — 우리는 둘 다 같은 창으로 낸다.
    /// </remarks>
    private void ScoopedRemark(Patron patron, DiscoveryTable.Record row, bool inTime, int paid)
    {
        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        string me = _player.Name;
        string what = row.Name;
        string who = _player.ScoopedBy(row.Id) ?? "";
        string eun = GameUi.Josa(what, "은", "는");
        string iga = GameUi.Josa(who, "이", "가");

        TalkDialog.Say(_view, FaceOf(patron), "", inTime
            ? Pick3($"{me}, 안됐지만 발견한 {what}{eun} 벌써 {who}에 의해 발표되었네. 이렇다면 {paid}닢 밖에 지불할 수 없네.",
                    $"{me}, 당신이 발견한 {what}{eun} {who}{iga} 먼저 발표했습니다. 안됐지만, {paid}닢 밖에 지불할 수 없습니다.",
                    $"늦었군, {me}. {what}{eun} 벌써 {who}{iga} 발표했네. 사례는 {paid}닢으로 충분하겠지.")
            : Pick3($"{me}, 자네가 발견한 {what}{eun} 벌써 {who}에 의해 발표되었네. 늦은데다 그 모양이라니 돈은 지불할 수 없네. 불만없겠지.",
                    $"{me}, 당신이 발견한 {what}{eun} {who}{iga} 먼저 발표해 버렸습니다. 계약기한이 지나 버렸으니, 사례는 지불할 수 없습니다.",
                    $"늦었군, {me}. {what}{eun} 벌써 {who}{iga} 발표했네. 기한이 지났으니 사례를 달라고는 못하겠지!"));
    }

    /// <summary>
    /// 사례를 치기 앞서 후원자가 하는 말(<c>0x00411AA0</c>) — 갈래 셋 x 기한 둘 x 말투 셋이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   굉장 · 기한 안  0x00530318 · 0x00530348 · 0x00530398
    ///   굉장 · 늦음     0x005303E8 · 0x00530418 · 0x00530458
    ///   보통 · 기한 안  <b>말이 없다</b>(0x00411B98 이 건너뛴다)
    ///   보통 · 늦음     0x005304C8 · 0x005304F0 · 0x00530520
    ///   시시 · 기한 안  0x00530588 · 0x005305C0 · 0x00530610   ; 사례 닢수가 들어간다
    ///   시시 · 늦음     0x00530670 · 0x005306B8 · 0x00530728
    /// </code>
    /// 시시 · 기한 안 대사에는 <b>사례 닢수가 들어간다</b> — 게임도 먼저 셈하고 말에 끼워 넣는다.
    /// </remarks>
    private void Remark(Patron patron, Palace.ReportGrade grade, bool inTime, int paid)
    {
        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };
        void Say(string words) => TalkDialog.Say(_view, FaceOf(patron), "", words);

        switch (grade, inTime)
        {
            case (Palace.ReportGrade.Good, true):
                Say(Pick3("굉장하다! 잘 해냈네!! 사례는 듬뿍하겠네.",
                          "이것은... 상상 이상입니다!! 제 눈이 틀림 없었던 것 같군요. 사례를 하지요.",
                          "오오, 굉장하군! 상상 이상의 것이다!! 내 기대에 보답해 주었군. 사례를 하지."));
                break;
            case (Palace.ReportGrade.Good, false):
                Say(Pick3("굉장하군! 잘 했다!! 늦은 것을 없었던 일로 하지.",
                          "이렇게 굉장할 수가! 잘 해내셨군요. 늦은 것은 잊어 버리지요.",
                          "오오, 이건 굉장하군! 상상 이상의 것이다!! 어쩔 수 없군. 늦은 것은 없었던 일로 하지."));
                break;
            case (Palace.ReportGrade.Mid, true):
                break;                                        // 보통 · 기한 안은 말이 없다
            case (Palace.ReportGrade.Mid, false):
                Say(Pick3("늦은 것은 공제하겠네, 불만없겠지!",
                          "기한에 늦은 것은 공제하겠습니다. 좋습니까?",
                          "으~음, 기한이 넘었군. 사례는 해 주겠지만... 늦은 것은 공제하겠네. 상관없겠지."));
                break;
            case (Palace.ReportGrade.Poor, true):
                Say(string.Format(Pick3(
                    "으~음, 이건가.... 이거라면, {0}닢 밖에 지불할 수 없네.",
                    "이런 것이었습니까..., 당신에게는 미안하게 됐지만, {0}닢 밖에 지불할 수 없군요.",
                    "흐~음, 이건가... 안됐지만, 사례는 {0}닢이면 되겠지."), paid));
                break;
            default:
                Say(Pick3("으~음, 이건가.... 늦은데다 이거라면, 돈은 지불할 수 없군, 불만없겠지.",
                          "어떻게 된 겁니까. 이런 것이라고는... 기한이 넘은데다, 이것이라면 약속한 사례는 드릴 수 없습니다. 괜찮겠지요.",
                          "흐~음... 기대를 벗어났군. 시간을 들인 것 치고는 변변치 않군. 설마 사례를 하라고는 말 못하겠지."));
                break;
        }
    }

    /// <summary>
    /// 모조품을 들고 왔을 때 후원자가 알아보는지 가늠한다(<c>0x00412020</c> 안쪽,
    /// 볼트 <c>23.분석-발견과 보고(발견물 인스턴스)</c> 부록).
    /// </summary>
    /// <remarks>
    /// 안 들키면(<see cref="Palace.CounterfeitCaught"/> 가 거짓) <b>아무 일도 안 하고
    /// false 를 내</b> — 부른 쪽(<see cref="ReportEach"/>)이 진짜와 똑같이 사례를 친다.
    /// 들켰으면 후원자 표의 <b>정적</b> 친밀도(<see cref="SponsorTable.Sponsor.Closeness"/>)와
    /// 운으로 다시 가늠해 봐줄지 정한다 — 봐주면 다음 기회를 주고 악명만 조금 오르고,
    /// 못 봐주면 사이가 상한다.
    /// </remarks>
    /// <returns>이 발견물의 사례를 건너뛰어야 하면 true.</returns>
    private bool ReportCounterfeit(Patron patron, DiscoveryTable.Record row, bool inTime,
                                   out bool broke)
    {
        broke = false;
        var sponsorRow = _game.Sponsors?.FindByName(patron.Name);
        int luck = _player.AbilityOf(Ability.Luck);
        if (!Palace.CounterfeitCaught(sponsorRow?.Closeness ?? 0, luck, _random)) return false;

        string me = _player.Name;
        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        // <b>감찰관을 매수해 증거품을 빼돌려 둔 판</b>이면 봐 줄지를 아예 안 굴린다
        // (0x004122FE 가 숨긴 것 개수 &gt; 0 이면 판정을 건너뛴다). 후원자 눈에는 둘이 짜고
        // 속인 것이라, 감찰관과 <b>함께</b> 감옥에 간다(0x00412353) — 「이 <b>자들을</b>」이
        // 복수인 까닭이 이것이다.
        if (_player.HiddenDiscoveries.Count > 0)
        {
            string sir = _game.Sponsors?.FindByName(patron.Name)?.Honorific ?? "각하";
            var spy = _game.Faces?.TryGetBgra(Inspector.Face, female: false);

            TalkDialog.Say(_view, spy, "", $"노, 농담을 {sir}. 모조품일리 없습니다.");
            TalkDialog.Say(_view, FaceOf(patron), "", Pick3(
                "이 배신자! 난 눈을 그냥 뜨고 있는 줄 아나! 이 자들을 전부 감옥에 넣어라!",
                "그렇게 신용하고 있었건만, 저를 속였군요! 이 자들을 전부 감옥에 넣어라!",
                "믿고 있었건만... 배신하리라고는. 이 정도는 누구라도 알 수 있다!! 이 자들을 감옥에 집어 넣어라!"));
            TalkDialog.Say(_view, spy, "", $"오~ , 오, 용서를. {sir}, 우, 저는 아무것도...");

            if (Jail(patron, new GameRandom(Environment.TickCount))) EndGame();
            broke = true;
            return true;
        }

        // 봐 줄 여지가 생기는 조건이 둘 더 있다(0x00412490) — <b>후원자 성미 칸 4 가 0 보다 크고</b>,
        // <b>계약 기한이 아직 남아 있어야</b> 한다. 기한을 넘겼으면 모조품은 절대 안 봐 준다.
        bool mayForgive = inTime && SponsorFortune(sponsorRow)[Palace.MercyFortune] > 0;

        if (mayForgive && Palace.CounterfeitForgiven(_player.ClosenessOf(patron.Name), luck, _random))
        {
            // 0x004123FA — 봐줄 때의 말투 셋(0x00530BC8 벌).
            TalkDialog.Say(_view, FaceOf(patron), "", Pick3(
                $".......{me}, 안됐지만, 이것은 모조품이네. 자네에게 한번 더 기회를 주겠다. 기한까지 진짜를 발견해 오게.",
                $"안됐지만 ......{me}, 이것은 모조품인 것 같군요. 이렇다면 사례를 할 수 없군요. 당신에게 한번 더 기회를 드리겠습니다. 기한까지 진짜를 발견해 오십시오.",
                $"......안됐지만, 이것은 모조품이로군. {me}, 이번에야 말로 진짜를 가져 오게. 기한까지 발견되기를 기대하겠네."));
            _player.Infamy += Palace.CounterfeitInfamyRoll(_random);
        }
        else
        {
            // 0x004123CA — 못 봐줄 때의 말투 셋(0x00530E88 벌).
            TalkDialog.Say(_view, FaceOf(patron), "", Pick3(
                "이런 모조품으로 나를 속이려 했나!",
                "이런 모조품으로 저를 속일 작정이라고는...용서할 수 없습니다.",
                "바보녀석, 이런 모조품으로 나를 속일 작정이었나!"));
            _player.Sulk(patron.Name);
            if (Punish(patron, sponsorRow, Pick3)) EndGame();
            broke = true;
        }
        return true;
    }

    /// <summary>
    /// 계약을 그르친 죄를 묻는다(<c>0x0044F100</c>) — <b>용서 · 위약금 · 감옥</b> 셋 중 하나다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   44f10e  친밀도 −20
    ///   44f11b  후원자 깃발 14(계약 파기 이력)나 13(감찰관 처벌)이 서 있으면 곧장 감옥
    ///   44f155  성미 칸 4 가 2 보다 작으면 감옥
    ///   44f166  친밀도가 0 이하면 감옥
    ///   44f170  문턱(<see cref="Palace.Reckoning"/>) 으로 갈린다
    /// </code>
    /// 깃발 14 는 삐짐이다(<see cref="Player.IsSulking"/> — 설득을 물렸거나 계약중단을 한 뒤 30일).
    /// 말은 신분마다 세 벌씩이고, 위약금을 못 내면 그대로 감옥이다.
    /// </remarks>
    /// <returns>감옥에서 놀이가 끝났으면 true.</returns>
    private bool Punish(Patron patron, SponsorTable.Sponsor? sponsorRow,
                        Func<string, string, string, string> Pick3)
    {
        var face = FaceOf(patron);
        void Say(string words) => TalkDialog.Say(_view, face, "", words);
        var dice = new GameRandom(Environment.TickCount);

        // 친밀도부터 깎고 시작한다(0x0044F10E → 0x00478530 이 −20, 0~100 으로 자른다).
        _player.Endear(patron.Name, -ClosenessLost);
        int close = _player.ClosenessOf(patron.Name);

        bool jail = _player.IsSulking(patron.Name)          // 깃발 14(0x0044F11B)
                    || _player.IsBetrayed(patron.Name)      // 깃발 13(0x0044F130)
                    || SponsorFortune(sponsorRow)[Palace.MercyFortune] < 2
                    || close <= 0;

        if (!jail)
        {
            int funds = _player.Contract?.Amount ?? 0;
            int mark = Palace.Reckoning(_player.Fame, _player.Infamy, funds,
                                        _player.AbilityOf(Ability.Faith));

            if (mark == 0)
            {
                // 0x0044F2AF — 그냥 봐 준다. 위약금도 감옥도 없다.
                Say(Pick3("인간이니 실패할 수도 있겠지. 어쩔 수 없군. 이번 실패는 불문에 부쳐두기로 하지.",
                          "인간이니 실패하는 일도 있겠지요. 이번 실패는 눈감아 드리지요. 다음을 기대하고 있겠습니다.",
                          "음, 실패 안하는 사람은 없으니까. 이번은 너그러이 봐 주겠다."));
                return false;
            }

            if (mark < close)
            {
                int fine = Palace.FineFor(funds);
                Say(string.Format(Pick3(
                    "어쩔 수 없다. {0}닢을 위약금으로 지불한다면 감옥행만은 면하게 해주지.",
                    "감옥으로 보내려고 생각했지만, 위약금으로 금화 {0}닢을 지불한다면 이번 건은 없었던 일로 해 드리지요.",
                    "위약금은 금화 {0}닢이다. 이것으로 없었던 일로 하지."), fine));

                if (_player.Gold >= fine)
                {
                    _player.Pay(fine);
                    Say(Pick3("음, 이것으로 용서해 주지.",
                              "이것으로 이번 건은 없었던 일로 하지.",
                              "자, 이번은 이것으로 눈감아 주지."));
                    return false;
                }

                // 못 내면 감옥이다. 이 갈래만은 「감옥에 쳐 넣어라」 셋이 안 나온다(0x0044F22B).
                Say(Pick3("뭐라고! 위약금을 지불하지 못하겠다고?···누가 이 놈을 감옥에 가두어라.",
                          "위약금도 지불할 수 없습니까? 당신에게 실망했습니다. 누가 이 자를 감옥에 넣어라.",
                          "뭐라고, 위약금도 지불할 수 없다고! 음~, 어처구니없어 말도 안나오는군. "
                        + "누구라도 좋으니, 이 놈을 감옥에 가둬 두어라."));
                return Jail(patron, dice);
            }
        }

        // 0x0044F241 — 곧장 감옥으로 갈 때만 이 셋이 나온다.
        Say(Pick3("이 놈을 감옥에 쳐 넣어라!",
                  "이 놈을 감옥에 넣어라.",
                  "누구라도 좋으니 눈에 거슬리는 이 놈을 감옥에 가둬 버려라."));
        return Jail(patron, dice);
    }

    /// <summary>
    /// 남이 먼저 보고해 버린 발견물인가(<c>0x004AADB0</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 발견물 인스턴스의 깃발 <c>0x80</c>(내가 발표함)이 안 서 있으면서 칸 2
    /// (발표자 이름)가 차 있으면 참으로 본다. 참이면 <b>명성이 한 톨도 안 오르고</b>
    /// 사례도 계약금/4(늦었으면 0)로 깎인다.
    ///
    /// 남의 이름이 칸 2 에 올라가는 길은 <b>누적 캐릭터</b>뿐이다 — 딸려 오는 대본
    /// (<c>DISEV.CDS</c> · <c>STORY0/1.CDS</c> · <c>HIST_EV.CDS</c>)에는 그 명령
    /// (<c>0x3F</c> · <c>0x68 0B</c>)이 없지만, 은퇴한 제독의 행적 갈래 9 가 되살아날 때
    /// <c>68 0B</c> 로 <b>만들어져</b> 돈다(<c>0x0041A7CF</c>). 새 판은 세 칸을 모두 비우고
    /// 시작한다(<c>0x004AA9B3</c>).
    /// </remarks>
    private bool KnownByOthers(DiscoveryTable.Record row) =>
        !_player.HasAnnounced(row.Id) && _player.ScoopedBy(row.Id) != null;

    private void ReportNow(Patron patron)
    {
        var contract = _player.Contract;
        var rows = ReportTargets(patron);
        if (contract == null || rows.Count == 0) return;

        var face = FaceOf(patron);
        void Say(string text) => TalkDialog.Say(_view, face, "", text);

        // 말투는 교섭과 같은 셋이다(0x00469450) — 여자면 존댓말, 상인 갈래면 둘째 반말, 그 밖은 반말.
        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) =>
            style switch { 1 => polite, 2 => merchant, _ => plain };

        // 보고를 시작하기 <b>앞서</b> 부관이 한 번 막아 선다(0x004124F0). 세 가지가 다 맞을
        // 때만이다 — 부관이 있고(0x00468EF0), 짐이 실려 있고(0x004B5950), 이 후원자에게
        // 돌려줄 배가 함대에 있을 때(0x0040FB80)다. 보고가 끝나면 빌린 배를 거둬 가니
        // 남은 배에 짐이 안 들어갈 수 있다는 말이다. NO 면 보고 자체가 없던 일이 된다.
        if (_player.MateAt(0).Length > 0 && _player.CargoHold.Count > 0 && _player.LentInFleet > 0
            && !ConfirmDialog.Ask(_view, "제독, 보고하고 배를 돌려주면 짐이 넘칠지도 모릅니다. 좋습니까?",
                                  face: _game.AideFace))
            return;

        bool inTime = contract.DaysLeft(_player.Date) > 0;
        bool world = rows.Any(r => r.Id == Palace.WorldRoute);

        // 후원자보다 <b>집사가 먼저</b> 문간에서 맞는다(0x004115CA). 네 갈래다 —
        // 계약한 사람이 아직 그 자리에 앉아 있는지(0x0044E590)와 기한을 지켰는지로 갈린다.
        // 앉은 사람이 그대로면 그 사람의 <b>경칭</b>을 부른다(0x004A2EA0 — 폐하·예하·각하·
        // 신부님·회장님·박사님·변호사 가운데 하나). 은퇴했으면 이름도 경칭도 안 부른다.
        string me = _player.Name;
        string rank = _game.Sponsors?.FindByName(contract.Sponsor)?.Honorific ?? "각하";
        bool seated = contract.Sponsor == patron.Name;
        void Steward(string words) => TalkDialog.Say(_view, StewardFace(), "", words);

        // 설득 · 배신 결판과 같은 자리다 — 인사를 받는 순간부터 알현 곡이 돌고, 보고를
        // 마치고 나서면 도시 곡으로 돌아간다(PersuadeBody · Reckon 의 볼트 주석 참고).
        _game.Bgm.Play(BgmPlayer.SponsorTrack);
        try
        {
            Steward(seated
                ? inTime
                    ? $"아니, {me}님. 귀환을 축하드립니다. {rank}{GameUi.Josa(rank, "이", "가")} 기다리고 계십니다. 안내하지요."
                    : $"아니, {me}, 꽤 귀환이 늦었군요... 일단 {rank}에게 보고하지요."
                : inTime
                    ? $"아니, {me}님. 무사 귀환을 축하드립니다."
                    : $"아니, {me}. 꽤 귀환이 늦었군요...");

            // 계약을 맺은 사람이 은퇴하고 뒷사람이 그 자리에 앉았으면 인사가 통째로 다르다
            // (0x00411620) — 집사가 자리가 바뀐 것을 먼저 이르고 대신 보고해 준다.
            if (!seated)
                HandOver(patron, contract, Pick3);
            else
            {
                // 집사가 후원자에게 아뢴다(0x00411751). 여기서도 후원자는 경칭으로만 부른다.
                Steward($"{rank}. {me}{GameUi.Josa(me, "이", "가")} 돌아왔습니다.");
                Say(inTime
                    ? Pick3("으음, 기다리고 있었네! 결과는 어떻게 되었나?",
                            "무사해서 다행입니다. 모험은 어떠했습니까?",
                            "오오, 무사히 돌아왔는가! 자 빨리 성과를 들려 주게.")
                    : Pick3("꽤 늦었군. 그래, 결과는 어떤가?",
                            "꽤 늦으셨군요. 그래도 성과는 있으셨겠지요?",
                            $"{me}, 기다리기 지쳤네. 그래, 성과는 있었나?"));
            }

            // 인사 다음에 <b>계약 정보 창</b>이 뜬다 — 발견물과 증거품이 거기 적힌다.
            var sheet = GameInfo.ContractSheetOf(_game);
            ContractDialog.Show(_view, sheet.Contract, _player.Date,
                                sheet.HintName, sheet.Found, sheet.Evidence,
                                _game.Sponsors?.FindByName(sheet.Contract?.Sponsor ?? "")?.Name);

            var stage = _view as CityPicView;
            int paid;
            Palace.ReportGrade grade;
            bool scooped;
            try
            {
                // 보고하는 동안 도시 그림이 파래진다 — 바다에서 발견할 때와 같다.
                stage?.Shade(true);
                (paid, grade, scooped) = ReportEach(patron, contract, rows, inTime);
            }
            finally
            {
                stage?.Shade(false);
            }

            // 사례는 파란 막이 걷힌 뒤에 받는다. 줄도 갈래마다 다르다
            // (0x005304B0 · 0x00530570 · 0x00530648 · 0x00530788).
            string him = patron.Name;
            GameDialog.Show(_view, grade != Palace.ReportGrade.Poor && !scooped
                ? $"금화 {paid}닢을 받았다!"
                : inTime ? $"{him}{GameUi.Josa(him, "은", "는")} 금화 {paid}닢 밖에 지불하지 않았다!"
                         : $"{him}{GameUi.Josa(him, "은", "는")} 돈을 지불하지 않았다!");

            // 마무리 대사(0x00411010 — 기한 · 세계일주인가 · 남이 먼저 발표했는가 셋을 받는다).
            // <code>
            //   0041115b  cmp 기한, 0     ; je  → 짧은 벌
            //   00411165  cmp 알려짐, 0   ; jne → 짧은 벌
            //   0041116f  → 긴 벌 — 0x0052FA50 · 0x0052FA78 · 0x0052FAB8
            //   0041119a  → 짧은 벌 — 0x0052FB08 · 0x0052FB28 · 0x0052FB58
            // </code>
            // 남이 먼저 발표해 버렸어도 짧은 벌이다(0x00411165).
            if (world) WorldFinale(patron, inTime, Pick3);
            else Say(inTime && !scooped
                ? Pick3("잘 했네. 무슨 일이 있으면 또 오게나.",
                        "수고하셨습니다. 다시 모험을 하게 되신다면 여기에 와 주십시오.",
                        "음음, 잘 했네. 또 흥미있는 이야기가 있을 때는 원조하겠네. 부담없이 와 주게나.")
                : Pick3("무슨 일이 있으면 또 오게나.",
                        "다시 모험을 하게 되신다면 여기에 와 주십시오.",
                        "또 흥미있는 이야기가 있을 때는 원조하겠네. 부담없이 와 주게나."));
        }
        finally
        {
            _game.Bgm.Play(_cityTrack);
        }

        // 나설 때의 차례 그대로다(0x0044E6C0) — 부하 재계약(0x00454160) · 빌린 배 돌려주기(0x004105A0) 다음이
        // 숨겨 둔 증거품(0x0041C480)이다.
        RecontractMates();

        // 숨겨 둔 증거품은 보고를 마치고 나설 때 손에 들어온다.
        HandHidden();

        // 보고가 끝나면 그 줄이 사라져야 한다 — 계약이 없어졌으니 「보고」 줄도 없다.
        // 줄 목록을 다시 지어 그리게 한다(TownWorks.LinesOf 가 후원자 줄을 다시 고른다).
        _menu.Refresh();
    }

    /// <summary>
    /// 계약중단 — 계약을 깨고 위약금을 문다.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x0044F7A0</c> 이다. <b>기한이 지났다고 저절로 무슨 일이 나지는 않는다</b> —
    /// 후원자를 다시 찾아갔을 때에야 따진다.
    /// <code>
    /// 44f7aa  ebx = (남은기한 &gt; 0) ? 1 : 0                ; 0x004ADB40
    /// 44f7c1  0x0044F2E0(건물, ebx)                        ; 내 쪽 대사
    /// 44f7c9  0x0044F4C0(건물, ebx)                        ; 집사 대사 — 기한을 넘겼으면 딴 말
    /// 44f7d6  edi = 0x0044F8B0(후원자, ebx)                 ; 용서받나
    /// 44f826  위약금 = 계약금 / 2                            ; 받은 선금과 같다
    /// 44f831  못 내면 "위약금을 지불할 수 없습니다!" 하고 미움을 산다
    /// 44f895  0x0044EEA0(건물)                              ; 계약을 끝낸다
    /// </code>
    /// 용서 판정은 이렇다.
    /// <code>
    /// 44f8b0  주사위 = rand(기한을 넘겼으면 150, 아니면 100)
    /// 44f8ca  문턱  = min(97, [후원자+0x20] + [0x5B60D0] + 1)
    /// 44f8de  용서받는다 = 주사위 &lt; 문턱
    /// </code>
    /// <c>[후원자+0x20]</c> 은 <b>친밀도</b>이고 <c>[0x5B60D0]</c> 은 <b>운</b>이다 —
    /// 모조품을 봐주는 판정과 똑같은 꼴이라, 정든 사이일수록·운이 좋을수록 잘 봐 준다.
    /// </remarks>
    public void BreakContract(Patron patron) => Alone(() => BreakContractNow(patron));

    private void BreakContractNow(Patron patron)
    {
        if (_player.IsBetrayed(patron.Name))
        {
            Reckon(patron);
            return;
        }

        if (_player.Contract is not { } contract) return;

        var owner = Owner;
        var face = FaceOf(patron);
        void Say(string text) => TalkDialog.Say(_view, face, "", text);

        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        bool overdue = contract.IsOverdue(_player.Date);
        // 원본은 「계약중단」을 누르면 묻지 않고 곧바로 집사가 나선다(0x0044F7A0).

        _cityMenu.Close();

        // 보고 · 설득과 같은 자리다 — 집사가 맞는 순간부터 알현 곡이 돌고, 나서면 도시
        // 곡으로 돌아간다(PersuadeBody 의 볼트 주석 참고).
        _game.Bgm.Play(BgmPlayer.SponsorTrack);
        try
        {
            // 문간에서 집사가 먼저 맞는다(0x0044F2E0) — <b>기한을 지켰는지</b>와 <b>계약한 사람이
            // 아직 그 자리에 앉아 있는지</b>로 네 갈래다(0x0054B520 · 0x0054B558 · 0x0054B5A0 ·
            // 0x0054B618). 뒤에 이어지는 「…이 왔습니다」는 집사가 주인에게 아뢰는 딴 말이다.
            GreetAtDoor(patron, contract, overdue);

            // 집사가 먼저 알린다. 기한을 넘겼으면 말이 달라진다.
            string me = _player.Name;
            // 계약을 맺은 사람이 은퇴하고 뒷사람이 앉았으면 집사가 <b>선대의 계약</b>이라 이른다
            // (0x0054B7A0 · 0x0054B7F0) — 주인의 대꾸도 따로 있다(0x0054B860 · 0x0054B878 · 0x0054B8A0).
            bool handed = contract.Sponsor != patron.Name;
            Say(handed
                ? (overdue
                    ? $"{patron.Name}님. {_player.NationName}의 {me}{GameUi.Josa(me, "이", "가")} 왔습니다. "
                      + "전 주인과 계약을 맺은 모양입니다만, 기한을 넘은 데다 아무런 성과도 없는 듯 합니다만."
                    : $"{patron.Name}님. {_player.NationName}의 {me}{GameUi.Josa(me, "이", "가")} 왔습니다. "
                      + "뭔가, 전 주인과의 계약을 파기 하고 싶다고 합니다만.")
                : (overdue
                    ? $"{patron.Name}님. {me}{GameUi.Josa(me, "이", "가")} 돌아왔습니다. "
                      + "기한을 넘은 데다, 아무런 성과도 없는 듯 합니다만."
                    : $"{patron.Name}님. {me}{GameUi.Josa(me, "이", "가")} 왔습니다. "
                      + "뭔가, 계약을 파기하고 싶다고 합니다만."));

            // 집사 말을 듣고 주인이 먼저 한숨을 짓는다(0x0044F2E0) — 말투 셋 x 기한 둘이다.
            // 선대의 계약이면 그 자리에 딴 말이 든다(0x0054B860 · 0x0054B878 · 0x0054B8A0).
            // <code>
            //   기한 안  0x0054B6D0 · 0x0054B6E0 · 0x0054B6F0
            //   늦음     0x0054B760 · 0x0054B770 · 0x0054B788
            // </code>
            Say(handed
                ? Pick3("전 주인과 계약....", "아니... 전 주인과의 계약입니까...", "흐~음, 전 주인과의 계약이라니...")
                : overdue
                    ? Pick3(".........", "무슨 일일까요...", "후~, 기대하고 있었건만.")
                    : Pick3("뭐라고...", "뭐라고...", "후~... 계약을 파기하리라고는."));

            // 계약중단은 어느 갈래로 끝나든 후원자가 삐진다(0x0044EEA0 의 비트 14) — 30일 동안 설득을 물린다.
            // 부관의 「제독, 곤란하게 되었습니다…」(0x00532430)는 여기서 안 나온다 — 감찰관을 처벌했을 때
            // 나서는 말이다(0x0044E6FD 의 +0xBC == 2).
            bool forgiven = Forgiven(patron, overdue);
            if (!forgiven)
            {
                // 용서받지 못하면 곧바로 죄를 묻는다(0x0044F7EB → 0x0044F87D 의 0x0044F100) —
                // 친밀도 −20 뒤 봐줌·위약금·감옥으로 갈린다.
                bool doomed = Punish(patron, _game.Sponsors?.FindByName(patron.Name), Pick3);
                _player.Sulk(patron.Name);
                ReturnLentShips(broken: true);
                _player.EndContract();
                if (doomed) { EndGame(); return; }
                RecontractMates();
                return;
            }

            // 눈감아 주는 말도 세 벌씩이다.
            // <code>
            //   기한 안  0x0054BA60 · 0x0054BAB8 · 0x0054BB00
            //   늦음     0x0054BB48 · 0x0054BBA8 · 0x0054BC08
            // </code>
            Say(overdue
                ? Pick3("자네에게 기대한 내가 어리석었다. 어쩔 수 없군. 실패한 죄는 묻지 않겠다. 빨리 사라져 버려라.",
                        "당신에게 기대했는데 실망했습니다. 실패한 죄는 묻지 않겠습니다. 제 앞에서 사라져 주십시오.",
                        "기대가 빗나갔군! 이번 실패는 잊어주지. 생각이 바뀌기 전에 나가주게.")
                : Pick3("안됐지만, 싫다는 사람을 강제로 보내서 좋을 일은 없다. 좋다, 계약은 없었던 일로 하지.",
                        "안됐군요, 무리하게 보내서는 성과도 없을테니, 이 계약은 잊어버립시다.",
                        "그래... 싫은가. 정말 안됐네. 어쩔 수 없군. 계약은 없었던 일로 하지."));

            int penalty = contract.Penalty;
            if (!_player.Pay(penalty))
            {
                GameDialog.Show(_view, "위약금을 지불할 수 없습니다!");
                Say(Pick3("이 바보같은 녀석!",
                          "이런 바보같은!",
                          "바보같은, 위약금을 지불할 수 없다고! 어디까지 어리석은..."));

                // <b>못 내면 죄를 묻는다</b>(0x0044F87F 가 0x0044F100 을 부른다) — 그 안에서
                // 친밀도를 20 깎고 용서·위약금·감옥으로 갈린다.
                bool over = Punish(patron, _game.Sponsors?.FindByName(patron.Name), Pick3);

                _player.Sulk(patron.Name);
                ReturnLentShips(broken: true);
                _player.EndContract();
                if (over) { EndGame(); return; }

                RecontractMates();
                return;
            }

            // 냈으면 친밀도만 20 깎인다(0x0044F886 이 -0x14 를 0x00478530 에 넘긴다).
            _player.Endear(patron.Name, -BreakPenaltyCloseness);
            _player.Sulk(patron.Name);
            ReturnLentShips(broken: true);
            _player.EndContract();
            // 낼 수 있으면 말 없이 돈만 빠진다(0x0044F874 → 0x0047CBC0) — 알림은 없다.
            RecontractMates();
        }
        finally
        {
            _game.Bgm.Play(_cityTrack);
        }
    }

    /// <summary>
    /// 계약을 그만두러 왔을 때 문간에서 집사가 맞는 말(<c>0x0044F2E0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   기한 안 · 그 사람   0x0054B520  "%s님. %s에게 용건이 있으시면, 안내하겠습니다만...."
    ///   기한 넘음 · 그 사람 0x0054B558  "너는 %s... 용케도 얼굴을 내밀었군. 그 배짱을 보아…"
    ///   기한 안 · 뒷사람    0x0054B5A0  "%s님. 잘 맞춰 오셨습니다. 몇일전에 …새로운 주인이…"
    ///   기한 넘음 · 뒷사람  0x0054B618  "너는 %s... 용케도 얼굴을 내밀었군. 가르쳐 주겠지만…"
    /// </code>
    /// 갈래를 가르는 것은 <c>0x0044F7B0</c> 이 넘기는 「기한이 남았나」와
    /// <c>0x0044E590</c> 의 「계약한 사람이 그 자리인가」다.
    /// </remarks>
    private void GreetAtDoor(Patron patron, Contract contract, bool overdue)
    {
        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        string shown = sponsor?.Name ?? patron.Name;
        string sir = sponsor?.Honorific ?? "각하";
        string me = _player.Name;

        var old = _game.Sponsors?.FindByName(contract.Sponsor);
        string oldName = $"{old?.Name ?? contract.Sponsor} {old?.Honorific ?? "각하"}";
        string now = $"{shown} {sir}";
        bool handed = contract.Sponsor != patron.Name;

        void Steward(string words) => TalkDialog.Say(_view, StewardFace(), "", words);

        if (!handed)
        {
            Steward(overdue
                ? $"너는 {me}... 용케도 얼굴을 내밀었군. 그 배짱을 보아 {now}"
                  + $"{GameUi.Josa(sir, "을", "를")} 만나게 해 주지."
                : $"{me}님. {sir}에게 용건이 있으시면, 안내하겠습니다만....");
            return;
        }

        Steward(overdue
            ? $"너는 {me}... 용케도 얼굴을 내밀었군. 가르쳐 주겠지만 {oldName}"
              + $"{GameUi.Josa(oldName, "이", "가")} 은퇴해 지금은 {now}"
              + $"{GameUi.Josa(now, "이", "가")} 새로운 주인이 되었다. 일단 안내하겠네."
            : $"{me}님. 잘 맞춰 오셨습니다. 몇일전에 {oldName}"
              + $"{GameUi.Josa(oldName, "이", "가")} 은퇴하여 지금은 {now}"
              + $"{GameUi.Josa(now, "이", "가")} 새로운 주인이 되었습니다. 오늘은 무슨 용건이십니까?");
    }

    /// <summary>
    /// 감찰관을 처벌한 뒤 옛 후원자를 찾아갔을 때의 결판(<c>0x0044F8F0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   집사 인사 → 집사 보고 → 후원자 한마디
    ///   관용 굴림(0x0044FC10)   기한 안: 성미[4] &gt; 0, 지남: 성미[4] == 2 라야 굴린다
    ///                           rand(150 · 200) &lt; 친밀도 + 운 + 1 — 못 넘으면 감옥
    ///   「감찰관은 어디에 있나?」 [병에 걸려 죽었다 / 도망쳤다]
    ///   거짓말 1   rand(120 · 150) ≤ 지력 + 1 — 못 넘으면 감찰관이 직접 나와 들킨다 → 친밀도 −20 · 감옥
    ///   거짓말 2   후원자 표 +0x30 &lt; rand(운 + 1) — 못 넘으면 「감찰관이 돌아오지 않을 이유가 없다!」 → −20 · 감옥
    ///   위약금     후원자 표 +0x20 × (성미[4] + 1) × 1000 — 못 내면 죄를 묻는다(0x0044FBBD)
    ///              내면 악명 +(rand100 + 150)×(199 − 매력)/100 · 친밀도 −20
    ///   끝에 배신 표시를 지우고(0x0044FBE7) 기분을 상하게 둔다
    /// </code>
    /// 후원자 성미는 NPC 셈(얼굴·혈액형·나라, <see cref="Sea.FleetRaid.FortuneOf"/>)으로 센다 — 후원자 객체의
    /// 가상 함수가 같은 셈인지는 확인하지 못했다. 대사는 신분마다 세 벌인데 한 벌만 쓴다.
    /// </remarks>
    private void Reckon(Patron patron)
    {
        var betrayal = _player.Betrayals.First(b => b.Sponsor == patron.Name);
        bool inTime = _player.Date < betrayal.DueOn;
        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        string shown = sponsor?.Name ?? patron.Name;
        string sir = sponsor?.Honorific ?? "각하";
        string me = _player.Name;
        var dice = new GameRandom(Environment.TickCount);

        var face = FaceOf(patron);
        void Say(string words) => TalkDialog.Say(_view, face, "", words);
        void Steward(string words) => TalkDialog.Say(_view, StewardFace(), "", words);

        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        _cityMenu.Close();
        bool over = false;
        _game.Bgm.Play(BgmPlayer.SponsorTrack);
        try
        {
            Steward(inTime
                ? $"아니, {me}님. {shown} {sir}에게 볼 일이시라면 안내하겠습니다만."
                : $"너는, {me}... 용케도 얼굴을 내밀었군. 그 배짱을 보아 {shown} {sir}{GameUi.Josa(sir, "을", "를")} 만나게 해 주지.");
            Steward($"{sir}. {me}{GameUi.Josa(me, "이", "가")} 왔습니다. 조금 전의 계약을 없었던 일로 하자고 합니다만...");
            // 0x0054B990 · 0x0054B998 · 0x0054B9A8 (기한 안) / 0x0054B9C8 · 0x0054B9E8 · 0x0054BA30 (늦음)
            Say(inTime
                ? Pick3("그래...", "그렇습니까...", "후~, 계약을 파기하리라고는.")
                : Pick3("기한을 넘은데다 그 꼴이라니...",
                        "그런 건방진. 기한을 지키지도 못한데다 약속을 없었던 일로 하자니...",
                        "사람을 기다리게 해 놓구선... 장난을 치다니!"));

            int[] fortune = SponsorFortune(sponsor);
            int kindness = fortune[4];
            int luck = _player.AbilityOf(Ability.Luck), mind = _player.AbilityOf(Ability.Mind),
                charm = _player.AbilityOf(Ability.Charm);

            bool mercy = (inTime ? kindness > 0 : kindness == 2)
                         && dice.Next(inTime ? 150 : 200) < _player.ClosenessOf(patron.Name) + luck + 1;
            if (!mercy)
            {
                // 못 넘으면 죄를 묻는 본체로 간다(0x0044F100) — 친밀도 −20 뒤, 배신 깃발(13)이 서 있으니
                // 곧장 감옥이고 말은 말투 셋 가운데 하나다(0x0054B4A8 · 0x0054B4C8 · 0x0054B4E0).
                over = Punish(patron, sponsor, Pick3);
                return;
            }

            // 0x0054BCD8 · 0x0054BD50 · 0x0054BDC8 (기한 안) / 0x0054BE30 · 0x0054BEB8 · 0x0054BF38 (늦음)
            Say(inTime
                ? Pick3("안됐지만, 싫다는 자를 억지로 보내서 좋을 일은 없지. 좋다. 계약은 없었던 일로 하지.\n그건 그렇고, 감찰관은 어디에 있나?",
                        "안됐군요. 무리하게 보내서는 성과도 없을테니 이 계약은 없었던 일로 하지요.\n그런데 동행한 감찰관이 없는 듯 합니다만.",
                        "그래.... 싫은가. 안됐군. 어쩔 수 없다. 계약은 없었던 일로 하지.\n응? 감찰관의 모습이 보이지 않는데...")
                : Pick3("어쩔 수 없다. 너같이 무능한 자에게 맡긴 내가 어리석었다. 좋다. 계약은 없었던 일로 하지.\n그런데 자네에게 붙인 감찰관은 어디에 있는가?",
                        "어쩔 수 없군요. 당신에게 부탁한 제가 어리석었습니다. 이 계약은 없었던 일로 하지요.\n그런데 당신에게 붙인 감찰관이 없군요.",
                        "그렇게 무능하리라고는... 나도 보는 눈이 없어졌나 보군 후~, 어쩔 수 없군. 계약일은 잊어버려 주지\n응? 감찰관은 어떻게 됐나?"));
            string word = ChoiceDialog.Pick(_view, "", ["병에 걸려 죽었다", "도망쳤다"]) == 1 ? "도망쳤다" : "죽었다";

            if (dice.Next(inTime ? 120 : 150) > mind + 1)
            {
                // 「%s%s」는 「죽었다·도망쳤다」에 조사 라면/이라면 을 붙인 것이다(0x0044FB67 의 0x004281B0(말, 9)).
                TalkDialog.Say(_view, _game.Faces?.TryGetBgra(Inspector.Face, female: false), "",
                               $"나라면 여기 있지만, 여행지에서 {word}{NameToken.Of(word, 9)} 누구를 말하는 건가?");
                // 0x0054C338 · 0x0054C360 · 0x0054C398
                Say(Pick3("이 거짓말장이를 감옥에 집어 넣어라!",
                          "자네들을 믿고 있었건만... 이 자들을 감옥에 집어 넣어라!",
                          "나를 속이려 하다니. 이 거짓말쟁이! 감옥에서 머리나 식히게!"));
                _player.Endear(patron.Name, -20);
                over = Jail(patron, dice);
                return;
            }

            if ((sponsor?.Closeness ?? 60) >= dice.Next(luck + 1))
            {
                // 0x0054C188 · 0x0054C1F8 · 0x0054C270
                Say(Pick3("감찰관이 돌아오지 않을 이유가 없다! 자네, 뭔가 불리한 일이 있어 없앤게 아닌가! 그 녀석을 감옥에 쳐 넣어라.",
                          "그 감찰관은 내 충복이다. 꼭 돌아 올 것이다...자네 설마...그자를...아아, 이런 일이! 누가, 이 자를 감옥에 끌고 가게.",
                          "그런 바보 같은! 감찰관이 돌아오지 못할 이유가 없지 않은가! 설마... 죽였군!! 요, 용서할 수 없다! 감옥에 쳐 넣어라."));
                _player.Endear(patron.Name, -20);
                over = Jail(patron, dice);
                return;
            }

            int penalty = (sponsor?.Eye ?? 50) * (kindness + 1) * 1000;
            // 0x0054BFE0 · 0x0054C020 · 0x0054C068
            Say(string.Format(Pick3(
                "그래... 그건 어쩔 수 없군. 그럼 위약금으로 금화 {0}닢을 받겠다.",
                "그렇습니까... 어쩔 수 없군요. 그럼 금화 {0}닢을 위약금으로 받겠습니다.",
                "뭐...그게 정말인가. 흐~음, 그렇다면 하는 수 없군. 그럼 위약금은 금화 {0}닢이다. 이것으로 계약일은 잊어버리지."), penalty));
            if (!_player.Pay(penalty))
            {
                GameDialog.Show(_view, "위약금을 지불할 수 없습니다!");
                // 이어 후원자가 말투대로 한마디 한다(0x0044FB27 — 0x0054C120 · 0x0054C138 · 0x0054C148).
                // 계약중단 쪽 말(0x0054BC70~)과는 띄어쓰기가 다른 딴 글이다.
                Say(Pick3("이 바보 같은 녀석!", "이런 바보 같은!",
                          "바보 같은, 위약금도 지불할 수 없다고! 어디까지 어리석은..."));
                // <b>여기서도 곧장 감옥은 아니다</b>(0x0044FBBD) — 죄를 묻는 본체가
                // 용서·다시 물리는 위약금·감옥으로 갈라 준다.
                over = Punish(patron, sponsor, Pick3);
                return;
            }

            GameDialog.Show(_view, $"위약금으로 금화 {penalty}닢을 지불했다!");
            _player.Infamy += (dice.Next(100) + 150) * Math.Max(0, 199 - charm) / 100;
            _player.Endear(patron.Name, -20);
        }
        finally
        {
            // 어느 결과든 결판은 났다 — 추격이 끝나고 한동안 기분이 상해 있다.
            _player.SettleBetrayal(patron.Name);
            _player.Sulk(patron.Name);
            _game.Bgm.Play(_cityTrack);
            if (over) EndGame();
        }
    }

    /// <summary>
    /// 감옥(<c>0x0044EF20</c>). 놀이가 끝났으면 true.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   항구가 없는 도시   「%s%s 감옥에서 일생을 마쳤다....」 → GAME OVER
    ///   항구 도시          「그리고 %d년의 세월이 흘렀다」  년 = rand(2) + (109 − 운)/10
    ///                      체력·매력 −5×년 · 소지금 0 · 명성 −2000 · 악명 +(rand300 + 500)×(199 − 매력)/100
    ///                      그 후원자 친밀도 0 · 소지품·보관품·배를 잃는다 → 「용케도 살아 있었군. 끈질긴 놈이군.」
    /// </code>
    /// 항구 여부는 게임이 도시 객체 <c>+0x1C</c> 비트 0 으로 보는데, 우리는 그 도시에 항구 건물이 있는지로 가른다.
    /// 감옥은 <b>부하·아내·아이까지 한꺼번에</b> 잃는 유일한 자리다(<c>0x004534E0</c> · <c>0x00465900</c> ·
    /// <c>0x0047D640</c> 을 차례로 부른다) — 말 한마디 없이 사라진다. 저금과 모항은 그대로다.
    /// </remarks>
    private bool Jail(Patron patron, GameRandom dice)
    {
        string me = _player.Name;
        bool harbor = _game.Buildings?.InCity(_cityId).Any(b => b.Kind == "항구") ?? true;
        if (!harbor)
        {
            GameDialog.Show(_view, $"{me}{GameUi.Josa(me, "은", "는")} 감옥에서 일생을 마쳤다....");
            return true;
        }

        int luck = _player.AbilityOf(Ability.Luck), charm = _player.AbilityOf(Ability.Charm);
        int years = dice.Next(2) + (109 - luck) / 10;
        GameDialog.Show(_view, $"그리고 {years}년의 세월이 흘렀다");

        _player.AdvanceDays(years * 365);
        var stats = _player.Abilities.ToArray();
        stats[Ability.Body] = Math.Max(Ability.Min, stats[Ability.Body] - 5 * years);
        stats[Ability.Charm] = Math.Max(Ability.Min, stats[Ability.Charm] - 5 * years);
        _player.SetAbilities(stats);
        _player.SetGold(0);
        _player.Fame = Math.Max(0, _player.Fame - 2000);
        _player.Infamy += (dice.Next(300) + 500) * Math.Max(0, 199 - charm) / 100;
        _player.Endear(patron.Name, -Player.MaxCloseness);

        // 부하는 말없이 다 흩어지고(0x004534E0), 아내와 아이도 사라진다(0x00465900 · 0x0047D640).
        for (int slot = 0; slot < _player.Mates.Count; slot++) _player.SetMate(slot, "");
        _player.Marry(null);
        _player.ClearChildren();

        _player.LoseBelongings();
        _player.LoseAllShips();

        TalkDialog.Say(_view, FaceOf(patron), "", "용케도 살아 있었군. 끈질긴 놈이군.");
        return false;
    }

    /// <summary>놀이를 끝낸다 — 도시 발견 대본이 게임 오버로 끝날 때와 같은 차례다.</summary>
    private void EndGame()
    {
        GameOverDialog.Show(_view, _game.EventStills, GameOverDialog.MutinyLost, bgm: _game.Bgm);
        if (_view.Owner is ShipMapWindow map) _view.Dispatcher.BeginInvoke(map.ReturnToTitle);
    }

    /// <summary>
    /// 그 후원자의 말투(<c>0x00469450</c>) — 여자면 존댓말(1), 직업 코드 18~21 이면 둘째 반말(2), 그 밖은 0.
    /// </summary>
    private int StyleOf(Patron patron)
    {
        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        return sponsor is { IsFemale: true } ? 1 : sponsor is { JobCode: >= 18 and <= 21 } ? 2 : 0;
    }

    /// <summary>후원자 성미 여덟 칸. 표를 못 읽었으면 다 보통(1)이다.</summary>
    internal static int[] SponsorFortune(SponsorTable.Sponsor? sponsor) =>
        sponsor is { } s ? Engine.Sea.FleetRaid.FortuneOf(s.Face, s.Blood, s.Nation) : [1, 1, 1, 1, 1, 1, 1, 1];

    /// <summary>
    /// 계약이 끝나면 <b>부하마다 다시 태울지</b> 묻는다(게임 <c>0x00454160</c>).
    /// </summary>
    /// <remarks>
    /// 볼트 <c>90.분석-보고 뒤 부하 재계약</c>. 보고로 끝나든 파기로 끝나든 계약이 끝난 건물을 나설 때
    /// 돈다(<c>0x0044E6C0</c>). 자리 0~3 을 차례로 보고 빈 자리만 건넌다 — 역사 인물·말·충성은 안 본다.
    /// <code>
    ///   선금 = 밑값 x (10 - 웅변) / 3          0x004541F0 → 0x00453940 (고용 계약금과 같다)
    ///   선금 &gt; 소지금  → 묻지 않고 떠난다   「이것으로 제독과의 계약을 달성했군요. 또 일이 있으면 불러 주십시오.」 0x0055AC18
    ///   YES           → 선금만 낸다(0x0047CBC0)
    ///   NO            → 떠난다(0x00453470)   「또 일이 있으면 불러 주십시오!」 0x0055ABF8
    /// </code>
    /// 말은 모두 그 부하 얼굴로 한다. 무작위는 없다.
    /// </remarks>
    private void RecontractMates()
    {
        int eloquence = _player.LevelOf(Skill.Names[Skill.Rhetoric]);
        for (int slot = 0; slot < Player.MaxMates; slot++)
        {
            string name = _player.MateAt(slot);
            if (name.Length == 0) continue;

            var face = _player.MateInfoOf(name) is { } info
                ? _game.Faces?.TryGetBgra(info.Face, female: false) : null;
            int baseFee = _game.World?.People.FirstOrDefault(r => r.Name == name)?.Fee ?? 0;
            int fee = Math.Max(0, baseFee * (10 - eloquence) / 3);

            if (fee > _player.Gold)
            {
                _player.SetMate(slot, "");
                TalkDialog.Say(_view, face, "", "이것으로 제독과의 계약을 달성했군요. 또 일이 있으면 불러 주십시오.");
                continue;
            }

            if (ConfirmDialog.Ask(_view,
                    $"선금으로 금화 {fee}닢이라면 한번 더 제독의 배를 탈 수 있습니다. 어떻게 하시겠습니까?",
                    null, face))
            {
                _player.Spend(fee);
                continue;
            }

            _player.SetMate(slot, "");
            TalkDialog.Say(_view, face, "", "또 일이 있으면 불러 주십시오!");
        }
    }

    /// <summary>
    /// 계약이 끝나 <b>빌린 배를 거둬 간다</b>(<c>0x0040FE40</c>) — 보고로 끝나든 파기로 끝나든 돈다.
    /// </summary>
    /// <remarks>
    /// 배가 한 척도 안 남으면 짐을 대신 팔아 준다 — <b>그 도시 매각가의 절반</b>이다
    /// (<see cref="Palace.DistressPrice"/>).
    /// </remarks>
    private void ReturnLentShips(bool broken = false)
    {
        bool mate = _player.MateAt(0).Length > 0;
        bool hadCargo = _player.CargoHold.Count > 0;
        if (_player.TakeBackLentShips() == 0) return;

        // 계약을 파기했으면 짐까지 가져간다(0x0040FE5C) — 빌린 배가 있었을 때만이다.
        if (broken && hadCargo)
        {
            _player.DropAllCargo();
            GameDialog.Show(_view, mate ? Palace.CargoSeized : Palace.CargoSeizedAlone);
        }

        if (_player.Ships.Count > 0)
        {
            GameDialog.Show(_view, mate ? Palace.ShipsReturned : Palace.ShipsReturnedAlone);
            // 남은 배에 짐이 넘치면 짐 덜기 창이다(0x0040FEFD).
            CargoDropDialog.Force(_view, _game, _cityId);
            return;
        }

        if (_player.CargoHold.Count == 0)
        {
            GameDialog.Show(_view, mate ? Palace.ShipsReturned : Palace.ShipsReturnedAlone);
            return;
        }

        _tradePost ??= _game.Trade is { } trade && _game.Goods is { } goods
            ? new Engine.Market.TradePost(trade, goods, _game.Rates, _game.CityRows, _game.Nations,
                            _game.Discoveries?.Table)
            : null;
        int gold = _tradePost is not { } post ? 0 : _player.CargoHold.Sum(
            c => c.Count * Palace.DistressPrice(post.SellPrice(_player, _cityId, c.Kind)));

        _player.DropAllCargo();
        _player.Earn(gold);
        GameDialog.Show(_view, mate ? Palace.ShipsReturnedCargoSold
                                    : Palace.ShipsReturnedCargoSoldAlone);
        GameDialog.Show(_view, $"금화 {gold}닢을 손에 넣었다!");
    }

    /// <summary>
    /// 처벌한 후원자가 빌려준 배들이 나를 따를지 가른다(<c>0x004101B0</c>).
    /// </summary>
    /// <remarks>
    /// 여느 계약 끝과 달리 <b>돌려줄 상대가 없다</b> — 그래서 배마다 남을지 굴린다. 규칙은
    /// <see cref="LentShips"/> 에 있다. 부하들이 안 따르면 「선장」이 일기토를 걸고,
    /// <b>지면 그 자리에서 판이 끝난다</b>(<c>0x0044AF40(4)</c>).
    ///
    /// <b>원본과 다른 데 하나.</b> 게임은 배마다 어느 후원자가 빌려준 것인지 적어 두어 처벌한
    /// 사람의 배만 고르는데, 우리 배는 빌린 것인지 아닌지만 안다. 처벌은 <b>지금 계약</b>의
    /// 후원자에게만 할 수 있으니 빌린 배도 그 사람 것뿐이라 보고 다 건다.
    /// </remarks>
    private void MutinousLentShips(string sponsor)
    {
        var lent = _player.Ships.Where(s => s.Lent).ToList();
        if (lent.Count == 0) { ReturnLentShips(); return; }

        var dice = _random;
        var sir = _game.Sponsors?.FindByName(sponsor);
        string shown = sir?.Name ?? sponsor;

        // 부하들이 순순히 따르지 않으면 배 한 척의 선장이 나서서 겨루자고 한다.
        if (!LentShips.Obeys(_player.AbilityOf(Ability.Charm), _player.Fame, _player.Infamy, dice)
            && !WonLoyaltyDuel(shown, lent[0].Name))
        {
            // 베였다 — 그 자리에서 판이 끝난다(0x0044AF40(4)). 배는 손대지 않는다.
            GameOverDialog.Show(_view, _game.EventStills, GameOverDialog.MutinyLost, bgm: _game.Bgm);
            if (_view.Owner is ShipMapWindow map)
                _view.Dispatcher.BeginInvoke(map.ReturnToTitle);
            return;
        }

        // 함대가 온통 빌린 배면 그래도 한 척은 남는다(0x004104B0).
        bool keep = LentShips.KeepsOne(lent.Count, _player.Ships.Count);
        var stays = new List<Ship>();
        foreach (var ship in lent)
        {
            bool mine = (keep && stays.Count == 0)
                        || LentShips.Stays(_player.AbilityOf(Ability.Luck), dice);
            if (mine) stays.Add(ship);
        }

        // 남는 배는 대출 표시를 지워 내 배가 되고(0x00410380), 표시가 남은 배는 떠난다.
        foreach (var ship in stays) ship.Keep();
        _player.TakeBackLentShips();

        // 떠난 배만큼 짐이 넘치면 짐 덜기 창이다(0x00410354 — 0x005A4D18 비트 8 일 때).
        CargoDropDialog.Force(_view, _game, _cityId);

        // <b>떠난다는 말은 없다.</b> 원본도 그 자리에서 아무 말을 안 한다 — 「%s호가
        // 탈주했습니다!」는 조건이 뒤집혀 절대 안 뜨는 죽은 가지다(LentShips 주석).
    }

    /// <summary>
    /// 「선장」과의 일기토(<c>0x0040FFC0</c>). 베였을 때만 false — 그러면 판이 끝난다.
    /// </summary>
    private bool WonLoyaltyDuel(string sponsor, string ship)
    {
        var face = _game.Faces?.TryGetBgra(LentShips.CaptainFace, female: false);
        TalkDialog.Say(_view, face, "", LentShips.Challenge(sponsor));

        var foe = LentShips.CaptainOf(_random) with { Name = LentShips.DuelName(ship) };
        var duel = new Duel(Mine(), foe, _player.Items.Contains(Duel.EdithShieldId),
                            Environment.TickCount);
        var dice = new GameRandom(Environment.TickCount);
        DuelDialog.Show(_view, duel, dice, face, _game.Fighters,
                        // 후원자 자리는 술집·여관이 아니므로 무대가 밑값 1(초원)이다(0x004A2D92).
                        FighterSprites.SetForCulture(_culture), arena: DuelArt.Field, bgm: _game.Bgm);
        // 지면 여느 일기토처럼 도망·용서·죽음이 갈리고, <b>베였을 때만</b> 놀이가 끝난다(0x00410145 의
        // 결과 3). 졌어도 살았으면 이긴 것과 같이 이어 간다 — 원본 함수는 그때도 1 을 낸다.
        // 빌린 배 선장과의 판은 종류 4 라 도망도 용서도 없다(0x004A9EDE).
        if (duel.Won != true && TavernMenu.LostDuel(_view, _player, duel, face, dice,
                                                    mateFought: false, canFlee: false, canSpare: false))
            return false;
        _player.Hurt(duel.BodyLost);
        return true;
    }

    /// <summary>일기토에 나서는 내 몫.</summary>
    private Duel.Fighter Mine() =>
        new(_player.Name.Length > 0 ? _player.Name : "제독",
            _player.AbilityOf(Ability.Body),
            _player.AbilityOf(Ability.Might),
            _player.LevelOf(Skill.Names[Skill.Sword]),
            _player.AbilityOf(Ability.Luck),
            BestItem(Duel.WeaponCategory),
            BestItem(Duel.ArmorCategory));

    /// <summary>지닌 것 가운데 그 갈래에서 가장 센 효과. 표를 못 읽었으면 0.</summary>
    private int BestItem(int category)
    {
        if (_game.Items is not { } table) return 0;
        int best = 0;
        foreach (int id in _player.Items)
            if (table.Find(id) is { } item && item.Category == category && item.Effect > best)
                best = item.Effect;
        return best;
    }

    private Engine.Market.TradePost? _tradePost;

    // ── 배를 빌린다 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 「배를 빌린다」 줄이 설 조건인지(<c>0x0044EA80</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0044ea83  계약이 있고 이 자리다
    ///   0044ea8e  <b>그 사람 본인</b>이다(0x0044E590 — 선대의 계약으로는 못 빌린다)
    ///   0044ea97  빌린 배가 하나도 없다(0x0040FC20 이 배 표를 훑는다)
    ///   0044eaa7  <b>항구가 있는 도시</b>다([+0x1C] 비트 1)
    ///   0044eac0  기한이 아직 남았다
    /// </code>
    /// </remarks>
    public bool CanBorrow(Patron patron, bool hasHarbor) =>
        hasHarbor
        && _player.Contract is { } c && c.Sponsor == patron.Name && c.City == _cityName
        && c.DaysLeft(_player.Date) > 0
        && !_player.Ships.Any(s => s.Lent)
        && !_player.Docked.Values.Any(list => list.Any(s => s.Lent));

    /// <summary>
    /// 계약 중에 배를 더 빌린다(<c>0x00410660</c>) — 계약을 맺을 때와 말이 다르다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   004106a4  아직 한 번도 안 빌렸으면  0x0055C4F0 · 0x0055C530 · 0x0055C580
    ///   004106c7  또 조르는 것이면          0x0055C5E0 · 0x0055C628 · 0x0055C680
    /// </code>
    /// </remarks>
    public void BorrowShips(Patron patron) => Alone(() => BorrowNow(patron));

    private void BorrowNow(Patron patron)
    {
        if (_player.Contract is not { } contract) return;
        // 모드의 「배 빌림 묻기」는 <b>계약할 때 저절로 묻는 것</b>만 끈다 — 여기는 사람이 줄을 골라 조르는
        // 자리라 꺼 두어도 돈다. 예전에는 여기서도 물러나, 줄은 뜨는데 눌러도 아무 일이 없었다.

        _cityMenu.Close();
        var face = FaceOf(patron);
        void Say(string words) => TalkDialog.Say(_view, face, "", words);

        int style = StyleOf(patron);
        string Pick3(string plain, string polite, string merchant) => style switch { 1 => polite, 2 => merchant, _ => plain };

        int ships = !_player.FleetHere(_cityId) ? 0
                  : Math.Min(contract.Amount / GoldPerShip + 1, Player.MaxShips - _player.Ships.Count);
        if (ships <= 0)
        {
            Say(Pick3("흐음, 빌려주고 싶은 마음은 굴뚝같지만 배가 전부 나가고 없네. 다시 오게.",
                      "안됐지만, 준비할 수 있는 배가 없습니다. 자신의 힘으로 해결해 주십시오.",
                      "흐~음, 때가 나쁘군. 지금은 가지고 있는 배가 없네. 다시 오게."));
            return;
        }

        Say(string.Format(contract.ShipsLent
            ? Pick3("뭐라고? 또 배를 빌려 달라고... 으~음, 그러면 {0}척 준비시키도록 하지.",
                    "···어쩔 수 없군요. 그러면 항구에 {0}척 준비시켜 놓겠습니다. 기대하고 있겠습니다.",
                    "또 배가 필요한가? 어쩔 수 없군. 항구에 {0}척 준비해 놓겠네.")
            : Pick3("좋다. 배를 {0}척 항구에 준비시켜 놓겠네. 충분히 사용하도록 하게.",
                    "좋습니다. 항구에 배를 {0}척 준비시켜 놓겠습니다. 모험에 도움이 될 것입니다.",
                    "처음부터 이야기했으면 좋았을 것을. 좋다. 항구에 배를 {0}척 준비시켜 놓겠다. 충분히 사용하게."),
            ships));

        if ((_player.Ships.Any(s => !s.Lent) || _player.DockedAt(_cityId).Any(s => !s.Lent))
            && !ConfirmDialog.Ask(_view, "배를 빌리겠습니까?"))
        {
            Say(Pick3("그런가. 그렇다면, 좋을 대로 하게.",
                      "그렇습니까. 좋을 대로 하십시오.",
                      "그래, 괜찮겠나."));
            return;
        }

        if (Hull.All.MinBy(h => h.Price) is not { } hull) return;

        int given = 0;
        for (int i = 0; i < ships; i++) if (_player.Give(hull, _cityId)) given++;
        if (given > 0) contract.ShipsLent = true;
        _menu.Refresh();
    }

    // ── 감찰관을 매수 ───────────────────────────────────────────────────────

    /// <summary>
    /// 「감찰관을 매수」 줄이 설 조건인지(<c>0x0044EA30</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   계약 중이고 이 자리이며 보고할 것이 하나 이상    0x0044EA00
    ///   계약의 매수 칸이 아직 열려 있다                  0x0044EA45 ([계약+0x14])
    ///   <b>증거품이 있는 대상이 둘 이상</b>               0x0044EA52
    ///   이미 숨겨 둔 것이 하나도 없다                    0x0044EA60
    /// </code>
    /// </remarks>
    public bool CanBribe(Patron patron) =>
        _player.Contract is { BribeOpen: true }
        && _player.HiddenDiscoveries.Count == 0
        && Bribable(patron).Count > 1;

    /// <summary>숨길 수 있는 것 — 보고할 것 가운데 <b>증거품이 있는</b> 것만이다(<c>0x0046B110</c>).</summary>
    private List<DiscoveryTable.Record> Bribable(Patron patron) =>
        [.. ReportTargets(patron).Where(r => r.GivesItem)];

    /// <summary>
    /// 감찰관을 매수한다(<c>0x0041C550</c>) — 고른 발견물을 이번 보고에서 빼 준다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0041c5a0  「무례한! 나는 돈때문에 주인을 배반하지는 않는다!」   ← 조건 없이 늘 첫 마디
    ///   0041c63d  「아이템 선택」 목록 — 줄은 <b>증거품 이름</b>이다
    ///   0041c66e  다 골랐으면 「증거물 하나도 없이 어떻게 보고할 작정인가!」
    ///   0041c6a0  돈이 모자라면 「그것 뿐이라면, …」 + 「돈이 모자랍니다!」 — <b>네 번째면 쫓겨난다</b>
    ///   0041c74e  「하는 수 없군···금화 %ld닢이라면 못본 척 해 드리지요.」 → 「뇌물을 주겠습니까?」
    ///   0041c6f1  물리면 「매수를 취소하겠습니까?」 — 아니오면 목록으로 돌아간다
    /// </code>
    /// 어떤 결말이든 그 계약 동안은 <b>다시 못 연다</b>(<c>0x0041C723</c>). 숨긴 증거품은
    /// 보고를 마치고 건물을 나설 때 소지품으로 들어온다(<see cref="HandHidden"/>).
    ///
    /// 원본 목록은 <b>여러 개를 한꺼번에 체크</b>하는 창인데 우리 고르기 창에는 그 꼴이 없어
    /// 한 줄씩 켜고 끄다가 「결정」으로 마친다 — 고르는 결과는 같다.
    /// </remarks>
    public void BribeInspector(Patron patron) => Alone(() => BribeNow(patron));

    private void BribeNow(Patron patron)
    {
        if (_player.Contract is not { } contract) return;

        var rows = Bribable(patron);
        var inspectorFace = _game.Faces?.TryGetBgra(Inspector.Face, female: false);
        void Inspector_(string words) => TalkDialog.Say(_view, inspectorFace, "", words);

        Inspector_("무례한! 나는 돈때문에 주인을 배반하지는 않는다!");
        if (rows.Count == 0) return;

        _cityMenu.Close();
        int closeness = _game.Sponsors?.FindByName(patron.Name)?.Closeness ?? DefaultCloseness;
        int all = ReportTargets(patron).Count;
        var picked = new HashSet<int>();
        int tries = 0;

        try
        {
            while (true)
            {
                var lines = rows
                    .Select(r => ((picked.Contains(r.Id) ? "· " : "  ") + EvidenceName(r), true))
                    .Append(("결정", true)).ToList();
                int at = ChoiceDialog.Pick(_view, "아이템 선택", lines, exitRow: false);

                if (at < 0)                                   // 물렸다
                {
                    if (ConfirmDialog.Ask(_view, "매수를 취소하겠습니까?"))
                    {
                        Inspector_("나, 나를 거스릴 작정이냐! 무례한 놈!");
                        return;
                    }
                    continue;
                }
                if (at < rows.Count)                          // 켜고 끈다
                {
                    if (!picked.Remove(rows[at].Id)) picked.Add(rows[at].Id);
                    continue;
                }
                if (picked.Count == 0) continue;              // 하나도 안 골랐으면 다시 묻는다

                // 다 숨기면 보고할 것이 없어진다(0x0041C66E).
                if (picked.Count == all)
                {
                    Inspector_("증거물 하나도 없이 어떻게 보고할 작정인가!");
                    continue;
                }

                int worth = rows.Where(r => picked.Contains(r.Id))
                                .Sum(r => _game.Items?.Find(r.ItemId)?.SellList ?? 0);
                int price = Palace.BribePrice(closeness, worth);

                if (_player.Gold < price)
                {
                    if (++tries > Palace.BribeTries)
                    {
                        Inspector_("가난뱅이와 이야기할 가치도 없군요. 그만둡시다.");
                        return;
                    }
                    Inspector_($"그것 뿐이라면, 금화 {price}닢이라 했건만···도저히 당신이 준비할 수 있을 것 같지 않군요.");
                    GameDialog.Show(_view, "돈이 모자랍니다!");
                    continue;
                }

                Inspector_($"하는 수 없군···금화 {price}닢이라면 못본 척 해 드리지요. 싫다면 상관없지만.");
                if (!ConfirmDialog.Ask(_view, "뇌물을 주겠습니까?")) return;

                _player.Pay(price);
                foreach (int id in picked) _player.Hide(id);
                return;
            }
        }
        finally
        {
            // 창을 연 것만으로 그 계약 동안은 다시 못 연다(0x0041C723).
            contract.BribeOpen = false;
            _menu.Refresh();
        }
    }

    /// <summary>그 발견물의 증거품 이름 — 목록 줄은 발견물 이름이 아니라 이것이다(<c>0x0041C440</c>).</summary>
    private string EvidenceName(DiscoveryTable.Record row) =>
        _game.Items?.Find(row.ItemId)?.Name ?? row.Name;

    /// <summary>
    /// 숨겨 둔 증거품을 소지품에 넣어 준다(<c>0x0044E6C0</c> → <c>0x0041C480</c>).
    /// </summary>
    /// <remarks>보고를 마치고 건물을 나설 때다 — 이것이 없으면 매수가 아무 이득이 없다.</remarks>
    private void HandHidden()
    {
        if (_player.HiddenDiscoveries.Count == 0) return;

        foreach (int id in _player.HiddenDiscoveries.ToList())
        {
            if (_game.Discoveries?.Table?.Find(id) is not { GivesItem: true } row) continue;

            // 넘치면 물릴 수 없는 버리기 창이다(0x0041C480 → 0x004B1710).
            string got = _game.Items?.Find(row.ItemId)?.Name ?? $"아이템 {row.ItemId}";
            GameDialog.Show(_view, $"[{got}]{GameUi.Josa(got, "을", "를")} 손에 넣었다!");
            ItemGain.AddForced(_view, _game, [row.ItemId]);
        }
        _player.ClearHidden();
    }

    /// <summary>계약을 깨는 것을 후원자가 눈감아 주는지(<see cref="Palace.Forgiven"/>).</summary>
    private bool Forgiven(Patron patron, bool overdue) =>
        Palace.Forgiven(_player.ClosenessOf(patron.Name), _player.AbilityOf(Ability.Luck),
                        overdue, _random);

    /// <summary>
    /// 보고 사례. 남이 먼저 발표해 버렸으면 <b>깎인 사례</b>다(<c>0x00411FC0</c> 이 가른다).
    /// </summary>
    private int RewardFor(Contract contract, Palace.ReportGrade grade, bool inTime, bool scooped) =>
        scooped ? Palace.ScoopedRewardFor(contract.Amount, inTime)
                : Palace.RewardFor(contract.Unpaid, grade, inTime, _random);

    /// <summary>
    /// 사례를 가를 때 보는 한 발견물 — <b>보수가 가장 큰 것</b>이다(<c>0x00412109</c>~<c>0x0041216F</c>
    /// 가 발견물 표 <c>+0x18</c> 로 고른다). 여럿을 함께 보고해도 이 하나로 갈린다.
    /// </summary>
    private static DiscoveryTable.Record? Headline(IReadOnlyList<DiscoveryTable.Record> rows)
    {
        DiscoveryTable.Record? best = null;
        foreach (var row in rows) if (best == null || row.Reward > best.Value.Reward) best = row;
        return best;
    }


    /// <summary>그 후원자의 얼굴. 표나 그림을 못 읽으면 null 이고, 그러면 대사만 나온다.</summary>
    private uint[]? FaceOf(Patron patron)
    {
        var sponsor = _game.Sponsors?.FindByName(patron.Name);
        if (sponsor == null) return null;
        return _game.Faces?.TryGetBgra(sponsor.Value.Face, sponsor.Value.IsFemale);
    }

    /// <summary>취차(집사)의 얼굴. 어느 후원자에게 가든 같은 사람이다.</summary>
    private uint[]? StewardFace() =>
        _game.Faces?.TryGetBgra(SponsorTable.StewardFace, female: false);

    /// <summary>이름 뒤에 붙는 목적격 조사. 받침이 있으면 "을", 없으면 "를".</summary>
    /// <remarks>
    /// 게임도 조사를 따로 끼워 넣는다 — "%s%s 데리고 왔습니다" 의 두 번째 자리가 이것이다.
    /// </remarks>
    private static string Particle(string name)
    {
        if (name.Length == 0) return "를";
        char last = name[^1];
        if (last is < '가' or > '힣') return "를";       // 한글이 아니면 그냥 둔다
        return (last - '가') % 28 == 0 ? "를" : "을";
    }


    /// <summary>후원자 자료. 못 읽으면 빈 목록이다 — 그렇다고 도시 화면까지 막을 일은 아니다.</summary>
    private static List<Patron> LoadPatrons()
    {
        if (_patrons != null) return _patrons;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "patrons.json");
            _patrons = new PatronService().LoadPatrons(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[City] 후원자 자료 없음: {ex.Message}");
            _patrons = [];
        }
        return _patrons;
    }


    /// <summary>
    /// 「스폰서 일람」 — 한 번이라도 만난 후원자를 늘어놓는다.
    /// </summary>
    /// <remarks>
    /// 게임의 목록 짓는 곳은 <c>0x00476660</c> 이다. 후원자 81명을 죽 훑으며 두 가지를 본다.
    /// <code>
    ///   vtbl[0x38]  0x004ADD70  지금 그 자리에 앉아 있는 사람인가(같은 자리를 여럿이 나눠 쓴다)
    ///   vtbl[0x34]  0x004AD800  후원자 객체 +0x28 의 비트 15 — 서 있으면 뺀다
    /// </code>
    /// 비트 15 는 <b>아직 못 만났다</b> 는 표다. 알현이 이루어져 주인이 "…모험 목적을 말해
    /// 보게" 하고 물을 때 지운다(<c>0x004AE595</c>). 그래서 <b>한 번 만나야 목록에 뜬다.</b>
    ///
    /// 우리 쪽은 비트를 따로 들지 않고 <see cref="Player.Met"/>(낯을 튼 사람)로 가른다.
    /// 자리 판정은 <see cref="Patron.IsActive"/> 로 물러선다 — 게임처럼 같은 자리를 두고
    /// 다투는 것까지는 못 가리지만, 대가 갈려 물러난 사람은 걸러진다.
    ///
    /// 이름은 게임 표에서 가져온다 — <c>patrons.json</c> 은 "페르난 마르틴스" 인데 게임 화면은
    /// 가운뎃점을 쓴다("페르난·마르틴스").
    /// </remarks>
    public void ShowPatrons()
    {
        int year = _player.Date.Year;
        var table = _game.Sponsors;

        var mine = LoadPatrons()
            .Where(p => p.IsActive(year) && _player.HasMet(p.Name))
            .Select(p => (Patron: p, Row: table?.FindByName(p.Name)))
            .ToList();
        var names = mine.Select(m => m.Row?.Name ?? m.Patron.Name).ToList();

        // 줄 왼쪽에 작게 붙일 얼굴 — 알현 대사에 쓰는 것과 같은 그림이다. 게임 창에는 없는 것이다.
        var faces = mine.Select(m => FaceOf(m.Patron)).ToList();

        // 이름 아래 한 줄 — 발견물의 취향. 상세 창(후원자 정보)과 같은 갈래 표에서 온다.
        // <b>「취향」 을 안 적는다</b> — 갈래 여덟이 다 붙는 후원자는 줄이 창 밖으로 잘렸다.
        // 가운뎃점도 뺐다(한 칸 띄우기).
        var likes = mine.Select(m => PatronInfoDialog.LikesText(m.Patron) is { Length: > 0 } text
                                         ? text
                                         : "없음")
                        .ToList();

        // 고르면 상세를 띄우고 닫으면 목록으로 돌아온다 — 게임도 그렇다(0x0049348E 가
        // 목록 짓는 데로 되돌아간다).
        // <b>도시 그림 창에 얹는다.</b> 부르는 쪽(CityPicView)이 도시 명령 창을 먼저
        // 닫는데, 그 창은 점으로 오므라드는 동안 <b>아직 살아 있고 보이기까지 한다</b>
        // (GameMenuHost.Close → CloseZoomed). 그 창을 주인으로 잡으면 오므라들기가
        // 끝나는 순간 우리 물음창까지 딸려 닫혀 판이 멎은 것처럼 보인다.
        var owner = _view;
        while (true)
        {
            int row = HintListDialog.Pick(owner, names, "스폰서 일람",
                                          "이 마을에는 아는 스폰서가 없습니다", faces: faces,
                                          subtitles: likes);
            if (row < 0 || row >= mine.Count) return;

            var (patron, sponsor) = mine[row];
            PatronInfoDialog.Show(owner, patron, sponsor?.Name, sponsor?.Job,
                                  _player.ClosenessOf(patron.Name),
                                  sponsor?.Face ?? -1, sponsor?.IsFemale ?? false,
                                  _game.Directory);
        }
    }
}
