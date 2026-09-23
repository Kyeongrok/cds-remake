using System.IO;
using System.Buffers.Binary;
using System.Text.Json.Nodes;
using System.Windows;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Game.Engine.Disev;

/// <summary>
/// DISEV.CDS 의 발견 대본을 <b>돌린다</b>.
/// </summary>
/// <remarks>
/// 편집기(<see cref="DisevArchive"/> · <see cref="DisevPart"/> · <see cref="DisevScript"/>)는
/// 대본을 읽고 고칠 뿐 돌리지는 않았다. 그래서 발견 알림이 표에서 지어 낸 한 줄뿐이었다.
/// 이것이 그 대본을 차례대로 밟는다.
///
/// 카르낙 거석군(파트 19)이 이런 대본이다.
/// <code>
///    1 대사   부관      제독! 저것을 보십시오!
///    2 미디어 동영상    AVI 44
///    3 음원   재생      75
///    4 대사   화자없음  카르낙 거석군을 발견했다!
///    5 외부분기 STORY0.CDS 109
///    6 대사   부관      드디어 찾아냈군요…
///   …
///    9 아이템 획득      고대의 소뿔
///   13 발견             카르낙 거석군
/// </code>
///
/// <b>파트 번호가 곧 발견물 번호다</b> — 274개가 발견물 표와 1:1 이다.
///
/// <b>아직 안 하는 것.</b> 외부 분기(<c>STORY0.CDS</c> · <c>STORY1.CDS</c>)는 그 파일을
/// 안 뜯어서 <b>건너뛴다</b> — 뛰지 않고 다음 줄로 간다. 그 밖에 뜻을 모르는 명령도
/// 건너뛴다. 대본이 끊기는 것보다 한 줄 빠지는 편이 낫다.
/// </remarks>
public sealed class DisevRunner
{
    /// <summary>
    /// 대본 책은 한 번만 읽는다. <b>적어 둔 것이 새로 써지면</b> 다시 읽는다.
    /// </summary>
    /// <remarks>
    /// 읽는 것은 <c>DISEV.CDS</c> 가 아니라 <c>발견이벤트.json</c> 이다
    /// (<see cref="DisevBook"/>). 그 파일이 없으면 책이 앱에 실린 원본 대본을 <b>먼저 적어 두고</b>
    /// 그것을 읽는다 — 게임 폴더에 <c>DISEV.CDS</c> 가 없어도 대본이 돈다.
    ///
    /// 다시 읽는 자리를 둔 까닭은 편집기 때문이다. 여기서 한 번 읽고 붙들고 있으면 앱을
    /// 껐다 켜기 전에는 고친 대본이 안 돈다.
    /// </remarks>
    /// <summary>
    /// 열어 둔 책들 — 열쇠는 책 이름(<see cref="DisevBook.Cache"/>). 이야기0·이야기1 도
    /// 발견 이벤트와 같은 그릇이라 이 하나로 셋 다 돌본다(<see cref="DisevBook.Books"/>).
    /// </summary>
    private static readonly Dictionary<string, (DisevBook Book, string Dir, DateTime When)> _shared = [];

    /// <summary>그 게임 폴더의 대본 책. 없으면 null 이고, 그러면 대본 없이 지나간다.</summary>
    public static DisevBook? Open(string gameDirectory, string cache = DisevBook.CacheName)
    {
        var when = Stamp(cache);
        if (_shared.TryGetValue(cache, out var entry) && entry.Dir == gameDirectory && entry.When == when)
            return entry.Book;

        if (DisevBook.Open(cache) is not { } book) return null;
        _shared[cache] = (book, gameDirectory, Stamp(cache));
        return book;
    }

    /// <summary>적어 둔 책에 쓴 시각. 아직 없으면 밑값이다.</summary>
    private static DateTime Stamp(string cache)
    {
        string path = DisevBook.PathOf(cache);
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    private readonly Window _owner;
    private readonly Game _game;

    /// <summary>지금 돌고 있는 책 이름 — Story0·Story1 조건식(<c>6D</c>·<c>6E</c>)이 이것으로 갈린다.</summary>
    private readonly string _cache;

    /// <summary>지금 들어와 있는 건물 코드. 모르면 -1 — <see cref="DisevCall.InBuilding"/> 이 이것을 본다.</summary>
    private readonly int _building;

    /// <summary>
    /// 아직 안 낸 DSTILL 그림 자리. 다음 대사와 <b>함께</b> 낸다.
    /// </summary>
    /// <remarks>
    /// 대본은 그림과 글을 두 줄로 나눠 적지만 화면에는 한 창에 함께 나온다 —
    /// 히랄다탑(파트 51)이 「DSTILL 69」 다음에 「히랄다탑을 발견했다!」 다.
    /// 동영상은 다르다. 그쪽은 화면을 가득 덮었다가 사라지고 글이 따로 뜬다.
    /// </remarks>
    private int _pendingStill = -1;

    /// <summary>물고 있는 그림이 사건 스틸(EVSTILL)인지. 아니면 발견물 스틸(DSTILL)이다.</summary>
    private bool _pendingIsEvent;

    /// <summary>
    /// 마지막 미니게임을 이겼는지 — 게임 해석기의 <c>[ebp-0x1C]</c> 다.
    /// </summary>
    /// <remarks>
    /// <c>0E 04 [n]</c> 이 채우고(<c>0x00408D3A</c> 벌), 조건 <c>47</c> 이 읽는다(<c>0x0040B1C8</c>).
    /// 기제의 3대 피라미드(파트 26)가 성배 퍼즐 뒤에 이 값으로 갈라진다 — 이기면 앵크를 얻고,
    /// 지면 「왕릉을 침해한 죄를 죽음으로 대신해라!」 뒤 <c>4A</c>(게임 오버)다.
    /// </remarks>
    /// <remarks>
    /// <b>밑값은 참이다</b> — 해석기가 들어서며 1 을 넣는다(<c>0x00408125</c>). 예전에는 거짓으로 두고
    /// 결과를 모르면 43 45 를 늘 뛰게 했는데, 이제 물음(0B 0A)·선택지·판정이 결과를 세우므로 게임대로 둔다.
    /// </remarks>
    private bool _result = true;

    /// <summary>
    /// 바로 앞 조건의 값 — 해석기의 <c>[ebp-0x14]</c>. 43 뒤 조건을 셀 때마다 <c>0x0040BCD2</c> 가 적고,
    /// 43 4B 가 읽는다(<c>0x0040B23F</c>).
    /// </summary>
    private bool _lastCondition = true;

    /// <summary>
    /// 다중 선택지(10|18 0A)에서 고른 값 — 고른 자리 + 대본의 밑값이다(<c>[ebp-0x44]</c>, <c>0x00408FB8</c>).
    /// 43 11 0A [u8] 이 이것과 견준다.
    /// </summary>
    private int _choice = -1;

    /// <summary>대본이 <c>26 1C 02</c> 로 빌려 준 아군 병력. 없으면 −1(함대 선원을 쓴다).</summary>
    private int _borrowedMen = -1;

    /// <summary>대본이 <c>26 1C 10</c> 으로 넣은 적 병력. 없으면 −1.</summary>
    private int _foeMen = -1;

    private readonly GameRandom _dice = new(Environment.TickCount);

    /// <summary>
    /// 마지막으로 돌린 대본이 <b>게임 오버</b>(<c>4A</c>)로 끝났는지. 부른 쪽이 보고 놀이를 끝낸다.
    /// </summary>
    /// <remarks>게임은 <c>0x0044AF40(0x5A4D18, 0)</c> 으로 놀이 상태를 끝으로 돌린다(<c>0x0040BDBA</c>).</remarks>
    public static bool LastEndedInGameOver { get; private set; }

    /// <summary>게임 오버로 끝났을 때 세울 그림 — 육상전 전멸이면 0x0D, 해전이면 0x0C, 그 밖은 0x0B.</summary>
    public static int LastGameOverPicture { get; private set; } = GameOverDialog.MutinyLost;

    /// <summary>
    /// 마지막으로 돌린 이야기 대본의 <b>결과 코드</b> — 밑값 2, <c>4C</c> 0 · <c>4D</c> 1 · <c>4E</c> 2(맥락 <c>+8</c>).
    /// </summary>
    /// <remarks>
    /// 건물에 들어선 사건(<c>0x004AB5A0</c>)은 이것이 <b>1 일 때만</b> 참을 돌려(<c>0x004AB4AC</c>) 건물에 못 들게 한다 —
    /// 「저택은 북쪽 초록 지붕 건물입니다」 뒤의 4D 가 그것이다. 0·2 면 말만 하고 그대로 들어간다.
    /// </remarks>
    public static int LastResult { get; private set; } = 2;

    /// <summary>
    /// 마지막으로 돌린 대본이 <b>이벤트 완전 종료</b>(<c>04 4D</c>)로 끝났는지 — 이야기 장(章)을
    /// 닫아야 하는지는 부른 쪽(<see cref="Discovery.StoryLog"/>)이 이것을 보고 정한다.
    /// </summary>
    public static bool LastStoryArcCompleted { get; private set; }

    /// <summary>
    /// 마지막으로 돌린 대본이 <b>다음 단계로</b>(<c>06 4D</c>)를 겪었는지 — 이야기 장의 진행
    /// 카운터를 올려야 하는지는 부른 쪽이 이것을 보고 정한다.
    /// </summary>
    public static bool LastAdvancedStep { get; private set; }

    /// <summary>마지막 대본이 이야기 단계를 몇 번 올렸는지 — <c>06</c> 마다 하나(0x00408B2F 가 맥락 +0x10 에 1 을 둔다).</summary>
    public static int LastStepsAdvanced { get; private set; }

    /// <summary>대본을 여기서 멈추라는 뜻으로 <see cref="Step"/> 이 내는 값.</summary>
    private const int Stop = int.MinValue;

    private DisevRunner(Window owner, Game game, string cache, int building)
    {
        _owner = owner;
        _game = game;
        _cache = cache;
        _building = building;
    }

    /// <summary>
    /// 그 발견물의 대본을 돌린다. 대본이 없으면 false — 부른 쪽이 예전처럼 한 줄만 낸다.
    /// </summary>
    /// <param name="owner">창을 얹을 자리.</param>
    /// <param name="game">이 판.</param>
    /// <param name="discoveryId">발견물 번호 = DISEV 파트 번호.</param>
    public static bool Run(Window owner, Game game, int discoveryId) =>
        Run(owner, game, DisevBook.CacheName, discoveryId, building: -1);

    /// <summary>
    /// 그 책의 그 파트를 돌린다. 발견 이벤트만이 아니라 이야기0·이야기1(STORY0/1.CDS)의
    /// 장면도 같은 길로 돈다 — 그릇도 명령도 같다(<see cref="DisevBook.Books"/>).
    /// </summary>
    /// <param name="owner">창을 얹을 자리.</param>
    /// <param name="game">이 판.</param>
    /// <param name="cache">돌릴 책 이름(<see cref="DisevBook.Cache"/>).</param>
    /// <param name="partIndex">파트 번호 — 발견 이벤트면 발견물 번호, 이야기책이면 장면 번호다.</param>
    /// <param name="building">지금 들어와 있는 건물 코드. 모르면 -1.</param>
    public static bool Run(Window owner, Game game, string cache, int partIndex, int building)
    {
        LastEndedInGameOver = false;
        LastGameOverPicture = GameOverDialog.MutinyLost;
        LastResult = 2;
        LastStoryArcCompleted = false;
        LastAdvancedStep = false;
        LastStepsAdvanced = 0;
        if (Open(game.Directory, cache) is not { } book) return false;
        if (partIndex < 0 || partIndex >= book.Count) return false;

        var raw = book.Part(partIndex);
        if (raw.Length == 0) return false;
        if (DisevPart.Parse(raw, out _) is not { } part) return false;

        var runner = new DisevRunner(owner, game, cache, building);
        int body = runner.PickBody(part);
        if (body < 0) return false;

        runner.RunChunk(part, body);
        return true;
    }

    /// <summary>
    /// 그 파트의 슬롯 조건 가운데 <b>지금 참인 것이 있는지</b> — 아무것도 실행하지 않고 묻기만
    /// 한다. <see cref="Discovery.StoryLog"/> 가 이야기 장면을 틀지 말지 미리 가늠할 때 쓴다.
    /// </summary>
    /// <remarks>
    /// 고르는 잣대는 <see cref="PickBody"/> 와 같다 — 둘 다 맞는 슬롯이 없으면 물러서지 않는다
    /// (<c>0x00407EFD</c>). 다만 이쪽은 본문을 돌리지 않고 <b>될지 안 될지만</b> 낸다.
    /// </remarks>
    public static bool IsEligible(Game game, string cache, int partIndex, int building)
    {
        if (Open(game.Directory, cache) is not { } book) return false;
        if (partIndex < 0 || partIndex >= book.Count) return false;

        var raw = book.Part(partIndex);
        if (raw.Length == 0 || DisevPart.Parse(raw, out _) is not { } part) return false;

        // 조건만 묻고 아무 것도 그리지 않으므로 창이 없어도 된다 — Step()·Speak() 은 안 부른다.
        var runner = new DisevRunner(null!, game, cache, building);
        foreach (var slot in part.Slots)
        {
            var (from, to) = part.ChunkRange(slot.Condition);
            if (runner.Passes(Lines(part, from, to))) return true;
        }
        return false;
    }

    /// <summary>
    /// 한 줄을 호출로 푼 것 — <see cref="DisevCalls"/> 표가 짓는다.
    /// </summary>
    /// <param name="Op">파트 안 자리·길이.</param>
    /// <param name="Raw">명령 바이트.</param>
    /// <param name="Call">호출 이름. 분기·절대 이동·모르는 바이트면 null.</param>
    /// <param name="Args">호출(또는 분기 조건식)의 인자.</param>
    /// <param name="Condition">분기면 <b>원문 그대로의</b> 조건식 이름 — 이것이 거짓이면 뛴다.</param>
    /// <param name="Target">분기·절대 이동이 뛰는 파트 안 자리.</param>
    private sealed record Line(DisevScript.Op Op, byte[] Raw, DisevCall? Call, JsonObject Args,
                               DisevCall? Condition, int? Target);

    /// <summary>파트 안 한 토막을 줄로 푼다.</summary>
    private static List<Line> Lines(DisevPart part, int from, int to)
    {
        var data = part.Data;
        var lines = new List<Line>();
        foreach (var op in DisevScript.Parse(data, from, to))
        {
            var raw = data.AsSpan(op.Offset, Math.Min(op.Length, data.Length - op.Offset)).ToArray();

            if (op.Kind == "절대 이동" && raw.Length == 4)
            {
                lines.Add(new Line(op, raw, null, [], null, 4 + BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2))));
                continue;
            }
            if (DisevFlow.TargetOf(data, op) is { } target && DisevCalls.ConditionOf(raw[..^2]) is { } condition)
            {
                lines.Add(new Line(op, raw, null, condition.Args, condition.Call, target));
                continue;
            }
            var call = DisevCalls.Decode(raw);
            lines.Add(new Line(op, raw, call?.Call, call?.Args ?? [], null, null));
        }
        return lines;
    }

    /// <summary>
    /// 그 발견물 대본이 판매를 켜는 교역품들(<c>01 15</c>). 대본이 없으면 빈 목록이다.
    /// </summary>
    /// <remarks>
    /// 교역품 켬을 세이브에 적기 전의 판을 불러올 때 쓴다 — 이미 발견한 것의 대본을 훑어 켜 준다.
    /// 갈래는 가리지 않는다.
    /// </remarks>
    public static IEnumerable<int> GoodsActivatedBy(Game game, int discoveryId) =>
        GoodsActivatedBy(game, DisevBook.CacheName, discoveryId);

    /// <summary>그 책 그 파트의 대본이 켜는 교역품들. 대본이 없으면 빈 목록이다.</summary>
    public static IEnumerable<int> GoodsActivatedBy(Game game, string cache, int discoveryId)
    {
        if (Open(game.Directory, cache) is not { } book || discoveryId < 0 || discoveryId >= book.Count) yield break;
        if (DisevPart.Parse(book.Part(discoveryId), out _) is not { } part) yield break;

        foreach (int start in part.ChunkStarts)
        {
            var (from, to) = part.ChunkRange(start);
            foreach (var line in Lines(part, from, to))
                if (line.Call == DisevCall.ActivateGoods && line.Args["Goods"] is { } goods)
                    yield return (int)long.Parse(goods.ToJsonString());
        }
    }

    /// <summary>
    /// 조건이 맞는 첫 슬롯의 본문 자리. 없으면 -1.
    /// </summary>
    /// <remarks>
    /// 슬롯은 <c>[조건][본문]</c> 짝이 여럿이고, 앞에서부터 조건이 맞는 것을 쓴다.
    /// 조건 덩이가 비었으면(바로 <c>FF</c>) 맞은 것으로 친다 — 카르낙 거석군의
    /// 「조건 없음 · 항상 발생」이 그 꼴이다.
    ///
    /// <b>맞는 슬롯이 하나도 없으면 사건을 아예 안 튼다</b>(<c>0x00407EFD</c> 이 0 을 내고
    /// <c>0x0040CF9F</c> 가 해석기를 안 부른다). 이야기 대본은 이 규칙을 그대로 따른다.
    /// 다만 발견 이벤트 274 파트는 죄다 슬롯 하나에 확률 조건
    /// (<c>2E 1A [분모] 1A [성공]</c>)만 붙어 있다. 발견물을 실제로 밟은 뒤에 다시
    /// 이 조건을 사건 발생 게이트로 쓰면 이벤트가 확률적으로 사라지므로, 발견 이벤트
    /// 에서는 그 조건만 무시하고 본문을 튼다.
    /// </remarks>
    private int PickBody(DisevPart part)
    {
        foreach (var slot in part.Slots)
        {
            var (from, to) = part.ChunkRange(slot.Condition);
            // 발견 이벤트의 슬롯 조건에 붙은 확률값은 이벤트 발생 여부가 아니라
            // 원본 분석 데이터에 남은 조건 표기다. 발견물을 밟은 뒤에는 본문을 늘 튼다.
            if (Passes(Lines(part, from, to), _cache == DisevBook.CacheName)) return slot.Body;
        }
        return -1;
    }

    /// <summary>
    /// 조건 덩이가 통과인지 — <c>Or</c>(50) 로 모으고 나머지는 AND 로 잇는다(<c>0x00407EB1</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 조건 하나를 셈한 뒤 <b>바로 다음 바이트가 0x50 인지</b>를 본다.
    /// <code>
    ///   004073ba  and  = 1          ; [esp+0x2c] — 덩이 전체
    ///   004073c2  orAcc = 0         ; [esp+0x28] — 슬롯을 열 때 한 번만 지운다
    ///   004073d5  inOr  = 0         ; [esp+0x20] — AND 항마다 지운다
    ///   00407eb1  다음 바이트가 0x50 이면  orAcc |= 값 · inOr = 1 · 다음 조건으로
    ///   00407ecb  아니면  inOr 이면 값 |= orAcc · and &amp;= 값
    /// </code>
    /// 곧 <c>A 50 B C</c> 는 <b><c>(A|B) &amp; C</c></b> 다 — A 가 참이라고 덩이가 끝나는 것이
    /// 아니라 <b>C 까지 다 본다</b>. 예전에는 A 가 참이면 그 자리에서 통과로 쳤다.
    ///
    /// <c>orAcc</c> 는 <b>AND 항이 바뀌어도 안 지워진다</b>(0x004073D5 가 <c>inOr</c> 만 지운다) —
    /// <c>A 50 B C 50 D</c> 면 뒤 덩이가 <c>D|A|B|C</c> 가 된다. 원본 그대로 둔다.
    ///
    /// 뜻을 모르는 <b>조건식</b>은 통과로 친다 — 막으면 대본이 통째로 안 돈다. 다만 바이트
    /// 자체를 못 읽으면 게임도 그 파트를 안 튼다(<c>0x00407F09</c>).
    ///
    /// 조건 덩이에서 <b>쓸 수 있는 명령은 정해져 있다</b> — <c>0x004073F1</c> 이
    /// <c>바이트 - 2</c> 를 <c>0x00407FA0</c> 의 갈래표에 넣고, 갈래 24 로 떨어지면
    /// <c>0x00407F09</c> 로 나가 <b>그 파트를 통째로 안 튼다</b>. 쓸 수 있는 것은 이 스물여덟이다.
    /// <code>
    ///   02 0F 12 17 1B 1C 24 27 28 2A 2B 2C 2D 2E 36 37 3A 41 42 59 5A 5D 5E 5F 60 65 67 FF
    /// </code>
    /// 50(OR)은 이 표에 없다 — 조건을 하나 셈한 <b>뒤에</b> 따로 본다(<c>0x00407EB1</c>).
    /// 분기(<c>43</c>)에서만 쓰는 조건식(<c>45</c> 결과 따위)은 조건 덩이에 오면 파트가
    /// 안 돈다. <b>스톡 DISEV.CDS 274 파트는 죄다 이 스물여덟 안에 든다</b>(2026-09-20 확인).
    /// </remarks>
    private bool Passes(List<Line> lines, bool ignoreRandomChance = false)
    {
        bool and = true, orAcc = false, inOr = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Call is DisevCall.End) break;
            if (line.Call is DisevCall.Or) continue;   // 앞 조건이 이미 거둬 갔다

            // 모르는 조건이 끼면 게임은 그 파트를 통째로 안 튼다(0x00407F09) — 참으로 흘리면 안 된다.
            if (line.Call is not { } call) return false;
            bool value = ignoreRandomChance && call == DisevCall.RandomChance
                ? true
                : Evaluate(call, line.Args) is not false;

            // 뒤에 50 이 붙어 있으면 모아 두고 다음 조건으로 넘어간다.
            if (i + 1 < lines.Count && lines[i + 1].Call is DisevCall.Or)
            {
                orAcc |= value;
                inOr = true;
                continue;
            }

            if (inOr) { value |= orAcc; inOr = false; }
            and &= value;
        }
        return and;
    }

    /// <summary>
    /// 조건식 한 번 부르기. 뜻을 모르면 null.
    /// </summary>
    /// <remarks>이름은 조건식의 <b>원문</b>이다 — 분기는 이 값이 거짓일 때 뛴다.</remarks>
    private bool? Evaluate(DisevCall condition, JsonObject args)
    {
        var player = _game.Player;
        int I(string key) => args[key] is { } node ? (int)long.Parse(node.ToJsonString()) : 0;
        int year = player.Date.Year;

        switch (condition)
        {
            case DisevCall.Result: return _result;                                   // 45 (0x0040B1B4)
            case DisevCall.ResultFalse: return !_result;                             // 47 (0x0040B1C8)
            case DisevCall.LastConditionFalse: return !_lastCondition;               // 4B (0x0040B23F)
            case DisevCall.NoAide: return player.MateAt(0).Length == 0;              // 56 (0x0040B368)
            case DisevCall.ChoiceIs: return _choice == I("Value");                   // 11 0A (0x00408FC0)
            case DisevCall.ChoiceIsNot: return _choice != I("Value");                // 43 11 0A — 개인 이야기가 쓴다
            case DisevCall.HintActive: return HintHeld(I("Hint"));                   // 0F 0E
            case DisevCall.HintInactive: return !HintHeld(I("Hint"));                // 12 0E (0x0040902F)
            case DisevCall.HasItem: return player.HasItem(I("Item"));                // 0F 05 (0x00408EC8)
            case DisevCall.LacksItem: return !player.HasItem(I("Item"));             // 12 05 (0x00409022)
            case DisevCall.Discovered: return player.HasFound(I("Discovery"));       // 02 0B (0x004089C2)
            case DisevCall.NotDiscovered: return !player.HasFound(I("Discovery"));   // 3A 0B (0x0040AB27)
            case DisevCall.DiscoveryDone: return player.HasFound(I("Discovery"));
            case DisevCall.DiscoveryNotDone: return !player.HasFound(I("Discovery"));
            case DisevCall.YearAtLeast: return year >= I("Year");
            case DisevCall.YearAtMost: return I("Year") >= year;                     // 39 16 (0x0040AAF0)
            case DisevCall.YearIs: return year == I("Year");                         // 1C 16 (0x00409704)
            case DisevCall.YearBetween: return year >= I("From") && year <= I("To");
            case DisevCall.InCity: return player.CityId == I("City");                // 17 08 (0x00409074)
            case DisevCall.InNation: return player.Nation == I("Nation");            // 17 00
            case DisevCall.InBuilding: return _building == I("Building");            // 17 10
            case DisevCall.NotInCity: return player.CityId != I("City");             // 41 08 (0x00407C7E)
            // 41 10 — 건물에 들어선 사건이면 그 건물이 아닐 때 참, 딴 사건(도시에 들어섬 따위)이면 거짓(0x00407CE8)
            case DisevCall.NotInBuilding: return _building >= 0 && _building != I("Building");
            // 건물 명령 고름(갈래 4)·후원자 건물 나섬(갈래 5) 사건은 아직 안 올린다 — 그 사건이 아니니 거짓이다.
            case DisevCall.HasFleet: return player.Ships.Count > 0;                   // 59 (0x00407DA9)
            case DisevCall.BuildingCommand:
            case DisevCall.SponsorVisitEnded:
                return false;
            case DisevCall.InCulture:                                                // 17 19
                return player.CityId >= 0 && _game.CityRows?.CultureOf(player.CityId) == I("Culture");
            case DisevCall.PersonUnmet:                                              // 37 0D
            {
                string who = PersonTable.Open()?.Find(I("Person"))?.Name ?? "";
                return who.Length == 0 || !player.HasMet(who);
            }
            case DisevCall.SponsorActive:                                            // 37 12
            {
                string who = _game.Sponsors?.Sponsors.FirstOrDefault(s => s.Index == I("Sponsor")).Name ?? "";
                return who.Length > 0 && player.Contract?.Sponsor == who;
            }
            case DisevCall.CityNationCheck:                                          // 28 00
                return _game.CityRows?.NationOf(I("City")) == I("Nation");
            case DisevCall.NoContract: return player.Contract == null;               // 5A
            case DisevCall.YearMonthIs:                                              // 1B 17
                return year == I("Year") && player.Date.Month == I("Month");
            case DisevCall.Story0: return _cache == "이야기0";                        // 6D
            case DisevCall.Story1: return _cache == "이야기1";                        // 6E
            case DisevCall.RandomChance:
            {
                int denominator = I("Denominator");
                return denominator > 0 && _game.Random.Next(denominator) < I("Success");
            }
            case DisevCall.GreaterThan:
            case DisevCall.GreaterOrEqual:
            case DisevCall.LessThan:
            case DisevCall.LessOrEqual:
            case DisevCall.EqualTo:
            case DisevCall.NotEqualTo:
            {
                // 비교 뜀표 0x0040C380 — 2A A>B · 2B A≥B · 2C A<B · 2D A≤B · 2E A==B. 분기(43)에서는 거꾸로 이름이 온다
                // (43 2D 가 GreaterThan, 43 2E 가 NotEqualTo) — 개인 이야기의 「의뢰 기한」 분기가 이것이다.
                if (ValueOf(args["A"] as JsonObject) is not { } a || ValueOf(args["B"] as JsonObject) is not { } b) return null;
                return condition switch
                {
                    DisevCall.GreaterThan => a > b,
                    DisevCall.GreaterOrEqual => a >= b,
                    DisevCall.LessThan => a < b,
                    DisevCall.LessOrEqual => a <= b,
                    DisevCall.NotEqualTo => a != b,
                    _ => a == b,
                };
            }
            // ── 아직 안 옮긴 명령 ────────────────────────────────────────────────
            // 실제로 쓰이는 것만 적는다(DISEV·이야기0·이야기1 을 앱 파서로 센 값).
            //   34  Wait(29 1A)          창이 모달이라 멈출 자리가 없다 — 연출이라 건너뛴다
            //   20  HideDialog(48)       "
            //   20  ShowDialog(49)       "
            //   10  OccupyCity(23 08)    도시 레코드 +0x04 에 비트 2 를 세운다(0x00409E36, 25 08 이 지운다).
            //                              마을 공략에 이겼을 때(0x00468B20)도 이 비트와 나라를 함께 세운다.
            //                              <b>그 비트를 읽는 곳을 못 찾았다</b> — 나라는 앞의 26 1C 1A 가 넘긴다.
            //    3  MoveEventTarget(3C 08) <b>옮길 것이 없다.</b> 대본 주인([문맥+0x10])의 갈래가 1(역사 항해자)일
            //                              때만 움직이는데(0x0040ABAE), 발견 대본의 주인은 제독(갈래 0,
            //                              0x004783D7)이다. HISTCHR 에서만 뜻이 있다.
            default:
                return null;
        }
    }

    /// <summary>본문 덩이를 차례대로 밟는다. 점프는 파트 안 어디로든 간다 — 절대 이동은 덩이를 건너뛴다.</summary>
    private void RunChunk(DisevPart part, int start)
    {
        var lines = new List<Line>();
        foreach (int chunk in part.ChunkStarts)
        {
            var (from, to) = part.ChunkRange(chunk);
            lines.AddRange(Lines(part, from, to));
        }

        var at = new Dictionary<int, int>();
        for (int i = 0; i < lines.Count; i++) at[lines[i].Op.Offset] = i;
        if (!at.TryGetValue(part.ChunkRange(start).Start, out int index)) return;

        // 대본이 꼬여 제자리를 맴돌 수 있다. 줄 수의 몇 곱으로 끊는다.
        int budget = Math.Max(64, lines.Count * 8);

        while (index < lines.Count && budget-- > 0)
        {
            var line = lines[index];
            if (line.Call is DisevCall.End) return;

            int? jump = Step(line);
            if (jump == Stop) return;
            if (jump is not { } target) { index++; continue; }
            if (!at.TryGetValue(target, out index)) return;   // 명령 머리가 아니면 멈춘다
        }
    }

    /// <summary>
    /// 한 줄을 치른다 — 뛰어야 하면 파트 안 자리를, 멈춰야 하면 <see cref="Stop"/> 을, 아니면 null 을 낸다.
    /// </summary>
    private int? Step(Line line)
    {
        var args = line.Args;
        int I(string key) => args[key] is { } node ? (int)long.Parse(node.ToJsonString()) : 0;

        // 절대 이동(30 1D) — 파트 +4+v(0x0040A48D).
        if (line.Condition == null && line.Target is { } absolute) return absolute;

        // 분기(43) — 조건식을 부르고 거짓이면 뛴다(0x0040BCF9). 뜻을 모르면 안 뛴다.
        if (line.Condition is { } condition)
        {
            if (Evaluate(condition, args) is not { } value) return null;
            _lastCondition = value;
            return value ? null : line.Target;
        }

        switch (line.Call)
        {
            case DisevCall.Say:
            case DisevCall.AskYesNo:
            case DisevCall.SayBare:
                Speak(line.Raw);
                return null;

            case DisevCall.AskChoice:
            case DisevCall.AskChoiceWide:
                _choice = Choose(line.Raw);
                return null;

            case DisevCall.PlayVideo:
                MoviePlayer.Play(_owner, DiscoveryDialog.MovieOf(_game.Directory, I("Id")));
                return null;

            case DisevCall.PlaySound:
                PlaySound(I("Id"));
                return null;

            // 00 0C [u16 n] — DISCOVER.CDS 파트 n 을 가운데에 틀고 돌아온다(0x00408429).
            case DisevCall.PlayCgAnimation:
                DiscoveryClipPlayer.Play(_owner, _game.Clips, I("Id"));
                return null;

            // 그림은 바로 안 낸다 — 다음 대사와 한 창에 함께 낸다.
            case DisevCall.ShowDStill:
                _pendingStill = I("Id");
                _pendingIsEvent = false;
                return null;
            case DisevCall.ShowEvStill:
                // <b>딴 파일이다.</b> 예전에는 이 번호를 DSTILL 에 대고 찾아 엉뚱한 그림이
                // 나왔다 — EVSTILL.CDS 는 사건 스틸 열여섯 장으로 따로 있다.
                _pendingStill = I("Id");
                _pendingIsEvent = true;
                return null;

            // 33 — 걸어 둔 그림을 <b>거둔다</b>(0x0040A79E). 대본이 그림만 세웠다가 말 없이
            // 접을 때가 있어, 안 거두면 엉뚱한 뒷줄에 그 그림이 따라붙는다.
            case DisevCall.CloseImage:
                _pendingStill = -1;
                return null;

            // 66 03 [소리] — 울리던 소리를 멈춘다.
            case DisevCall.StopSound:
                _game.Sfx?.Stop();
                return null;

            // 00 1E [n] — 특수 조우 연출(0x004085D2). 지도 창이 아닌 데서 돌면 그릴 자리가 없어 건너뛴다.
            case DisevCall.SpecialEncounter:
            {
                int n = I("Scene");
                if (n < DisevScript.Encounters.Length && _owner is UI.Views.ShipMapWindow map)
                    map.PlayEventScene(DisevScript.Encounters[n].Scene);
                return null;
            }

            // 32 [값 식] — <b>그만큼 날을 보낸다</b>. 하루에 피로 −1 · 사기 +3 이 함께 먹는다
            // (마을에서 날을 넘길 때와 같은 셈).
            case DisevCall.AdvanceDays:
                if (ValueOf(args["Days"] as JsonObject) is { } days) _game.Player.AdvanceDays((int)days);
                return null;

            case DisevCall.AddStat:
                if (ValueOf(args["Value"] as JsonObject) is { } plus) Adjust(I("Stat"), +(int)plus);
                return null;
            case DisevCall.SubStat:
                if (ValueOf(args["Value"] as JsonObject) is { } minus) Adjust(I("Stat"), -(int)minus);
                return null;

            // 26 1C [칸] — 육상전에 넘길 두 묶음과 소지금만 받는다(0x0040A15C 의 뜀표 0x0040C314).
            //   칸 2  아군 임시 묶음의 병력(0x0040A1F9 → 0x0045FF40)
            //   칸 16 적 대장 묶음의 병력(0x0040A242 → 0x0045FF40)
            case DisevCall.SetStat:
            {
                if (ValueOf(args["Value"] as JsonObject) is not { } set) return null;
                int value = (int)set;
                switch (I("Stat"))
                {
                    case 2: _borrowedMen = value; break;
                    case 16: _foeMen = value; break;
                    case 3:
                        // 소지금을 그 값으로 — 델포이(파트 24)가 공물 5000 이 없으면 0 으로 만든다.
                        if (value < _game.Player.Gold) _game.Player.Pay(_game.Player.Gold - value);
                        else _game.Player.Earn(value - _game.Player.Gold);
                        break;
                    case 29: _game.Player.SetStoryQuestDeadline(value); break;   // STORY 의뢰 남은 기한(일)
                }
                return null;
            }

            // 26 1C 1A 00 08 [도시] — 그 도시를 <b>제독의 나라</b>로 넘긴다. SetStat 의 칸 26 만
            // 뒤에 값 식 대신 도시가 오는 딴 꼴이다(0x0040A168 cmp ax,0x1A): 도시 레코드 +0x00 에
            // 제독 물건 vt+0x14(= [0x005B60B4], 국적)를 그대로 넣는다(0x0040A1A3). 21 08 과 달리
            // 수도여도 그 나라 도시를 함께 넘기지 않는다. 대본은 늘 같은 도시의 23 08 앞에 둔다.
            case DisevCall.ChangeCityNation:
                _game.Player.SetHistoryNation(I("City"), _game.Player.Nation);
                return null;

            // 22 00 [나라] — 그 나라를 <b>망하게</b> 한다. 나라 레코드(0x005859C0 + 나라 x 16)의 +0x04 에
            // 2 를 넣는다(0x00409DAD). 역사 대본이 쓰는 것과 같은 칸이다.
            case DisevCall.DestroyNation:
                _game.Player.SetNationStatus(I("Nation"), 2);
                return null;

            // 22 10 [비트] 08 [도시] — 그 도시 건물 낱말(+0x1C)의 비트를 끈다(0x00409DD1).
            // 26 10 은 켠다(0x0040A118). 역사 대본과 같은 핸들러다.
            case DisevCall.RemoveFacility:
                Market.CityHistory.SetBuilding(_game.Player, _game.CityRows, I("City"), I("Facility"), on: false);
                return null;
            case DisevCall.BuildSpecialBuilding:
                Market.CityHistory.SetBuilding(_game.Player, _game.CityRows, I("City"), I("Building"), on: true);
                return null;

            // 22 08 [도시] — 도시를 <b>없앤다</b>(도시 레코드 +0x04 |= 4, 0x00409DC8).
            // 26 08 [도시] — 도시를 <b>세운다</b>(+0x04 &amp;= ~4, 0x0040A038). 대본 주인([문맥+0x10],
            // 발견 대본은 제독 0x004783C0)의 갈래가 1(역사 항해자)이면 세우지 않고 그리로 뱃길을 트는데
            // (0x0040A04C) — 그것은 HISTCHR 의 「경유 항구」다. 발견 이벤트 263 이 테노치티틀란을 없애고
            // 멕시코를 세운다.
            case DisevCall.RemoveCity:
                _game.Player.SetScriptedCity(I("City"), false);
                return null;
            case DisevCall.CreateCity:
                _game.Player.SetScriptedCity(I("City"), true);
                return null;

            // 20 0A [글] 00 08 [도시] — 그 도시 소문 가게에 글을 적는다(0x004099CB). 역사 대본과 같은
            // 핸들러라 술집·여관 무명 손님이 이것을 말한다. 제독 이름 자리표는 적을 때 편다(0x0040C410).
            case DisevCall.AddCityRumor:
            {
                var raw = line.Raw;
                int zero = Array.IndexOf(raw, (byte)0, 2);
                if (zero < 0) return null;
                string text = DisevScript.DecodeDialogue(raw.AsSpan(2, zero - 2), normalize: true,
                                                         player: _game.Player.Name).Body;
                _game.Player.AddRumor(I("City"), text, _game.Player.Date);
                return null;
            }

            // 20 0A [글] 19 [문화권] — 그 <b>문화권 도시 전부</b>의 소문 가게에 같은 글을 적는다
            // (0x00409A18). 꼬리 바이트만 08(도시 하나)과 다르고 앞은 같은 꼴이다.
            // <code>
            //   00409a29  for (i = 0; i &lt; 0xE2; i++)            ; 도시 226
            //   00409a32      if (도시[i] + 0x58 == 문화권)
            //   00409a44          0x0044E1D0(i, 글)              ; 같은 소문 가게
            // </code>
            case DisevCall.AddCultureRumor:
            {
                var raw = line.Raw;
                int zero = Array.IndexOf(raw, (byte)0, 2);
                if (zero < 0) return null;
                string text = DisevScript.DecodeDialogue(raw.AsSpan(2, zero - 2), normalize: true,
                                                         player: _game.Player.Name).Body;
                if (_game.CityRows is not { } rows) return null;

                int want = I("Culture");
                for (int city = 0; city < Local.Helpers.CityExeTable.Count; city++)
                    if (rows.CultureOf(city) == want)
                        _game.Player.AddRumor(city, text, _game.Player.Date);
                return null;
            }

            // 34 1C [칸] [값 식] — 병력을 <b>반으로</b>(올림) 줄인다(0x0040A7C8). 값 식은 읽기만 하고 안 쓴다.
            //   칸 2  빌린 묶음이 서 있으면 그 병력(0x0040A84E → 0x0045FF40), 아니면 제독 육상 묶음
            //         0x005AA2B8 의 수(0x0040A830). 대본이 쓰는 곳은 잉카(파트 264)뿐이고, 800 을 빌려
            //         싸우다 달아날 때라 빌린 쪽만 옮긴다.
            //   칸 10·20 은 부관·함대 쪽인데 대본에 안 쓰인다.
            case DisevCall.HalveTroops:
                if (I("Stat") == 2 && _borrowedMen >= 0) _borrowedMen = (_borrowedMen + 1) / 2;
                return null;

            // 46 — 결과를 거짓으로(0x0040B1BC).
            case DisevCall.ClearResult:
                _result = false;
                return null;

            // 35 1C [u16 칸] — 그 능력으로 판정해 결과를 세운다. 무력 6 · 운 18 · 지력 21 · 신앙심 23 만
            // 판정하고 다른 번호는 결과를 그대로 둔다(cds_disev_editor v1.0: R(100) ≤ 값 + 1).
            case DisevCall.AbilityCheck:
            {
                int? value = I("Stat") switch
                {
                    6 => _game.Player.AbilityOf(Support.Local.Models.Ability.Might),
                    18 => _game.Player.AbilityOf(Support.Local.Models.Ability.Luck),
                    21 => _game.Player.AbilityOf(Support.Local.Models.Ability.Mind),
                    23 => _game.Player.Abilities.Length > 5 ? _game.Player.Abilities[5] : null,
                    _ => null,
                };
                if (value is { } v) _result = _game.Random.Next(100) <= v + 1;
                return null;
            }

            case DisevCall.GiveHint:
                _game.Player.GainHint(I("Hint"));
                return null;

            // 01 15 [교역품] — 판매 게이트 0x0058BAB0[교역품] = 1(0x004088D8). 교역소가 그 품목을 팔기 시작한다.
            case DisevCall.ActivateGoods:
                _game.Player.ActivateGoods(I("Goods"));
                return null;

            // 5B 08 [도시] 15 [교역품] · 5B 15 [교역품] — 의뢰한 짐을 <b>정가로 인수</b>한다(0x0040B3C8).
            case DisevCall.BuyCargoFrom:
                BuyCargo(I("Goods"), I("City"));
                return null;
            case DisevCall.BuyCargo:
                BuyCargo(I("Goods"), null);
                return null;

            // 05 05 — 16칸 소지품에 넣는다. 아이템 획득(00 05)과 달리 알림 창은 없다.
            // 05 05 [아이템] — 준다(0x00408A06). 다만 그 아이템이 <b>발견물 아이템</b>(표 +0x30)이면 아무 일도
            // 없다 — 발견물 아이템은 발표할 때 들어온다. 꽉 찼으면 버리기 창 없이 놓친다.
            case DisevCall.AddEventItem:
                GiveUnlessDiscovery(I("Item"));
                return null;
            // 00 05 [아이템] — 아이템 창을 <b>보여 주기만</b> 한다(0x004083DC → 0x0046E7D0(아이템, 0)).
            // 발견한 뒤 대본이 그 물건을 보이는 자리다. 소지품에는 안 넣는다.
            case DisevCall.GiveItem:
                ShowItem(I("Item"));
                return null;
            // 26 05 [아이템] — 준다(0x00409F24). 꽉 찼으면 버릴 것을 고르게 하고, 안 고르면 받았다고만 한다.
            case DisevCall.MarkEventItem:
                GiveMakingRoom(I("Item"));
                return null;
            case DisevCall.RemoveItem:
                _game.Player.Drop(I("Item"));
                return null;

            case DisevCall.Discover:
            {
                // <b>발견과 그 물건은 이 명령만 준다</b> — 발견 판정 0x0048D3F0 은 대본을 돌린 뒤 결과
                // 코드만 보고(0·1 이면 인스턴스 +0x17 비트 1) 발견을 따로 적지 않는다. 존왕의 술잔은
                // 낚시에 지면 「놓쳤습니다」 뒤 4E 로 끝나 여기까지 안 온다. 물건 알림은 대본 대사가 한다.
                // 게임 명령은 발견물 칸 가운데 하나만 켜므로 표에 없는 번호는 버린다.
                int id = I("Discovery");
                if (_game.Discoveries is { } log && log.Table.Find(id) != null) log.Discover(_game.Player, id);
                return null;
            }

            case DisevCall.AddGold:
                _game.Player.Earn(I("Amount"));
                return null;
            case DisevCall.SubGold:
                _game.Player.Pay(I("Amount"));
                return null;

            // 후원자 친밀도 증감 — 지금 맺은 계약의 후원자가 움직인다. 계약이 없으면
            // Endear 가 빈 이름을 조용히 지나친다.
            case DisevCall.AddAffinity:
                if (ValueOf(args["Value"] as JsonObject) is { } affinityUp)
                    _game.Player.Endear(_game.Player.Contract?.Sponsor ?? "", (int)affinityUp);
                return null;
            case DisevCall.SubAffinity:
                if (ValueOf(args["Value"] as JsonObject) is { } affinityDown)
                    _game.Player.Endear(_game.Player.Contract?.Sponsor ?? "", -(int)affinityDown);
                return null;

            // 31 — 델포이 신탁(0x0040A4C0). 제독 성미 여덟 칸 가운데 0·2 인 것만 낱말로 잇는다(1 은 건너뜀).
            // 그 뒤에 붙는 자녀 적성·배우자·남은 수명 경고는 Town.Oracle.Words 로 옮겼다.
            case DisevCall.DelphiOracle:
            {
                var slots = Sea.FleetRaid.AdmiralFortuneOf(_game.Player);
                var words = slots.Select((v, k) => v switch { 0 => TraitWords[k].Low, 2 => TraitWords[k].High, _ => null })
                                 .Where(w => w != null).ToArray();
                if (words.Length > 0) TalkDialog.Say(_owner, null, "", "〈무당〉 " + string.Join("! ", words) + "!");
                // 그 뒤로 아이·반려자·수명을 일러 주고 맺는다(0x0040A615~0x0040A758, Town.Oracle).
                foreach (string said in Town.Oracle.Words(_game))
                    TalkDialog.Say(_owner, null, "", said);
                return null;
            }

            // 육상전 — 이겼는지를 남기고, 전멸했으면 그 자리에서 게임 오버로 멈춘다.
            case DisevCall.LandBattle:
            case DisevCall.LandBattleCity:
            {
                var battle = line.Call == DisevCall.LandBattle ? LeaderBattle(I("Person")) : CityBattle(I("City"));
                _result = LandBattleScene.Run(_owner, _game, battle, _dice);
                if (battle.Wiped)
                {
                    LastEndedInGameOver = true;
                    LastGameOverPicture = GameOverDialog.LandLost;   // 0x00449920 의 까닭 3
                    return Stop;
                }
                return null;
            }

            // 26 0F [벌] — 이어 붙는 일기토의 <b>벌 번호</b>를 적어 둔다(무대·몸짓 그림).
            case DisevCall.SetDuelSet:
                _duelSet = I("Set");
                return null;

            // 0C 0D [인물] — 그 인물과 <b>일기토</b>. 이기면 결과 참이다.
            case DisevCall.Duel:
            {
                _result = DuelWith(I("Person"));
                return null;
            }

            // 0D 0D [인물] — 그 인물(괴물)과 해전. 지도 창에서만 연다 — 판을 열 손이 거기 있다.
            case DisevCall.SeaBattle:
            {
                if (_owner is not UI.Views.ShipMapWindow sea) return null;

                var end = sea.SeaFight(I("Person"));
                _result = end.Won;
                if (end.Over)
                {
                    LastEndedInGameOver = true;
                    LastGameOverPicture = GameOverDialog.FleetLost;  // 0x0044386D 의 까닭 2
                    return Stop;
                }
                return null;
            }

            // 미니게임 한 판 — 이겼는지를 들고 있다가 조건 47 이 읽는다.
            case DisevCall.Minigame:
                _result = PlayMinigame(I("Game"));
                // 이기면 MINI GAME 차림표에 그 줄이 풀린다(0x00408D47 벌 → 0x00406B60(n)).
                if (_result) Local.Settings.GameSettings.UnlockMinigame(I("Game"));
                return null;

            // 0E 14|1A [u32 판자] 04 [u16 n] — 코인 게임·발라몬의 탑(0x00408DF7). 번호가 4·5 가 아니면
            // 아무것도 안 하고 결과도 안 건드린다.
            case DisevCall.PuzzleMinigame:
            case DisevCall.PuzzleMinigame1A:
                switch ((DisevMinigame)I("Game"))
                {
                    case DisevMinigame.Coin:
                        _result = CoinPuzzleDialog.Play(_owner, _game.Random, _game.Player, _game.AideFace);
                        break;
                    case DisevMinigame.Tower:
                        _result = TowerPuzzleDialog.Play(_owner, _game.Random, I("Discs"));
                        break;
                    default:
                        return null;
                }
                if (_result) Local.Settings.GameSettings.UnlockMinigame(I("Game"));   // 0x00408E59 · 0x00408E86
                return null;

            // 게임 오버 — 대본을 멈추고 부른 쪽에 알린다.
            case DisevCall.GameOver:
                LastEndedInGameOver = true;
                return Stop;

            // 결과 코드를 적고 대본을 끝낸다(0x0040BDD5 벌). 스핑크스에게 쫓겨나면 여기서 멎는다.
            case DisevCall.EndDone:
            case DisevCall.EndFailed:
            case DisevCall.EndUnhandled:
                LastResult = line.Call == DisevCall.EndDone ? 0 : line.Call == DisevCall.EndFailed ? 1 : 2;
                return Stop;

            // 04 4D — 이야기 장(章)을 완전히 끝낸다. 부른 쪽(StoryLog)이
            // LastStoryArcCompleted 를 보고 그 장을 닫아 다시 트리거되지 않게 한다.
            case DisevCall.EndEventCompletely:
                LastStoryArcCompleted = true;
                LastResult = 1;                       // 04 다음의 4D
                return Stop;

            // 06 4D — 이야기 장의 다음 단계로. 06 FF 는 같은 뜻이되 그 자리에서 대본도 끝낸다.
            // 부른 쪽이 LastAdvancedStep 을 보고 진행 카운터를 올린다.
            // 40 0D — 그 인물을 부관 자리에 앉힌다. 신상은 게임 세이브 인물표에서 채운다(Game.MateInfo).
            // 38 12 — 그 후원자를 이미 만난 것으로 친다. 문간 명성 관문이 안 걸린다(CityPicView.PassFameGate).
            case DisevCall.MeetSponsor:
                if (_game.Sponsors?.Sponsors.FirstOrDefault(s => s.Index == I("Sponsor")) is { Name.Length: > 0 } sponsor)
                    _game.Player.Meet(sponsor.Name);
                return null;

            // 1F 0A [이름] 0B [발견물] — 대본이 <b>기본 이름</b>을 박는다. 무르고 나서
            // 43 47 이 이리로 흘러온다 — 묻지 않고 그대로 정한다.
            case DisevCall.SetDiscoveryName:
                if (args["Name"]?.GetValue<string>() is { Length: > 0 } fixedName)
                {
                    _game.Player.NameDiscovery(I("Discovery"), fixedName);
                    Local.Helpers.DiscoveryTable.SetName(I("Discovery"), fixedName);
                }
                return null;

            // 1F 0B [발견물] 0A [꼬리말] — <b>이름을 지어 준다</b>(0x004098C0).
            // 「명명」 창에 스무 글자에서 꼬리말 길이를 뺀 만큼 받고, 꼬리말을 뒤에 붙인 뒤
            // 「[…]로 명명하겠습니다. 좋습니까?」로 한 번 더 묻는다. 아니오면 다시 받는다.
            // 무르면 <b>결과 코드를 0↔1 로 뒤집는다</b> — 덮어쓰는 것이 아니다(0x00409992).
            // <code>
            //   00409992  cmp dword ptr [ebp - 0x1c], 1
            //   00409996  sbb eax, eax ; neg eax        ; 결과 &lt; 1 이면 1, 아니면 0
            // </code>
            // 곧 앞서 0 이던 결과는 1 이 되고, 1·2 던 결과는 0 이 된다.
            // 이름을 <b>정했으면 결과를 안 건드린다</b>(0x00409982 가 0x0040999D 로 바로 뛴다) —
            // 앞선 「이름을 붙이겠습니까?」가 세워 둔 1 이 그대로 남아 43 47 이 뛴다.
            case DisevCall.InputDiscoveryName:
                if (!NameDiscovery(I("Discovery"), args["Suffix"]?.GetValue<string>() ?? ""))
                    LastResult = LastResult < 1 ? 1 : 0;
                return null;

            // 38 0D — 그 <b>인물</b>을 이미 만난 것으로 친다(후원자 쪽은 38 12 다).
            // 술집에서 낯을 튼 것과 같은 자리라, 다시 만나도 통성명을 안 한다.
            case DisevCall.MeetPerson:
                if (Local.Helpers.PersonTable.Open()?.Find(I("Person")) is { Name.Length: > 0 } known)
                    _game.Player.Meet(known.Name);
                return null;

            case DisevCall.SetAide:
                Seat(AideSlot, "부관", I("Person"));
                return null;

            // 3D 0D — 그 인물을 <b>통역</b> 자리에 앉힌다(0x0040ADE0). 부관(40 0D)과 같은 꼴이다.
            case DisevCall.HireInterpreter:
                Seat(InterpreterSlot, "통역", I("Person"));
                return null;

            case DisevCall.NextStep:
                // 06 다음의 4D 는 결과 1 을 적고 끝낸다(0x0040BDF5) — 그 건물에는 안 들어간다.
                LastAdvancedStep = true;
                LastStepsAdvanced++;
                LastResult = 1;
                return Stop;
            case DisevCall.CloseStory:
                LastStoryArcCompleted = true;
                return null;
            case DisevCall.AdvanceStep:
                LastAdvancedStep = true;
                LastStepsAdvanced++;
                return null;
            case DisevCall.NextStepFF:
                LastAdvancedStep = true;
                LastStepsAdvanced++;
                return Stop;

            default:
                return null;
        }
    }

    /// <summary>성미 여덟 칸의 낱말 짝(0 · 2) — <c>0x00538A28</c> 부터다.</summary>
    private static readonly (string Low, string High)[] TraitWords =
    [
        ("소심", "거만"), ("우유부단", "독선"), ("변덕", "집착"), ("겁장이", "무모"),
        ("냉혹", "팔방 미인"), ("편협", "욕심장이"), ("무신경", "신경질"), ("낭비가", "깍쟁이"),
    ];

    /// <summary>그 힌트를 얻었거나 이미 보고까지 했는지.</summary>
    private bool HintHeld(int hint) =>
        _game.Player.HasHint(hint) || (_game.Discoveries?.IsHintDone(_game.Player, hint) ?? false);

    /// <summary>
    /// 다중 선택지를 띄우고 고른 값(자리 + 밑값)을 낸다. 선택지는 <c>81 5E</c>(여기서는 「/」)로 갈린다.
    /// </summary>
    /// <remarks>
    /// 게임은 앞 대사 창 밑에 세로 메뉴를 세운다(<c>0x004878A0</c>). 물러나면 마지막 줄을 고른 것으로 친다 —
    /// 대본의 마지막 선택지가 늘 「도망간다」·「떠난다」 쪽은 아니지만, 메뉴를 그냥 닫을 길을 막을 수는 없다.
    /// </remarks>
    private int Choose(byte[] raw)
    {
        int term = Array.IndexOf(raw, (byte)0, 2);
        if (term < 0) return -1;
        int baseValue = term + 1 < raw.Length ? raw[term + 1] : 0;

        var (_, text) = DisevScript.DecodeDialogue(raw.AsSpan(2, term - 2), _game.Player.Name);
        var choices = text.Split('/').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
        if (choices.Length == 0) return baseValue;

        int picked = ChoiceDialog.Ask(_owner, "", choices[..^1], choices[^1]);
        int value = (picked >= 0 ? picked : choices.Length - 1) + baseValue;

        // 고른 값이 0 이면 <b>교섭</b>이다(0x00409204) — 금이나 물건을 바쳐야 이야기가 이어진다.
        if (value == 0) Appease();
        return value;
    }

    /// <summary>
    /// 「뭔가 우호의 증표를 줍시다」(<c>0x0040920A</c>) — 금을 주거나 물건을 준다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00538618  「뭔가 우호의 증표를 줍시다」
    ///   0x00538658  차림표 「교섭」 — 「금을 준다」 · 「아이템을 준다」
    ///   금을 준다   계산판으로 얼마를 줄지 적는다(0x00481FE0) → 그만큼 소지금에서 빠진다
    ///               <b>백 닢이 안 되면</b> 「아무래도 마음에 들지 않았던 모양입니다」(0x00538660) 하고 다시 묻는다
    ///   아이템을 준다  그 자리에서 받아들인다(원본도 무엇을 줄지는 안 묻는다)
    ///   물리면       다시 묻는다 — 주지 않고는 못 지나간다
    /// </code>
    /// </remarks>
    private void Appease()
    {
        var player = _game.Player;
        NoticeDialog.Show(_owner, "뭔가 우호의 증표를 줍시다");

        while (true)
        {
            int at = ChoiceDialog.Ask(_owner, "교섭", ["금을 준다", "아이템을 준다"]);
            if (at == 1) return;                       // 물건을 주면 그것으로 끝난다
            if (at < 0) continue;                      // 물러도 다시 묻는다(0x004092B0)

            // 금액은 계산기 판으로 받는다(0x004092D2 → 0x00481FE0, 1~소지금). 물리면 다시 고르기로.
            if (player.Gold <= 0 || NumberPadDialog.Ask(_owner, 1, 1, player.Gold) is not { } gold || gold <= 0)
                continue;

            player.SetGold(player.Gold - gold);
            if (gold >= AppeaseLeast) return;
            NoticeDialog.Show(_owner, "아무래도 마음에 들지 않았던 모양입니다");
        }
    }

    /// <summary>이만큼은 줘야 마음에 들어 한다(<c>0x00409312</c> 의 <c>cmp 0x64</c>).</summary>
    private const int AppeaseLeast = 100;

    /// <summary>
    /// 미니게임 한 판(<c>0x00408D16</c> 의 뜀표). 이겼으면 true.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0  성배 퍼즐     0x004684D0   0 이 아니면 이김
    ///   1  스핑크스 퀴즈 0x0047BFE0   1 이면 이김
    ///   2  미궁 64       0x0042C8A0   (이 판에는 안 붙어 있다 — 이긴 것으로 친다)
    ///   3  낚시          0x0047BDD0
    ///   6  큐브 퍼즐     0x0049B3C0
    /// </code>
    /// 큐브는 창이 결과를 안 돌려줘 <b>이긴 것으로 친다</b> — 원본도 늘 1 이다. 낚시는 대어를 잡았는지를 그대로 쓴다.
    /// </remarks>
    private bool PlayMinigame(int game)
    {
        switch ((DisevMinigame)game)
        {
            case DisevMinigame.Grail:
                return GrailPuzzleDialog.Play(_owner, _game.Player, _game.Random, _game.Sfx);
            case DisevMinigame.Sphinx:
                return SphinxQuizDialog.Play(_owner, _game.Random);
            // 미궁은 딴 어셈블리(CdsHelper.Maze)라 띄우는 쪽이 걸어 둔 자리로 부른다.
            // 게임은 돌파 보상을 치른 갈래에서만 결과 1 을 박는다(0x0042B154) — 덫·실패·포기는 0.
            // 걸려 있지 않으면 대본이 막히지 않게 이긴 것으로 친다.
            case DisevMinigame.Maze:
                return UI.Views.ShipMapWindow.MazeGame?.Invoke(_owner, _game.Random, _game.Player, _game.Sfx) ?? true;
            // 낚시는 대어일 때만 이긴 것이다(0x0047AD6C).
            case DisevMinigame.Fishing:
                return FishingGameDialog.Play(_owner, _game.Random);
            case DisevMinigame.Cube:
                CubePuzzleDialog.Play(_owner, _game.Player, _game.Random, _game.Sfx);
                return true;
            // 4·5 와 7 넘는 번호는 뜀표가 곧장 다음 명령으로 간다 — 결과를 안 건드린다(0x0040C1B0).
            default:
                return _result;
        }
    }

    /// <summary>
    /// <c>2F 0D [인물]</c> 의 판 — 그 인물이 적 대장이다(<c>0x0040A40B</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0040a41d  아군 = 26 1C 02 가 세운 임시 묶음, 없으면 함대(0x005AA2B8)
    ///   0040a423  적   = 인물 핸들 0x1000|번호(0x00477AF0), 병력은 26 1C 10 이 넣은 것
    ///   0040a442  지형 = 배 자리의 부류(0x00426740) → 7 도시 · 2 초지 · 4 황무지 · 그 밖 숲
    ///   0040a459  0x0044AA30(3, 아군, 적, 0, 지형)
    ///   0040a47e  [ebp-0x1C] = (돌려준 값 == 0)   ; 0 이김 · 1 물러남 · 2 전멸(게임 오버)
    /// </code>
    /// 파르테논 신전(파트 23)은 26 1C 02 = 70, 26 1C 10 = 100~139, 적 대장 206 이다.
    /// <b>지형</b>은 우리가 배 자리 부류를 안 들고 있어 도시 안이면 도시, 아니면 숲으로 둔다.
    /// </remarks>
    /// <summary>이어 붙는 일기토의 벌(<c>26 0F</c>). 안 정했으면 1 이다.</summary>
    private int _duelSet = 1;

    /// <summary>
    /// 대본이 거는 일기토(<c>0C 0D</c>) — 이기면 참.
    /// </summary>
    /// <remarks>
    /// 상대 능력치는 인물 표의 것을 그대로 쓴다(체력 0 · 무력 2 · 운 4, 검술은 기능 표).
    /// 무기·방어구는 인물 표에 칸이 없어 0 으로 둔다. 몸짓 그림 벌은 바로 앞의
    /// <c>26 0F</c> 가 정한다(<see cref="_duelSet"/>).
    /// </remarks>
    private bool DuelWith(int person)
    {
        var me = _game.Player;
        var mine = new Town.Duel.Fighter(me.Name.Length > 0 ? me.Name : "제독",
            me.AbilityOf(Support.Local.Models.Ability.Body), me.AbilityOf(Support.Local.Models.Ability.Might),
            me.LevelOf(Support.Local.Models.Skill.Names[Support.Local.Models.Skill.Sword]), me.AbilityOf(Support.Local.Models.Ability.Luck), 0, 0);

        var foe = new Town.Duel.Fighter("상대", 80, 80, 2, 50, 0, 0);
        uint[]? face = null;
        if (PersonTable.Open()?.Find(person) is { } row && row.Stats.Length >= 5)
        {
            int sword = row.Skills.Length > Support.Local.Models.Skill.Sword ? row.Skills[Support.Local.Models.Skill.Sword] : 0;
            foe = new Town.Duel.Fighter(row.Name, row.Stats[0], row.Stats[2], sword, row.Stats[4], 0, 0);
            face = _game.PersonTemplates?.Find(person) is { } t
                ? _game.Faces?.TryGetBgra(t.Face, female: false) : null;
        }

        // 대본 일기토는 판 종류 2 라 <b>부관을 대신 내보낼지 묻는다</b>(0x004A8680 의 종류 <= 2).
        var stand = UI.Views.TavernMenu.SendMate(_owner, me, _game, _dice);
        if (stand is { } who)
            mine = new Town.Duel.Fighter(who.Name, who.Body, who.Might, who.Sword, who.Luck, 0, 0);

        var duel = new Town.Duel(mine, foe, me.Items.Contains(Town.Duel.EdithShieldId),
                                 Environment.TickCount);
        bool won = UI.Views.DuelDialog.Show(_owner, duel, _dice, face, _game.Fighters,
            foeSet: Math.Max(0, _duelSet),
            myFace: _game.Faces?.TryGetBgra(
                Local.Helpers.PortraitAges.At(me.Face, me.Age, false, _game.Faces), female: false),
            bgm: _game.Bgm);
        // 대신 나간 사람이 다친다(0x004AA5CA).
        if (stand is { } hurt) me.HurtMate(hurt.Name, duel.BodyLost);
        else me.Hurt(duel.BodyLost);
        return won;
    }

    private LandBattle LeaderBattle(int person)
    {
        var player = _game.Player;
        var aide = AideInfo();
        int myMen = (_borrowedMen >= 0 ? _borrowedMen : player.Crew) + 1;

        // 적 총원 = 묶음 +0x0C + 1(0x004A050D). 대본이 안 넣었으면 백으로 친다.
        int foeMen = (_foeMen >= 0 ? _foeMen : 100) + 1;

        // 적 대장 능력 — 인물 표의 능력 여섯(0 체력 · 1 지력 · 2 무력 · 4 운).
        var foe = (Might: 75, Mind: 70, Luck: 65, Body: 85);
        (int Sword, int Gunnery, int Shooting)? foeSkills = null;
        try
        {
            if (PersonTable.Open().Find(person) is { } row && row.Stats.Length >= 5)
            {
                foe = (row.Stats[2], row.Stats[1], row.Stats[4], row.Stats[0]);
                // 편성(병종)은 적 대장의 기능 자리로 갈린다 — 인물 표에 적힌 그대로 쓴다.
                if (row.Skills.Length > Support.Local.Models.Skill.Shooting)
                    foeSkills = (row.Skills[Support.Local.Models.Skill.Sword],
                                 row.Skills[Support.Local.Models.Skill.Gunnery],
                                 row.Skills[Support.Local.Models.Skill.Shooting]);
            }
        }
        catch (Exception)
        {
            // 인물 표를 못 읽으면 도시 규모 3~4 의 밑값으로 싸운다.
        }

        // 문화권은 적 대장 나라의 수도 것이다(0x00447070). 규모도 그 수도에서 온다(0x004494B3).
        int culture = 0, scale = 0, foeNation = -1;
        if (_game.PersonTemplates?.Find(person) is { } who
            && _game.Nations?.Find(who.Nation) is { } nation)
        {
            foeNation = who.Nation;
            culture = _game.CityRows?.CultureOf(nation.Capital) ?? 0;
            scale = _game.CityRows?.ScaleOf(nation.Capital) ?? 0;
        }

        int terrain = player.CityId >= 0 ? 0 : 2;
        // 적 기능은 생성자로 넘긴다 — 편성이 생성자 안에서 지어진다.
        return new LandBattle(Deploy(aide), myMen, foeMen, player, aide, culture, terrain, foe, _dice,
                              foeSkills, scale: scale, nation: foeNation)
        {
            KeepsCrew = _borrowedMen >= 0,
            MyCulture = _game.MyCulture,
        };
    }

    /// <summary>
    /// <c>2F 08 [도시]</c> 의 판 — 그 도시를 친다(<c>0x0040A3B2</c> → <c>0x0044AA30(4, 아군, 0, 도시, 7)</c>).
    /// </summary>
    /// <remarks>
    /// 적은 도시 규모로 짓는다(<c>0x00449E50</c> 은 갈래 2·4 가 함께 쓴다). 지형 인자 7 은
    /// 싸움터 0(도시)이다(<c>0x0044A624</c>). 우리 판에 갈래 4 가 따로 없어 마을 공략(2)으로 세운다.
    /// </remarks>
    private LandBattle CityBattle(int city)
    {
        var player = _game.Player;
        var aide = AideInfo();
        int myMen = _borrowedMen >= 0 ? _borrowedMen + 1 : 0;
        var rows = _game.CityRows;
        return new LandBattle(Deploy(aide), player, aide, rows?.ScaleOf(city) ?? 0,
                              rows?.NationOf(city) ?? -1, rows?.CultureOf(city) ?? 0,
                              0, _dice, myMen, city: city)
        {
            KeepsCrew = _borrowedMen >= 0,
            MyCulture = _game.MyCulture,
        };
    }

    /// <summary>부관 신상. 없으면 null.</summary>
    /// <summary>
    /// 이름에 받을 수 있는 <b>바이트</b> 수(<c>0x0040990B</c> 의 <c>mov edi, 0x14</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0040990b  edi = 0x14
    ///   00409917  edi -= strlen(꼬리말)       ; 0x004B7DA4 — <b>바이트</b> 길이다
    ///   0040992f  0x004B0740("명명", 받는 칸, edi)
    /// </code>
    /// CP949 라 한글 한 자가 <b>두 바이트</b>다 — 「의 곶」 같은 꼬리말이면 남는 칸이
    /// 글자 수로 센 것보다 훨씬 빠듯하다.
    /// </remarks>
    private const int NameRoom = 20;

    /// <summary>CP949 로 적었을 때의 바이트 수 — 게임이 세는 길이다.</summary>
    private static int Bytes(string text) =>
        System.Text.Encoding.GetEncoding(949).GetByteCount(text);

    /// <summary>
    /// 발견물에 이름을 지어 준다(<c>0x004098C0</c>). 이름을 정했으면 참.
    /// </summary>
    /// <remarks>
    /// 지은 이름은 발견물 표에 덧씌워(<see cref="Local.Helpers.DiscoveryTable.SetName"/>)
    /// 그 뒤로는 일람이든 지도든 어디서든 그 이름이 보인다 — 게임도 레코드를 갈아 버린다.
    /// </remarks>
    private bool NameDiscovery(int discovery, string suffix)
    {
        suffix ??= "";
        while (true)
        {
            // 남는 칸은 <b>바이트</b>로 센다(0x00409917) — 꼬리말 한글 한 자가 두 칸을 먹는다.
            // 받는 칸도 바이트라 한글이면 그 절반만 들어간다.
            int room = Math.Max(1, (NameRoom - Bytes(suffix)) / 2);
            string? typed = UI.Views.TextInputDialog.Ask(_owner, "", room, "명명");
            if (typed == null) return false;                 // 무르면 기본 이름으로 간다

            string name = typed + suffix;
            if (!ConfirmDialog.Ask(_owner, $"[{name}]{GameUi.Josa(name, "으로", "로")} 명명하겠습니다. 좋습니까?"))
                continue;                                     // 아니오면 다시 받는다

            _game.Player.NameDiscovery(discovery, name);
            Local.Helpers.DiscoveryTable.SetName(discovery, name);
            return true;
        }
    }

    /// <summary>부하 자리 번호 — <see cref="Support.Local.Models.Player.MateRoles"/> 차례다.</summary>
    private const int AideSlot = 0, InterpreterSlot = 3;

    /// <summary>
    /// 짐칸에서 그 교역품(산지를 주면 그 도시산만)을 몽땅 거둬 가고 값을 치른다(<c>0x0040B3C8</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0040b430  짐칸 여덟을 돌며 종류(·산지)가 맞으면 수를 더하고 그 칸을 비운다(0x004742F0)
    ///   0040b494  지금 도시(0x00477EB0)가 없으면 여기서 끝 — 짐은 이미 걷혔다
    ///   0040b4ba  단가 = 교역품 기준가[그 도시 지역](0x0042E3C0) x 시세 / 100, 0 이면 1(0x00429DC0)
    ///   0040b4d2  소지금 += 단가 x 수 · 알림 「금화 %ld닢을 손에 넣었다」(0x00538EA0)
    /// </code>
    /// 교역소 매각가와 달리 특산가·햇수·도시 상태를 안 본다 — 기준가에 시세만 먹인 「정가」다.
    /// </remarks>
    private void BuyCargo(int goods, int? origin)
    {
        var player = _game.Player;
        int count = 0;
        for (int slot = player.CargoHold.Count - 1; slot >= 0; slot--)
        {
            var c = player.CargoHold[slot];
            if (c.Kind != goods || (origin is { } city && c.Origin != city)) continue;
            count += c.Count;
            player.UnloadCargo(slot, c.Count);
        }

        int here = player.CityId;
        if (here < 0 || _game.Trade is not { } trade) return;

        int basis = trade.BasePrice(trade.RegionOf(here), goods);
        int unit = basis * _game.Rates.Of(here) / Market.MarketRates.Par;
        if (basis > 0 && unit < 1) unit = 1;

        int gold = unit * count;
        player.Earn(gold);
        NoticeDialog.Show(_owner, $"금화 {gold}닢을 손에 넣었다");
    }

    /// <summary>
    /// 대본이 그 인물을 부하 자리에 앉힌다(<c>0x0040B0CA</c> 부관 · <c>0x0040AE0B</c> 통역).
    /// </summary>
    /// <remarks>
    /// 앉히고 나서 <b>알림 상자</b>가 뜬다(<c>0x0049E3E0</c>) — 그 자리에 앉아 있던 사람이
    /// 있으면 「%s%s 해고하고 %s%s 부관으로 삼았습니다」처럼 <b>해고까지 함께</b> 이른다.
    /// 조사는 을/를이다(<c>0x004281B0(이름, 2)</c>).
    /// </remarks>
    private void Seat(int slot, string role, int person)
    {
        if (Local.Helpers.PersonTable.Open()?.Find(person) is not { Name.Length: > 0 } who) return;

        string old = _game.Player.MateAt(slot);
        _game.Player.SetMate(slot, who.Name);
        _game.MateInfo(who.Name);

        string got = $"{who.Name}{GameUi.Josa(who.Name, "을", "를")}";
        NoticeDialog.Show(_owner, old.Length > 0
            // 이음말이 자리마다 다르다 — 부관은 「해고하고」(0x00538D60), 통역은 「해고하여」(0x00538C98).
            ? $"{old}{GameUi.Josa(old, "을", "를")} {(slot == InterpreterSlot ? "해고하여" : "해고하고")} {got} {role}으로 삼았습니다"
            : $"{got} {role}으로 삼았습니다");
    }

    private Support.Local.Models.Player.MateInfo? AideInfo()
    {
        var player = _game.Player;
        return player.Mates.Count > 0 && player.Mates[0].Length > 0
            ? player.MateInfoOf(player.Mates[0]) : null;
    }

    /// <summary>
    /// 부대배치 화면 — 게임도 판을 열기 전에 편다(<c>0x0044A963</c> → <c>0x00446E60</c>).
    /// </summary>
    /// <remarks>게임 화면에는 물리는 길이 없다. 창을 닫으면 제독 부대 하나만 세운다.</remarks>
    private int[] Deploy(Support.Local.Models.Player.MateInfo? aide)
    {
        int city = _game.Player.CityId;
        string where = city >= 0 ? _game.CityName(city) : "";
        // 대본이 병력을 빌려 줬으면(파르테논 70명) 배치 판도 그 수로 짓는다 — 함대 선원이 아니다.
        if (LandDeployDialog.Show(_owner, _game, where, _borrowedMen) is { } line) return line;

        var alone = new int[LandRoster.SlotCount];
        Array.Fill(alone, -1);
        alone[0] = LandRoster.For(_game.Player, aide).KindAt(LandRoster.Leader);
        return alone;
    }

    /// <summary>
    /// 아이템을 하나 얻고 <b>그 물건의 정보 창</b>을 낸다.
    /// </summary>
    /// <remarks>
    /// 게임은 손에 넣은 자리에서 그림·갈래·설명이 든 창을 띄운다 — 소지품 창에서 물건을
    /// 눌렀을 때 뜨는 것과 같은 창이다(<see cref="ItemInfoDialog"/>).
    /// 소지품 열여섯 칸이 다 찼으면 <b>버릴 것을 고르게 한다</b>(<c>0x00409F93</c>) — 안 버리면 못 든다.
    /// </remarks>
    private string ItemName(int itemId) => _game.Items?.Find(itemId)?.Name ?? $"아이템 {itemId}";

    /// <summary>아이템 창만 띄운다(00 05).</summary>
    private void ShowItem(int itemId)
    {
        if (_game.Items?.Find(itemId) is not { } item) return;
        ItemInfoDialog.Show(_owner, item, _game.ItemText?.Of(itemId) ?? "", _game.ItemPictures);
    }

    /// <summary>
    /// 05 05 — 발견물 아이템이 아니면 준다(<c>0x00408A06</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00408A4F  274 발견물의 표 +0x30 과 같으면 끝(말 없음)
    ///   0x005385D0  「[%s]%s 손에 넣었다」
    ///   0x00538598  「소유 아이템이 너무 많기 때문에 [%s]%s 포기했습니다」   ← 꽉 찼을 때, 버리기 창 없음
    /// </code>
    /// </remarks>
    private void GiveUnlessDiscovery(int itemId)
    {
        if (_game.Discoveries?.Table is { } table && table.Discoveries.Any(r => r.ItemId == itemId)) return;
        string name = ItemName(itemId);
        if (_game.Player.IsBagFull)
        {
            NoticeDialog.Show(_owner, $"소유 아이템이 너무 많기 때문에 [{name}]{GameUi.Josa(name, "을", "를")} 포기했습니다");
            return;
        }
        _game.Player.Take(itemId);
        NoticeDialog.Show(_owner, $"[{name}]{GameUi.Josa(name, "을", "를")} 손에 넣었다");
    }

    /// <summary>
    /// 26 05 — 준다. 꽉 찼으면 버릴 것을 고르게 한다(<c>0x00409F24</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   빈 칸이 있으면 말 없이 넣는다
    ///   0x00538888  「[%s]을 손에 넣었습니다만 소유 아이템이 많아서 더 이상 가질 수 없습니다.
    ///                필요없는 아이템을 선택해 버려 주십시오.」
    ///   0x005388F8  「버릴 아이템을 선택해 주십시오」   ← 지금 든 16개 목록(0x004B1100)
    ///   고르면 그것을 버리고 다시 넣어 본다 · 물리면 0x00538918 「[%s]을 손에 넣었습니다」만 하고 안 넣는다
    /// </code>
    /// </remarks>
    private void GiveMakingRoom(int itemId)
    {
        var player = _game.Player;
        string name = ItemName(itemId);
        while (player.IsBagFull)
        {
            NoticeDialog.Show(_owner, $"[{name}]을 손에 넣었습니다만 소유 아이템이 많아서 더 이상 가질 수 없습니다. "
                                    + "필요없는 아이템을 선택해 버려 주십시오.");
            var held = player.Items.ToList();
            int at = ChoiceDialog.Ask(_owner, "버릴 아이템을 선택해 주십시오", [.. held.Select(ItemName)]);
            if (at < 0 || at >= held.Count)
            {
                NoticeDialog.Show(_owner, $"[{name}]을 손에 넣었습니다");
                return;
            }
            player.Drop(held[at]);
        }
        player.Take(itemId);
    }

    /// <summary>
    /// 능력치 한 칸을 그만큼 움직인다. 게임의 뜀표(<c>0x004094C7</c> 의 <c>0x0040C228</c>, 0~29)를 따른다 —
    /// 표에 없는 칸(5·9·12~16·19·24~)은 게임도 지나간다.
    /// </summary>
    /// <remarks>
    /// 번호는 <see cref="DisevScript.StatNames"/> 표 그대로다. 갈래마다 게임이 하는 일은 이렇다.
    /// <code>
    ///    0  0x004094CE  함대 피로도            vt+0x10(n)            1  0x004094E5  규율 0x00474060(n)
    ///    2  0x004094F8  총 선원 수             vt+4(n) — 0~정원      3  0x0040950F  소지금 0x0047CBC0(n)
    ///    4  0x00409522  악명 0x004800E0(1, n)  0..99999 로 자르고 「악명이 %d 올라갔다/내려갔다」(0x005386E0 · 0x005386C8)
    ///    6  무력 · 7 체력 · 18 운 · 21 지력 · 22 매력 · 23 신앙심   0x00432C50(칸, n) — 보이는 값 1~100 으로 자른다
    ///    8  0x0040959E  제독 컨디션            0x00432C80(n) — 0..2000
    ///   10  0x004095B1  부관 체력 · 11 0x004095D6 부관 컨디션 — 부관 자리(+0x20)에 같은 셈. 부관이 없으면 지나간다
    ///   17  0x004095F9  명성 0x004800E0(0, n)  0..99999 로 자르고 상태 칸을 다시 그린 뒤(0x0047E360(…, 6))
    ///                                          「명성이 %d 올라갔다/내려갔다」(0x00538710 · 0x005386F8)
    ///   20  0x0040966C  기함 내구              hp = clamp(hp + n, 0, 250)(0x0044C860 → 0x0049E560 → 0x0044C850)
    /// </code>
    /// 명성·악명 알림은 0 이면 안 낸다(<c>0x00409554</c> 의 <c>jle</c>).
    /// </remarks>
    private void Adjust(int stat, int by)
    {
        var player = _game.Player;
        switch (stat)
        {
            case 0: player.Tire(by); break;      // 피로도
            case 1: player.Cheer(by); break;     // 규율(사기)
            case 2: player.AddCrew(by); break;   // 총 선원 수
            case 3:                              // 소지금
                if (by >= 0) player.Earn(by); else player.Pay(-by);
                break;
            case 4:                              // 악명
                player.Infamy = Math.Clamp(player.Infamy + by, 0, Sea.FleetRaid.MaxRenown);
                if (by < 0) NoticeDialog.Show(_owner, $"악명이 {-by} 내려갔다");
                else if (by > 0) NoticeDialog.Show(_owner, $"악명이 {by} 올라갔다");
                break;
            case 6: player.AdjustAbility(Support.Local.Models.Ability.Might, by); break;   // 무력
            case 7: player.AdjustAbility(Support.Local.Models.Ability.Body, by); break;    // 체력
            case 8: player.SetCondition(player.Condition + by); break;                     // 컨디션
            case 10:                             // 부관 체력
                if (AideInfo() is { } aideBody)
                    player.RememberMate(aideBody with
                    {
                        Body = Math.Clamp(Support.Local.Models.Ability.Display(aideBody.Body) + by,
                                          1, Support.Local.Models.Ability.Max) - 1,
                    });
                break;
            case 11:                             // 부관 컨디션
                if (AideInfo() is { } aideLife)
                    player.RememberMate(aideLife with
                    {
                        Condition = Math.Clamp(aideLife.Condition + by, 0,
                                               Support.Local.Models.Player.ConditionMax),
                    });
                break;
            case 17:                             // 명성
                player.Fame = Math.Clamp(player.Fame + by, 0, Sea.FleetRaid.MaxRenown);
                if (by < 0) NoticeDialog.Show(_owner, $"명성이 {-by} 내려갔다");
                else if (by > 0) NoticeDialog.Show(_owner, $"명성이 {by} 올라갔다");
                break;
            case 18: player.AdjustAbility(Support.Local.Models.Ability.Luck, by); break;   // 운
            case 20: player.FlagshipHull?.Batter(by); break;                               // 기함 내구
            case 21: player.AdjustAbility(Support.Local.Models.Ability.Mind, by); break;   // 지력
            case 22: player.AdjustAbility(Support.Local.Models.Ability.Charm, by); break;  // 매력
            case 23: player.AdjustAbility(Support.Local.Models.Ability.Faith, by); break;  // 신앙심
        }
    }

    /// <summary>대사 한 줄을 낸다. 화자에 따라 얼굴이 갈린다.</summary>
    /// <summary>
    /// 대본이 적어 둔 <b>사운드 ID</b> 하나를 낸다.
    /// </summary>
    /// <remarks>
    /// <b>ID 는 파트 번호가 아니다.</b> 표(<c>0x004C3810</c>)가 두 갈래로 나뉜다 —
    /// <c>0~27</c> 은 CD 트랙이고 <c>28~77</c> 은 WAVES.CDS 의 파트다. 파트 번호는
    /// ID 에서 28 을 뺀 값이다(<see cref="WaveBank.FirstSoundId"/>).
    ///
    /// 예전에는 ID 를 <see cref="SoundBank.Play"/> 에 그대로 넘겼다. 그러면 알함브라 궁전의
    /// <c>0E 03 4B 00</c>(ID 75)이 파트 75 를 찾다가 표 밖이라 조용히 넘어갔다 —
    /// 실제로 나야 할 것은 파트 <c>47</c> 이다.
    /// </remarks>
    private void PlaySound(int soundId)
    {
        int track = WaveBank.CdTrackFromSoundId(soundId);
        if (track >= 0) { _game.Bgm.Play(track); return; }

        int part = WaveBank.PartFromSoundId(soundId);
        if (part >= 0) _game.Sfx?.Play(part);
    }

    private void Speak(byte[] raw)
    {
        // 창 플래그 한 바이트가 앞에 붙을 수 있다. 0A 부터가 알맹이다.
        int textStart = raw.Length > 0 && raw[0] == 0x0A ? 1 : 2;
        if (textStart >= raw.Length) return;

        int end = Array.IndexOf(raw, (byte)0, textStart);
        if (end < 0) end = raw.Length;

        // 자리표(※ｓ·※Ｈ …)에 제독 이름과 조사를 채워 넣는다.
        var (speaker, body) = DisevScript.DecodeDialogue(raw.AsSpan(textStart, end - textStart),
                                                        _game.Player.Name);
        if (body.Length == 0) return;

        // <b>감찰관이 없으면 감찰관 대사는 통째로 건너뛴다</b> — 화자 해석기 0x0040C880 이 감찰관 객체를 못 찾으면
        // 0 을 돌려 그 줄을 안 낸다. 감찰관은 후원자 계약마다 하나 딸려 오므로 계약이 없으면 없다.
        //
        // <b>부관은 없어도 말한다</b> — 대신 기본 화자(뱃사람, MALE #299)가 선다(0x00478280, <see cref="Game.AideFace"/>).
        // 예전에는 부관 대사도 건너뛰었는데, 그러면 델포이의 「신의 계시를 받으시겠습니까?」 같은 물음이 통째로 빠졌다.
        if (speaker is Inspector or "검사관" && string.IsNullOrEmpty(_game.Player.Contract?.Inspector)) return;

        // <b>0B 0A [대사] 는 물음이다</b>(0x00408B5B) — 창 0x0040C880 을 YES/NO 로 띄우고
        // 결과 = (단추 == 2), 2 가 YES 다. 뒤따르는 43 45 · 43 47 이 이 결과로 가른다.
        // 델포이의 성지 「신의 계시를 받으시겠습니까?」가 이 꼴이다 — 예전에는 창 플래그로만
        // 보고 그냥 대사로 흘려 묻지도 않고 지나갔다.
        if (raw.Length > 1 && raw[0] == 0x0B && raw[1] == 0x0A)
        {
            _result = ConfirmDialog.Ask(_owner, body, face: FaceOf(speaker));
            return;
        }

        // 앞줄이 그림을 걸어 두었으면 그림과 글을 한 창에 낸다.
        if (_pendingStill >= 0)
        {
            int still = _pendingStill;
            _pendingStill = -1;
            DiscoveryDialog.Show(_owner, _pendingIsEvent ? _game.EventStills : _game.Stills,
                                 still, body, face: FaceOf(speaker));
            return;
        }

        TalkDialog.Say(_owner, FaceOf(speaker), "", body);
    }

    /// <summary>
    /// 그 화자의 얼굴. 모르는 화자면 null 이고, 그러면 얼굴 없이 글만 나온다.
    /// </summary>
    /// <remarks>
    /// 「검사관」은 CP932 로 <c>監察官</c> 이라 <b>감찰관</b>이다 — 계약할 때 딸려 온 그
    /// 사람이고 얼굴이 늘 232 다(<see cref="Town.Inspector"/>).
    /// 「부관」은 부하 첫 자리라 그 사람 제 얼굴을 쓴다.
    /// </remarks>
    private uint[]? FaceOf(string? speaker) => speaker switch
    {
        null or "" => null,
        Aide => MateFace(),
        Inspector or "검사관" => _game.Faces?.TryGetBgra(Town.Inspector.Face, female: false),
        // 執事 — 게임은 인물 275 를 세우고 얼굴을 229 로 박는다(0x0040CA16~0x0040CA40).
        Butler => _game.Faces?.TryGetBgra(ButlerFace, female: false),
        _ => FacilityFace(speaker) ?? NamedFace(speaker),
    };

    /// <summary>
    /// 이름으로 선 화자(후원자·인물)의 얼굴 — 화자 칸의 일본어 이름을 두 표에서 찾는다(<c>0x0040C8E0</c> · <c>0x0040CA7D</c>).
    /// </summary>
    private uint[]? NamedFace(string speaker)
    {
        if (_game.SpeakerNames?.Find(speaker) is not { } who) return null;
        if (who.Sponsor >= 0 && _game.Sponsors?.Sponsors.FirstOrDefault(s => s.Index == who.Sponsor) is { Name.Length: > 0 } sponsor)
            return _game.Faces?.TryGetBgra(sponsor.Face, sponsor.IsFemale);
        if (who.Person >= 0 && _game.PersonTemplates?.Find(who.Person) is { } person)
            return _game.Faces?.TryGetBgra(person.Face, female: false);
        return null;
    }

    /// <summary>
    /// 값 식 <c>1C [u16 칸]</c> 이 내는 값(<c>0x00406E76</c> 의 뜀표 <c>0x00407310</c>). 아는 칸만 낸다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   3  0x005B6194  소지금
    ///   5  vtbl+0x24   제독 성미 여덟 칸 가운데 [5](편협 0 · 1 · 욕심장이 2) — 생일·혈액형·운명 코드·나이로
    ///                  센다(<see cref="Sea.FleetRaid.AdmiralFortuneOf(Support.Local.Models.Player)"/>)
    ///   6  0x005B60C8  무력 + 1
    /// </code>
    /// </remarks>
    private long? ValueOf(JsonObject? expr)
    {
        if (expr == null) return null;
        long N(JsonNode? node) => node == null ? 0 : long.Parse(node.ToJsonString());

        if (expr["Const"] is { } constant) return N(constant);
        if (expr["Random"] is JsonObject random)
            return N(random["From"]) + _game.Random.Next((int)Math.Max(1, N(random["Width"])));
        // 08 [도시] 15 [교역품] — 함대 짐칸(16바이트 x n, +0 종류 · +4 수 · +8 원산지)을 훑어 <b>그 도시에서 산 그 교역품</b>의
        // 수를 다 더한다(0x00406D9B~0x00406E21). 개인 이야기의 「캘리컷 후추 100 이상」 같은 의뢰 조건이 이것이다.
        if (expr["Cargo"] is JsonObject cargo)
        {
            long city = N(cargo["City"]), goods = N(cargo["Goods"]);
            return _game.Player.CargoHold.Where(c => c.Kind == goods && c.Origin == city).Sum(c => (long)c.Count);
        }
        if (expr["Stat"] is not { } stat) return null;

        // 값 표는 0x00406E76 의 점프표(0x00407310) 그대로다. 능력치는 <b>1 을 더해</b> 낸다.
        // 16(육상전 상대 병력)과 28(적재량)은 <b>원본도 아무 값을 안 낸다</b> — 그 자리가 빈 칸이다.
        var player = _game.Player;
        return N(stat) switch
        {
            0 => player.Fatigue,                                        // 0x005AA2B8 vt+0x0C
            1 => player.Morale,                                         // 0x005B3954
            2 => player.Crew,                                           // 0x005AA2C4
            3 => player.Gold,
            4 => player.Infamy,
            5 => Sea.FleetRaid.AdmiralFortuneOf(player)[5],
            6 => player.AbilityOf(Support.Local.Models.Ability.Might) + 1,
            7 => player.AbilityOf(Support.Local.Models.Ability.Body) + 1,
            8 => player.Condition,                                      // 0x005B60D8
            17 => player.Fame,
            18 => player.AbilityOf(Support.Local.Models.Ability.Luck) + 1,
            21 => player.AbilityOf(Support.Local.Models.Ability.Mind) + 1,
            22 => player.AbilityOf(Support.Local.Models.Ability.Charm) + 1,
            23 => player.AbilityOf(Support.Local.Models.Ability.Faith) + 1,
            25 => player.LevelOf(Support.Local.Models.Skill.Names[ScienceSkill]),   // 0x005B6110
            26 => player.Nation,                                        // 제독 국적(vt+0x14)
            27 => player.Contract?.DaysLeft(player.Date) ?? 0,          // 후원자 계약 남은 기한(일)
            29 => player.StoryQuestDaysLeft,                            // STORY 의뢰 남은 기한(일)
            _ => null,
        };
    }

    /// <summary>기능 칸 12 — 과학(<c>0x005B6110</c> 은 기능 표 <c>0x005B60E0</c> 의 열두째다).</summary>
    private const int ScienceSkill = 12;

    /// <summary>부관 화자 이름. 대본에는 CP932 로 <c>副官</c> 이라 적혀 있다.</summary>
    private const string Aide = "부관";

    /// <summary>집사 화자 이름. 대본에는 CP932 로 <c>執事</c> 다.</summary>
    private const string Butler = "집사";

    /// <summary>집사 얼굴(<c>0x0040CA40</c> 의 <c>0xE5</c>).</summary>
    private const int ButlerFace = 229;

    /// <summary>감찰관 화자 이름. 대본에는 CP932 로 <c>監察官</c> 이다. 예전 이름 「검사관」도 받는다.</summary>
    private const string Inspector = "감찰관";

    /// <summary>
    /// 시설 화자의 건물 코드. 화자표(<c>0x0056823C[건물][문화권]</c>)를 그대로 탄다.
    /// </summary>
    /// <remarks>
    /// 몽생미셸(파트 65)의 <c>+0x00A9</c> 가 화자 <b>교회</b> 라 신부 얼굴이 붙는다 —
    /// 예전에는 모르는 화자로 흘려 얼굴 없이 냈다. 건물 코드는 볼트
    /// <c>15.분석-건물 화면 엔진</c> 의 그 차례다.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, int> FacilitySpeakers =
        new Dictionary<string, int>
        {
            ["교역소"] = 1, ["왕궁"] = 2, ["교회"] = 3, ["술집"] = 4,
            ["여관"] = 5, ["조선소"] = 6, ["조합"] = 9, ["성문"] = 10,
        };

    /// <summary>시설 화자의 얼굴. 그 시설 화자가 아니면 null 이다.</summary>
    /// <remarks>
    /// 문화권은 <b>지금 있는 도시</b>의 것이다 — 바다 위라 도시를 모르면 유럽(0)으로 둔다.
    /// </remarks>
    private uint[]? FacilityFace(string speaker)
    {
        if (!FacilitySpeakers.TryGetValue(speaker, out int code)) return null;

        int city = _game.Player.CityId;
        int culture = city >= 0 ? _game.CityRows?.CultureOf(city) ?? 0 : 0;
        return _game.SpeakerFace(code, culture);
    }

    /// <summary>부하 첫 자리의 얼굴. 판이 들고 있는 것을 그대로 쓴다.</summary>
    private uint[]? MateFace() => _game.AideFace;
}
