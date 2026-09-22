using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CdsHelper.Api.Data;
using CdsHelper.Form.Local.ViewModels;
using CdsHelper.Main.UI.Views;
using CdsHelper.Navigation.UI.Views;
using CdsHelper.Support.Local.Events;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Support.UI.Units;
using Prism.Events;
using Prism.Ioc;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_SettingsMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_SphinxMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_EventQueueMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DbTableViewerMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_WaveBankMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_UiSpriteDumpMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_SoundTestMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_GameDataMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_MovieBankMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_PortraitBookMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_CityCultureMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_NationEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DisevEditorMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_QuestEditorMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_VoyagerEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_BuildingListMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_PersonEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_FormationMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_FortuneMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_MenuDesignerMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_TavernHintMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DiscoveryEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_BookEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_MotionMakerMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_ImageShrinkMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_VideoShrinkMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_ShipRegistryMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_FigureheadEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_ShipMapMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_HelpMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_WorldMapMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_DiscoveryStillMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_CultureEditMenu, Type = typeof(MenuItem))]
[TemplatePart(Name = PART_AccordionMenu, Type = typeof(NavigationMenu))]
[TemplatePart(Name = PART_ContentRegion, Type = typeof(ContentControl))]
[TemplatePart(Name = PART_HamburgerButton, Type = typeof(Button))]
[TemplatePart(Name = PART_MenuPopup, Type = typeof(Popup))]
public class CdsHelperWindow : CdsWindow
{
    private const string PART_SettingsMenu = "PART_SettingsMenu";
    private const string PART_SphinxMenu = "PART_SphinxMenu";
    private const string PART_EventQueueMenu = "PART_EventQueueMenu";
    private const string PART_DbTableViewerMenu = "PART_DbTableViewerMenu";
    private const string PART_WaveBankMenu = "PART_WaveBankMenu";
    private const string PART_UiSpriteDumpMenu = "PART_UiSpriteDumpMenu";
    private const string PART_SoundTestMenu = "PART_SoundTestMenu";
    private const string PART_GameDataMenu = "PART_GameDataMenu";
    private const string PART_MovieBankMenu = "PART_MovieBankMenu";
    private const string PART_PortraitBookMenu = "PART_PortraitBookMenu";
    private const string PART_CityCultureMenu = "PART_CityCultureMenu";
    private const string PART_NationEditMenu = "PART_NationEditMenu";
    private const string PART_DisevEditorMenu = "PART_DisevEditorMenu";
    private const string PART_QuestEditorMenu = "PART_QuestEditorMenu";
    private const string PART_VoyagerEditMenu = "PART_VoyagerEditMenu";
    private const string PART_BuildingListMenu = "PART_BuildingListMenu";
    private const string PART_PersonEditMenu = "PART_PersonEditMenu";
    private const string PART_FormationMenu = "PART_FormationMenu";
    private const string PART_FortuneMenu = "PART_FortuneMenu";
    private const string PART_MenuDesignerMenu = "PART_MenuDesignerMenu";
    private const string PART_TavernHintMenu = "PART_TavernHintMenu";
    private const string PART_DiscoveryEditMenu = "PART_DiscoveryEditMenu";
    private const string PART_BookEditMenu = "PART_BookEditMenu";
    private const string PART_MotionMakerMenu = "PART_MotionMakerMenu";
    private const string PART_ImageShrinkMenu = "PART_ImageShrinkMenu";
    private const string PART_VideoShrinkMenu = "PART_VideoShrinkMenu";
    private const string PART_ShipRegistryMenu = "PART_ShipRegistryMenu";
    private const string PART_FigureheadEditMenu = "PART_FigureheadEditMenu";
    private const string PART_ShipMapMenu = "PART_ShipMapMenu";
    private const string PART_HelpMenu = "PART_HelpMenu";
    private const string PART_WorldMapMenu = "PART_WorldMapMenu";
    private const string PART_DiscoveryStillMenu = "PART_DiscoveryStillMenu";
    private const string PART_CultureEditMenu = "PART_CultureEditMenu";
    private const string PART_AccordionMenu = "PART_AccordionMenu";
    private const string PART_ContentRegion = "PART_ContentRegion";
    private const string PART_HamburgerButton = "PART_HamburgerButton";
    private const string PART_MenuPopup = "PART_MenuPopup";

    private CdsHelperViewModel? _viewModel;
    private readonly IRegionManager _regionManager;
    private Button? _hamburgerButton;
    private Popup? _menuPopup;
    private NavigationMenu? _accordionMenu;

    static CdsHelperWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CdsHelperWindow),
            new FrameworkPropertyMetadata(typeof(CdsHelperWindow)));
    }

    public CdsHelperWindow(CdsHelperViewModel viewModel, IRegionManager regionManager)
    {
        _viewModel = viewModel;
        _regionManager = regionManager;
        DataContext = viewModel;

        // 게임처럼 화면 한가운데에서 뜬다. 안 정해 두면 윈도가 계단식으로 흘려 놓아
        // 구석에서 뜬다. 크기는 Themes/Views/CdsHelperWindow.xaml 의 Style 에 있다 —
        // 자리는 이쪽이다. WindowStartupLocation 은 의존 속성이 아니라 Setter 에 못 넣는다.
        //
        // CenterScreen 만으로는 어긋난다. 그 값은 창이 뜨기 전 크기로 자리를 잡는데,
        // 이 창은 템플릿이 붙으면서 크기가 한 번 더 정해지기 때문이다. 그래서 다 뜬 뒤에
        // 작업 영역(작업 표시줄을 뺀 자리) 기준으로 한 번 더 맞춘다.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Loaded += (_, _) => CenterOnScreen();
    }

    /// <summary>작업 영역 한가운데로 옮긴다. 최대화·최소화 상태면 건드리지 않는다.</summary>
    private void CenterOnScreen()
    {
        if (WindowState != WindowState.Normal) return;

        var area = SystemParameters.WorkArea;
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsNaN(w) || double.IsNaN(h)) return;

        Left = area.Left + (area.Width - w) / 2;
        Top = area.Top + (area.Height - h) / 2;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(PART_SettingsMenu) is MenuItem settingsMenu)
        {
            settingsMenu.Click += OnSettingsMenuClick;
        }

        if (GetTemplateChild(PART_SphinxMenu) is MenuItem sphinxMenu)
        {
            sphinxMenu.Click += OnSphinxMenuClick;
        }

        if (GetTemplateChild(PART_EventQueueMenu) is MenuItem eventQueueMenu)
        {
            eventQueueMenu.Click += OnEventQueueMenuClick;
        }

        if (GetTemplateChild(PART_DbTableViewerMenu) is MenuItem dbTableViewerMenu)
        {
            dbTableViewerMenu.Click += OnDbTableViewerMenuClick;
        }

        if (GetTemplateChild(PART_UiSpriteDumpMenu) is MenuItem uiSpriteDumpMenu)
            uiSpriteDumpMenu.Click += OnUiSpriteDumpMenuClick;

        if (GetTemplateChild(PART_SoundTestMenu) is MenuItem soundTestMenu)
            soundTestMenu.Click += OnSoundTestMenuClick;

        if (GetTemplateChild(PART_GameDataMenu) is MenuItem gameDataMenu)
            gameDataMenu.Click += OnGameDataMenuClick;

        if (GetTemplateChild(PART_WaveBankMenu) is MenuItem waveBankMenu)
        {
            waveBankMenu.Click += OnWaveBankMenuClick;
        }

        if (GetTemplateChild(PART_MovieBankMenu) is MenuItem movieBankMenu)
        {
            movieBankMenu.Click += OnMovieBankMenuClick;
        }

        if (GetTemplateChild(PART_PortraitBookMenu) is MenuItem portraitBookMenu)
        {
            portraitBookMenu.Click += OnPortraitBookMenuClick;
        }

        if (GetTemplateChild(PART_CityCultureMenu) is MenuItem cityCultureMenu)
        {
            cityCultureMenu.Click += OnCityCultureMenuClick;
        }

        if (GetTemplateChild(PART_NationEditMenu) is MenuItem nationEditMenu)
        {
            nationEditMenu.Click += OnNationEditMenuClick;
        }

        if (GetTemplateChild(PART_FigureheadEditMenu) is MenuItem figureheadEditMenu)
        {
            figureheadEditMenu.Click += OnFigureheadEditMenuClick;
        }

        if (GetTemplateChild(PART_CultureEditMenu) is MenuItem cultureEditMenu)
        {
            cultureEditMenu.Click += OnCultureEditMenuClick;
        }

        if (GetTemplateChild(PART_DisevEditorMenu) is MenuItem disevEditorMenu)
        {
            disevEditorMenu.Click += OnDisevEditorMenuClick;
        }

        if (GetTemplateChild(PART_QuestEditorMenu) is MenuItem questEditorMenu)
        {
            questEditorMenu.Click += OnQuestEditorMenuClick;
        }

        if (GetTemplateChild(PART_VoyagerEditMenu) is MenuItem voyagerEditMenu)
        {
            voyagerEditMenu.Click += OnVoyagerEditMenuClick;
        }

        if (GetTemplateChild(PART_BuildingListMenu) is MenuItem buildingListMenu)
        {
            buildingListMenu.Click += OnBuildingListMenuClick;
        }

        if (GetTemplateChild(PART_PersonEditMenu) is MenuItem personEditMenu)
        {
            personEditMenu.Click += OnPersonEditMenuClick;
        }

        if (GetTemplateChild(PART_FormationMenu) is MenuItem formationMenu)
        {
            formationMenu.Click += OnFormationMenuClick;
        }

        if (GetTemplateChild(PART_FortuneMenu) is MenuItem fortuneMenu)
        {
            fortuneMenu.Click += OnFortuneMenuClick;
        }

        if (GetTemplateChild(PART_MenuDesignerMenu) is MenuItem menuDesignerMenu)
        {
            menuDesignerMenu.Click += OnMenuDesignerMenuClick;
        }

        if (GetTemplateChild(PART_TavernHintMenu) is MenuItem tavernHintMenu)
        {
            tavernHintMenu.Click += OnTavernHintMenuClick;
        }

        if (GetTemplateChild(PART_DiscoveryEditMenu) is MenuItem discoveryEditMenu)
        {
            discoveryEditMenu.Click += OnDiscoveryEditMenuClick;
        }

        if (GetTemplateChild(PART_BookEditMenu) is MenuItem bookEditMenu)
        {
            bookEditMenu.Click += OnBookEditMenuClick;
        }

        if (GetTemplateChild(PART_MotionMakerMenu) is MenuItem motionMakerMenu)
        {
            motionMakerMenu.Click += OnMotionMakerMenuClick;
        }

        if (GetTemplateChild(PART_ImageShrinkMenu) is MenuItem imageShrinkMenu)
        {
            imageShrinkMenu.Click += OnImageShrinkMenuClick;
        }

        if (GetTemplateChild(PART_VideoShrinkMenu) is MenuItem videoShrinkMenu)
        {
            videoShrinkMenu.Click += OnVideoShrinkMenuClick;
        }

        if (GetTemplateChild(PART_ShipRegistryMenu) is MenuItem shipRegistryMenu)
        {
            shipRegistryMenu.Click += OnShipRegistryMenuClick;
        }

        if (GetTemplateChild(PART_ShipMapMenu) is MenuItem shipMapMenu)
        {
            shipMapMenu.Click += OnShipMapMenuClick;
        }

        if (GetTemplateChild(PART_HelpMenu) is MenuItem helpMenu)
        {
            helpMenu.Click += OnHelpMenuClick;
        }


        if (GetTemplateChild(PART_WorldMapMenu) is MenuItem worldMapMenu)
        {
            worldMapMenu.Click += (_, _) => NavigateAndSync("WorldMapContent");
        }

        // 발견물 그림은 햄버거 차림표에서 「요소」를 거쳐 「에셋」 메뉴로 옮겼다(fb-ui-23) — 본문 자리에 그대로 띄운다.
        if (GetTemplateChild(PART_DiscoveryStillMenu) is MenuItem discoveryStillMenu)
        {
            discoveryStillMenu.Click += (_, _) => NavigateAndSync("DiscoveryStillContent");
        }

        _accordionMenu = GetTemplateChild(PART_AccordionMenu) as NavigationMenu;
        if (_accordionMenu != null)
        {
            _accordionMenu.ItemClickCommand = new DelegateCommand<string>(OnAccordionItemClick);
            _accordionMenu.SelectItemByTag(AppSettings.DefaultView);
        }

        OpenShipMapIfWanted();

        _menuPopup = GetTemplateChild(PART_MenuPopup) as Popup;
        _hamburgerButton = GetTemplateChild(PART_HamburgerButton) as Button;
        if (_hamburgerButton != null && _menuPopup != null)
        {
            _hamburgerButton.Click += (_, _) => _menuPopup.IsOpen = !_menuPopup.IsOpen;
            // AllowsTransparency=True + PopupAnimation 조합에서 첫 오픈 시
            // 팝업이 화면 (0,0)에 떴다가 제 위치로 점프하는 WPF 버그 회피.
            // off+1 로 바꾼 뒤 "다음 디스패처 사이클"에 off 로 되돌려야 실제 재배치가 일어난다.
            // (같은 호출 안에서 off+1; off; 하면 두 변경이 상쇄되어 재배치가 트리거되지 않음)
            _menuPopup.Opened += (_, _) =>
            {
                var popup = _menuPopup;
                if (popup == null) return;
                var off = popup.HorizontalOffset;
                popup.HorizontalOffset = off + 1;
                popup.Dispatcher.BeginInvoke(new Action(() => popup.HorizontalOffset = off),
                    System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        // ControlTemplate 내의 ContentControl에 Region 설정
        if (GetTemplateChild(PART_ContentRegion) is ContentControl contentRegion)
        {
            RegionManager.SetRegionManager(contentRegion, _regionManager);
            RegionManager.SetRegionName(contentRegion, "MainContentRegion");

            // 초기 Navigation (설정에서 지정한 기본 뷰)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _viewModel?.NavigateToContent(AppSettings.DefaultView);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // 창 로드 후 네이티브 DLL 다운로드 확인 → 업데이트 확인
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_viewModel == null) return;
            await _viewModel.CheckAndDownloadNativeDepsAsync();
            await _viewModel.CheckForUpdateAsync();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnAccordionItemClick(string? viewName)
    {
        System.Diagnostics.Debug.WriteLine($"[AccordionClick] viewName: {viewName}");
        if (!string.IsNullOrEmpty(viewName))
        {
            _viewModel?.NavigateToContent(viewName);
            // 네비게이션 후 햄버거 팝업 닫기
            if (_menuPopup != null) _menuPopup.IsOpen = false;
        }
    }

    private void NavigateAndSync(string viewName)
    {
        _viewModel?.NavigateToContent(viewName);
        _accordionMenu?.SelectItemByTag(viewName);
    }

    private void OnSettingsMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void OnSphinxMenuClick(object sender, RoutedEventArgs e)
    {
        _viewModel?.NavigateToContent("SphinxCalculatorContent");
    }

    private void OnEventQueueMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new EventQueueDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // WAVES.CDS 에 든 게임 효과음 50개를 늘어놓고 들어 보는 창.
    private void OnWaveBankMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WaveBankDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 앱이 EXE 에서 읽어 적어 둔 표들을 들여다보고 다시 굽는 창. 게임 햄버거에 있던 줄을
    // 여기로 옮겼다 — 놀면서 쓸 일이 없고 표를 손보는 일은 도구 앱 몫이다.
    private void OnGameDataMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.GameDataDialog.Show(this);

    // MISC.CDS 의 화면 조각을 PNG 로 뽑아 asset/ui 에 넣는다. 게임 창 「개발」에 있던 줄을
    // 여기로 옮겼다 — 놀면서 쓸 일이 없고 그림을 손보는 일은 도구 앱 몫이다.
    private void OnUiSpriteDumpMenuClick(object sender, RoutedEventArgs e)
    {
        string dir = System.IO.Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        MessageBox.Show(this,
            dir.Length == 0 || !System.IO.Directory.Exists(dir)
                ? "게임 폴더를 아직 모릅니다 — 세이브 파일을 먼저 열어 주십시오"
                : CdsHelper.Game.UI.Views.UiSpriteDump.Run(dir),
            "화면 조각");
    }

    // 게임 AVI 동영상을 늘어놓고, 갈아 끼울 동영상을 asset/movie 에 올리는 창.
    private void OnMovieBankMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new MovieBankDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // MALE.CDS · FEMALE.CDS 의 얼굴을 번호와 함께 늘어놓는 창. 게임 자료가 사람을
    // 얼굴 번호로 가리키므로(인물표 · 후원자표 · 시설 화자표) 그 번호를 찾아볼 데가 필요하다.
    private void OnPortraitBookMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CdsHelper.Game.UI.Views.PortraitBookDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 도시마다의 문화권과, 그 문화권이 부르는 시설 화자 얼굴을 맞대어 보는 창.
    private void OnCityCultureMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CdsHelper.Game.UI.Views.CityCultureDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 나라 이름·쓰는 말·수도를 고치는 창. 고친 것은 놀이에도 그대로 쓰인다.
    private void OnNationEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.NationEditDialog.Show(this);

    private void OnFigureheadEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.FigureheadEditDialog.Show(this);

    // 도시마다의 문화권만 고치는 창 — 「도시 · 문화권 · 왕국」에서 떼어 요소로 뽑았다.
    private void OnCultureEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.CityCultureEditDialog.Show(this);

    // DISEV.CDS 의 발견 이벤트 스크립트를 보고 고치는 창. 게임 파일을 직접 고치므로
    // 저장할 때 옆에 시각을 붙인 백업을 남긴다.
    private void OnDisevEditorMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.DisevEditorDialog.Show(this);

    // 새 주인공(NORMAL)의 직업별 퀘스트 — 같은 편집기를 개인 이야기 책(PEX~ECQ)부터 펴서 연다.
    // 책 콤보에서 국적 x 직업 여덟 책을 오가고, 장면마다 「조건 · 첫 대사」 이름표가 붙는다.
    private void OnQuestEditorMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.DisevEditorDialog.Show(this, "PEX");

    // 역사 항해자 열넷이 언제 무엇을 채가는지 고치는 창. 이 놀이의 유일한 경쟁자다.
    private void OnVoyagerEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.VoyagerEditDialog.Show(this);

    private void OnBuildingListMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.BuildingListDialog.Show(this);

    // 세이브의 인물 281명을 고치는 창. 나라 표와 달리 세이브를 그 자리에서 고치므로
    // 처음 고칠 때 시각을 붙인 백업을 옆에 남긴다.
    private void OnPersonEditMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.PersonEditDialog.Show(this);

    // 적이 내는 부대 세트 여덟 벌을 보고 고치는 창. 나라를 고르면 그 나라 수도의
    // 문화권으로 진형이 정해진다 — 맘루크는 이슬람 상비군, 무로마치는 일본 무가군이다.
    private void OnFormationMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.LandFormationDialog.Show(this);

    // 여급 127명의 궁합 코드를 보고 고치는 창. 운명 코드는 0~31 한 덩어리라
    // 아래 열여섯이 젊은 제독, 위 열여섯이 그 중년 몫이다.
    private void OnFortuneMenuClick(object sender, RoutedEventArgs e) =>
        CdsHelper.Game.UI.Views.FortuneDialog.Show(this);

    // 게임 창들의 꼴을 손잡이로 짜 보는 창. 단추 여백만 놀이에 곧바로 든다.
    private void OnMenuDesignerMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CdsHelper.Game.UI.Views.MenuDesignerDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 힌트 186줄을 보고 고치거나 새로 더하는 창. 가리키는 발견물·수록된 책과, 술집 주인이
    // 어느 쪽으로 가라 이를지(곁다리 정보 한 칸)를 함께 내어 짝이 어긋난 줄을 눈으로 찾는다.
    private void OnTavernHintMenuClick(object sender, RoutedEventArgs e)
    {
        CdsHelper.Game.UI.Views.TavernHintEditDialog.Show(this);
    }

    // 발견물 274줄을 보고 고치거나, 원본에 없던 새 발견물을 더하는 창.
    private void OnDiscoveryEditMenuClick(object sender, RoutedEventArgs e)
    {
        CdsHelper.Game.UI.Views.DiscoveryEditDialog.Show(this);
    }

    // 책 257권을 보고 고치거나, 원본에 없던 새 책을 더하는 창.
    private void OnBookEditMenuClick(object sender, RoutedEventArgs e)
    {
        CdsHelper.Game.UI.Views.BookEditDialog.Show(this);
    }

    // 일기토 그림을 늘어놓고 번호를 적어 이어 돌려 보는 창. 몸짓 차례를 코드에 적기
    // 앞서 눈으로 맞춰 보는 데 쓴다.
    private void OnMotionMakerMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CdsHelper.Game.UI.Views.MotionMakerDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 그림 파일을 골라 크기·용량을 줄이는 창. 게임과는 상관없는 손도구다.
    private void OnImageShrinkMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ImageShrinkDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 동영상의 크기를 줄이고 소리를 빼고 압축하는 창. 이것도 게임과는 상관없는 손도구다.
    private void OnVideoShrinkMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new VideoShrinkDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 이 앱이 품은 놀이의 조선소에 낼 배를 등록하는 창. 게임 EXE 는 건드리지 않는다.
    private void OnShipRegistryMenuClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ShipRegistryDialog
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    // 게임 화면처럼 지도 위에 함대만 띄우는 창. 세계지도 탭과 달리 D3D 로 그린다.
    private void OnShipMapMenuClick(object sender, RoutedEventArgs e) => OpenShipMap();

    /// <summary>
    /// 사운드테스트 — 게임 타이틀에 있던 줄을 이리로 옮겼다(<c>0x0045FBCD</c>).
    /// </summary>
    /// <remarks>
    /// 소리를 트는 것은 놀이 쪽 <c>ShipMapWindow</c> 다(배경음악·효과음 둘 다 그쪽이 쥐고 있다).
    /// 이미 떠 있는 창이 있으면 그것에 물어보고, 없으면 새로 띄워 놓고 부른다.
    /// </remarks>
    private void OnSoundTestMenuClick(object sender, RoutedEventArgs e)
    {
        var map = Application.Current.Windows.OfType<CdsHelper.Game.UI.Views.ShipMapWindow>().FirstOrDefault();
        if (map == null)
        {
            OpenShipMap();
            map = Application.Current.Windows.OfType<CdsHelper.Game.UI.Views.ShipMapWindow>().FirstOrDefault();
        }
        if (map == null) return;

        map.Activate();
        map.SoundTest();
    }

    private void OpenShipMap()
    {
        // 미궁 64 퍼즐은 CdsHelper.Maze 에 따로 있다. 그쪽이 CdsHelper.Game 을 물고
        // 있어서 게임 쪽에서 곧장 못 부른다 — 띄우는 여기서 걸어 준다.
        CdsHelper.Game.UI.Views.ShipMapWindow.MazeGame = CdsHelper.Maze.MazeGame.Play;

        var win = new CdsHelper.Game.UI.Views.ShipMapWindow { Owner = this };
        win.Show();
    }

    /// <summary>설정에서 켜 뒀으면 앱을 띄울 때 함대 보기도 같이 연다.</summary>
    private void OpenShipMapIfWanted()
    {
        if (!GameSettings.AutoOpenShipMap) return;
        // 본 창이 자리를 잡은 뒤에 연다 — Owner 가 아직 뜨지 않은 채로 열면 가운데가 안 맞는다.
        Dispatcher.BeginInvoke(new Action(OpenShipMap), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnHelpMenuClick(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
        MessageBox.Show($"CDS Helper\n버전: {version}", "도움말", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnDbTableViewerMenuClick(object sender, RoutedEventArgs e)
    {
        var dbContext = ContainerLocator.Container.Resolve<AppDbContext>();
        var dialog = new DbTableViewerDialog(dbContext)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

}
