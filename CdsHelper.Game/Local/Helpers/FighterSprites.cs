using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 <c>FIGHTER.CDS</c> — 일기토에서 칼을 겨루는 두 사람의 그림.
/// </summary>
/// <remarks>
/// LS12 아카이브이고 <b>싸움꾼 한 스프라이트셋이 파트 둘</b>을 쓴다.
/// <code>
///   파트 2s     646,272바이트 = 144x136 짜리 <b>서른세 장</b>, 8bpp 색인
///   파트 2s+1      768바이트 = 256색 팔레트, 한 색이 (파랑, 빨강, 초록)
/// </code>
/// <b>스프라이트셋 0 은 제독</b>이고(<c>0x004A8E21</c> 이 파트 0 을 읽는다) 스프라이트셋 1~8 이 상대다 —
/// 상대는 문화권으로 갈리고 파트 번호가 <b>그 두 배</b>다(<c>0x004A8F62</c>).
/// 파트 18 위쪽은 이 표에 안 걸리는 딴 그림이다.
///
/// <b>제 팔레트가 얹히는 자리가 스프라이트셋마다 다르다.</b> 제독은 160 부터, 상대는 203 부터다.
/// 그 아래 색인은 공용 색표(<see cref="GamePalette"/>)에서 온다. 게임도 읽자마자
/// 테두리 색인을 공용 색표 쪽으로 옮긴다 — 제독은 <c>0x9F→0x49</c>, 상대는
/// <c>0xCA→0x49</c> 다(<c>0x004A8E71</c> · <c>0x004A8F9B</c>).
///
/// 바탕은 스프라이트셋마다 색인이 다른데 <b>귀퉁이 한 점이 곧 바탕</b>이라 그것을 비침으로 친다.
/// 게임은 문화권마다 그 색인을 따로 적어 두고 0 으로 바꾼다.
///
/// <b>상대 그림은 이미 왼쪽을 본다</b> — 뒤집을 것이 없다.
/// </remarks>
public sealed class FighterSprites
{
    /// <summary>한 장의 크기(<c>0x004A7B01</c> 의 <c>0x90</c> x <c>0x88</c>).</summary>
    public const int Width = 144, Height = 136;

    /// <summary>한 스프라이트셋의 장수. 표(<see cref="Starts"/>)의 마지막 자리가 여기서 끝난다.</summary>
    public const int Frames = 33;

    /// <summary>상대 스프라이트셋 수(문화권 여덟).</summary>
    public const int Sets = 8;

    private const int Pixels = Width * Height;
    private const int AdmiralBase = 160, FoeBase = 203;
    private const int AdmiralEdge = 0x9F, FoeEdge = 0xCA, EdgeTo = 0x49;

    /// <summary>동작 하나. 표 <c>0x00572A40</c> 의 자리 차례 그대로다.</summary>
    /// <remarks>
    /// <b><see cref="Victory"/>(스프라이트셋 6)는 이겼을 때의 몸짓이다</b> — 손에 쥔 필살
    /// (상단필살 따위)이 아니다. 예전에는 이 여섯 장을 「필살」로 읽고 필살을 쓴 판에
    /// 걸었는데, 그림은 칼을 곧추세우고 서는 자세이고 게임도 판이 끝난 뒤 <b>눈금 17
    /// 부터</b> 이것을 낸다(<c>0x004A8155</c>). 손에 쥔 필살은 여느 찌르는 그림을 그대로
    /// 쓰고 아픈 값만 곱이 된다.
    /// </remarks>
    public enum Move
    {
        HighThrust = 0, MidThrust = 1, LowThrust = 2,
        Jump = 3, Dodge = 4, Crouch = 5,
        Victory = 6, Fall = 7, Idle = 8,
    }

    /// <summary>
    /// 동작마다 첫 장의 번호. 게임은 이것을 <b>바이트 자리</b>로 적어 둔다
    /// (<c>0x00572A40</c>: 0 · 0xE580 · 0x1CB00 …). 한 장이 <c>0x4C80</c> 바이트라
    /// 그것을 나눈 값이 여기 있다.
    /// </summary>
    public static readonly int[] Starts = [0, 3, 6, 9, 12, 15, 18, 24, 30];

    /// <summary>동작마다의 장수. 승리와 쓰러짐만 여섯 장이다.</summary>
    public static readonly int[] Lengths = [3, 3, 3, 3, 3, 3, 6, 6, 3];

    /// <summary>그 동작의 <paramref name="step"/> 번째 장. 넘치면 마지막 장이다.</summary>
    public static int FrameOf(Move move, int step)
    {
        int at = (int)move;
        return Starts[at] + Math.Clamp(step, 0, Lengths[at] - 1);
    }

    /// <summary>
    /// 그 문화권이 쓰는 상대 스프라이트셋(1~8). 게임 <c>0x004A88EA</c> 의 갈래 그대로다.
    /// </summary>
    /// <remarks>
    /// 스프라이트셋 이름은 <c>0x00534420</c> 에 적혀 있다 — 1 유럽 · 2 아프리카 · 3 아랍 ·
    /// 4 아시아A · 5 아시아Ｂ · 6 중국 · 7 일본 · 8 아즈텍.
    /// </remarks>
    public static int SetForCulture(int culture) => culture switch
    {
        3 => 2,             // 아프리카
        4 or 5 => 3,        // 이슬람 · 인도 → 아랍
        6 => 6,             // 중국
        7 => 4,             // 중앙아시아 → 아시아A
        8 => 5,             // 동남아시아 → 아시아Ｂ
        9 => 7,             // 일본
        10 => 8,            // 아메리카 → 아즈텍
        _ => 1,             // 이베리아 · 북유럽 · 지중해 → 유럽
    };

    private readonly Ls12Reader _archive;
    private readonly Dictionary<int, uint[]?> _sheets = [];

    private FighterSprites(Ls12Reader archive) => _archive = archive;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더에서 연다. 없거나 형식이 아니면 null.</summary>
    public static FighterSprites? Open(string gameDirectory)
    {
        LastError = "";
        string path = CdsAssetPath.Resolve(gameDirectory, "FIGHTER.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < (Sets + 1) * 2) { LastError = "FIGHTER.CDS 에 스프라이트셋이 모자랍니다"; return null; }
        return new FighterSprites(archive);
    }

    /// <summary>
    /// 한 스프라이트셋을 통째로 푼다(서른세 장이 잇달아 있는 BGRA). 못 풀면 null.
    /// </summary>
    /// <param name="set">0 이면 제독, 1~8 이면 그 상대 스프라이트셋.</param>
    private uint[]? Sheet(int set)
    {
        if (_sheets.TryGetValue(set, out var kept)) return kept;

        uint[]? made = null;
        int part = set * 2;
        var idx = _archive.Decode(part);
        var pal = _archive.Decode(part + 1);

        if (idx != null && pal != null && idx.Length >= Pixels * Frames && pal.Length >= 3)
        {
            int baseIndex = set == 0 ? AdmiralBase : FoeBase;
            int edge = set == 0 ? AdmiralEdge : FoeEdge;
            byte clear = idx[0];                 // 귀퉁이 한 점이 바탕이다

            made = new uint[Pixels * Frames];
            for (int i = 0; i < made.Length; i++)
            {
                byte v = idx[i];
                if (v == clear) { made[i] = 0; continue; }   // 비침
                if (v == edge) v = EdgeTo;                   // 테두리는 공용 색표로 옮긴다

                byte r, g, b;
                int k = (v - baseIndex) * 3;
                if (v >= baseIndex && k + 2 < pal.Length)
                {
                    b = pal[k];                              // (파랑, 빨강, 초록) 차례다
                    r = pal[k + 1];
                    g = pal[k + 2];
                }
                else
                {
                    r = GamePalette.Rgb[v * 3];
                    g = GamePalette.Rgb[v * 3 + 1];
                    b = GamePalette.Rgb[v * 3 + 2];
                }
                made[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }

        _sheets[set] = made;
        return made;
    }

    /// <summary>그 스프라이트셋의 그 장을 BGRA 로 낸다. 못 풀면 null.</summary>
    public uint[]? TryGetBgra(int set, int frame)
    {
        if (set < 0 || set > Sets || frame < 0 || frame >= Frames) return null;
        if (Sheet(set) is not { } sheet) return null;

        var one = new uint[Pixels];
        Array.Copy(sheet, frame * Pixels, one, 0, Pixels);
        return one;
    }
}
