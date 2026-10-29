using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// MALE.CDS · FEMALE.CDS — 인물 초상화. 대사 창에 얼굴을 띄울 때 쓴다.
/// </summary>
/// <remarks>
/// 도시 그림과 같은 LS12 아카이브인데 훨씬 단순하다 — 파트 하나가 곧 얼굴 한 장이고,
/// 모두 정확히 7680바이트다.
/// <code>
///   파트 하나  7680바이트 = 80 x 96, 8bpp 색인, 위에서 아래로
///   MALE.CDS   414장 · FEMALE.CDS 144장
/// </code>
/// <b>제 팔레트가 없다.</b> 쓰이는 색인이 0~73 뿐이라 <see cref="GamePalette"/> 공용 색표만으로
/// 다 그려진다 — 그래서 도시 그림과 달리 팔레트 파트가 붙어 있지 않다.
///
/// 어느 얼굴을 쓰는지는 인물 표에 적혀 있다(<see cref="SponsorTable"/>).
///
/// <b>우리가 들고 있는 벌을 먼저 본다.</b> 얼굴은 게임 내내 쓰이는 것이라 게임 폴더가
/// 잡혀 있어야만 나오면 곤란하다 — 부하 인물정보처럼 우리 세이브만으로 서야 하는 자리도
/// 있다. 그 벌이 어디 사는지는 <see cref="PortraitStore"/> 가 안다. 없을 때에만 게임
/// 폴더로 물러선다.
///
/// <b>줄이지 않고 넣은 얼굴</b>(<see cref="PortraitStore.HdPathOf"/>)도 여기서 건다. 얼굴은 게임 곳곳에
/// 80x96 색 배열로 넘겨지므로 그 틀은 그대로 두고, 배열을 낼 때 본디 그림을 <b>그 배열에 짝지어</b> 둔다.
/// 그리는 쪽은 배열로 그림을 만들 때 <see cref="Bitmap"/> 을 거치기만 하면 된다 — 짝이 있으면 그 그림이,
/// 없으면 배열 그대로의 80x96 이 나온다.
/// </remarks>
public sealed class Portraits
{
    public const int Width = 80;
    public const int Height = 96;

    /// <summary>
    /// 원본이 들고 나온 남자 얼굴 수. 이 뒤는 <b>우리가 더 넣은 것</b>이다.
    /// </summary>
    /// <remarks>
    /// 손 안 댄 <c>MALE.CDS</c> 의 파트 수다(<c>FEMALE.CDS</c> 는 144장). 주인공 얼굴을
    /// 고르는 자리가 「원본 것 앞의 열여섯」과 「더 넣은 것」을 가르는 데 쓴다.
    /// </remarks>
    public const int GameMaleCount = 414;
    private const int Pixels = Width * Height;

    private readonly Ls12Reader _male;
    private readonly Ls12Reader _female;

    private Portraits(Ls12Reader male, Ls12Reader female)
    {
        _male = male;
        _female = female;
    }

    /// <summary>왜 못 열었는지. 잘 열렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>남자 얼굴 장수.</summary>
    public int MaleCount => _male.PartCount;

    /// <summary>여자 얼굴 장수.</summary>
    public int FemaleCount => _female.PartCount;

    /// <summary>
    /// 초상화 두 벌을 연다. 우리 벌(<see cref="PortraitStore"/>)을 먼저 보고 없으면
    /// <paramref name="gameDirectory"/> 로 물러선다. 하나라도 못 구하면 null.
    /// </summary>
    /// <param name="gameDirectory">게임 폴더. 몰라도 되므로 비워 둘 수 있다.</param>
    public static Portraits? Open(string gameDirectory = "")
    {
        LastError = "";
        var male = OpenOne(gameDirectory, female: false);
        if (male == null) return null;
        var woman = OpenOne(gameDirectory, female: true);
        if (woman == null) return null;
        return new Portraits(male, woman);
    }

    private static Ls12Reader? OpenOne(string gameDirectory, bool female)
    {
        string file = PortraitStore.NameOf(female);

        // 아직 꺼내 놓지 않았으면 여기서 꺼내 놓는다.
        var path = PortraitStore.PathOf(female);
        if (path.Length == 0 || !File.Exists(path))
        {
            if (gameDirectory.Length == 0)
            {
                LastError = PortraitStore.LastError.Length > 0
                    ? PortraitStore.LastError : $"{file} 이(가) 없습니다";
                return null;
            }
            path = Path.Combine(gameDirectory, file);
            if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }
        }

        var archive = Ls12Reader.Open(path);
        if (archive == null) { LastError = $"{path} 를 읽지 못했습니다"; return null; }
        if (archive.PartCount == 0) { LastError = $"{file} 에 얼굴이 없습니다"; return null; }
        return archive;
    }

    /// <summary>
    /// 얼굴 한 장을 80x96 BGRA 로 푼다. 없는 번호이거나 못 풀면 null.
    /// </summary>
    /// <param name="face">얼굴 번호(인물 표의 값).</param>
    /// <param name="female">여자 얼굴이면 true.</param>
    public uint[]? TryGetBgra(int face, bool female)
    {
        if (face >= ShippedBase) return Shipped(face, female);

        var archive = female ? _female : _male;
        if (face < 0 || face >= archive.PartCount) return null;

        var idx = archive.Decode(face);
        if (idx == null || idx.Length < Pixels) return null;

        var bgra = new uint[Pixels];
        for (int i = 0; i < Pixels; i++)
        {
            int c = idx[i] * 3;
            bgra[i] = (uint)(0xFF << 24 | GamePalette.Rgb[c] << 16
                             | GamePalette.Rgb[c + 1] << 8 | GamePalette.Rgb[c + 2]);
        }

        // 줄이지 않고 넣은 그림이 있으면 이 배열에 짝지어 둔다 — Bitmap 이 그것을 건다.
        if (Hd(face, female) is { } hd) HdOf.AddOrUpdate(bgra, hd);
        return bgra;
    }

    // ── 앱에 실어 보내는 얼굴 ──────────────────────────────────────────

    /// <summary>
    /// 이 번호부터는 초상화 벌이 아니라 <b>앱에 실린 그림</b>(<c>asset/face/male-9001.png</c>)에서 읽는다.
    /// </summary>
    /// <remarks>
    /// 「초상화 넣기」로 넣은 얼굴은 그 PC 의 초상화 벌(<see cref="PortraitStore"/>)에만 든다 — 번호도 PC 마다 달라
    /// 앱에 실어 보내는 시나리오(<see cref="Engine.Scenarios"/>)의 주인공 얼굴로는 못 쓴다. 그래서 실어 보낼 얼굴은
    /// 벌 밖의 큰 번호를 주고 그림 파일로 따로 싣는다. 그림은 줄이지 않은 것을 그대로 건다(<see cref="Bitmap"/>).
    /// </remarks>
    public const int ShippedBase = 9000;

    /// <summary>
    /// 여급이 아닌 아내의 얼굴 — 개발 창 「맞는다」 · 대본으로 맞은 아내(카타리나)는 여급 표에 없어 얼굴을 못 찾았다.
    /// 카타리나 폰 보라(크라나흐, 1526 · 퍼블릭 도메인)를 <c>asset/face/female-9001.png</c> 로 싣는다.
    /// </summary>
    public const int NamedWifeFace = ShippedBase + 1;

    /// <summary>
    /// 요한 테첼(도미니코회 면벌부 설교자) — 루터 시나리오의 대본이 화자 「#9002」로 세운다.
    /// 사용자가 그린 초상화를 얼굴 위주로 잘라 <c>asset/face/male-9002.png</c> 로 싣는다.
    /// </summary>
    public const int TetzelFace = ShippedBase + 2;

    /// <summary>
    /// 메카의 순례자 — 카바 신전 발견 대본(DISEV 63)이 화자 「#9004」로 세운다. 원본 남자 초상화 316(베이지 터번)을
    /// 다섯 배로 키워 <c>asset/face/male-9004.png</c> 로 싣는다.
    /// </summary>
    public const int PilgrimFace = ShippedBase + 4;

    private static readonly Dictionary<(bool Female, int Face), (uint[] Bgra, BitmapSource Art)?> ShippedCache = [];

    /// <summary>
    /// 불러온 시나리오가 들고 온 얼굴 그림 자리(<c>%APPDATA%\CdsHelper\face</c>) — 앱을 업데이트해도 남는다.
    /// 같은 번호가 앱에 실린 것보다 먼저다.
    /// </summary>
    public static string UserFaceFolder =>
        UserDataFolder.Combine("face");

    /// <summary>얼굴 그림 파일 이름(<c>male-9002.png</c>).</summary>
    public static string ShippedFileName(bool female, int face) => $"{(female ? "female" : "male")}-{face}.png";

    /// <summary>실어 보낸 얼굴 그림 자리 — 불러온 것(<see cref="UserFaceFolder"/>)이 있으면 그것, 없으면 실행 파일 옆의 <c>asset/face</c> 다.</summary>
    public static string ShippedPathOf(bool female, int face)
    {
        string user = Path.Combine(UserFaceFolder, ShippedFileName(female, face));
        return File.Exists(user) ? user : Path.Combine(AppContext.BaseDirectory, "asset", "face", ShippedFileName(female, face));
    }

    /// <summary>읽어 둔 얼굴 그림을 버린다 — 시나리오를 불러와 그림 파일이 바뀌었을 때.</summary>
    public static void ForgetShipped()
    {
        lock (ShippedCache) ShippedCache.Clear();
    }

    /// <summary>실어 보낸 얼굴 한 장 — 80x96 으로 줄인 색 배열에 본디 그림을 짝지어 낸다. 그림이 없으면 null.</summary>
    private static uint[]? Shipped(int face, bool female)
    {
        (uint[] Bgra, BitmapSource Art)? kept;
        lock (ShippedCache)
        {
            if (!ShippedCache.TryGetValue((female, face), out kept))
                ShippedCache[(female, face)] = kept = LoadShipped(ShippedPathOf(female, face));
        }
        if (kept is not { } found) return null;

        // 부르는 쪽이 배열을 고쳐도 담아 둔 것이 안 상하게 베껴 준다.
        var bgra = (uint[])found.Bgra.Clone();
        HdOf.AddOrUpdate(bgra, found.Art);
        return bgra;
    }

    private static (uint[] Bgra, BitmapSource Art)? LoadShipped(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var file = File.OpenRead(path);
            var art = new BitmapImage();
            art.BeginInit();
            art.CacheOption = BitmapCacheOption.OnLoad;
            art.StreamSource = file;
            art.EndInit();
            art.Freeze();

            // 얼굴을 색 배열로 받는 자리(80x96)에 넘길 줄인 것.
            var small = new TransformedBitmap(art, new System.Windows.Media.ScaleTransform(
                (double)Width / art.PixelWidth, (double)Height / art.PixelHeight));
            var plain = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
            int w = Math.Min(Width, plain.PixelWidth), h = Math.Min(Height, plain.PixelHeight);
            var got = new uint[w * h];
            plain.CopyPixels(new System.Windows.Int32Rect(0, 0, w, h), got, w * 4, 0);

            var bgra = new uint[Pixels];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    bgra[y * Width + x] = got[y * w + x] | 0xFF000000u;
            return (bgra, art);
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException
                                      or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // ── 줄이지 않고 넣은 그림 ──────────────────────────────────────────────────

    /// <summary>낸 색 배열 → 그 얼굴의 본디 그림. 배열이 버려지면 짝도 함께 사라진다.</summary>
    private static readonly ConditionalWeakTable<uint[], BitmapSource> HdOf = new();

    /// <summary>읽어 둔 본디 그림 — 파일이 바뀌면(적힌 때가 다르면) 다시 읽는다.</summary>
    private static readonly Dictionary<(bool Female, int Face), (DateTime Stamp, BitmapSource Art)> HdCache = [];

    /// <summary>그 얼굴의 줄이지 않고 넣은 그림. 없거나 못 읽으면 null.</summary>
    public static BitmapSource? Hd(int face, bool female)
    {
        string path = PortraitStore.HdPathOf(female, face);
        var key = (female, face);
        lock (HdCache)
        {
            if (!File.Exists(path)) { HdCache.Remove(key); return null; }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (HdCache.TryGetValue(key, out var kept) && kept.Stamp == stamp) return kept.Art;

            try
            {
                // 흐름으로 읽는다 — 주소로 읽으면 WPF 가 같은 주소의 옛 그림을 담아 두었다가 도로 내준다.
                using var file = File.OpenRead(path);
                var made = new BitmapImage();
                made.BeginInit();
                made.CacheOption = BitmapCacheOption.OnLoad;
                made.StreamSource = file;
                made.EndInit();
                made.Freeze();
                HdCache[key] = (stamp, made);
                return made;
            }
            catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException
                                          or ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 얼굴 색 배열을 걸 그림으로 만든다. 그 얼굴에 <b>줄이지 않고 넣은 그림</b>이 있으면 그것이 나온다.
    /// </summary>
    /// <remarks>얼굴을 그리는 자리는 모두 이것을 거친다 — 80x96 으로 손수 만들면 본디 그림이 안 걸린다.</remarks>
    public static BitmapSource Bitmap(uint[] bgra)
    {
        if (HdOf.TryGetValue(bgra, out var hd)) return hd;

        var made = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, bgra, Width * 4);
        made.Freeze();
        return made;
    }

    /// <summary>그 그림이 80x96 보다 큰, 줄이지 않고 넣은 얼굴인지.</summary>
    public static bool IsHd(ImageSource? art) => art is BitmapSource { PixelWidth: > Width };

    /// <summary>
    /// 얼굴을 걸 때의 늘이는 결. 큰 그림을 작게 거는 것이라 <b>곱게 줄여야</b> 점이 안 빠진다 —
    /// 80x96 도트 그림은 여느 조각처럼 <see cref="UI.Views.GameUi.SpriteScaling"/> 이다.
    /// </summary>
    public static BitmapScalingMode Scaling(ImageSource? art) =>
        IsHd(art) ? BitmapScalingMode.HighQuality : UI.Views.GameUi.SpriteScaling;
}
