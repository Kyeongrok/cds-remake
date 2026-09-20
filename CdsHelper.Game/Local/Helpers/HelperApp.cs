using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 게임에서 <b>도구 앱</b>(Editor.exe)을 띄운다.
/// </summary>
/// <remarks>
/// 두 exe 는 따로 도는 앱이고, 게임 쪽(CostaDelSol.Play)은 도구 쪽(CdsHelper.Form)을 참조하지
/// 않는다 — 참조하면 게임 하나 띄우는 데 도구 창까지 다 딸려 올라온다. 그래서 창을 여는
/// 것이 아니라 <b>프로세스를 띄운다</b>.
///
/// exe 를 찾는 차례는 이렇다.
/// <code>
///   1. 나와 같은 폴더            배포 판. 두 exe 를 나란히 놓는다.
///   2. 옆 프로젝트의 구운 자리   .../CdsHelper.Play/bin/... → .../CdsHelper/bin/...
/// </code>
/// 이미 떠 있으면 하나 더 띄우지 않고 그 창을 앞으로 불러온다 — 도구 앱은 같은 자료를
/// 손보는 곳이라 두 벌이 같이 떠 있으면 서로 적어 둔 것을 덮어쓴다.
/// </remarks>
public static class HelperApp
{
    private const string ExeName = "Editor.exe";

    /// <summary>프로세스 이름(확장자 없이). 이미 떠 있는지 이것으로 본다.</summary>
    private const string ProcessName = "Editor";

    /// <summary>도구 앱 프로젝트 폴더 — 굽고 돌릴 때 옆 프로젝트의 bin 을 찾는 데 쓴다. exe 이름과 다르다.</summary>
    private const string ProjectFolder = "CdsHelper";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    /// <summary>SW_RESTORE — 작업 표시줄로 내려가 있으면 도로 편다.</summary>
    private const int Restore = 9;

    /// <summary>왜 못 띄웠는지. 잘 띄웠으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>도구 앱 exe 자리. 못 찾으면 null.</summary>
    public static string? Find()
    {
        string here = Path.Combine(AppContext.BaseDirectory, ExeName);
        if (File.Exists(here)) return here;

        // 굽고 돌릴 때는 프로젝트마다 제 bin 을 쓴다. 판·틀·RID 가 같으니 폴더 이름
        // 하나만 갈아 끼우면 옆 프로젝트의 같은 구운 자리가 나온다.
        string mine = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        foreach (string play in (string[])["CostaDelSol.Play", "CdsHelper.Duel", "CdsHelper.Maze"])
        {
            string mark = Path.DirectorySeparatorChar + play + Path.DirectorySeparatorChar;
            int at = mine.IndexOf(mark, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;

            string beside = Path.Combine(
                mine[..at], ProjectFolder, mine[(at + mark.Length)..], ExeName);
            if (File.Exists(beside)) return beside;
        }

        return null;
    }

    /// <summary>
    /// 도구 앱을 띄운다. 이미 떠 있으면 그 창을 앞으로 부른다.
    /// 못 띄웠으면 false 를 내고 까닭이 <see cref="LastError"/> 에 남는다.
    /// </summary>
    public static bool Run()
    {
        LastError = "";

        foreach (var open in Process.GetProcessesByName(ProcessName))
        {
            if (open.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(open.MainWindowHandle, Restore);
            SetForegroundWindow(open.MainWindowHandle);
            return true;
        }

        if (Find() is not { } exe)
        {
            LastError = $"{ExeName} 을 찾지 못했습니다.";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            });
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }
}
