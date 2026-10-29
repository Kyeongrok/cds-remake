using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 잡지 못한 오류를 회원이 <b>보고할 수 있게</b> 남긴다 — 오류 글을 파일로 적고 클립보드에 넣은 뒤 알림 창을 띄운다.
/// 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 통계(<see cref="PlayStats.Error"/>)는 오류 갈래와 난 자리만 보내서 무엇을 하다 났는지는 모른다. 회원이 알림 창의
/// 글을 그대로 붙여 넣어 알려 주면 그 사이를 메울 수 있다. 게임이 꺼지는 오류(UI 스레드 밖 · 따로 뜬 창의 스레드)도
/// 꺼지기 전에 이 창을 거친다.
/// 파일은 <c>%APPDATA%\CdsHelper\crash\</c> 에 쌓이고, 오래된 것은 새 것 <see cref="Keep"/> 개만 남기고 지운다.
/// </remarks>
public static class CrashReport
{
    /// <summary>남겨 둘 보고 파일 수.</summary>
    private const int Keep = 20;

    private static readonly string Folder = UserDataFolder.Combine("crash");

    /// <summary>같은 오류가 잇달아 날 때 창이 끝없이 뜨지 않게 — 한 번 띄운 오류 자리는 다시 안 띄운다.</summary>
    private static readonly HashSet<string> Shown = [];

    private static string _version = "";

    /// <summary>판 번호를 적어 둔다 — 게임 exe 가 켤 때 한 번 부른다.</summary>
    public static void Start(Assembly app)
    {
        var version = app.GetName().Version;
        _version = version is null ? "" : ReleaseNotes.Trim(version).ToString();
    }

    /// <summary>
    /// 오류를 적고 알린다. <paramref name="fatal"/> 이면 「곧 꺼진다」고 알린다.
    /// 알리다 또 오류가 나도 삼킨다 — 여기서 터지면 보고조차 못 남는다.
    /// </summary>
    public static void Report(Exception ex, bool fatal)
    {
        string text = Compose(ex, fatal);
        string? path = Save(text);

        string where = $"{ex.GetType().FullName}@{ex.TargetSite?.DeclaringType?.FullName}.{ex.TargetSite?.Name}";
        lock (Shown)
            if (!fatal && !Shown.Add(where)) return;

        // 화면 밖 시험은 파일에만 남긴다 — 알림 창은 사람 화면 한가운데 뜨고, 클립보드는 사용자 것이다.
        if (OffscreenTest.Isolated) return;

        bool copied = Copy(text);
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(Lang.T(fatal ? "오류가 나서 게임이 꺼집니다." : "오류가 났습니다."));
            sb.AppendLine(Lang.T("버그 리포트를 보내 주시면 고치는 데 큰 도움이 됩니다."));
            sb.AppendLine();
            if (copied) sb.AppendLine(Lang.T("오류 내용을 클립보드에 복사했습니다. 그대로 붙여 넣어 보내 주세요."));
            if (path != null) sb.AppendLine(Lang.T("오류 내용을 파일로도 저장했습니다:")).AppendLine(path);
            sb.AppendLine();
            sb.Append($"{ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(sb.ToString(), Lang.T("오류"), MessageBoxButton.OK, MessageBoxImage.Error,
                            MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
        }
        catch { /* 알림을 못 띄워도 파일은 남았다 */ }
    }

    /// <summary>보고 글 — 판 · 때 · 윈도 · 언어와 오류 전문(안쪽 오류까지).</summary>
    private static string Compose(Exception ex, bool fatal)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"대항해시대3 리메이크 {(_version.Length > 0 ? "v" + _version : "(개발 판)")}{(fatal ? " — 꺼짐" : "")}");
        sb.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  ·  {Environment.OSVersion}  ·  {Lang.Current.Code}");
        sb.AppendLine();
        sb.AppendLine(ex.ToString());
        return sb.ToString();
    }

    private static string? Save(string text)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, text);
            foreach (var old in new DirectoryInfo(Folder).GetFiles("crash-*.txt")
                         .OrderByDescending(f => f.Name).Skip(Keep))
                old.Delete();
            return path;
        }
        catch { return null; }
    }

    /// <summary>클립보드는 STA 스레드에서만 만질 수 있다 — 지금 스레드가 아니면 하나 세워 넣는다.</summary>
    private static bool Copy(string text)
    {
        bool done = false;
        void Put()
        {
            try { Clipboard.SetText(text); done = true; }
            catch { /* 다른 앱이 클립보드를 잡고 있으면 못 넣는다 */ }
        }
        try
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) Put();
            else
            {
                var thread = new Thread(Put);
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join(2000);
            }
        }
        catch { }
        return done;
    }
}
