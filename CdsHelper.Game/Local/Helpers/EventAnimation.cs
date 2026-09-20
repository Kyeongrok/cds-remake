using System.IO;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임 폴더의 EVANIME.CDS — 지도 위에 통째로 겹쳐 도는 <b>사건 애니메이션</b>.
/// </summary>
/// <remarks>
/// 자세한 것은 볼트 <c>애니메이션/91.분석-이벤트 애니메이션 장면(EVANIME 재생)</c> 에 있다.
/// 장면을 여는 손은 <c>0x0048E820(장면)</c> 이고, 지도 창이 0.1초마다(<c>0x0048B120</c>)
/// <c>0x0049ABB0(장면, 걸음)</c> 을 불러 한 걸음씩 그린다. 장면마다 점프표(<c>0x0049B2A8</c>)가
/// 객체 하나를 두고, 그 객체가 처음 불릴 때 그림을 읽는다.
/// <code>
///   0x0049A210(버퍼, 폭, 총높이, 그림파트, 팔레트파트)
///     그림파트가 0·1 이 아니면 팔레트파트를 160번부터 86색 얹고(0x004BA161(0xA0, 0x56))
///     색인을 0x0041F990(…, 0xA0, 0) 으로 옮긴다 — 0 은 0 그대로(비침), 나머지는 +160
///     그림파트가 0·1 이면 팔레트 없이 +10, 비침은 0x40
/// </code>
/// 그림 파트는 <b>프레임을 세로로 쌓은 긴 띠</b>다. 비침은 DirectDraw 색 열쇠 0 이다
/// (<c>0x0049A700</c> 이 <c>DDBLT_KEYSRCOVERRIDE</c>, 열쇠 0 으로 뿌린다).
/// 팔레트의 한 색은 (파랑, 빨강, 초록) 순이다 — <see cref="CityPictures"/> 와 같다.
///
/// <list type="table">
///   <item><term>2 폭풍</term><description>파트 3 (320x240 x 11) · 빗방울 파트 2 (32x32) · 팔레트 32 · 소리 0x40</description></item>
///   <item><term>3 눈보라</term><description>파트 5 (240x320) · 눈송이 파트 4 (16x16 x 3) · 팔레트 33 · 소리 0x41</description></item>
///   <item><term>8 덤불</term><description>파트 10 (96x64 x 15) · 팔레트 38 — 짐승·독충</description></item>
///   <item><term>11 오로라</term><description>파트 18 (640x192 x 22) · 팔레트 44 · 소리 0x3A · 곡 끊음</description></item>
///   <item><term>13 회오리</term><description>파트 17 (96x128 x 8) · 팔레트 43 · 소리 0x42</description></item>
/// </list>
/// </remarks>
public sealed class EventAnimation
{
    /// <summary>폭풍 장면 — 바다에서 폭풍을 맞으면(<c>0x00474D4D</c>, 갈래 2).</summary>
    public const int Storm = 2;

    /// <summary>눈보라 장면 — 같은 자리에서 갈래 3 이면.</summary>
    public const int Blizzard = 3;

    /// <summary>
    /// 덤불에서 눈이 번뜩이는 장면 — 뭍에서 <b>독충</b>(<c>0x00427866</c>)과 <b>짐승</b>
    /// (<c>0x00427B4C</c>)이 같이 쓴다. 함대 그림 자리에 선다.
    /// </summary>
    public const int Bush = 8;

    /// <summary>회오리 장면 — 뭍에서 회오리를 맞으면(<c>0x00427E4A</c>).</summary>
    public const int Tornado = 13;

    /// <summary>
    /// 오아시스·사태·늪·유사·유빙 다섯. 함대 자리에서 띠 한 벌이 돈다.
    /// </summary>
    /// <remarks>
    /// 그림 파트·크기·팔레트는 EXE 그대로다 —
    /// 4 파트 6(128x128 x14, 팔레트 0x22) · 5 파트 7(128x128 x15, 0x23) ·
    /// 6 파트 8(96x96 x18, 0x24) · 7 파트 9(96x96 x17, 0x25) · 14 파트 15(192x96 x4, 0x2A).
    /// 걸음마다 어느 장을 쓰는지도 옮겼다 — <b>유빙만 빼고</b>다(부딪히고 나서 흔들리는 갈래는
    /// 우리 쪽에 부딪히는 자리가 없다).
    /// </remarks>
    /// <remarks>
    /// 번호는 EVANIME 만들기 오류 문구 차례 그대로다(<c>0x0056C058</c>~) —
    /// 0 비 · 1 눈 · 2 폭풍 · 3 눈보라 · <b>4 오아시스</b> · <b>5 사태</b> · <b>6 늪</b> ·
    /// <b>7 유사</b> · 8 짐승의 그림자 · 9 유령선 · 10 일식 · 11 오로라 · 12 유성군 ·
    /// 13 회오리 바람 · <b>14 유빙(대)</b> · 15 유빙(소).
    /// </remarks>
    public const int Oasis = 4, Landslide = 5, Swamp = 6, Quicksand = 7, Iceberg = 14;

    /// <summary>
    /// 유성 장면 — 뭍에서 8월·12월에 유성이 흐르면(<c>0x00427D59</c>). EVANIME 파트 19(368x192 스물여섯 장)다.
    /// </summary>
    public const int Meteor = 12;

    /// <summary>
    /// 오로라 장면 — 발견 대본의 <c>00 1E 04</c>(특수 조우 4)가 부른다(<c>0x0061D280</c>).
    /// </summary>
    /// <remarks>
    /// 발견물 <b>194 오로라</b>의 DISEV 파트가 「굉장하다! 제독, 위를 보십시오!」 바로 뒤에
    /// 이것을 건다. 지도를 <b>깜깜하게 덮고</b> 밤하늘에 빛의 장막을 펼친 뒤 도로 밝힌다.
    /// </remarks>
    public const int Aurora = 11;

    /// <summary>얹는 팔레트 색 수(<c>0x56</c>).</summary>
    private const int PaletteColors = 0x56;

    /// <summary>파일 이름. 게임은 <c>"C:EvAnime.CDS"</c>(<c>0x0056E2A8</c>)로 연다.</summary>
    private const string FileName = "EvAnime.CDS";

    /// <summary>파일에 든 파트 수. 그림 31개 뒤에 팔레트가 32~51 에 있다.</summary>
    private const int PartTotal = 52;

    /// <summary>
    /// 프레임을 세로로 쌓은 띠 하나. <see cref="Bgra"/> 는 띠 전체(폭 x 프레임높이 x 장수)다.
    /// </summary>
    public sealed record Strip(int Width, int FrameHeight, int Count, uint[] Bgra);

    private readonly Ls12Reader _archive;

    private EventAnimation(Ls12Reader archive) => _archive = archive;

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>게임 폴더의 EVANIME.CDS 를 연다. 없거나 모양이 다르면 null.</summary>
    public static EventAnimation? Open(string gameDirectory)
    {
        LastError = "";

        var path = CdsAssetPath.Resolve(gameDirectory, FileName);
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount < PartTotal)
        {
            LastError = "EVANIME.CDS 에 파트가 모자랍니다";
            return null;
        }
        return new EventAnimation(archive);
    }

    /// <summary>
    /// 그림 파트 하나를 띠째로 BGRA 로 푼다. 색인 0 은 알파 0 이다. 못 풀면 null.
    /// </summary>
    /// <param name="part">그림 파트.</param>
    /// <param name="width">띠 폭. 게임이 <c>0x0049A210</c> 에 넘기는 값이다.</param>
    /// <param name="frameHeight">한 프레임 높이. 그리는 손(<c>0x0049A700</c>)에 넘기는 값이다.</param>
    /// <param name="palettePart">팔레트 파트.</param>
    /// <summary>
    /// 비·눈 조각(파트 0·1)을 푼다 — <b>팔레트가 없다</b>. 색인에 10 을 더해 공용 팔레트로 칠하고
    /// 원래 색인 0x40 이 비침이다(<c>0x0049A374</c> 의 <c>0x0041F990</c>).
    /// </summary>
    public Strip? TryGetWeather(int part, int width, int frameHeight)
    {
        var idx = _archive.Decode(part);
        if (idx == null || width <= 0 || frameHeight <= 0 || idx.Length < width * frameHeight) return null;
        int count = idx.Length / width / frameHeight;
        var bgra = new uint[count * width * frameHeight];
        for (int i = 0; i < bgra.Length; i++)
        {
            int v = idx[i];
            if (v == WeatherKey) continue;
            int c = Math.Min(255, v + WeatherShift) * 3;
            bgra[i] = 0xFF000000u | ((uint)GamePalette.Rgb[c] << 16) | ((uint)GamePalette.Rgb[c + 1] << 8)
                      | GamePalette.Rgb[c + 2];
        }
        return new Strip(width, frameHeight, count, bgra);
    }

    /// <summary>비·눈 조각의 비침 색인과 색인 밀기.</summary>
    private const int WeatherKey = 0x40, WeatherShift = 10;

    /// <summary>비 장면 번호(파트 0, 32x32 한 장)와 눈 장면 번호(파트 1, 16x16 세 장).</summary>
    public const int Rain = 0, Snow = 1;

    public Strip? TryGetStrip(int part, int width, int frameHeight, int palettePart)
    {
        if (width <= 0 || frameHeight <= 0) return null;

        var idx = _archive.Decode(part);
        if (idx == null || idx.Length < width * frameHeight) return null;
        var pal = _archive.Decode(palettePart);
        if (pal == null || pal.Length < 3) return null;

        int count = idx.Length / width / frameHeight;
        int pixels = count * width * frameHeight;
        int palLen = Math.Min(pal.Length, PaletteColors * 3);

        var bgra = new uint[pixels];
        for (int i = 0; i < pixels; i++)
        {
            byte v = idx[i];
            if (v == 0) continue;                         // 색 열쇠 — 지도가 비친다
            int k = v * 3;
            if (k + 2 >= palLen) continue;
            byte b = pal[k], r = pal[k + 1], g = pal[k + 2];   // (파랑, 빨강, 초록) 순
            bgra[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        return new Strip(width, frameHeight, count, bgra);
    }
}
