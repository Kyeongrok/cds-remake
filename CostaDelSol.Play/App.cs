using System.IO;
using System.Text;
using System.Windows;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Settings;

namespace CostaDelSol.Play;

/// <summary>
/// 놀이만 띄우는 실행 파일 — <c>CostaDelSol.exe</c>.
/// </summary>
/// <remarks>
/// 세이브 뷰어(<c>Editor.exe</c>)를 거치지 않고 <see cref="ShipMapWindow"/> 를 바로 연다.
/// 두 exe 는 <b>같은 폴더에 나란히</b> 놓이고 설정도 같은 자리를 본다
/// (<c>%APPDATA%\CdsHelper</c>) — 뷰어에서 세이브를 열어 두었으면 이쪽도 그 게임 폴더를
/// 그대로 쓴다.
///
/// 게임 폴더를 아직 모르면 <b>처음 켤 때 한 번 묻는다</b>. 뷰어에는 "세이브 파일 열기" 가
/// 있지만 이쪽에는 없으니, 여기서 안 물으면 곡도 그림도 못 읽는다.
/// </remarks>
internal sealed class App : Application
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.InitializeComponent_();
        app.Run();
    }

    private void InitializeComponent_()
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        DispatcherUnhandledException += (_, args) =>
        {
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
    }

    /// <summary>
}
