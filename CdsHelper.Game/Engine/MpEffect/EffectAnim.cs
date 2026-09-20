using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 MPEFFECT.CDS — 화면에 겹쳐 도는 <b>동그란 애니메이션</b> 여섯 종.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>22.분석-애니메이션(MPEFFECT·EVANIME)</c> 에 있다.
/// <code>
///   파트 = 애니 x 4 + 프레임(0~3)
///   한 장 6,400바이트 = 80 x 80 8bpp 색인
/// </code>
/// 팔레트 파트가 따로 없다 — 쓰는 색인이 10~74 안이라 <see cref="GamePalette"/> 공용 색표로
/// 다 풀린다. 동그란 테두리도 그림에 같이 그려져 있고, <b>원 바깥은 색인 74</b> 다(비침).
///
/// <list type="table">
///   <item><term>0</term><description>짐 싣기</description></item>
///   <item><term>1</term><description>책상의 서기</description></item>
///   <item><term>2</term><description>대포 발사</description></item>
///   <item><term>3</term><description>하트</description></item>
///   <item><term>4</term><description>동전 던지기</description></item>
///   <item><term>5</term><description>설득 — 무릎 꿇고 청하다가 엎어진다</description></item>
/// </list>
/// </remarks>
public sealed class EffectAnim
{
    /// <summary>
    /// 짐 싣기(파트 0~3). 껍데기는 <c>0x004A6120</c> 이다.
    /// </summary>
    /// <remarks>
    /// 이름은 그림에서 붙였는데, <b>적대 도시에서 들킨 뒤 달아나는 굴림</b>도 이 벌을 쓴다
    /// (<c>0x004A5419</c>). 게임이 벌을 아껴 쓴 자리다.
    /// </remarks>
    public const int Load = 0;

    /// <summary>한 장의 한 변. 게임이 늘 이 크기로 그린다.</summary>
    public const int Size = 80;
    private const int Pixels = Size * Size;

    /// <summary>애니 하나에 든 프레임 수.</summary>
    public const int FrameCount = 4;

    /// <summary>설득 애니메이션 번호. 후원자 건물의 명성 관문에서 돈다.</summary>
    /// <summary>대포 발사(파트 8~11). 자택 "후손을 남긴다" 가 이 벌을 돌린다.</summary>
    /// <remarks>
    /// 게임의 껍데기가 <c>0x004A6340</c> 이고, 인자가 1 이면 소리 <c>0x2A</c>, 아니면
    /// <c>0x2B</c> 를 함께 낸다 — 여섯 벌 가운데 소리를 넘기는 것은 이것뿐이다.
    /// </remarks>
    public const int Cannon = 2;

    /// <summary>
    /// 하트(파트 12~15) — 커졌다가 깨진다. 껍데기는 <c>0x004A6360</c> 이고 소리는 없다.
    /// </summary>
    /// <remarks>
    /// 후원자를 설득할 때 <b>마음이 동하는지</b>를 이 벌로 낸다 — <c>0x004AE7B7</c> 과
    /// <c>0x004AE815</c> 가 굴림 결과를 그대로 넘긴다.
    /// </remarks>
    public const int Heart = 3;

    /// <summary>
    /// 동전 던지기(파트 16~19) — 팽팽 돌다가 멎는다. 껍데기는 <c>0x004A6380</c> 이다.
    /// </summary>
    /// <remarks>
    /// 적대 도시에 <b>잠입</b>할 때 이 벌이 돈다(<c>0x004A53D0</c>). 굴림을 하고 나서
    /// 부르므로 멎은 쪽이 곧 결과다.
    /// </remarks>
    public const int Coin = 4;

    public const int Persuade = 5;

    // ── 장을 넘기는 차례 ───────────────────────────────────────────────────────

    /// <summary>
    /// 벌마다 장을 넘기는 셈이 다르다 — <c>0x004A5F37</c> 의 갈래표가 셋으로 나눈다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   갈래 0 (짐 싣기 · 서기 · 하트 · 설득)  0x004A5CB0   장 간격 2
    ///   갈래 1 (대포)                          0x004A5D20   장 간격 10
    ///   갈래 2 (동전)                          0x004A5D80   장 간격 1
    /// </code>
    /// 「장 간격」은 <c>[객체+0xD0]</c> 이고, 걸음이 그만큼 쌓여야 한 장이 넘어간다
    /// (<c>0x004A5DDE</c>). 그래서 <b>동전이 하트보다 두 배 빠르다</b>.
    /// </remarks>
    public const int HeartStep = 2, CoinStep = 1, CannonStep = 10;

    /// <summary>그 벌의 장 간격. 갈래표(<c>0x004A6100</c>)가 정한다.</summary>
    public static int StepOf(int anim) =>
        anim == Cannon ? CannonStep : anim == Coin ? CoinStep : HeartStep;

    /// <summary>그 벌이 넘어가는 차례. 동전만 제 셈이고 나머지는 갈래 0 을 쓴다.</summary>
    public static int[] Frames(int anim, bool won) =>
        anim == Coin ? CoinFrames(won) : HeartFrames(won);

    /// <summary>
    /// 갈래 0 — 짐 싣기 · 서기 · 하트 · 설득이 넘어가는 차례(<c>0x004A5CB0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   걸음 0~5   0 과 1 을 번갈아 낸다(0 에서 시작하므로 1 부터 나온다)
    ///   걸음 6~7   장을 안 바꾸고 그대로 둔다
    ///   걸음 8     결말 장 — 되면 2, 어그러지면 3 (0x004A5CF9)
    ///   걸음 9     그대로 두고, 10 에서 끝난다
    /// </code>
    /// </remarks>
    public static int[] HeartFrames(bool won) =>
        [1, 0, 1, 0, 1, 0, 0, 0, won ? 2 : 3, won ? 2 : 3];

    /// <summary>
    /// 동전이 넘어가는 차례(<c>0x004A5D80</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   004a5d8c  cmp [객체+0xBC], 1      ; 굴림 결과
    ///   004a5d92  sbb edx,edx; add edx,0x15  ; 되면 21, 어그러지면 20 걸음까지 돈다
    ///   004a5da0  and eax, 3             ; 그리는 장 = 걸음 % 4
    ///   004a5db9  add edx, 2             ; 그 뒤 두 걸음은 장을 안 바꾼다
    /// </code>
    /// 마지막으로 돈 걸음이 되면 20(<c>20 % 4 = 0</c>), 어그러지면 19(<c>19 % 4 = 3</c>)라
    /// <b>멎는 장이 갈린다</b> — 되면 첫째 장, 어그러지면 넷째 장에서 멎는다.
    /// </remarks>
    public static int[] CoinFrames(bool won)
    {
        int spins = won ? 21 : 20;                    // 0x15 / 0x14
        var order = new int[spins + 2];               // 뒤 두 걸음은 멎은 장 그대로
        for (int step = 0; step < spins; step++) order[step] = step % FrameCount;
        order[spins] = order[spins + 1] = order[spins - 1];
        return order;
    }

    /// <summary>원 바깥(비침) 색인.</summary>
    private const byte Transparent = 74;

    /// <summary>파일 이름이 소문자다 — 대문자로 찾으면 못 찾는다.</summary>
    private const string FileName = "MPEffect.cds";

    private readonly Ls12Reader _archive;

    private EffectAnim(Ls12Reader archive) => _archive = archive;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 MPEFFECT.CDS 를 연다. 없거나 모양이 다르면 null.</summary>
    public static EffectAnim? Open(string gameDirectory)
    {
        LastError = "";

        // 게임 폴더는 대소문자를 안 가리지만(NTFS) 이름을 그대로 적어 둔다.
        var path = CdsAssetPath.Resolve(gameDirectory, "MPEFFECT.CDS");
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < 6 * FrameCount)
        {
            LastError = "MPEFFECT.CDS 에 애니메이션이 모자랍니다";
            return null;
        }
        return new EffectAnim(archive);
    }

    /// <summary>
    /// 한 프레임을 80x80 BGRA 로 푼다. 원 바깥은 알파 0 이다. 못 풀면 null.
    /// </summary>
    public uint[]? TryGetBgra(int anim, int frame)
    {
        if (anim < 0 || frame < 0 || frame >= FrameCount) return null;

        var idx = _archive.Decode(anim * FrameCount + frame);
        if (idx == null || idx.Length < Pixels) return null;

        var bgra = new uint[Pixels];
        for (int i = 0; i < Pixels; i++)
        {
            byte v = idx[i];
            if (v == Transparent) continue;
            int k = v * 3;
            bgra[i] = (uint)(0xFF << 24 | GamePalette.Rgb[k] << 16
                             | GamePalette.Rgb[k + 1] << 8 | GamePalette.Rgb[k + 2]);
        }
        return bgra;
    }
}
