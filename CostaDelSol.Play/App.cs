using System.IO;
using System.Text;
using System.Windows;
using Velopack;
using Velopack.Sources;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Settings;

namespace CostaDelSol.Play;

/// <summary>
/// 놀이만 띄우는 실행 파일 — <c>CostaDelSol.exe</c>.
/// </summary>
/// <remarks>
/// 개발도구(<c>Editor.exe</c>)를 거치지 않고 <see cref="ShipMapWindow"/> 를 바로 연다.
/// 두 exe 는 <b>같은 폴더에 나란히</b> 놓이고 설정도 같은 자리를 본다
/// (<c>%APPDATA%\CdsHelper</c>) — 개발도구에서 세이브를 열어 두었으면 이쪽도 그 게임 폴더를
/// 그대로 쓴다.
///
/// 게임 폴더를 아직 모르면 <b>처음 켤 때 한 번 묻는다</b>. 개발도구에는 "세이브 파일 열기" 가
/// 있지만 이쪽에는 없으니, 여기서 안 물으면 곡도 그림도 못 읽는다.
/// </remarks>
internal sealed class App : Application
{
    [STAThread]
    public static void Main()
    {
        // 설치판(Setup.exe)의 메인 exe 가 이것이다 — Velopack 이 설치·업데이트·제거 때 특수 인자로 불러
        // 곧바로 끝나기를 기다리므로 <b>맨 앞</b>에서 받아 준다. 그냥 켰으면 아무 일 없이 지나간다.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent_();
        app.Run();
    }

    private void InitializeComponent_()
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += (_, args) =>
        {
            // 게임 창을 닫는 사이 늦게 뜨려던 창이 내는 오류는 조용히 넘긴다 — 닫힌 창을 주인으로 삼으려던 것이다.
            if (CdsHelper.Game.UI.Views.ShipMapWindow.ShuttingDown) { args.Handled = true; return; }
            PlayStats.Error(args.Exception);
            MessageBox.Show($"{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                            "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        Startup += (_, _) => Begin();
    }

    private void Begin()
    {
        // 게임 자료가 CP949 라 코드페이지를 먼저 열어 둔다.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        GameSettings.Load();

        // 미궁은 딴 어셈블리에 있어 놀이 쪽에서 곧장 못 부른다 — 여기서 걸어 준다.
        // 일기토는 이제 놀이 쪽(ShipMapWindow.PlayDuel)이 제 판을 곧장 부른다.
        ShipMapWindow.MazeGame = CdsHelper.Maze.MazeGame.Play;

        // 놀이 창은 주인이 없다. 창이 <c>CenterOwner</c> 로 서 있어 주인이 없으면
        // 자리가 어정쩡하게 잡힌다 — 여기서는 화면 한가운데로 못 박는다.
        var window = new ShipMapWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        MainWindow = window;
        window.Show();

        // 창이 다 뜬 뒤에 띄운다 — 지금 띄우면 주인 창이 아직 자리를 못 잡아 알림이 엉뚱한 데 선다.
        window.Dispatcher.BeginInvoke(() => { ShowReleaseNotes(window); AskStats(window); },
                                      System.Windows.Threading.DispatcherPriority.ApplicationIdle);

        // 놀이 통계는 이 exe 에서만 센다 — 끌 때 모은 것을 줄에 세운다.
        PlayStats.Start(typeof(App).Assembly);
        Exit += (_, _) => PlayStats.Flush();

        _ = CheckForUpdateAsync(window);
    }

    /// <summary>
    /// 업데이트한 뒤 처음 켰으면 그 사이 판들의 릴리즈 노트를 띄운다(<see cref="ReleaseNotes"/>).
    /// </summary>
    /// <remarks>
    /// 「업데이트됐다」는 것은 <b>켠 판이 마지막으로 노트를 보여 준 판과 다르다</b>로 안다
    /// (<see cref="GameSettings.NotesSeenVersion"/>). 노트에 지금 판 대목이 없으면(개발 중 빌드) 아무것도 안 한다.
    /// </remarks>
    private static void ShowReleaseNotes(ShipMapWindow window)
    {
        if (typeof(App).Assembly.GetName().Version is not { } version) return;
        string current = ReleaseNotes.Trim(version).ToString();
        if (GameSettings.NotesSeenVersion == current) return;

        string notes = ReleaseNotes.Since(ReleaseNotes.Read(), GameSettings.NotesSeenVersion, version);
        if (notes.Length == 0) return;

        GameSettings.NotesSeenVersion = current;
        if (window.IsLoaded) NoticeDialog.Show(window, notes, "릴리즈 노트");
    }

    /// <summary>
    /// 놀이 통계를 모은다고 <b>한 번</b> 알리고 켠다(<see cref="PlayStats"/>). 이미 켜져 있으면
    /// (<see cref="GameSettings.SendStats"/>) 다시 알리지 않는다. 보낼 수 없는 판(받는 곳 주소가 없거나
    /// 손으로 빌드한 판)에서는 알리지도 켜지도 않는다.
    /// </summary>
    private static void AskStats(ShipMapWindow window)
    {
        if (GameSettings.SendStats == true || !PlayStats.CanUploadBuild || !window.IsLoaded) return;
        NoticeDialog.Show(window,
            "테스트 중 오류와 통계 데이터를 수집해서 더 좋은 결과물을 만들기 위해 수집 합니다.",
            "통계 보내기");
        GameSettings.SendStats = true;
    }

    /// <summary>
    /// 새 버전을 뒤에서 확인한다 — 있으면 받아 두고 지금 다시 시작할지 묻는다.
    /// </summary>
    /// <remarks>
    /// 개발도구(<c>Editor.exe</c>)의 <c>UpdateService</c> 와 같은 저장소(GitHub 릴리즈)를 본다. Setup.exe 로 깔지 않은
    /// 판(단일 파일 exe · 개발 중 빌드)은 <see cref="UpdateManager.IsInstalled"/> 가 거짓이라 아무것도 안 한다.
    /// 「아니오」면 게임을 끌 때 적용된다(<see cref="UpdateManager.WaitExitThenApplyUpdates"/>).
    /// 인터넷이 없거나 GitHub 이 막혀도 놀이는 그대로 돈다 — 조용히 넘어간다.
    /// </remarks>
    private static async Task CheckForUpdateAsync(ShipMapWindow window)
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!manager.IsInstalled) return;

            window.Say("업데이트를 확인합니다.");

            var update = await manager.CheckForUpdatesAsync();
            if (update == null) return;

            await manager.DownloadUpdatesAsync(update);
            var target = update.TargetFullRelease;

            bool now = window.IsLoaded && CdsHelper.Game.UI.Views.ConfirmDialog.Ask(window,
                $"새 버전({target.Version})을 받았습니다.\n지금 다시 시작해 적용하겠습니까?"
                + "\n(진행 중인 판은 저장되지 않습니다. 아니오를 고르면 게임을 끌 때 적용됩니다.)");
            if (now) manager.ApplyUpdatesAndRestart(target);
            else manager.WaitExitThenApplyUpdates(target, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>릴리즈가 올라가는 저장소 — 개발도구의 업데이트와 같은 자리다.</summary>
    private const string RepoUrl = "https://github.com/Kyeongrok/cds-remake";
}
