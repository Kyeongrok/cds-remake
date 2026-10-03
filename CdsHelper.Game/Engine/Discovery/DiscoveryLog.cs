using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Discovery;

/// <summary>
/// 발견물을 맡는다 — 지금 선 칸에서 발견될 것을 찾아 주고, 발견한 것을 주인공에게 적는다.
/// </summary>
/// <remarks>
/// 게임은 항해 루프를 한 번 돌 때마다 <c>0x0048D3F0</c> 에서 이 판정을 한다. 그 차례를
/// 그대로 옮겼다.
/// <code>
///   칸 = (0x5B63B0 / 16, 0x5B63B4 / 16)      배든 말이든 같은 값을 쓴다
///   i  = 0x00425640(칸)                       사각형 안에 드는 것 중 가장 좁은 것
///   0x004AAD20(obj[i])                        깃발 0x08 이 서 있고 아직 안 발견한 것인가
///   표[i].+0x28 == *0x5B61B4                  바다에서 찾을 것을 바다에서 만났는가
///   → DISEV.CDS 의 i 번 사건을 튼다
/// </code>
/// 깃발 <c>0x08</c> 은 새 판을 열 때 표의 <c>+0x24</c> 로 세우고(<c>0x004AA9A0</c>), 나중에
/// 힌트를 얻으면 힌트 쪽에서 세워 준다(<c>0x004AE030</c>). 여기서는 깃발을 들고 있지 않고
/// <see cref="IsOpen"/> 이 그때그때 따진다 — 결과가 같고, 힌트를 잃는 길이 없어 어긋날 수도
/// 없다.
///
/// 사건 연출(DISEV.CDS)은 <see cref="Disev.DisevRunner"/> 가 돈다 — 여기는 발견을 적고
/// 알리는 것까지만 한다.
/// </remarks>
public sealed class DiscoveryLog
{
    private readonly DiscoveryTable _table;
    private readonly HintTable? _hints;
    private readonly HistoryVoyages? _history;

    /// <param name="table">EXE 의 발견물 표.</param>
    /// <param name="hints">
    /// EXE 의 힌트 표. 없으면 힌트로 열리는 발견물(12 대발견 등)은 끝내 안 열린다 —
    /// 표를 못 읽었다고 아무 데서나 발견되게 하는 것보다 낫다.
    /// </param>
    /// <param name="history">
    /// 역사 항해자 열넷(<c>HISTCHR.CDS</c>). 없으면 아무도 선수를 안 친다.
    /// </param>
    public DiscoveryLog(DiscoveryTable table, HintTable? hints, HistoryVoyages? history = null)
    {
        _table = table;
        _hints = hints;
        _history = history;
    }

    /// <summary>
    /// 그 발견물을 <b>역사가 이미 가져갔는지</b>. 가져갔으면 그 사람 번호, 아니면 -1.
    /// </summary>
    /// <remarks>
    /// <b>한 번짜리</b>(<see cref="DiscoveryTable.Record.Once"/>)에만 걸린다. 게임의
    /// <c>0x004AAC10</c> 이 표 <c>+0x2C</c> 를 보고, 한 번짜리인데 사람 칸 0·1 에 이미
    /// 이름이 있으면 발견을 아예 안 적는 그 자리다. 여럿이 거듭 발견하는 것(생물·교역품
    /// 따위)은 몇 번이고 다시 잡힌다.
    /// </remarks>
    public int TakenBy(in DiscoveryTable.Record row, DateTime date) =>
        row.Once && _history != null ? _history.TakenBy(row.Id, date) : -1;

    /// <summary>
    /// 사람 칸 0·1 에 <b>누구 이름이든</b> 올라 있는지 — 게임의 <c>0x004AAD80</c> 이다.
    /// </summary>
    /// <remarks>
    /// <see cref="TakenBy"/> 와 달리 한 번짜리만 보지 않는다. 역사 항해자가 거듭 찾는 것(하마·후추 따위)을
    /// 찾아도 칸 1 이 차고(<c>0x004AAC65</c> — 칸 1 이 비었을 때만 적는다), 지도 지우기
    /// (<c>0x004AADD0</c>)가 이것으로 그 자리를 드러낸다.
    /// </remarks>
    public bool Named(Player player, int id, DateTime date) =>
        player.HasFound(id) || (_history?.TakenBy(id, date) ?? -1) >= 0;

    /// <summary>발견물 표.</summary>
    public DiscoveryTable Table => _table;

    /// <summary>
    /// 그 발견물이 <b>열려 있는지</b>. 표의 <c>+0x24</c> 가 서 있으면 처음부터 열려 있고,
    /// 아니면 그것을 가리키는 힌트로 <b>계약을 맺어야</b> 열린다 — 힌트만 쥐고 있는 것으로는
    /// 안 열린다.
    /// </summary>
    /// <remarks>
    /// EXE 를 직접 뜯어 확인했다 — 발견물 인스턴스의 열림 깃발(<c>+0x16</c> 비트 <c>0x08</c>,
    /// <c>0x004AAD20</c> 이 이 깃발을 본다)을 세우는 자리는 실행 파일을 통틀어 <b>둘뿐</b>이다.
    /// <code>
    ///   0x004AA9A0   새 판을 열 때 표 +0x24(OpenAtStart)로 세운다
    ///   0x004ADEE0   계약을 맺을 때(Contract.cs) 그 힌트가 가리키는 발견물에 세운다
    /// </code>
    /// 힌트를 얻는 자리(도서관 <c>LibraryDialog</c>, 술집)에는 이 깃발을 세우는 코드가
    /// 아예 없다 — 힌트는 <b>계약을 맺을 때 고를 거리</b>일 뿐, 그 자체로는 아무것도 열지
    /// 않는다. 담비처럼 손쉬운 것도 마찬가지다 — 계약이 하도 쉽게 받아들여져 눈에 안 띌
    /// 뿐이지, 희망봉 같은 큰 발견과 매한가지로 계약 없이는 자리에 가도 안 잡힌다.
    ///
    /// 계약이 기한을 넘기거나 깨져도 이 깃발을 지우는 코드가 없어(<c>AND</c> 명령이 아예
    /// 없다) 한 번 열리면 계속 열려 있다. 그래서 지금 <see cref="Player.Contract"/> 가
    /// 아니라 <b>맺어 본 적 있는 힌트를 전부</b> 담는 <see cref="Player.OpenedHints"/> 로
    /// 따진다.
    ///
    /// 힌트와 발견물은 번호로 짝을 맺는다 — 힌트의 <see cref="HintTable.Hint.Discovery"/> 와
    /// 발견물의 <see cref="DiscoveryTable.Record.Hint"/> 가 같으면 그 짝이다.
    /// </remarks>
    public bool IsOpen(Player player, in DiscoveryTable.Record row)
    {
        // 대본 명령 「발견물 닫기 · 열기」(원본에 없는 것)가 계약 · 처음부터보다 먼저다.
        if (player.ScriptClosedDiscoveries.Contains(row.Id)) return false;
        if (player.ScriptOpenedDiscoveries.Contains(row.Id)) return true;
        if (row.OpenAtStart) return true;
        if (_hints == null) return false;

        foreach (int id in player.OpenedHints)
            if (_hints.Find(id) is { } hint && hint.Discovery == row.Hint) return true;
        return false;
    }

    /// <summary>
    /// 그 힌트가 <b>보고까지 끝났는지</b> — 원본 힌트 상태의 bit1 이다.
    /// </summary>
    /// <remarks>
    /// 원본은 힌트마다 상태 한 칸을 들고 있다(<c>0x0058B4E0</c>, 8바이트 x 186 의 <c>+0x04</c>).
    /// 나오는 값은 넷뿐이다.
    /// <code>
    ///   8  1000  아무것도 아직
    ///   13 1101  힌트만 얻었다            ← 일람·설득 목록에 뜨는 것은 이것뿐이다
    ///   11 1011  힌트 없이 찾아 보고했다
    ///   15 1111  힌트 얻고 보고까지
    /// </code>
    /// <b>bit1 은 "발견" 이 아니라 "보고" 다.</b> 발견만 해서는 안 켜진다 — 후원자에게
    /// 보고하는 <c>0x004AACA0</c> 이 보고한 발견물의 유적 번호(<c>+0x08</c>)를 꺼내 힌트
    /// 186개를 훑으며 같은 번호를 가진 것을 모두 켠다(<c>or [힌트+4], 3</c>). 그래서 발견하고
    /// 보고하기 전까지는 힌트가 목록에 그대로 남아 있는 것이 원본의 옳은 모습이다.
    ///
    /// 여기서는 상태 칸을 따로 들지 않고 <see cref="Player.Announced"/> 로 그때그때 따진다 —
    /// 보고를 무를 길이 없으니 결과가 같고, 두 곳이 어긋날 수도 없다.
    /// </remarks>
    public bool IsHintDone(Player player, int hintId)
    {
        if (_hints?.Find(hintId) is not { } hint) return false;

        foreach (int id in player.Announced)
            if (!player.Unresolved.Contains(id)   // 모조품을 들켜 도장만 찍힌 것은 안 친다(0x0047E630)
                && _table.Find(id) is { } row && row.Hint == hint.Discovery) return true;
        return false;
    }

    /// <summary>
    /// 아직 살아 있는 힌트 — 얻었고 아직 보고 안 한 것(상태 13). 번호 차례로 낸다.
    /// </summary>
    /// <remarks>「취득 힌트 일람」과 후원자 설득 목록이 함께 쓴다.</remarks>
    public List<int> LiveHints(Player player) =>
        [.. player.Hints.Where(id => !IsHintDone(player, id)).Order()];

    /// <summary>
    /// 그 힌트가 가리키는 발견물(같은 유적 번호)을 <b>하나라도 찾았는지</b> — 보고 여부는 안 본다.
    /// </summary>
    public bool IsHintFound(Player player, int hintId)
    {
        if (_hints?.Find(hintId) is not { } hint) return false;

        foreach (int id in player.Discoveries)
            if (_table.Find(id) is { } row && row.Hint == hint.Discovery) return true;
        return false;
    }

    /// <summary>
    /// 후원자에게 내밀 수 있는 힌트 — 살아 있는 힌트(<see cref="LiveHints"/>) 가운데 <b>아직 못 찾은 것</b>이다.
    /// </summary>
    /// <remarks>
    /// <b>원본과 다르다.</b> 원본 설득 목록(<c>0x0044E7B0</c>)은 상태 13(얻었고 보고 전)을 다 올려, 이미 찾은 발견물의
    /// 힌트로도 계약을 맺을 수 있었다. 찾은 것을 두고 지원을 청하는 꼴이라 빼 달라는 요청으로 거른다.
    /// 「취득 힌트 일람」은 원본 그대로 <see cref="LiveHints"/> 를 쓴다 — 보고 전까지는 거기 남는다.
    /// </remarks>
    public List<int> PersuadableHints(Player player) =>
        [.. LiveHints(player).Where(id => !IsHintFound(player, id))];

    /// <summary>
    /// 지금 칸에서 발견될 것. 없으면 -1.
    /// </summary>
    /// <param name="player">주인공. 이미 발견한 것과 가진 힌트를 본다.</param>
    /// <param name="cellX">지금 선 칸(가로). 0~2499.</param>
    /// <param name="cellY">지금 선 칸(세로). 0~1249.</param>
    /// <param name="onLand">뭍에 올라 있는지. 게임의 <c>0x5B61B4</c> 자리다.</param>
    /// <remarks>
    /// 사각형이 겹치면 <b>가장 좁은 것</b>이 이긴다 — 신대륙(480칸 폭) 안에 있는 유적이
    /// 신대륙에 가려지지 않게 하려는 것이다. 넓이가 같으면 번호가 작은 쪽이 이긴다
    /// (게임도 <c>0x004256DE</c> 에서 &gt; 로만 갈아 끼운다).
    ///
    /// <b>후보는 하나만 뽑는다.</b> 게임의 <c>0x00425640</c> 은 <b>자리와 너비만</b> 보고
    /// 한 줄을 고르고, 받은 <c>0x0048D462</c> 가 그 <b>한 줄에만</b> 관문을 건다 —
    /// 열렸는지(<c>0x08</c>) · 사건을 매듭지었는지(<c>0x0100</c>) · 바다뭍(<c>+0x28</c>)이
    /// 안 맞으면 <b>그냥 아무 일도 안 일어난다</b>. 다른 후보로 물러서지 않는다.
    /// 예전에는 관문을 통과하는 것 가운데 가장 좁은 것을 골라, 겹친 자리에서 이미 찾은
    /// 발견물을 건너뛰고 넓은 쪽이 잡히곤 했다.
    ///
    /// 역사가 가져간 한 번짜리는 사건부터 안 뜬다 — <c>0x004AAD20</c> 이 칸 1 에 이름이 있으면
    /// 막는다(<see cref="CanFire"/>).
    /// </remarks>
    public int At(Player player, int cellX, int cellY, bool onLand)
    {
        // ① 자리와 너비만 보고 한 줄을 고른다(0x00425640).
        int found = -1;
        int best = int.MaxValue;

        foreach (var row in _table.Discoveries)
        {
            if (!row.Covers(cellX, cellY)) continue;
            if (row.Span >= best) continue;
            best = row.Span;
            found = row.Id;
        }

        if (found < 0 || _table.Find(found) is not { } picked) return -1;

        // ② 그 한 줄에만 관문을 건다(0x0048D462 → 0x004AAD20).
        if (!CanFire(player, picked)) return -1;
        if (picked.OnLand != onLand) return -1;      // 표 +0x28 과 0x5B61B4 를 견주는 자리

        return found;
    }

    /// <summary>
    /// 그 발견물의 사건이 뜰 수 있는지 — 게임의 <c>0x004AAD20</c> 이다. 바다·뭍 자리, 도시 입장,
    /// 발견물이 된 건물 셋이 모두 이것으로 거른다(<c>0x0048D462</c> · <c>0x004928D8</c> · <c>0x00492A79</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   4AAD27  깃발 0x0100 이 서 있으면 0        매듭지은 사건(Player.Settled)
    ///   4AAD30  깃발 0x08 이 안 서 있으면 0       열림(IsOpen)
    ///   4AAD47  표 +0x2C 가 1(한 번짜리)이면      칸 0·1 이 비어야 1 — 내가 찾았거나 역사가 가져갔으면 0
    ///   4AAD61  표 +0x2C 가 0(거듭 찾는 것)이면   칸 2 가 비어야 1 — 보고·발표했거나 남이 발표했으면 0
    /// </code>
    /// 거듭 찾는 것(생물·교역품 따위)은 찾기만 해서는 안 막힌다 — 대본 밖에서(교역소 따위) 먼저 찾아도
    /// 보고하기 전이면 자리 사건이 뜬다. 한 번 돌고 나면 0x0100 이 막는다.
    /// </remarks>
    public bool CanFire(Player player, in DiscoveryTable.Record row)
    {
        if (player.IsSettled(row.Id)) return false;
        if (!IsOpen(player, row)) return false;
        if (row.Once) return !player.HasFound(row.Id) && TakenBy(row, player.Date) < 0;
        return !player.HasAnnounced(row.Id) && player.ScoopedBy(row.Id) == null;
    }

    /// <summary>
    /// 사건을 돌린 뒤 매듭을 짓는다 — 대본 결과가 0 이나 1 이면 깃발 <c>0x0100</c> 을 세운다
    /// (<c>0x0048D569</c> · <c>0x0049294A</c> · <c>0x00492B06</c>).
    /// </summary>
    /// <param name="scripted">
    /// 대본이 돌았는지. 대본이 없어 여기서 곧장 적은 것은 끝난 것으로 본다 — 안 그러면 거듭 찾는 것이
    /// 그 자리를 지날 때마다 또 뜬다.
    /// </param>
    public static void Settle(Player player, int id, bool scripted)
    {
        if (!scripted || Disev.DisevRunner.LastResult is 0 or 1) player.Settle(id);
    }

    /// <summary>
    /// 발견한 것으로 적는다. <b>주는 아이템은 소지품에 넣지 않는다</b> — 보고·발표할 때까지는 소지품
    /// 일람에 비쳐 보이기만 한다(<see cref="GameInfo.VirtualItems"/>, <c>0x004AAC10</c> 은 칸 0 만 채운다).
    /// </summary>
    /// <remarks>
    /// 관문이 하나 더 있다(<c>0x004AAC10</c>) — 발견물 표 <c>+0x2C</c> 가 1
    /// (<see cref="DiscoveryTable.Record.Once"/>, 한 번만 발견되는 것)인데 사람 칸 0·1 에
    /// 이미 이름이 올라가 있으면 <b>아무것도 적지 않는다</b>. 깃발 <c>0x40</c> 도 안 서서
    /// 항구 발표 목록(<c>0x00476DA0</c>)에 뜨지도 않는다. 그것이 <see cref="TakenBy"/> 다.
    ///
    /// 칸 1 을 채우는 것은 <b>역사 항해자 열넷</b>(<see cref="HistoryVoyages"/>)이다.
    /// 예순일곱 건을 채가고 그 중 스물한 건이 한 번짜리라 <b>선수를 빼앗기면 영영 못
    /// 얻는다</b> — 희망봉(1488.01)·마젤란해협(1520.10)·기저의 3대 피라미드(1519.07)·
    /// 잉카제국(1533.11) 따위다.
    /// </remarks>
    /// <returns>새로 발견했으면 true.</returns>
    public bool Discover(Player player, int id)
    {
        if (_table.Find(id) is not { } row) return false;
        if (TakenBy(row, player.Date) >= 0) return false;
        return player.Discover(id);
    }
}
