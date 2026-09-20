using System.IO;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 실행 파일 옆에 정적으로 넣은 JSON 표를 읽는다.
/// </summary>
/// <remarks>
/// 표가 없거나 깨졌으면 해당 기능만 열리지 않는다. 원본 게임 EXE는 실행 중 참조하지 않는다.
/// </remarks>
internal static class ExeTable
{
    /// <summary>표 변환기의 옛 시그니처를 유지한다.</summary>
    public delegate T? Reader<T>(PeImage exe, out string error) where T : class;

    /// <summary>적어 두는 자리(게임데이터 창이 여기를 훑는다).</summary>
    public static string Folder => TableCache.Folder;

    /// <summary>
    /// 정적으로 넣은 표를 연다.
    /// </summary>
    /// <param name="name">적어 둘 파일 이름(확장자 뺀 것).</param>
    /// <param name="gameDirectory">호환성을 위해 남겨 둔 옛 인자. 사용하지 않는다.</param>
    /// <param name="read">호환성을 위해 남겨 둔 옛 변환기. 사용하지 않는다.</param>
    /// <param name="error">못 열었을 때의 까닭. 열렸으면 빈 문자열.</param>
    /// <param name="version">
    /// 알맹이의 모양 판. 표에 칸을 더하면 이 값을 올린다 — 옛 모양으로 적어 둔 파일을
    /// 버리고 다시 굽게 하는 표다.
    /// </param>
    public static T? Open<T>(string name, string gameDirectory, Reader<T> read, out string error,
                             int version = 1)
        where T : class
    {
        error = "";
        var cached = TableCache.Read<T>(name);
        if (cached == null)
        {
            error = $"{TableCache.Folder}\\{name}.json 을 찾지 못했습니다";
            return null;
        }

        if (cached.Version != version)
        {
            error = $"{name}.json 의 표 버전이 맞지 않습니다";
            return null;
        }

        return cached.Data;
    }
}
