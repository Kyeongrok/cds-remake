using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 적대 도시 앞에 서면 뜨는 차림표 — 공격한다 · 잠입한다 · 교섭한다 · 떠난다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x004A56F0</c> 이다. 규칙은 <see cref="Standoff"/> 에 모아 두었고 여기서는
/// <b>차례</b>만 맡는다 — 게임처럼 <b>고를 때마다 다시 뜬다</b>. 문이 열리거나 물러설
/// 때까지 돌고, 꺼진 칸도 자리를 안 비운다(<c>0x004A5726</c> 이 넉 줄을 먼저 깔고
/// 그 뒤에 켜고 끈다).
///
/// 앞머리(<c>0x004A5210</c>)가 먼저다 — <b>도시 그림을 펴고</b> 문지기가
/// "외국인은 들어올 수 없다" 고 말한 뒤에야 차림표가 뜬다. 말을 못 알아들으면 글자가
/// ×로 뭉개지고, 대원 중에도 아는 이가 없으면 한 마디 덧붙는다.
///
/// 공격은 부대배치 → 육상전(<c>0x0044A870</c>, 볼트 <c>65.분석-육상전</c>)이다. 이기면 문이 열리고,
/// 부대가 모두 쓰러지면 게임 오버, 퇴각하면 차림표로 돌아간다.
/// </remarks>
internal static class HostileCityMenu
{
    /// <summary>
    /// 마을을 칠 때 펴는 싸움터 — LANDDATA 파트 1(<b>도시</b>)이다.
    /// </summary>
    /// <remarks>
    /// 싸움터 넷 가운데 어느 것을 펼지는 판을 세우는 자리(<c>0x0044A646</c> 어름)가
    /// 전투 갈래로 가르는데, 마을 공략은 도시 그림이다.
    /// </remarks>
    private const int CityField = 0;

    /// <summary>한 판의 끝.</summary>
    /// <param name="Entered">문이 열렸는지 — 들어가도 되면 참.</param>
    /// <param name="GameOver">잡혀 죽었는지(<c>0x004A559F</c>).</param>
    /// <param name="MustLeave">
    /// 마을을 <b>떠나야</b> 하는지 — 잠입하다 들켜 달아났거나(<c>0x004A55A7</c>) 재판에서
    /// 추방·벌금을 받았을 때(<c>0x004A555F</c> → <c>0x004A55A4</c>)다. 게임은 건물 객체의
    /// <c>+0xA0</c>(마을을 떠난다)을 세우고 돌아가서, 항구 차림표를 닫고 도시 화면까지 끝내
    /// 바다로 나간다. 「떠난다」·교섭 실패로 물러선 것은 이 플래그가 없어 항구 차림표에 남는다.
    /// </param>
    public readonly record struct Outcome(bool Entered, bool GameOver, int Picture = GameOverDialog.MutinyLost,
                                          bool MustLeave = false);

    /// <summary>
    /// 적대 도시 앞에 선다.
    /// </summary>
    /// <param name="byLand">말로 왔는지 — 마을 쪽이면 참, 배로 온 항구 쪽이면 거짓.</param>
    /// <param name="mapArea">도시 그림을 펼 자리. 비어 있으면 임자 창 한가운데다.</param>
    /// <param name="byTreaty">
    /// 조약으로 막힌 문인지 — 게임은 그때 <b>딴 화면</b>(<c>0x0046ABB0</c>)을 쓴다.
    /// 차림표도 말도 벌이 다르고 교섭 주사위가 <c>rand(150)</c> 으로 헐렁하다.
    /// </param>
    /// <param name="inCity">
    /// 도시 그림이 <b>이미 떠 있는</b> 채로 부르는지 — 배로 닿아 항구 차림표에서 「마을에
    /// 들어간다」를 고른 자리다. 그때는 그림을 새로 펴지 않고 그 창 위에 차림표만 낸다.
    /// </param>
    /// <param name="stage">
    /// <paramref name="inCity"/> 일 때 하트·동전을 돌릴 무대 — 도시 그림 창 자신이다.
    /// </param>
    public static Outcome Run(Window owner, Engine.Game game, int city, string cityName,
                              bool byLand, Rect mapArea = default, bool byTreaty = false,
                              bool inCity = false, IGateStage? stage = null)
    {
        // 게임도 그림부터 편다 — 도시는 그려지고 성문에서 막히는 것이다.
        var scene = inCity ? null : GateScene.Open(owner, game, city, mapArea);
        IGateStage? fx = scene ?? stage;

        // 그림이 펴졌으면 이미 도시에 닿은 것이라 곡도 그 도시 것으로 바뀐다.
        // 못 들어가고 물러서면 부르는 쪽이 뭍·바다 곡으로 되돌린다(ShipMapWindow.PassGate).
        if (scene != null)
            game.Bgm.Play(BgmPlayer.CityTrackForCulture(game.CityRows?.CultureOf(city) ?? 0));
        try
        {
            return AtTheGate(scene as Window ?? owner, fx, game, city, cityName, byLand, byTreaty);
        }
        finally
        {
            // 닫기 전에 임자 창(지도)을 앞으로 세운다 — 안 그러면 떠 있던 창이 사라지는 순간
            // 윈도가 초점을 딴 앱 창으로 넘긴다(「떠난다」로 물러설 때 그랬다). 도시 그림 창과 같은 다룸이다.
            if (scene != null)
            {
                owner.Activate();
                scene.Close();
            }
        }
    }

    /// <summary>성문 앞에서 문지기를 만나고 차림표를 돌린다.</summary>
    private static Outcome AtTheGate(Window owner, IGateStage? scene, Engine.Game game, int city,
                                     string cityName, bool byLand, bool byTreaty)
    {
        var say = byTreaty ? Standoff.Treaty : Standoff.Hostile;
        var player = game.Player;
        var dice = new GameRandom(Environment.TickCount);
        int nation = game.CityRows?.NationOf(city) ?? -1;
        int sect = nation >= 0 ? game.Nations?.Find(nation)?.Sect ?? 0 : 0;
        string where = Standoff.Where(byLand);
        bool canTalk = true;

        // 문지기가 먼저 말한다(0x004A521A). 아는 말이 아니면 ×로 뭉개져 나오고,
        // 그때는 대원이 한 마디 덧붙인다 — 마을과 항구의 문구가 다르다(0x004A526E).
        // 알아듣는 정도 — 그 말 수준(0~3)만큼 덜 뭉개진다(0x004780E0 → 0x004252F0).
        int heard = TongueAt(game, city);
        int culture = game.CityRows?.CultureOf(city) ?? 0;
        var gate = game.SpeakerFace(Standoff.GateSpeaker(byLand), culture);
        // 조약으로 막힌 문에서는 이 인사가 아예 없다 — 부르는 쪽이 이미 조약 문구를 냈다
        // (0x0046ABC9 가 0x0046A6C0 하나만 부른다).
        if (!byTreaty)
        {
            TalkDialog.Say(owner, gate, "", Standoff.Heard(Standoff.GateWord, heard));
            // 덧붙이는 것은 <b>부관</b>이다 — 부관이 없으면 아예 아무 말도 없다(0x004A523D).
            // 무슨 말을 하는지는 제독과 부관 가운데 누가 더 그 말을 잘하느냐로 셋이 갈린다.
            if (Standoff.HasAide(player))
                TalkDialog.Say(owner, game.AideFace, "",
                               Standoff.GateAideWord(TongueAt(game, city),
                                                     AideTongueAt(game, city), byLand));
        }

        while (true)
        {
            // 넉 줄을 먼저 깔고 켜고 끈다 — 꺼진 칸도 자리를 지킨다.
            var rows = new (string, bool)[]
            {
                // 「공격한다」는 성문(건물 10)에서만 켜진다 — 배로 온 항구 문(건물 0)에서는 흐리다
                // (0x004A574E 가 화면 vt+0x48 을 켜짐 칸에, 조약 문도 0x0046AC12 에서 같다).
                (say.Rows[Standoff.Attack], byLand),
                // 조약 문의 「침입한다」는 종파를 안 본다(0x0046AC12) — 종파 3·4 검사는 적대도 쪽 잠입뿐이다.
                (say.Rows[Standoff.Sneak], byTreaty || Standoff.CanSneak(sect)),
                (say.Rows[Standoff.Talk], canTalk),
                (say.Rows[Standoff.Leave], true),
            };

            int pick = ChoiceDialog.Pick(owner, Standoff.GateTitle(cityName), rows);
            switch (pick)
            {
                case Standoff.Attack:
                    // 물음은 하나다 — 게임이 글 둘을 넘기면 0x00469680 이 부관 있고 없고로
                    // 하나만 띄운다. 되묻는 것도 <b>부관</b>이고 <b>제목 띠가 없다</b>.
                    if (!ConfirmDialog.Ask(owner,
                            Standoff.HasAide(player) ? Standoff.SureWord : Standoff.AttackWord,
                            null, game.AideFace))
                        break;

                    // 게임도 물음 뒤에 부대배치 화면부터 편다(0x0044A870 의 0x00446E60).
                    // 배치가 끝나면 그 길로 싸움터로 넘어간다.
                    if (LandDeployDialog.Show(owner, game, cityName) is not { } line) break;

                    var aide = player.Mates.Count > 0 && player.Mates[0].Length > 0
                        ? player.MateInfoOf(player.Mates[0]) : null;
                    var field = new LandBattle(line, player, aide,
                                               game.CityRows?.ScaleOf(city) ?? 0,
                                               nation, culture, CityField, dice, city: city)
                    { MyCulture = game.MyCulture };
                    if (!LandBattleScene.Run(owner, game, field, dice))
                    {
                        // 부대가 모두 쓰러졌으면 놀이가 끝난다 — 마을 공략에서 지면 게임 오버다.
                        if (field.Wiped) return new Outcome(Entered: false, GameOver: true, GameOverDialog.LandLost);

                        // 퇴각했으면 부관이 물러서자고 한다(0x00468A17). 부관이 없으면 상자만 뜬다.
                        // 싸움을 치른 뒤에는 이기든 물러나든 차림표가 다시 안 뜬다(0x004A57E7).
                        if (game.AideFace is { } backFace)
                            TalkDialog.Say(owner, backFace, "", Standoff.RaidLostWord);
                        else
                            NoticeDialog.Show(owner, Standoff.RaidLostNews, "");
                        return new Outcome(false, false);
                    }

                    // 그리고 그 도시를 <b>내 나라로 넘긴다</b>(0x004689F1 → 0x00468AC0). 그 도시가 제 나라의
                    // 수도였으면 그 나라 도시가 모두 넘어간다(0x00468B40, 226곳을 훑는다). 넘겨받는 나라는
                    // 0x005B394C 인데, 늘 제독 국적(vt+0x14)을 옮겨 둔 값이다.
                    Engine.Market.CityHistory.ChangeNation(player, game.CityRows, game.Nations, city, player.Nation);
                    // 행적에 공략을 적는다(0x00468A01, 원본 갈래 1) — 은퇴하면 누적 캐릭터가 이 도시를 되빼앗는다.
                    player.Note(Player.TraceCapture, city, player.Nation);
                    // 공략 문구는 교섭 것과 따로다(0x004689BA) — 마을 이름이 안 들어간다.
                    if (game.AideFace is { } wonFace)
                        TalkDialog.Say(owner, wonFace, "", Standoff.RaidWonWord);
                    else
                        NoticeDialog.Show(owner, Standoff.RaidWonNews, "");
                    // 조약을 깨고 쳐서 이겼을 때만 부관이 걱정한다(0x0046A78E → 0x004696B0, 부관이 없으면 말 없음).
                    // 교섭·침입으로 들어갔을 때는 이 말이 없다. 이어지는 굴림(0x0046A79D)이 무엇을 바꾸는지는 아직 모른다.
                    if (byTreaty && Standoff.HasAide(player))
                        TalkDialog.Say(owner, game.AideFace, "", Standoff.TreatyBrokenWord);
                    return new Outcome(true, false);

                case Standoff.Sneak:
                    // 잠입은 되든 안 되든 차림표가 다시 안 뜬다(0x004A57E7 이 반환값을
                    // 안 보고 고리를 빠져나간다). 달아났어도 그대로 물러선다.
                    return Sneak(owner, scene, game, dice, city, gate, heard, say);

                case Standoff.Talk:
                    // 게임도 돈부터 본다(0x00468BF0 → 「소지금이 모자랍니다!」). 한 번
                    // 걸리면 그 자리에서는 교섭 칸이 죽는다(0x004A5800).
                    if (player.Gold <= 0)
                    {
                        NoticeDialog.Show(owner, Standoff.TooPoorWord, "");
                        canTalk = false;
                        break;
                    }
                    if (Talk(owner, scene, game, dice, city, where, say))
                        return new Outcome(Entered: true, GameOver: false);
                    canTalk = false;          // 이번 방문에서는 다시 못 조른다
                    break;

                case Standoff.Leave:
                    // 떠나는 말은 <b>부관만</b> 한다(0x004A582C → 0x004696B0) — 부관이 없으면 아무 말 없이 떠난다.
                    if (Standoff.HasAide(player))
                        TalkDialog.Say(owner, game.AideFace, "", say.GiveUp);
                    return new Outcome(false, false);

                default:
                    // 창을 닫으면 차림표가 다시 뜬다 — 떠나는 것은 「떠난다」 줄뿐이다(0x004A57C7 → 0x004A5804).
                    break;
            }
        }
    }

    /// <summary>
    /// 교섭한다 — 되면 돈을 건네고 문이 열린다(<c>0x004A55C0</c>).
    /// </summary>
    /// <remarks>
    /// 어그러지면 그 자리(마을 쪽·항구 쪽)에 <b>실패 표시</b>가 서서 <b>「교섭한다」가 죽는다</b> —
    /// 게임도 도시 레코드 <c>+0xB0</c>(마을) · <c>+0xB4</c>(항구)에 1 을 적고, 차림표를 깔
    /// 때 그 값을 도로 읽어 칸을 끈다.
    /// <code>
    ///   4a5779  cmp [도시+0xb0], 1      ; 마을 쪽 ([도시+0x9c] 이 0 이 아닐 때)
    ///   4a5781  cmp [도시+0xb4], 1      ; 항구 쪽
    ///   4a5788  sbb eax,eax; neg eax    ; 적힌 적 없으면 1(켜짐)
    ///   4a578f  → 교섭 줄의 켜짐 칸
    ///   4a5800  같은 칸을 0 으로 — 이번 판에 어그러졌거나 소지금이 0 일 때
    /// </code>
    /// <b>그 표시는 도시가 아니라 성문 화면 객체에 산다.</b> <c>+0xB0</c>·<c>+0xB4</c> 는
    /// 92바이트짜리 도시 레코드(<c>0x005863A8</c> + 번호 x 0x5C) 밖이고, 화면 객체는 성문에
    /// 다가설 때마다 새로 서므로 <b>물러섰다 다시 오면 교섭 칸이 되살아난다</b>. 그래서
    /// 우리도 세이브에 적지 않고 이 고리 안의 <c>canTalk</c> 하나로 든다.
    /// </remarks>
    private static bool Talk(Window owner, IGateStage? scene, Engine.Game game, GameRandom dice,
                             int city, string where, Standoff.Script say)
    {
        var player = game.Player;

        // 게임도 굴리고 나서 하트를 돌린다(0x004A55EE) — 깨지면 이미 진 것이다.
        bool won = Standoff.Talks(player, dice, say.TalkRoll);
        scene?.PlayHeart(won);

        // 결과 문구는 0x00469680 이 부관 여부로 골라 <b>하나만</b> 낸다.
        // 부관이 있으면 부관이 말하고(「잘됐습니다…」·「교섭할 수 없군요…」),
        // 없으면 그냥 서술한다(「교섭에 성공/실패했습니다…」).
        bool aide = Standoff.HasAide(player);

        // 부관이 있으면 부관 얼굴을 걸고(0x00469680 → 0x004695E0), 없으면 얼굴 없는 상자다.
        void AideOrNews(string word, string news)
        {
            if (aide) TalkDialog.Say(owner, game.AideFace, "", word);
            else NoticeDialog.Show(owner, news, "");
        }

        if (!won)
        {
            AideOrNews(say.TalkLostWord, string.Format(say.TalkLostNews, where));
            return false;
        }

        int price = Standoff.Price(player, dice);
        if (say.IsTreaty) price = Math.Max(price, Standoff.TreatyMinPrice);
        int paid = player.Spend(price);
        NoticeDialog.Show(owner, string.Format(say.Paid, paid), "");
        AideOrNews(string.Format(say.TalkWonWord, where), string.Format(say.TalkWonNews, where));
        return true;
    }

    /// <summary>
    /// 잠입한다 — 되면 그대로 들어가고, 들키면 달아나거나 재판이다(<c>0x004A52F0</c>).
    /// </summary>
    /// <remarks>
    /// <b>어느 쪽이든 성문을 떠난다.</b> 차림표는 잠입이 돌려준 값을 아예 안 본다 —
    /// <c>0x004A57E2</c> 가 잠입을 부르고 <c>0x004A57E7</c> 이 곧장 고리 밖으로 뛴다.
    /// 달아났어도 다시 조를 기회를 안 준다는 뜻이다.
    /// </remarks>
    private static Outcome Sneak(Window owner, IGateStage? scene, Engine.Game game,
                                 GameRandom dice, int city, uint[]? gate, int heard,
                                 Standoff.Script say)
    {
        var player = game.Player;

        // 그 도시가 쓰는 말을 얼마나 아는지. 셋에 못 미치면 부관이 말린다.
        // 부관이 없으면 아예 말이 없다 — 0x004A52F6 이 0x00468EF0 으로 먼저 막는다.
        int tongue = TongueAt(game, city);
        bool aide = Standoff.HasAide(player);
        if (aide)
            TalkDialog.Say(owner, game.AideFace, "", tongue >= Standoff.SafeTongue ? say.Care : say.TongueThin);

        // 터번은 적대도 쪽 잠입만 본다 — 조약 쪽 침입은 굴림이 따로다(0x0046A867).
        bool turban = !say.IsTreaty && HasTurban(game, player);
        if (turban)
            NoticeDialog.Show(owner, $"{Standoff.TurbanName}을 사용했다", "");

        // 게임도 굴리고 나서 동전을 돌린다(0x004A53D0) — 멎은 쪽이 곧 결과다.
        bool got = say.IsTreaty ? Standoff.Intrudes(player, tongue, dice)
                                : Standoff.Sneaks(player, tongue, turban, dice);
        scene?.PlayCoin(got);

        if (got)
        {
            // 게임은 여기서 아무 말도 안 한다 — 0x004A53DC 가 곧장 돌아서서 도시로 든다(열린 문은 안 남는다).
            return new Outcome(Entered: true, GameOver: false);
        }

        TalkDialog.Say(owner, gate, "", Standoff.Heard(say.Spotted, heard));

        // 달아나기도 굴리고 나서 벌을 돌린다(0x004A5419 → 파트 0).
        bool away = Standoff.Escapes(player, dice);
        scene?.PlayEscape(away);

        if (away)
        {
            if (aide) TalkDialog.Say(owner, game.AideFace, "", say.GotAway);
            // 차림표로 안 돌아간다 — 마을을 떠난다(0x004A55A7 이 +0xA0 을 세운다).
            return new Outcome(false, false, MustLeave: true);
        }

        return Trial(owner, scene, game, dice, gate, heard, aide, say);
    }

    /// <summary>
    /// 잡힌 뒤의 재판(<c>0x004A5439</c> 부터).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   4a546e  가벼움 = rand(2000) + 1000 &gt; max(0, 악명 - 운 - 1)
    ///   4a549f  그 굴림을 하트로 낸다
    ///   4a54af  가벼우면 rand(100) &lt; 운 + 1
    ///   4a54cc  그 굴림을 동전으로 낸다
    ///           되면 추방만, 아니면 벌금 + 소지금 몰수(0x004A5508)
    ///   4a5561  무거우면 "죽음으로서 속죄하라!" — 그대로 놀이가 끝난다(0x0044AF70)
    /// </code>
    /// <b>감옥에 갇히는 갈래는 없다.</b> 재판의 끝은 이 셋뿐이고, 갇혀 날짜가 흐르는
    /// 자리도 없다 — 그 사이의 <c>0x004A5AE0(-1, 1)</c> 은 <c>0x00428000(40, 1)</c> 을
    /// 부르는 <b>40밀리초 기다리기</b>지 날짜가 아니다.
    /// </remarks>
    private static Outcome Trial(Window owner, IGateStage? scene, Engine.Game game,
                                 GameRandom dice, uint[]? gate, int heard, bool aide,
                                 Standoff.Script say)
    {
        var player = game.Player;
        TalkDialog.Say(owner, gate, "", Standoff.Heard(say.Caught, heard));

        // ① 죄가 가벼운가 — 굴리고 나서 하트를 돌린다(0x004A549F).
        // 조약 쪽 재판은 운이 아니라 <b>매력</b>을 뺀다(0x0046A93A).
        int weight = Math.Max(0, player.Infamy
                                 - player.AbilityOf(say.IsTreaty ? Ability.Charm : Ability.Luck) - 1);
        bool light = dice.Next(2000) + 1000 > weight;
        scene?.PlayHeart(light);

        if (!light)
        {
            TalkDialog.Say(owner, gate, "",
                Standoff.Heard(string.Format(say.Villain, player.Name,
                                             NameToken.Of(player.Name, say.IsTreaty ? 0 : 10)), heard));
            return new Outcome(Entered: false, GameOver: true);
        }

        // ② 운을 한 번 더 — 굴리고 나서 동전을 돌린다(0x004A54CC).
        bool lucky = dice.Next(100) < player.AbilityOf(Ability.Luck) + 1;
        scene?.PlayCoin(lucky);

        if (lucky)
        {
            TalkDialog.Say(owner, gate, "", Standoff.Heard(say.Banished, heard));
        }
        else
        {
            TalkDialog.Say(owner, gate, "", Standoff.Heard(say.Fined, heard));
            player.Spend(player.Gold);
            NoticeDialog.Show(owner, say.Robbed, "");
        }

        if (aide) TalkDialog.Say(owner, game.AideFace, "", say.GiveUpHere);
        // 추방이든 벌금이든 마을을 떠난다(0x004A555F → 0x004A55A4 가 +0xA0 을 세운다).
        return new Outcome(false, false, MustLeave: true);
    }

    /// <summary>그 도시가 쓰는 말을 얼마나 아는지. 표를 못 읽으면 0.</summary>
    /// <summary>부관(부하 자리 0)이 그 도시 나라 말을 얼마나 아는지. 없으면 0.</summary>
    /// <remarks>게임은 <c>0x00478050(부관, 문지기)</c> 로 공유 언어를 잰다(<c>0x004A5261</c>).</remarks>
    private static int AideTongueAt(Engine.Game game, int city)
    {
        string mate = game.Player.MateAt(0);
        if (mate.Length == 0) return 0;
        int nation = game.CityRows?.NationOf(city) ?? -1;
        if (nation < 0 || game.Nations?.Find(nation) is not { } row) return 0;
        if (row.Language < 0 || row.Language >= Skill.Languages.Length) return 0;
        var who = Local.Helpers.PersonTable.Open()?.People.FirstOrDefault(r => r.Name == mate);
        return who != null && row.Language < who.Languages.Length
            ? who.Languages[row.Language] : 0;
    }

    private static int TongueAt(Engine.Game game, int city)
    {
        int nation = game.CityRows?.NationOf(city) ?? -1;
        if (nation < 0 || game.Nations?.Find(nation) is not { } row) return 0;
        if (row.Language < 0 || row.Language >= Skill.Languages.Length) return 0;
        return game.Player.TongueOf(Skill.Languages[row.Language]);
    }

    /// <summary>터번을 들었는지. 아이템 표를 못 읽으면 안 든 것으로 본다.</summary>
    private static bool HasTurban(Engine.Game game, Player player)
    {
        if (game.Items?.Find(Standoff.TurbanName) is not { } turban) return false;
        return player.Items.Contains(turban.Id);
    }
}
