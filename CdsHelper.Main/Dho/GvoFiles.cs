// DHO 저장소(C:\Users\ocean\git\dho, src/Dho.Data)에서 옮겨 온 것이다 — 편집기 「DHO 배 찍기」의 3D 미리보기가 쓴다. 고칠 때는 그쪽과 맞춘다.
using System.IO;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Dho.Data;

/// <summary>
/// 게임 폴더(대항해시대 온라인 클라이언트)의 파일을 읽는다. 읽기만 한다.
/// </summary>
/// <remarks>폴더는 환경 변수 <c>GVO_DIR</c>, 없으면 <c>C:\Program Files (x86)\Papaya Play\GV Online KR</c>(2023년부터의 서비스처 — 옛 넷마블 폴더는 2022-12 에서 멈춰 있다).</remarks>
public static class GvoFiles
{
    /// <summary>
    /// 클라이언트를 찾아볼 자리들 — 앞의 것부터. 지금은 바탕화면에 옮겨 둔 것을 먼저 쓴다(설치 폴더는 다시 까는 중일 수 있다).
    /// 폴더가 있기만 해서는 안 되고, 게임이 읽는 파일이 들어 있어야 고른다 — 설치 도중의 빈 폴더를 집지 않게.
    /// </summary>
    private static readonly string[] Candidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "GV Online KR"),
        @"C:\Program Files (x86)\Papaya Play\GV Online KR",
    ];

    private static bool Complete(string folder) =>
        File.Exists(Path.Combine(folder, @"0003\wm000000.bin")) && File.Exists(Path.Combine(folder, @"0000\local\dt000001.bin"))
        && File.Exists(Path.Combine(folder, @"0001\sh0000.bin"));

    public static readonly string Root =
        Environment.GetEnvironmentVariable("GVO_DIR") ?? Candidates.FirstOrDefault(Complete) ?? Candidates.FirstOrDefault(Directory.Exists) ?? Candidates[0];

    public const int Japanese = 0, Korean = 1;

    public static string PathOf(string relative) => Path.Combine(Root, relative);

    public static byte[] Read(string relative) => File.ReadAllBytes(PathOf(relative));

    /// <summary>
    /// 파일 안의 MWC 덩이를 차례로 푼다.
    /// 덩이 = <c>"MWC\x1a"</c>, u32 풀린 크기, u32 눌린 크기, zlib 스트림.
    /// </summary>
    public static List<byte[]> MwcChunks(byte[] file)
    {
        var chunks = new List<byte[]>();
        ReadOnlySpan<byte> magic = "MWC\x1a"u8;
        int at = 0;
        while (true)
        {
            int found = file.AsSpan(at).IndexOf(magic);
            if (found < 0) break;
            at += found;
            int unpacked = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 4));
            int packed = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(at + 8));
            if (packed <= 0 || at + 12 + packed > file.Length) { at += 4; continue; }

            var output = new byte[unpacked];
            using (var zlib = new ZLibStream(new MemoryStream(file, at + 12, packed), CompressionMode.Decompress))
                zlib.ReadExactly(output);
            chunks.Add(output);
            at += 12 + packed;
        }
        return chunks;
    }

    public static byte[] ReadMwc(string relative, int index = 0) => MwcChunks(Read(relative))[index];
}
