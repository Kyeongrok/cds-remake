using System.IO;
using System.Text.Json;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// <b>화면 밖 시험</b>의 스위치 — 환경 변수로만 켠다. 하나도 없으면 아무 일도 안 한다(게임을 그냥 켜면 예전과 같다).
/// </summary>
/// <remarks>
/// 사람이 PC 를 쓰는 동안 자동 시험이 창을 띄우고 소리를 내고 세이브를 덮어쓰지 않게 둔 문이다
/// (창세기전3 리메이크 duel-dx 의 <c>DUELDX_OFFSCREEN</c> · <c>DUELDX_SAVEDIR</c> · <c>DUELDX_MUTE</c> 를 옮겼다).
/// <code>
///   CDS_OFFSCREEN=1        창을 화면 밖(-8000)에 포커스 · 작업 표시줄 단추 없이 띄운다. 렌더는 그대로 돈다.
///   CDS_SAVEDIR=&lt;폴더&gt;     세이브 · 설정 · 등록한 배 따위 사용자 자료(%APPDATA%\CdsHelper)를 그 폴더로 바꾼다.
///   CDS_MUTE=1             배경음악 · 효과음 · 동영상 소리를 끈다. 화면 밖이면 늘 끈다(CDS_MUTE=0 이면 낸다).
///   CDS_GAMEDIR=&lt;폴더&gt;     원본 게임 폴더. 없으면 설정의 세이브 자리 — CDS_SAVEDIR 이면 사용자 설정을 <b>읽기만</b> 해서 찾는다.
///   CDS_START=sea:&lt;도시&gt;:&lt;선체 key&gt;   타이틀을 건너뛰고 그 도시 앞바다에서 그 배를 탄 채 항해 지도로 든다.
///   CDS_HEADING=&lt;0~7&gt;     그때 뱃머리(ShipRegistry.DirectionNames 차례 — 0 북, 2 서, 7 북동).
///   CDS_SAIL=1             닻을 올리고 그 쪽으로 저절로 나아간다.
///   CDS_SHOT=&lt;png&gt;:&lt;초&gt;[;&lt;png&gt;:&lt;초&gt;…]   켠 지 그 초에 화면을 저장하고, 마지막 것을 찍으면 앱을 끈다.
/// </code>
/// 창에 <c>PostMessage(0x8001)</c>(<see cref="SnapshotMessage"/>)를 보내면 <see cref="SnapshotPath"/> 에 지금 화면을 저장한다.
/// 창 핸들은 <see cref="HwndFile"/> 에 적어 둔다 — 작업 표시줄 단추가 없는 창은 <c>Process.MainWindowHandle</c> 로 안 잡힌다.
///
/// 업데이트 확인 · 통계 보내기 · 내려받기 · 처음 켤 때 묻는 창 · 오류 알림 창(클립보드 복사)은 <see cref="Isolated"/> 이면 끈다 —
/// 바깥으로 나가거나 사람을 기다리는 것이다.
/// </remarks>
public static class OffscreenTest
{
    /// <summary>창이 받으면 화면을 저장하는 메시지(WM_APP + 1). duel-dx 의 <c>WM_APP_SNAPSHOT</c> 과 같은 값이다.</summary>
    public const int SnapshotMessage = 0x8001;

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    /// <summary><c>CDS_OFFSCREEN=1</c> — 창을 화면 밖에 포커스 없이 띄운다.</summary>
    public static bool Enabled { get; } = Env("CDS_OFFSCREEN") == "1";

    /// <summary><c>CDS_SAVEDIR</c> — 사용자 자료 폴더를 갈음할 곳. 없으면 null.</summary>
    public static string? SaveDir { get; } = Env("CDS_SAVEDIR") is { } dir ? Path.GetFullPath(dir) : null;

    /// <summary>소리를 끄는지 — <c>CDS_MUTE=1</c>, 또는 화면 밖인데 <c>CDS_MUTE=0</c> 이 아니면.</summary>
    public static bool Muted { get; } = Env("CDS_MUTE") == "1" || (Enabled && Env("CDS_MUTE") != "0");

    /// <summary>시험 판인지 — 바깥으로 나가는 것(업데이트 · 통계 · 내려받기)과 사람을 기다리는 창을 끈다.</summary>
    public static bool Isolated => Enabled || SaveDir != null;

    /// <summary>훅이 하나라도 켜졌는지 — 꺼져 있으면 시험 장치를 아예 안 붙인다.</summary>
    public static bool Any => Isolated || Muted || Env("CDS_START") != null || Env("CDS_SHOT") != null;

    /// <summary><c>CDS_START</c> 값(없으면 null).</summary>
    public static string? Start => Env("CDS_START");

    /// <summary><c>CDS_HEADING</c> — 8방위(0 북, 반시계). 없거나 틀리면 null.</summary>
    public static int? Heading => int.TryParse(Env("CDS_HEADING"), out int h) && h is >= 0 and < 8 ? h : null;

    /// <summary><c>CDS_SAIL=1</c> — 들자마자 저절로 나아간다.</summary>
    public static bool Sail => Env("CDS_SAIL") == "1";

    /// <summary><c>CDS_SHOT</c> 값(없으면 null).</summary>
    public static string? Shot => Env("CDS_SHOT");

    /// <summary>스냅샷 · 창 핸들 파일을 두는 곳 — <c>CDS_SAVEDIR</c>, 없으면 %TEMP%.</summary>
    public static string OutputFolder => SaveDir ?? Path.GetTempPath();

    /// <summary>0x8001 을 받으면 화면을 저장하는 자리.</summary>
    public static string SnapshotPath => Path.Combine(OutputFolder, "cds_snapshot.png");

    /// <summary>게임 창 핸들(10진수)을 적어 두는 파일.</summary>
    public static string HwndFile => Path.Combine(OutputFolder, "cds_hwnd.txt");

    /// <summary>
    /// 원본 게임 폴더 — <c>CDS_GAMEDIR</c>, 없고 <c>CDS_SAVEDIR</c> 이면 <b>사용자 설정을 읽기만</b> 해서 마지막 세이브 자리를 쓴다.
    /// 둘 다 아니면 null(평소대로 앱 설정을 본다).
    /// </summary>
    public static string? GameDir
    {
        get
        {
            if (Env("CDS_GAMEDIR") is { } dir) return dir;
            if (SaveDir == null) return null;
            try
            {
                string own = Path.Combine(SaveDir, "settings.json");
                string path = File.Exists(own) ? own : Path.Combine(UserDataFolder.RealRoaming, "settings.json");
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("LastSaveFilePath", out var save) && save.GetString() is { Length: > 0 } file)
                    return Path.GetDirectoryName(file);
            }
            catch { /* 못 읽으면 평소대로 */ }
            return null;
        }
    }
}
