using System.IO;
using CdsHelper.Game.Engine.Disev;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Market;

/// <summary>
/// 역사 대본이 도시 상태를 바꾼다 — <c>HIST_EV.CDS</c> 79파트와 <c>HISTCHR.CDS</c> 역사 항해자 열넷.
/// </summary>
/// <remarks>
/// 게임은 달이 바뀔 때마다(<c>0x0044B2A0</c>) 이 차례로 돈다.
/// <code>
///   인물마다 vtbl+0x40     HISTCHR 0~13 — 사람 대본(0x004327F0 → 0x0040CF70)
///   도시마다 0x0042A280     재고를 채우고 시세를 흔든다
///   HIST_EV 파트 0..78      0x0040CFD0(파일, i, 0) — 파트마다 칸 고르기(0x00407390) 뒤 몸통 하나
/// </code>
/// <b>끝남 깃발이 없다.</b> 한 파트는 조건이 참인 <b>첫 칸 하나</b>만 돌고, 그 달에 또 참이면 다음 달에도 돈다.
/// 한 번만 도는 것은 조건이 그렇게 짜여 있어서다 — <c>1C</c>(그 해 그 달)이거나, <c>1B</c>(그 달부터)에
/// 몸통이 스스로 깨는 조건(「그 도시가 없어야」·「나라가 아니어야」)이 붙어 있다.
///
/// 여기서 옮기는 것은 <b>도시 상태</b>(<c>21 08 [도시] 18 [상태]</c>), <b>나라 바뀜</b>(<c>21 08 [도시] 00 [나라]</c>,
/// 수도면 나라째), <b>규모 ±</b>(<c>19/1A 08</c>)다. 도시 세우기는 <see cref="CityFounding"/> 이 따로 센다.
/// <b>소문</b>(<c>20 0A</c>)은 도시 소문 가게에 적는다(술집·여관 무명 손님이 반쯤 이것을 말한다).
/// <b>건물 비트</b>(<c>22/26 10</c>, 왕궁을 세우고 헐기)와 <b>나라 멸망/등장</b>(<c>22/26 00</c>)도 적는다. 힌트 깃발은 길이만 읽고 넘긴다.
///
/// 원본 데이터의 버릇도 그대로 남는다 — 파트 20 은 두 칸 날짜가 같아(1499/7) 해제 칸이 영영 안 돌아
/// 라구사·파마가스타·간디아는 전쟁, 베니스는 대조선으로 남는다.
/// </remarks>
public sealed class CityHistory
{
    public const string FileName = "HIST_EV.CDS";

    /// <summary>놀이 첫 달. 이 달은 안 돌고 다음 달부터 돈다(달이 바뀔 때 돌기 때문이다).</summary>
    public const int FirstYear = 1480;

    /// <summary>HIST_EV 파트들(원본 바이트).</summary>
    private readonly byte[][] _parts;

    /// <summary>HISTCHR 사람 대본(원본 바이트). 0~13.</summary>
    private readonly byte[][] _persons;

    private CityHistory(byte[][] parts, byte[][] persons)
    {
        _parts = parts;
        _persons = persons;
    }

    /// <summary>게임 폴더에서 연다. HIST_EV 를 못 읽으면 null.</summary>
    public static CityHistory? Open(string gameDirectory)
    {
        if (gameDirectory.Length == 0) return null;
        var parts = ReadParts(Path.Combine(gameDirectory, FileName), int.MaxValue);
        if (parts == null) return null;
        var persons = ReadParts(Path.Combine(gameDirectory, HistoryVoyagesFile), 14) ?? [];
        return new CityHistory(parts, persons);
    }

    private const string HistoryVoyagesFile = "HISTCHR.CDS";

    private static byte[][]? ReadParts(string path, int limit)
    {
        var archive = Ls12Reader.Open(path);
        if (archive == null) return null;
        int count = Math.Min(archive.PartCount, limit);
        var parts = new byte[count][];
        for (int i = 0; i < count; i++) parts[i] = archive.Decode(i) ?? [];
        return parts;
    }

    /// <summary>달을 가리는 값(해 x 12 + 달).</summary>
    public static int MonthKey(int year, int month) => year * 12 + month;

    /// <summary>놀이 첫 달의 값.</summary>
    public static int StartKey => MonthKey(FirstYear, 1);

    /// <summary>그 발견물을 누가 찾았는지(주인공이든 역사 항해자든). 칸 0·1 에 이름이 있는지다(<c>0x004AAD80</c>).</summary>
    public delegate bool FoundCheck(int discovery, DateTime when);

    /// <summary>
    /// 한 달을 돈다 — 그 달이 막 시작되었을 때 게임이 하는 몫. 사람 대본 먼저, 그 다음 HIST_EV.
    /// </summary>
    public void RunMonth(Player player, int year, int month, CityExeTable? cities, NationTable? nations,
                         FoundCheck found)
    {
        var when = new DateTime(year, month, 1);

        // HISTCHR — 사람마다 그 달에 맞는 첫 칸 하나. 날짜 조건(1C)만 있는 칸에서 상태 명령만 본다.
        for (int who = 0; who < _persons.Length; who++)
        {
            var part = _persons[who];
            if (Pick(part, year, month, when, player, cities, found, -1) is not { } body) continue;
            Run(part, body.Body, BodyEnd(part, body.Body), player, cities, nations, when, statesOnly: true);
            SpeakLines(part, body.Body, BodyEnd(part, body.Body), player, who, when);
        }

        for (int i = 0; i < _parts.Length; i++)
        {
            var part = _parts[i];
            if (Pick(part, year, month, when, player, cities, found, i) is not { } slot) continue;
            if (slot.Once) player.MarkHistoryDone(i * 100 + slot.Index);
            Run(part, slot.Body, part.Length, player, cities, nations, when, statesOnly: false);
        }
    }

    private readonly record struct Slot(int Index, int Body, bool Once);

    /// <summary>
    /// 칸 고르기(<c>0x00407390</c>) — 칸을 차례로 보아 조건이 참인 첫 칸. 모르는 조건이 나오면 그 파트는 안 돈다.
    /// </summary>
    /// <param name="partIndex">HIST_EV 파트 번호. HISTCHR 면 -1(한 번 돈 칸을 적지 않는다).</param>
    private static Slot? Pick(byte[] part, int year, int month, DateTime when, Player player,
                              CityExeTable? cities, FoundCheck found, int partIndex)
    {
        if (part.Length < 4) return null;
        int slots = U16(part, 2);
        for (int s = 0; s < slots; s++)
        {
            int at = 4 + s * 4;
            if (at + 4 > part.Length) return null;
            int cond = U16(part, at) + 4, body = U16(part, at + 2) + 4;
            if (cond >= part.Length || body >= part.Length) return null;

            bool once = false;
            bool? ok = Test(part, cond, year, month, when, player, cities, found, ref once);
            if (ok is null) return null;                         // 모르는 조건 — 파트째 건너뛴다(0x00407F09)
            if (partIndex < 0 && once) continue;                 // 사람 대본은 날짜(1C) 칸만 본다
            if (once && player.HistoryDone.Contains(partIndex * 100 + s)) continue;
            if (ok == true) return new Slot(s, body, once);
        }
        return null;
    }

    /// <summary>조건 한 줄을 따진다. 모르는 조건이면 null.</summary>
    private static bool? Test(byte[] p, int i, int year, int month, DateTime when, Player player,
                              CityExeTable? cities, FoundCheck found, ref bool once)
    {
        bool all = true, orMode = false, orAcc = false;
        while (i < p.Length)
        {
            if (p[i] == 0xFF) return all;
            if (i + 1 >= p.Length) return null;

            bool term;
            switch (p[i], p[i + 1])
            {
                case (0x1C, 0x17) when i + 5 < p.Length:                        // 그 해 그 달
                    term = year == U16(p, i + 4) && month == p[i + 2];
                    i += 6;
                    break;
                case (0x1B, 0x17) when i + 5 < p.Length:                        // 그 해 그 달부터
                    term = MonthKey(year, month) >= MonthKey(U16(p, i + 4), p[i + 2]);
                    once = true;
                    i += 6;
                    break;
                case (0x1B, 0x0B) when i + 6 < p.Length:                        // 발견된 뒤 n 해 — 원본은 n 을 안 본다(0x004076A1)
                case (0x02, 0x0B) when i + 3 < p.Length:                        // 발견됐는가
                    term = found(U16(p, i + 2), when);
                    i += p[i] == 0x1B ? 7 : 4;
                    break;
                case (0x27, 0x08) when i + 3 < p.Length:                        // 도시가 있어야
                    term = Standing(U16(p, i + 2), when, player);
                    i += 4;
                    break;
                case (0x28, 0x08) when i + 3 < p.Length:                        // 도시가 없어야
                    // 도시 세우기는 CityFounding 이 따로 세므로 이 칸이 세우는 도시와 부딪친다.
                    // 「그 달부터」 칸은 한 번 돈 것을 적어 두므로 참으로 둔다.
                    term = once || !Standing(U16(p, i + 2), when, player);
                    i += 4;
                    break;
                case (0x27, 0x00) or (0x28, 0x00) when i + 6 < p.Length:        // 나라가 ~이어야 / 아니어야
                {
                    int nation = U16(p, i + 2), city = U16(p, i + 5);
                    int now = player.HistoryNations.TryGetValue(city, out int changed)
                        ? changed : cities?.StartNationOf(city) ?? -1;
                    term = p[i] == 0x27 ? now == nation : now != nation;
                    i += 7;
                    break;
                }
                default:
                    return null;
            }

            // 50 은 이 항과 다음 항을 OR 로 묶는다(0x00407EAD).
            if (i < p.Length && p[i] == 0x50)
            {
                i++;
                orAcc |= term;
                orMode = true;
                continue;
            }
            if (orMode) term |= orAcc;
            all &= term;
        }
        return null;
    }

    /// <summary>
    /// 몸통을 돈다(<c>0x004080F0</c>). 도시 상태와 나라 바뀜만 적고 나머지는 길이만 건넌다.
    /// 모르는 명령을 만나면 거기서 멈춘다.
    /// </summary>
    /// <param name="statesOnly">사람 대본 — 명령 짜임이 넓어 상태·나라 명령 꼴만 훑어 찾는다.</param>
    private static void Run(byte[] p, int i, int end, Player player, CityExeTable? cities, NationTable? nations,
                            DateTime when, bool statesOnly)
    {
        end = Math.Min(end, p.Length);
        if (statesOnly)
        {
            for (; i + 6 < end; i++)
            {
                if (p[i] != 0x21 || p[i + 1] != 0x08) continue;
                int city = U16(p, i + 2), value = U16(p, i + 5);
                if (city >= CityExeTable.Count) continue;
                // 상태(18)와, 역사 항해자가 공략한 도시의 나라(00) — 고아·말라카는 포르투갈, 쿠스코는 에스파니아.
                if (p[i + 4] == 0x18 && value < CityState.Names.Length) player.SetCityState(city, value);
                else if (p[i + 4] == 0x00 && value < NationTable.Count) ChangeNation(player, cities, nations, city, value);
            }
            return;
        }

        while (i + 1 < end)
        {
            byte a = p[i], b = p[i + 1];
            if (a == 0xFF) return;
            switch (a, b)
            {
                case (0x20, 0x0A):                                              // 소문 — 글 00 [08 도시|19 문화권] u16
                {
                    int zero = Array.IndexOf(p, (byte)0, i + 2);
                    if (zero < 0 || zero + 3 >= p.Length) return;
                    // 제독 이름 자리표는 적을 때 편다(0x004099CB → 0x0040C410).
                    string text = DisevScript.DecodeDialogue(p.AsSpan(i + 2, zero - i - 2), normalize: true,
                                                             player: player.Name).Body;
                    int where = U16(p, zero + 2);
                    if (p[zero + 1] == 0x08) player.AddRumor(where, text, when);
                    else if (p[zero + 1] == 0x19)
                        // 그 문화권 도시 226곳 모두 — 아직 안 선 도시도 받는다(0x00409A44).
                        for (int c = 0; c < CityExeTable.Count; c++)
                            if (cities?.CultureOf(c) == where) player.AddRumor(c, text, when);
                    i = zero + 4;
                    break;
                }
                case (0x21, 0x08) when i + 6 < end:
                {
                    int city = U16(p, i + 2), value = U16(p, i + 5);
                    if (p[i + 4] == 0x18) player.SetCityState(city, value);
                    else if (p[i + 4] == 0x00) ChangeNation(player, cities, nations, city, value);
                    i += 7;
                    break;
                }
                case (0x19 or 0x1A, 0x08) when i + 8 < end && p[i + 4] == 0x1A:   // 규모 ± (0x00409385, 0~7 로 자름)
                {
                    int city = U16(p, i + 2);
                    int by = BitConverter.ToInt32(p, i + 5) * (a == 0x19 ? 1 : -1);
                    int was = cities?.ScaleOf(city) ?? 0;
                    player.SetCityScale(city, was + by);
                    i += 9;
                    break;
                }
                case (0x22 or 0x26, 0x00) when i + 3 < end:                     // 나라 멸망·등장(0x409D9B · 0x409F03)
                    player.SetNationStatus(U16(p, i + 2), a == 0x22 ? 2 : 1);
                    i += 4;
                    break;
                case (0x01 or 0x26 or 0x22 or 0x23 or 0x25, 0x08):              // 도시 드러냄·세움·깃발
                case (0x26, 0x0E):                                              // 힌트 깃발
                    i += 4;
                    break;
                case (0x22 or 0x26, 0x10) when i + 6 < end:                     // 건물 비트 끄기·켜기(0x409DD1 · 0x40A118)
                {
                    SetBuilding(player, cities, U16(p, i + 5), U16(p, i + 2), a == 0x26);
                    i += 7;
                    break;
                }
                case (0x43, 0x2B) when i + 11 < end && p[i + 2] == 0x1C:        // 능력 >= 값 이 아니면 뜀
                {
                    int stat = U16(p, i + 3);
                    long need = BitConverter.ToUInt32(p, i + 6);
                    int skip = U16(p, i + 10);
                    i += 12;
                    // 17 = 명성. 뒤에는 소문만 따라와 다른 능력은 뛰지 않고 건넌다.
                    if (stat == 17 && player.Fame < need) i += skip;
                    break;
                }
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// 사람 대본의 <c>26 0A [글] 00</c> — 그 사람이 술집에서 「정보를 듣는다」에 할 말을 적는다(<c>0x0040A082</c>).
    /// 글 속 바이트는 0x81 이상이라 <c>26 0A</c> 꼴과 헷갈리지 않는다.
    /// </summary>
    private static void SpeakLines(byte[] p, int i, int end, Player player, int who, DateTime when)
    {
        end = Math.Min(end, p.Length);
        for (; i + 2 < end; i++)
        {
            if (p[i] != 0x26 || p[i + 1] != 0x0A) continue;
            int zero = Array.IndexOf(p, (byte)0, i + 2);
            if (zero < 0 || zero > end) return;
            string text = DisevScript.DecodeDialogue(p.AsSpan(i + 2, zero - i - 2), normalize: true,
                                                     player: player.Name).Body;
            player.SetPersonLine(who, text, when);
            i = zero;
        }
    }

    /// <summary>
    /// 도시의 나라를 바꾼다(<c>0x00409AB6</c>) — 그 도시가 옛 나라의 <b>수도</b>면 옛 나라 도시가 다 넘어간다.
    /// </summary>
    /// <remarks>
    /// 수도는 나라 레코드(<c>0x005859C0</c> + 나라 x 16)의 <c>+0x00</c> 이다. 놀이 중에 이 칸을 바꾸는 곳을 못 찾아
    /// 나라 표의 수도(<see cref="NationTable.Nation.Capital"/>)로 본다.
    ///
    /// 마을 공략에 이겼을 때(<c>0x00468AC0</c>)도 같은 셈이다 — 그쪽은 도시 레코드 <c>+0x04</c> 에
    /// 비트 2 를 함께 세우지만(<c>0x00468B28</c>) 그 비트를 읽는 곳은 못 찾았다.
    /// </remarks>
    public static void ChangeNation(Player player, CityExeTable? cities, NationTable? nations, int city, int nation)
    {
        int old = cities?.NationOf(city) ?? -1;
        if (old >= 0 && nations?.Find(old) is { } was && was.Capital == city && cities != null)
        {
            for (int c = 0; c < CityExeTable.Count; c++)
                if (cities.NationOf(c) == old) player.SetHistoryNation(c, nation);
            return;
        }
        player.SetHistoryNation(city, nation);
    }

    /// <summary>
    /// 도시 건물 낱말(<c>+0x1C</c>)의 한 비트를 켜거나 끈다(<c>0x0040A118</c> · <c>0x00409E0F</c>).
    /// 발견 대본의 <c>22/26 10</c> 도 같은 핸들러다.
    /// </summary>
    public static void SetBuilding(Player player, CityExeTable? cities, int city, int bit, bool on)
    {
        if (cities == null || city < 0 || city >= CityExeTable.Count || bit is < 0 or >= 16) return;
        int word = player.CityBuildings.TryGetValue(city, out int had) ? had
                   : Math.Max(0, cities.StartBuildingsOf(city));
        word = on ? word | 1 << bit : word & ~(1 << bit);
        player.SetCityBuildings(city, word);
    }

    /// <summary>사람 대본 한 칸의 몸통 끝 — 그보다 뒤에서 시작하는 가장 가까운 몸통.</summary>
    private static int BodyEnd(byte[] part, int body)
    {
        int end = part.Length;
        int slots = U16(part, 2);
        for (int s = 0; s < slots && 4 + s * 4 + 4 <= part.Length; s++)
        {
            int other = U16(part, 4 + s * 4 + 2) + 4;
            if (other > body && other < end) end = other;
        }
        return end;
    }

    private static int _standingKey = -1;
    private static HashSet<int> _standing = [];

    /// <summary>그 달에 그 도시가 서 있는지 — 한 달 안에서 여러 번 묻기에 그 달 셈을 들고 있는다.</summary>
    private static bool Standing(int city, DateTime when, Player player)
    {
        if (player.ScriptedCities.TryGetValue(city, out bool set)) return set;
        if (!CityFounding.Hidden.Contains(city)) return true;
        int key = MonthKey(when.Year, when.Month);
        if (key != _standingKey)
        {
            _standing = CityFounding.FoundedBy(when);
            _standingKey = key;
        }
        return _standing.Contains(city);
    }

    private static int U16(byte[] data, int at) => data[at] | (data[at + 1] << 8);
}
