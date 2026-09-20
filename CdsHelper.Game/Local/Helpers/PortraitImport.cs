using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 바깥 그림 한 장을 게임 초상화(<b>80x96 · 256색</b>)로 옮겨 넣는다.
/// </summary>
/// <remarks>
/// 두 가지를 한다.
/// <list type="number">
///   <item><b>네모에 맞춘다.</b> 비율이 다르면 <see cref="Fit.Cover"/> 로 <b>넘치게</b>
///   키운 뒤 넘어가는 만큼 잘라 낸다 — 얼굴이 찌그러지지 않는다. 안 자르고 통째로
///   넣고 싶으면 <see cref="Fit.Contain"/> 이다.</item>
///   <item><b>색을 게임 팔레트로 줄인다.</b> 게임 초상화는 색인 그림이라
///   <see cref="GamePalette"/> 의 256색 가운데 가장 가까운 것을 고른다.</item>
/// </list>
/// 어디를 얼마나 크게 넣을지는 <see cref="Crop"/> 이 들고 있다 — 창에서 끌고
/// 굴려 고른 그대로다.
///
/// 넣는 데는 <see cref="PortraitStore"/> 가 들고 있는 우리 벌이다 —
/// <see cref="Portraits.Open"/> 이 게임 폴더보다 이 벌을 먼저 보므로, 게임 파일은
/// 안 건드리고도 앱에 바로 먹는다.
/// </remarks>
public static class PortraitImport
{
    /// <summary>네모에 맞추는 두 가지 결.</summary>
    public enum Fit
    {
        /// <summary>넘치게 키워 넣고 넘어가는 만큼 <b>잘라 낸다</b>. 비율이 안 찌그러진다.</summary>
        Cover,

        /// <summary>통째로 들어가게 <b>줄이고</b> 남는 자리는 가장자리 색으로 메운다.</summary>
        Contain,
    }

    /// <summary>왜 못 했는지. 잘 됐으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 원본 그림에서 <b>어디를 얼마나 크게</b> 떠 올지.
    /// </summary>
    /// <remarks>
    /// <paramref name="Zoom"/> 은 원본 점 하나가 초상화에서 몇 점이 되는지고,
    /// <paramref name="CenterX"/>·<paramref name="CenterY"/> 는 80x96 네모 한가운데에
    /// 놓일 <b>원본 점 자리</b>다. 창에서 끌면 가운데가 움직이고 굴리면 배수가 바뀐다.
    /// </remarks>
    public readonly record struct Crop(double Zoom, double CenterX, double CenterY)
    {
        /// <summary>그림 크기에 <paramref name="fit"/> 을 적용한 첫 자리 — 가운데를 문다.</summary>
        public static Crop For(int width, int height, Fit fit)
        {
            double sx = (double)Portraits.Width / Math.Max(1, width);
            double sy = (double)Portraits.Height / Math.Max(1, height);
            return new Crop(fit == Fit.Cover ? Math.Max(sx, sy) : Math.Min(sx, sy),
                            width / 2.0, height / 2.0);
        }

        /// <summary>배수를 <paramref name="by"/> 배로 하되 <b>가운데는 그대로</b> 둔다.</summary>
        public Crop Scaled(double by) => this with { Zoom = Zoom * by };

        /// <summary>화면에서 <paramref name="dx"/>·<paramref name="dy"/> 점만큼 끈 만큼 민다.</summary>
        /// <remarks>화면 점을 배수로 나눠야 원본 점이 된다 — 크게 볼수록 조금 움직인다.</remarks>
        public Crop Moved(double dx, double dy) =>
            this with { CenterX = CenterX - dx / Zoom, CenterY = CenterY - dy / Zoom };
    }

    /// <summary>배수를 이 안에 가둔다 — 너무 줄이면 점이 되고 너무 키우면 뭉갠다.</summary>
    public const double MinZoom = 0.05, MaxZoom = 20;

    /// <summary>
    /// 그림 파일 하나를 80x96 BGRA 로 맞춘다. 못 읽으면 null.
    /// </summary>
    /// <param name="path">그림 파일. PNG · JPG · BMP · GIF 를 읽는다.</param>
    /// <param name="fit">네모에 맞추는 결.</param>
    public static BitmapSource? Load(string path)
    {
        LastError = "";
        if (!File.Exists(path)) { LastError = $"{path} 가 없습니다"; return null; }

        try
        {
            var made = new BitmapImage();
            made.BeginInit();
            made.UriSource = new Uri(path);
            made.CacheOption = BitmapCacheOption.OnLoad;
            made.EndInit();
            made.Freeze();
            return made;
        }
        catch (NotSupportedException e) { LastError = $"못 읽는 그림입니다 — {e.Message}"; return null; }
        catch (IOException e) { LastError = e.Message; return null; }
    }

    /// <summary>그림 파일 하나를 80x96 BGRA 로 맞춘다. 못 읽으면 null.</summary>
    /// <param name="path">그림 파일. PNG · JPG · BMP · GIF 를 읽는다.</param>
    /// <param name="fit">네모에 맞추는 결.</param>
    public static uint[]? Shape(string path, Fit fit)
    {
        if (Load(path) is not { } source) return null;
        return Shape(source, Crop.For(source.PixelWidth, source.PixelHeight, fit));
    }

    /// <summary>이미 읽어 둔 그림에서 <paramref name="crop"/> 자리를 80x96 BGRA 로 뜬다.</summary>
    public static uint[] Shape(BitmapSource source, Crop crop)
    {
        int w = Portraits.Width, h = Portraits.Height;

        double zoom = Math.Clamp(crop.Zoom, MinZoom, MaxZoom);
        double drawW = source.PixelWidth * zoom;
        double drawH = source.PixelHeight * zoom;

        // 고른 가운데가 네모 한가운데에 오게 놓는다 — 넘어간 만큼은 밖으로 잘려 나간다.
        var box = new Rect(w / 2.0 - crop.CenterX * zoom,
                           h / 2.0 - crop.CenterY * zoom, drawW, drawH);

        var canvas = new DrawingVisual();
        using (var draw = canvas.RenderOpen())
        {
            // 남는 자리는 검정으로 메운다 — 그림보다 네모가 클 때만 보인다.
            draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
            draw.DrawImage(source, box);
        }

        var shot = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        shot.Render(canvas);

        var bgra = new uint[w * h];
        shot.CopyPixels(bgra, w * 4, 0);
        return bgra;
    }

    /// <summary>
    /// 80x96 BGRA 를 게임 팔레트 색인 7,680바이트로 줄인다.
    /// </summary>
    /// <remarks>
    /// 가장 가까운 색은 <b>제곱 거리</b>로 고른다. 팔레트가 256색뿐이라 한 점에 256번을
    /// 재는데, 7,680점이라 다 해도 이백만 번이 안 된다 — 눈 깜짝할 새다.
    /// </remarks>
    public static byte[] Quantize(uint[] bgra)
    {
        var made = new byte[Portraits.Width * Portraits.Height];
        var rgb = GamePalette.Rgb;
        int colors = Math.Min(256, rgb.Length / 3);

        for (int i = 0; i < made.Length && i < bgra.Length; i++)
        {
            int r = (byte)(bgra[i] >> 16), g = (byte)(bgra[i] >> 8), b = (byte)bgra[i];

            int best = 0, near = int.MaxValue;
            for (int c = 0; c < colors; c++)
            {
                int dr = r - rgb[c * 3], dg = g - rgb[c * 3 + 1], db = b - rgb[c * 3 + 2];
                int far = dr * dr + dg * dg + db * db;
                if (far >= near) continue;
                near = far;
                best = c;
                if (far == 0) break;
            }
            made[i] = (byte)best;
        }
        return made;
    }

    /// <summary>줄여 둔 색인을 다시 BGRA 로 — 넣기 전에 눈으로 보라고 낸다.</summary>
    public static uint[] Preview(byte[] indexed)
    {
        var rgb = GamePalette.Rgb;
        var bgra = new uint[indexed.Length];
        for (int i = 0; i < indexed.Length; i++)
        {
            int c = indexed[i] * 3;
            if (c + 2 >= rgb.Length) continue;
            bgra[i] = (uint)(0xFF << 24 | rgb[c] << 16 | rgb[c + 1] << 8 | rgb[c + 2]);
        }
        return bgra;
    }

    /// <summary>
    /// 고칠 수 있는 초상화 벌이 놓인 자리. 없으면 빈 글.
    /// </summary>
    /// <remarks>
    /// <see cref="PortraitStore"/> 가 <c>%APPDATA%/CdsHelper/asset</c> 에 한 벌만 둔다 —
    /// 빌드 출력이 아니므로 다시 구워도 안 날아가고, 앱 둘이 같은 벌을 본다.
    /// </remarks>
    public static string PathOf(bool female) => PortraitStore.PathOf(female);

    /// <summary>
    /// 그 자리에 초상화를 넣는다. <paramref name="face"/> 가 지금 장수와 같으면 뒤에 붙인다.
    /// </summary>
    /// <returns>넣은 얼굴 번호. 못 넣었으면 −1 이고 까닭은 <see cref="LastError"/> 다.</returns>
    public static int Put(bool female, int face, byte[] indexed)
    {
        LastError = "";

        string path = PathOf(female);
        if (path.Length == 0)
        {
            LastError = PortraitStore.LastError.Length > 0
                ? PortraitStore.LastError
                : $"{PortraitStore.NameOf(female)} 를 열지 못했습니다";
            return -1;
        }

        // 원본은 DLL 안에 박혀 있으므로 .bak 을 남길 까닭이 없다 —
        // 되돌리려면 PortraitStore.Reset 이 이 파일을 다시 꺼내 놓는다.
        if (!Ls12Writer.Put(path, face, indexed))
        {
            LastError = Ls12Writer.LastError;
            return -1;
        }
        return face;
    }

    /// <summary>
    /// <b>맨 뒤</b> 얼굴을 아주 지운다. 잘 됐으면 참.
    /// </summary>
    /// <remarks>
    /// 가운데는 못 지운다. 파트 표에는 번호가 안 적혀 있고 <b>줄 차례가 곧 번호</b>라,
    /// 가운데를 들어내면 그 뒤가 죄다 한 칸씩 당겨진다. 게임 자료는 사람을 얼굴 번호로
    /// 가리키므로(인물표 · 후원자표 · 시설 화자표 <c>0x0056823C</c>) 그러면 엉뚱한
    /// 사람들 얼굴이 한꺼번에 바뀐다. 가운데를 비우려면 <see cref="Blank"/> 다.
    /// </remarks>
    public static bool Remove(bool female, int face)
    {
        LastError = "";
        if (!Ready(female, out string path)) return false;

        int count = Count(female);
        if (face != count - 1)
        {
            LastError = $"맨 뒤({count - 1}번)만 지울 수 있습니다";
            return false;
        }

        if (!Ls12Writer.Remove(path, face)) { LastError = Ls12Writer.LastError; return false; }
        return true;
    }

    /// <summary>
    /// 그 얼굴을 <b>빈 자리 그림</b>으로 갈아 끼운다. 번호는 그대로 남는다.
    /// </summary>
    /// <remarks>
    /// 가운데를 들어내면 뒤 번호가 죄다 밀리므로, 대신 「없음」 그림을 덮어 자리만
    /// 비운다. 장수도 그대로고 그 뒤 사람들 얼굴도 그대로다.
    /// </remarks>
    public static bool Blank(bool female, int face)
    {
        LastError = "";
        if (!Ready(female, out string path)) return false;

        int count = Count(female);
        if (face < 0 || face >= count)
        {
            LastError = $"{face}번은 없습니다(0~{count - 1})";
            return false;
        }

        if (!Ls12Writer.Put(path, face, Quantize(BlankFace())))
        {
            LastError = Ls12Writer.LastError;
            return false;
        }
        return true;
    }

    /// <summary>「없음」을 뜻하는 80x96 그림 — 어두운 바탕에 가위표를 긋는다.</summary>
    public static uint[] BlankFace()
    {
        int w = Portraits.Width, h = Portraits.Height;

        var ground = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));
        var chalk = new Pen(new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)), 2);

        var canvas = new DrawingVisual();
        using (var draw = canvas.RenderOpen())
        {
            draw.DrawRectangle(ground, null, new Rect(0, 0, w, h));
            draw.DrawRectangle(null, chalk, new Rect(2, 2, w - 4, h - 4));
            draw.DrawLine(chalk, new Point(2, 2), new Point(w - 2, h - 2));
            draw.DrawLine(chalk, new Point(w - 2, 2), new Point(2, h - 2));
        }

        var shot = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        shot.Render(canvas);

        var bgra = new uint[w * h];
        shot.CopyPixels(bgra, w * 4, 0);
        return bgra;
    }

    /// <summary>고칠 벌이 열리는지 본다. 열리면 그 자리를 낸다.</summary>
    private static bool Ready(bool female, out string path)
    {
        path = PathOf(female);
        if (path.Length > 0) return true;

        LastError = PortraitStore.LastError.Length > 0
            ? PortraitStore.LastError
            : $"{PortraitStore.NameOf(female)} 를 열지 못했습니다";
        return false;
    }

    /// <summary>그 벌에 든 얼굴 장수.</summary>
    public static int Count(bool female)
    {
        var faces = Portraits.Open();
        return faces == null ? 0 : female ? faces.FemaleCount : faces.MaleCount;
    }
}
