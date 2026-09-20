using System.IO;
using System.Text.Json;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 효과음 한 줄마다 손으로 적어 두는 비고. 파트 번호에 글 한 줄을 걸어 둔다.
/// </summary>
/// <remarks>
/// 게임 파일에는 쓸 자리가 없어 <c>%APPDATA%\CdsHelper\효과음-비고.json</c> 에 따로 둔다.
/// 어떤 소리가 어디에 쓰이는지(닻·문 여닫기·전투 …)를 들어 보며 적어 두려는 것이라,
/// 게임 폴더가 바뀌어도 그대로 남아야 한다.
/// </remarks>
public static class WaveNotes
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CdsHelper",
        "효과음-비고.json");

    private static Dictionary<string, string>? _notes;

    /// <summary>못 쓰거나 못 읽었으면 그 까닭 한 줄.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>파트에 적어 둔 비고. 없으면 빈 문자열.</summary>
    public static string Of(int part) => Load().GetValueOrDefault(part.ToString(), "");

    /// <summary>비고를 적어 두고 바로 파일에 쓴다. 빈 글은 지운다.</summary>
    public static void Put(int part, string? note)
    {
        var notes = Load();
        string key = part.ToString();
        string text = (note ?? "").Trim();

        if (text.Length == 0) notes.Remove(key);
        else notes[key] = text;

        Save();
    }

    private static Dictionary<string, string> Load()
    {
        if (_notes != null) return _notes;

        try
        {
            _notes = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? []
                : [];
            LastError = "";
        }
        catch (Exception ex)
        {
            // 읽다 걸려도 빈 표로 이어 간다 — 적는 것까지 막을 일은 아니다.
            _notes = [];
            LastError = ex.Message;
        }
        return _notes;
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(_notes,
                new JsonSerializerOptions { WriteIndented = true }));
            LastError = "";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }
}
