using System.IO;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 사용자 자료 폴더 — 평소에는 <c>%APPDATA%\CdsHelper</c>. 세이브 · 설정 · 등록한 배 · 발견물 · 얼굴 · 애드온 · 오류 기록이 여기 산다.
/// </summary>
/// <remarks>
/// <c>CDS_SAVEDIR=&lt;폴더&gt;</c>(<see cref="OffscreenTest.SaveDir"/>) 면 그 폴더를 쓴다 — 화면 밖 시험이 <b>사람이 쓰는 세이브와 설정을
/// 덮어쓰지 않게</b> 둔 문이다. 자리를 한 곳에서만 정하려고 둔 것이라, 새로 <c>%APPDATA%\CdsHelper</c> 밑에 무엇을 두려면 여기를 쓴다.
///
/// 내려받은 것을 쌓는 <c>%LOCALAPPDATA%\CdsHelper</c>(배경음악 · 효과음 · 동영상 · 확대 모형)는 사용자 자료가 아니라 <b>안 옮긴다</b> —
/// 시험도 그것을 읽기만 한다(내려받기는 <see cref="OffscreenTest.Isolated"/> 에서 꺼진다).
/// </remarks>
public static class UserDataFolder
{
    /// <summary>진짜 사용자 폴더(<c>%APPDATA%\CdsHelper</c>) — 시험 중에도 읽기만 할 때 쓴다.</summary>
    public static string RealRoaming { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CdsHelper");

    /// <summary>지금 쓰는 사용자 자료 폴더 — <c>CDS_SAVEDIR</c>, 없으면 <see cref="RealRoaming"/>.</summary>
    public static string Roaming { get; } = OffscreenTest.SaveDir ?? RealRoaming;

    /// <summary>사용자 자료 폴더 밑의 자리.</summary>
    public static string Combine(params string[] parts) => Path.Combine([Roaming, .. parts]);
}
