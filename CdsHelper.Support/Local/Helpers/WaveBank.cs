using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// WAVES.CDS — 게임 효과음 묶음. 이름이 파도(waves)처럼 보이지만 <c>.WAV</c> 를 담은 것이다.
/// </summary>
/// <remarks>
/// 파일은 Ls12 압축이고 파트 하나가 <b>RIFF WAVE 파일 통째로</b>다 — 헤더까지 들어 있어
/// 풀어낸 바이트를 그대로 재생기에 넘기면 된다. 50개 모두 22050Hz / 8bit / 모노 PCM 이다.
///
/// <para>게임 사운드 표</para>
/// CDS_95.EXE 는 사운드를 78개짜리 표 하나로 다룬다(VA 0x4C3810, 24바이트 x 78).
/// <list type="bullet">
///   <item>사운드 ID 0~27 = CD 트랙 2~29 (게임 폴더 <c>bgm/TrackNN.mp3</c>)</item>
///   <item>사운드 ID 28~77 = 이 파일의 파트 0~49</item>
/// </list>
/// 초기화는 VA 0x40D4C0 의 <c>Init(&amp;"CDS95", 1, "C:WAVES.CDS", 0x4C3810, 78)</c> 이고,
/// 부르는 쪽은 <c>mov ecx, 0x585FA8</c> + <c>push &lt;ID&gt;</c> 꼴로 .text 에 흩어져 있다.
/// </remarks>
public sealed class WaveBank
{
    public const string FileName = "WAVES.CDS";

    /// <summary>파트 0 에 해당하는 게임 사운드 ID. 그 앞 28개는 CD 트랙 자리다.</summary>
    public const int FirstSoundId = 28;

    /// <summary>사운드 ID 0 이 가리키는 CD 트랙 번호.</summary>
    public const int FirstCdTrack = 2;

    /// <summary>CD 트랙에 걸린 사운드 ID 수(트랙 2~29).</summary>
    public const int CdTrackCount = 28;

    private readonly Ls12Reader _reader;
    private readonly byte[]?[] _cache;

    private static WaveBank? _cached;
    private static string? _cachedPath;

    private WaveBank(Ls12Reader reader, WaveInfo[] items)
    {
        _reader = reader;
        _cache = new byte[items.Length][];
        Items = items;
    }

    /// <summary>못 올렸으면 그 까닭 한 줄. 올렸으면 빈 문자열.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>파트마다 한 줄. 자리는 파트 번호 그대로다.</summary>
    public IReadOnlyList<WaveInfo> Items { get; }

    public int Count => Items.Count;

    /// <summary>사운드 ID 를 파트 번호로. CD 쪽이거나 표 밖이면 -1.</summary>
    public static int PartFromSoundId(int soundId) =>
        soundId >= FirstSoundId ? soundId - FirstSoundId : -1;

    /// <summary>사운드 ID 가 가리키는 CD 트랙 번호. WAVE 쪽이면 -1.</summary>
    public static int CdTrackFromSoundId(int soundId) =>
        soundId >= 0 && soundId < CdTrackCount ? soundId + FirstCdTrack : -1;

    /// <summary>
    /// 게임 폴더의 WAVES.CDS 를 열어 파트 표를 읽는다. 실패하면 null 이고
    /// <see cref="LastError"/> 에 까닭이 남는다. 경로가 같으면 앞서 읽은 것을 다시 쓴다.
    /// </summary>
    public static WaveBank? LoadFromDirectory(string directory)
    {
        var path = CdsAssetPath.Resolve(directory, FileName);
        if (_cached != null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            LastError = "";
            return _cached;
        }

        if (!File.Exists(path)) { LastError = $"{FileName} 없음"; return null; }

        var reader = Ls12Reader.Open(path);
        if (reader == null) { LastError = $"{FileName} 이 Ls12 형식이 아님"; return null; }

        // 같은 소리를 두 자리에 걸어 둔 것이 있어(파트 5 와 17) 해시로 짝을 지어 둔다.
        var items = new WaveInfo[reader.PartCount];
        var seen = new Dictionary<string, int>();
        for (int i = 0; i < reader.PartCount; i++)
        {
            var raw = reader.Decode(i);
            string hash = raw != null ? Convert.ToHexString(MD5.HashData(raw)) : "";
            int dup = -1;
            if (raw != null)
            {
                if (seen.TryGetValue(hash, out int first)) dup = first;
                else seen[hash] = i;
            }
            items[i] = Describe(i, reader.PartSize(i), raw, dup);
        }

        LastError = "";
        _cached = new WaveBank(reader, items);
        _cachedPath = path;
        return _cached;
    }

    /// <summary>파트 하나를 RIFF WAVE 바이트로. 그대로 파일에 쓰거나 재생기에 넘기면 된다.</summary>
    public byte[]? Wav(int part)
    {
        if (part < 0 || part >= Count) return null;
        return _cache[part] ??= _reader.Decode(part);
    }

    /// <summary>파트 하나를 <paramref name="path"/> 에 .wav 로 쓴다.</summary>
    public bool Save(int part, string path)
    {
        var wav = Wav(part);
        if (wav == null) return false;
        File.WriteAllBytes(path, wav);
        return true;
    }

    /// <summary>RIFF 청크를 훑어 한 줄로 간추린다. 못 읽으면 크기만 채운 줄을 낸다.</summary>
    private static WaveInfo Describe(int part, uint rawSize, byte[]? d, int duplicateOf)
    {
        var info = new WaveInfo { Part = part, RawSize = (int)rawSize, DuplicateOf = duplicateOf };
        if (d == null || d.Length < 12) { info.Error = "압축 해제 실패"; return info; }
        if (System.Text.Encoding.ASCII.GetString(d, 0, 4) != "RIFF"
            || System.Text.Encoding.ASCII.GetString(d, 8, 4) != "WAVE")
        {
            info.Error = "RIFF WAVE 아님";
            return info;
        }

        int pos = 12;
        while (pos + 8 <= d.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(d, pos, 4);
            int len = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos + 4));
            if (id == "fmt " && pos + 8 + 16 <= d.Length)
            {
                info.FormatTag = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos + 8));
                info.Channels = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos + 10));
                info.SampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(pos + 12));
                info.Bits = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(pos + 22));
            }
            else if (id == "data")
            {
                info.DataBytes = Math.Min(len, d.Length - pos - 8);
                break;
            }
            pos += 8 + len + (len & 1);   // 청크는 짝수로 맞춰 이어진다
        }

        if (info.DataBytes == 0) info.Error = "data 청크 없음";
        return info;
    }
}

/// <summary>WAVES.CDS 파트 하나의 됨됨이.</summary>
public sealed class WaveInfo
{
    /// <summary>파트 번호(0부터).</summary>
    public int Part { get; init; }

    /// <summary>게임이 쓰는 사운드 ID. 파트 번호에 28 을 더한 값이다.</summary>
    public int SoundId => Part + WaveBank.FirstSoundId;

    public int FormatTag { get; set; }
    public int Channels { get; set; }
    public int SampleRate { get; set; }
    public int Bits { get; set; }

    /// <summary>data 청크 길이(바이트).</summary>
    public int DataBytes { get; set; }

    /// <summary>압축을 푼 파트 전체 크기(RIFF 헤더 포함).</summary>
    public int RawSize { get; init; }

    /// <summary>바이트까지 같은 앞선 파트 번호. 없으면 -1.</summary>
    public int DuplicateOf { get; init; } = -1;

    /// <summary>읽다 걸린 문제 한 줄. 멀쩡하면 빈 문자열.</summary>
    public string Error { get; set; } = "";

    /// <summary>길이(초). fmt 를 못 읽었으면 0.</summary>
    public double Seconds
    {
        get
        {
            int frame = Channels * (Bits / 8);
            return frame > 0 && SampleRate > 0 ? (double)DataBytes / (SampleRate * frame) : 0;
        }
    }

    public string FormatText => FormatTag == 0
        ? "?"
        : $"{SampleRate}Hz {Bits}bit {(Channels == 1 ? "모노" : $"{Channels}ch")}" +
          (FormatTag == 1 ? "" : $" tag{FormatTag}");

    public string DuplicateText => DuplicateOf >= 0 ? $"파트 {DuplicateOf} 와 같음" : "";

    /// <summary>
    /// 손으로 적어 두는 비고 — 어떤 자리에서 나는 소리인지 들어 보며 적는다.
    /// </summary>
    /// <remarks>
    /// 게임 파일이 아니라 <see cref="WaveNotes"/> 가 따로 챙긴다. 값을 넣으면 바로 쓰므로
    /// 표에서 고치고 창을 닫아도 남는다.
    /// </remarks>
    public string Note
    {
        get => WaveNotes.Of(Part);
        set => WaveNotes.Put(Part, value);
    }
}
