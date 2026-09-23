using System.IO;
using System.Text;
using System.Windows;
using CdsHelper.Api.Data;
using CdsHelper.Form.Local.Services;
using CdsHelper.Form.Local.ViewModels;
using CdsHelper.Form.UI.Views;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Main.UI.Views;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.Local.Helpers;

namespace cds_helper;

internal class App : PrismApplication
{
    protected override Window CreateShell()
    {
        return Container.Resolve<CdsHelperWindow>();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 전역 예외 핸들러 등록
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            MessageBox.Show($"UnhandledException:\n{ex?.Message}\n\n{ex?.StackTrace}", "치명적 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"DispatcherUnhandledException:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "UI 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // EUC-KR 인코딩 지원 등록
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // 놀이 설정을 앱 설정보다 먼저 읽어 둔다. 옛 settings.json 에서 옮겨 오는 일이
        // 여기서 벌어지는데, 앱 설정이 먼저 저장되면 옛 값이 지워진 뒤라 놓치게 된다.
        GameSettings.Load();

        // BGM 은 놀이 창(ShipMapWindow)을 열어야 받아지는데, 헬퍼는 그 창을 안 거치고도
        // 오래 쓴다 — 여기서도 조용히 미리 받아 둔다(물음창 없이, 실패해도 그냥 넘어간다).
        PrefetchBgmAsync();

        base.OnStartup(e);
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // AppDbContext 등록
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var dbPath = Path.Combine(basePath, "cdshelper.db");
        containerRegistry.RegisterSingleton<AppDbContext>(() => AppDbContextFactory.Create(dbPath));

        // Services 등록
        containerRegistry.RegisterSingleton<UpdateService>();
        containerRegistry.RegisterSingleton<CharacterService>();
        containerRegistry.RegisterSingleton<BookService>();
        containerRegistry.RegisterSingleton<CityService>();
        containerRegistry.RegisterSingleton<PatronService>();
        containerRegistry.RegisterSingleton<FigureheadService>();
        containerRegistry.RegisterSingleton<ItemService>();
        containerRegistry.RegisterSingleton<SaveDataService>();
        containerRegistry.RegisterSingleton<HintService>();
        containerRegistry.RegisterSingleton<DiscoveryService>();
        containerRegistry.RegisterSingleton<AutoPlayService>();

        // ViewModel 등록
        containerRegistry.Register<CdsHelperViewModel>();
        containerRegistry.Register<PlayerContentViewModel>();

        // Navigation용 View 등록
        containerRegistry.RegisterForNavigation<CharacterContent>();
        containerRegistry.RegisterForNavigation<PatronContent>();
        containerRegistry.RegisterForNavigation<FigureheadContent>();
        containerRegistry.RegisterForNavigation<ItemContent>();
        containerRegistry.RegisterForNavigation<PlayerContent>();
        containerRegistry.RegisterForNavigation<SphinxCalculatorContent>();
        containerRegistry.RegisterForNavigation<DiscoveryStillContent>();
        containerRegistry.RegisterForNavigation<AutoPlayContent>();
        containerRegistry.RegisterForNavigation<WorldMapContent>();
    }

    /// <summary>
    /// 게임 폴더에 BGM 이 있으면 아무 일도 안 한다. 없으면 릴리즈에서 캐시로 받아 두어,
    /// 나중에 놀이 창을 열었을 때 "다운로드할까요?" 물음이 안 뜨게 한다.
    /// 캐시에 타이틀 곡이 있어도 부른다 — 이미 받은 곡은 건너뛰고 빠진 곡만 뒤에서 마저 받는다
    /// (한동안 23번부터만 받아 2~22번이 빠진 캐시가 있다).
    /// </summary>
    private static async void PrefetchBgmAsync()
    {
        try
        {
            string dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath) is { Length: > 0 } saved
                        && Directory.Exists(saved)
                ? saved
                : AppDomain.CurrentDomain.BaseDirectory;
            if (!File.Exists(Path.Combine(dir, "bgm", $"Track{BgmPlayer.RequiredTrack:D2}.mp3")))
                await BgmAssetDownloader.DownloadAsync();
        }
        catch { /* 조용히 넘어간다 — 놀이 창을 열 때 다시 시도된다. */ }
    }
}