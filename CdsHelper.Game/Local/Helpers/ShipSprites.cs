using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 함대 그림 8방향. 게임이 안 켜져 있어도 배를 그릴 수 있게 <c>asset/ship</c> 에 넣어 둔 것이다.
/// </summary>
/// <remarks>
/// 원본은 실행 중인 CDS_95 의 배 아틀라스(VA 0x5D68C8)에서 클래스 0 의 8방향을 뜬 것이다.
/// EXE 파일에는 없다 — 그 자리가 .data 의 초기화되지 않은 뒷부분(rawsize 0x51C00 밖)이라
/// 실행 중에만 찬다. 그래서 한 번 떠서 PNG 로 남겨 두고 여기서 읽는다.
///
/// 파일은 <c>asset/ship/ship_0.png</c> ~ <c>ship_7.png</c>(배)와
/// <c>asset/horse/horse_0.png</c> ~ <c>horse_7.png</c>(말), 한 장 48x48 이고 비침은 알파 0 이다.
/// 말은 게임이 16방향을 넷으로 접어 쓰므로 두 장씩 같은 그림이다.
///
/// 말은 <b>걷는 그림</b>이 따로 있다 — <c>asset/horse/horse_walk.png</c> 한 장에
/// 8칸(걸음) x 4줄(방향)이 들어 있다(<c>tools/extract_horse_walk.py</c>). 이것이 있으면
/// 낱장 대신 이쪽을 잘라 쓰고, 없으면 예전처럼 낱장으로 물러선다.
/// 색인이 아니라 색이 그대로 들어 있으므로 그림판으로 열어 고쳐도 그대로 나온다.
///
/// 번호는 게임 방향(반시계)을 둘로 접은 것이다 — 0 북, 2 서, 4 남, 6 동.
/// 게임이 떠 있으면 <see cref="GameShipReader"/> 가 읽은 것을 쓰고, 없을 때 이 표로 물러선다.
/// </remarks>
public static class ShipSprites
{
    public const int Width = 48;
    public const int Size = Width * Width;
    public const int Directions = 8;

    /// <summary>실행 파일 옆의 이 폴더들에서 찾는다.</summary>
    public const string ShipDirectory = "asset/ship";
    public const string HorseDirectory = "asset/horse";

    /// <summary>배 그림 벌 수(0~3). 게임의 함선 등급과 같은 차례다.</summary>
    public const int SkinCount = 4;

    private static int _skin = -1;
    private static string? _folder;

    /// <summary>
    /// 그림 벌이 갈릴 때마다 느는 번호. <b>그리는 쪽이 이 값을 봐야 한다.</b>
    /// </summary>
    /// <remarks>
    /// 배 그림은 뱃머리가 바뀔 때만 다시 올린다(<c>ShipMapHost</c> 의 그림 열쇠). 그런데
    /// 배를 사거나 기함을 바꾸면 뱃머리는 그대로인 채 <b>그림만</b> 갈린다 — 그때 다시
    /// 올릴 낌새가 없어서, 새로 등록한 배를 사고도 지도에는 옛 배가 그대로 떠 있었다.
    /// 그림 열쇠에 이 번호를 끼워 두면 벌이 갈리는 순간 저절로 다시 올라간다.
    /// </remarks>
    public static int Generation { get; private set; }

    /// <summary>
    /// 어느 벌의 배 그림을 쓸지(0~3). <c>asset/ship-g0</c> ~ <c>ship-g3</c> 에서 읽는다 —
    /// 게임의 <c>CDS95Util/shipskin</c> 에 있는 넉 벌을 풀어 둔 것이다.
    /// -1 이면 예전처럼 <see cref="ShipDirectory"/> 를 쓴다.
    /// </summary>
    /// <remarks>바꾸면 들고 있던 그림을 버린다 — 다음에 그릴 때 새 벌로 다시 읽는다.</remarks>
    public static int Skin
    {
        get => _skin;
        set
        {
            int next = value >= 0 && value < SkinCount ? value : -1;
            if (_skin == next) return;
            lock (Gate)
            {
                _skin = next;
                Generation++;
                for (int i = 0; i < Directions; i++) Frames[0][i] = null;   // 배만 다시 읽는다
            }
        }
    }

    /// <summary>
    /// 등록해 넣은 배가 제 그림을 들고 있는 폴더의 온 경로. null 이면 <see cref="Skin"/> 대로 읽는다.
    /// </summary>
    /// <remarks>
    /// <see cref="Skin"/> 보다 이쪽이 세다. 앱에서 손으로 등록한 배는 <c>asset</c> 밖
    /// (<c>%APPDATA%\CdsHelper\ships\{Id}</c>)에 그림을 두기 때문이다.
    /// </remarks>
    public static string? Folder
    {
        get => _folder;
        set
        {
            string? next = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_folder == next) return;
            lock (Gate)
            {
                _folder = next;
                Generation++;
                for (int i = 0; i < Directions; i++) Frames[0][i] = null;   // 배만 다시 읽는다
            }
        }
    }

    /// <summary>어느 배의 그림을 쓸지 한 번에 정한다 — 폴더와 벌 번호를 같이 맞춘다.</summary>
    public static void Use(Hull? hull)
    {
        Folder = hull?.SpriteFolder;
        Skin = hull?.Skin ?? 0;
    }

    /// <summary>[0] 배, [1] 말(육상·정박).</summary>
    private static readonly uint[]?[][] Frames = [new uint[Directions][], new uint[Directions][]];
    private static readonly object Gate = new();

    /// <summary>말이 한 걸음 도는 데 드는 그림 수. 게임 아틀라스가 방향마다 여덟 장이다.</summary>
    public const int WalkPhases = 8;

    /// <summary>말 그림의 방향 수. 게임은 16방향을 넷으로 접어 쓴다.</summary>
    private const int WalkRows = 4;

    /// <summary>걷는 그림 한 장(<c>horse_walk.png</c>)에서 잘라 둔 것. [줄][걸음].</summary>
    private static uint[]?[]? _walk;

    /// <summary>걷는 그림을 못 읽었으면 참 — 다시 읽어 보지 않는다.</summary>
    private static bool _walkMissing;

    /// <summary>못 읽은 까닭 한 줄. 다 읽었으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 16방향 값으로 48x48 그림 한 장(BGRA, 비침은 알파 0). 파일이 없으면 빈 span.
    /// 한 번 읽은 것은 들고 있는다.
    /// </summary>
    public static ReadOnlySpan<uint> Frame(int heading16, bool onLand = false, int phase = -1)
    {
        if (onLand && phase >= 0)
        {
            var step = Walk(heading16, phase);
            if (step != null) return step;
        }

        int set = onLand ? 1 : 0;
        int i = (heading16 & 0xF) >> 1;
        var cached = Frames[set][i];
        if (cached != null) return cached;

        lock (Gate)
        {
            Frames[set][i] ??= Load(set, i) ?? [];
            return Frames[set][i];
        }
    }

    /// <summary>
    /// 걷는 그림 한 장. 방향과 걸음으로 고른다. 그림이 없으면 null 이라 낱장으로 물러선다.
    /// </summary>
    /// <remarks>
    /// 게임이 고르는 그대로다(렌더러 <c>0x48A82E</c>).
    /// <code>
    ///   dd    = (16방향 + 1) &amp; 0xF
    ///   프레임 = (dd &gt;&gt; 2) * 8 + 걸음번호
    /// </code>
    /// 걸음 번호는 게임에서 <c>0x00569550</c> 에 있고 지도 한 틱마다 는다.
    /// </remarks>
    private static uint[]? Walk(int heading16, int phase)
    {
        if (_walkMissing) return null;
        if (_walk == null)
        {
            lock (Gate)
            {
                if (_walk == null && !_walkMissing)
                {
                    _walk = LoadWalkSheet();
                    _walkMissing = _walk == null;
                }
            }
            if (_walk == null) return null;
        }

        int row = ((heading16 + 1) & 0xF) >> 2;
        return _walk[row * WalkPhases + (phase % WalkPhases + WalkPhases) % WalkPhases];
    }

    /// <summary>8칸 x 4줄짜리 한 장을 서른두 장으로 자른다.</summary>
    private static uint[]?[]? LoadWalkSheet()
    {
        string path = Path.Combine(AppContext.BaseDirectory, HorseDirectory, "horse_walk.png");
        if (!File.Exists(path)) return null;

        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth != Width * WalkPhases || src.PixelHeight != Width * WalkRows)
            {
                LastError = $"{path} 크기가 {src.PixelWidth}x{src.PixelHeight} — " +
                            $"{Width * WalkPhases}x{Width * WalkRows} 이어야 합니다";
                return null;
            }

            int stride = src.PixelWidth * 4;
            var all = new uint[src.PixelWidth * src.PixelHeight];
            src.CopyPixels(all, stride, 0);

            var cut = new uint[]?[WalkRows * WalkPhases];
            for (int row = 0; row < WalkRows; row++)
                for (int phase = 0; phase < WalkPhases; phase++)
                {
                    var one = new uint[Size];
                    for (int y = 0; y < Width; y++)
                        Array.Copy(all, (row * Width + y) * src.PixelWidth + phase * Width,
                                   one, y * Width, Width);
                    cut[row * WalkPhases + phase] = one;
                }
            LastError = "";
            return cut;
        }
        catch (Exception ex)
        {
            LastError = $"{path} 를 읽지 못했습니다 — {ex.Message}";
            return null;
        }
    }

    private static uint[]? Load(int set, int index)
    {
        string path;
        if (set == 0 && _folder != null)
        {
            // 등록해 넣은 배 — 그림이 asset 밖에 있으므로 온 경로 그대로 쓴다.
            path = Path.Combine(_folder, $"ship_{index}.png");
        }
        else
        {
            var dir = set == 1 ? HorseDirectory : _skin >= 0 ? $"asset/ship-g{_skin}" : ShipDirectory;
            var name = set == 1 ? "horse" : "ship";
            path = Path.Combine(AppContext.BaseDirectory, dir, $"{name}_{index}.png");
        }

        // 못 찾으면 기본 벌로 물러선다 — 반쪽짜리라도 배는 그려야 한다.
        if (!File.Exists(path) && set == 0 && (_skin >= 0 || _folder != null))
            path = Path.Combine(AppContext.BaseDirectory, ShipDirectory, $"ship_{index}.png");
        if (!File.Exists(path))
        {
            LastError = $"{path} 없음";
            return null;
        }
        try
        {
            using var fs = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var src = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            if (src.PixelWidth != Width || src.PixelHeight != Width)
            {
                LastError = $"{path} 크기가 {src.PixelWidth}x{src.PixelHeight} — {Width}x{Width} 이어야 합니다";
                return null;
            }
            var px = new uint[Size];
            src.CopyPixels(px, Width * 4, 0);
            LastError = "";
            return px;
        }
        catch (Exception ex)
        {
            LastError = $"{path} 를 읽지 못했습니다 — {ex.Message}";
            return null;
        }
    }
}
