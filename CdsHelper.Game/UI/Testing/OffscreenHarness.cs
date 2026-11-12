using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Testing;

/// <summary>
/// <b>화면 밖 시험 장치</b> — 게임 창을 사람 화면 밖에서 띄우고, 바로 바다로 들이고, 화면을 PNG 로 떠 준다.
/// </summary>
/// <remarks>
/// 스위치는 모두 환경 변수다(<see cref="OffscreenTest"/>). 하나도 없으면 <see cref="Prepare"/> · <see cref="Attach"/> 가
/// 아무것도 안 한다 — 게임을 그냥 켜면 예전과 같다.
/// <code>
///   창 자리        CDS_OFFSCREEN=1 이면 (-8000, -8000) 에 포커스 · 작업 표시줄 단추 없이. 이 실(UI 스레드)의 창은
///                  아예 활성이 안 된다(CBT 훅) — 대화 창이 Activate() 로 사람의 포커스를 뺏지 않고, 커서도 배를 안 몬다.
///   스냅샷         창에 PostMessage(0x8001) → OffscreenTest.SnapshotPath(%TEMP% 또는 CDS_SAVEDIR 의 cds_snapshot.png).
///                  WPF 화면(RenderTargetBitmap) 위에 지도 칸(D3D 스왑체인 백버퍼)과 딸린 창(도시 화면 · 대화 창)을 겹쳐 그린다.
///   바로 시작      CDS_START=sea:&lt;도시&gt;:&lt;선체 key&gt; (+ CDS_HEADING · CDS_SAIL) — WorldMapWindow.StartAtSeaForTest.
///                  CDS_START=land:&lt;도시&gt;:&lt;선체 key&gt;:&lt;x&gt;:&lt;y&gt; — 말을 그 칸에 세운다(발견물 자리 사건 밟기, StartAshoreForTest).
///   버그 리포트 창 CDS_DIALOG=bugreport — 왼쪽 위 메뉴로 연 것처럼 띄운다(그동안 배 · 말이 서는지 본다).
///   내 리포트 창   CDS_DIALOG=mybugs — 버그 리포트의 「내 리포트」 창을 띄운다(받는 곳은 CDS_STATS_URL).
///   개발 창       CDS_DIALOG=dev:&lt;탭&gt; — 개발 창을 그 탭(「에디터」 따위)으로 띄운다.
///   발견물 추가   CDS_DIALOG=userdiscovery[:&lt;탭&gt;] — 커스텀 발견물 창을 그 탭(「아이템」 따위)으로 띄운다. CDS_ITEMPIC=&lt;그림&gt; 이면 아이템 그림 칸에 올린다.
///   모드 창       CDS_DIALOG=mod[:찾는 말] — 모드 창을 띄우고 찾기 칸에 그 말을 넣는다.
///   누적 캐릭터    CDS_DIALOG=acc[:del:&lt;자리&gt;][,hide:&lt;자리&gt;] — 누적 캐릭터를 등장시킨 게임으로 두고 누적 캐릭터 창을 띄운다(지우기 · 등장 안 함).
///   인물 이동 성문 CDS_DIALOG=personmove-gate:&lt;도시&gt; — 도시에 들어가 인물 이동 창을 띄운 채 성문으로 나서고 닫는다(묻는 중 · 멈춤이 남는지).
///   매매 창       CDS_DIALOG=trade:&lt;도시&gt; — 그 도시 교역소 매매 창을 짐 셋을 실은 채 띄운다.
///   스폰서 일람    CDS_DIALOG=patrons[:&lt;명&gt;][:story:&lt;명&gt;][:year:&lt;해&gt;][:city:&lt;도시&gt;] — 후원자를 만난 것으로 두고 일람을 띄워 휠 · 글쇠 · 굴림대로 굴려 본다.
///   도서관 서가    CDS_DIALOG=library:&lt;도시&gt;[:menu[:&lt;글쇠&gt;]] — 서가를 띄우고, menu 면 오른쪽 단추로 「서적 선택」을 낸다.
///   미니 게임 프레임 CDS_DIALOG=minigame:&lt;fish|cube|none&gt;[:city:&lt;도시&gt;][:secs:&lt;초&gt;] — 미니 게임을 띄워 두고 WPF · 지도 프레임과 그림이 바뀐 틈을 잰다.
///   타이틀 글쇠    CDS_TITLE=Down,Enter — 타이틀에서 1초 간격으로 누른다(LOAD GAME). CDS_AUTOKEY=Enter 면 새로 뜬 창마다 글을 적고 그 글쇠를 보낸다.
///   찍고 끄기      CDS_SHOT=&lt;png&gt;:&lt;초&gt;[;&lt;png&gt;:&lt;초&gt;…] — 창이 뜬 지 그 초에 찍고, 마지막 것을 찍으면 창을 닫는다.
/// </code>
/// 무엇을 했는지는 <c>OffscreenTest.OutputFolder\cds_test.log</c> 에 한 줄씩 적는다. 창 핸들은 <see cref="OffscreenTest.HwndFile"/>.
///
/// 못 찍는 것: WPF <c>Popup</c>(좌표 상자 · 미니맵 · 비 · 눈 — 어차피 창이 활성일 때만 뜬다)과 딴 실의 창(불러오는 쪽지).
/// </remarks>
public static class OffscreenHarness
{
    /// <summary>화면 밖 창 자리(DIP). duel-dx 와 같은 값이다.</summary>
    public const double OffscreenLeft = -8000, OffscreenTop = -8000;

    /// <summary>스냅샷 때 지도 프레임을 기다리는 끝 — 지도가 안 돌면(타이틀) 이만큼 뒤 WPF 화면만 뜬다.</summary>
    private static readonly TimeSpan GrabWait = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// 창을 띄우기 <b>전에</b> 부른다 — 화면 밖 자리 · 포커스 안 뺏기 · 작업 표시줄 단추 없음을 건다.
    /// <c>CDS_OFFSCREEN</c> 이 없으면 아무것도 안 한다.
    /// </summary>
    public static void Prepare(Window window)
    {
        if (!OffscreenTest.Enabled) return;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = OffscreenLeft;
        window.Top = OffscreenTop;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        BlockActivation();
    }

    /// <summary>
    /// 게임 창에 시험 장치를 붙인다 — 스냅샷 메시지, 창 핸들 파일, 바로 시작, 찍고 끄기. 창을 띄우기 전에 부른다.
    /// 훅이 하나도 없으면(<see cref="OffscreenTest.Any"/>) 아무것도 안 한다.
    /// </summary>
    public static void Attach(WorldMapWindow window)
    {
        if (!OffscreenTest.Any) return;

        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg != OffscreenTest.SnapshotMessage) return IntPtr.Zero;
                handled = true;
                Save(window, OffscreenTest.SnapshotPath, null);
                return IntPtr.Zero;
            });
            try
            {
                Directory.CreateDirectory(OffscreenTest.OutputFolder);
                File.WriteAllText(OffscreenTest.HwndFile, handle.ToInt64().ToString());
            }
            catch (Exception ex) { Log($"창 핸들을 적지 못했다 — {ex.Message}"); }
            Log($"창 {handle.ToInt64()} · 화면 밖 {OffscreenTest.Enabled} · 소리 끔 {OffscreenTest.Muted} · 자료 {UserDataFolder.Roaming}");
        };

        // 창의 OnLoaded(게임 폴더 · 그림 · 타이틀)가 다 돈 뒤에 — 그 처리기가 먼저 걸려 있다.
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            if (OffscreenTest.Start is { } start) RunStart(window, start);
            // CDS_NAV=<도시> — 바로 시작한 뒤 네비게이션으로 그 도시에 자동항해를 걸고, 1초마다 배 자리 · 끝 · 뜬 창을 적는다.
            if (Environment.GetEnvironmentVariable("CDS_NAV") is { } nav && int.TryParse(nav, out int navCity))
                RunNav(window, navCity);
            ScheduleShots(window);
            // CDS_TITLE=Down,Enter — 타이틀에서 그 글쇠들을 1초 간격으로 누른다(LOAD GAME · CONTINUE 따위).
            // CDS_AUTOKEY=Enter — 새로 뜬 창마다 그 글을 기록에 적고, 그 글쇠를 한 번 보낸다(목록은 맨 위 칸 · 물음은 「예」).
            if (Environment.GetEnvironmentVariable("CDS_TITLE") is { Length: > 0 } title) RunTitleKeys(window, title);
            // CDS_DIALOG=bugreport — 왼쪽 위 메뉴로 연 것처럼 버그 리포트 창을 띄운다. 닫힐 때까지 돌아오지 않는다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") == "bugreport")
                window.Dispatcher.BeginInvoke(() =>
                {
                    Log("버그 리포트 창을 띄운다");
                    window.OpenBugReportForTest();
                    Log("버그 리포트 창이 닫혔다");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=personmove-gate:<도시> — 그 도시에 들어가 인물 이동 창을 띄운 채 성문으로 나서고 닫는다(버그 리포트 #44).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } gate && gate.StartsWith("personmove-gate:")
                && int.TryParse(gate["personmove-gate:".Length..], out int gateCity))
                window.Dispatcher.BeginInvoke(() => window.PersonMoveGateForTest(gateCity, Log), DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=sound — 효과음 하나를 내고 배경음악이 도는 동안 소리 진단(버그 리포트에 싣는 것)을 2초마다 다섯 번 적는다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") == "sound")
            {
                int left = 5;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) =>
                {
                    SoundBank.Current?.Play(SoundBank.AnchorPart);
                    Log("소리 " + string.Join(" | ", SoundDiagnostics.Collect().Select(p => $"{p.Key}={p.Value}")));
                    if (--left == 0) timer.Stop();
                };
                timer.Start();
            }
            // CDS_DIALOG=storm — 바로 시작한 바다에서 폭풍 장면을 돌리고, 앞뒤와 도는 동안 소리 진단(장치 음량 · 앱 음량 · 곡)을 적는다(버그 리포트 #45).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") == "storm")
                window.Dispatcher.BeginInvoke(() =>
                {
                    void Sound(string when) => Log($"소리 {when} " + string.Join(" | ", SoundDiagnostics.Collect().Select(p => $"{p.Key}={p.Value}")));
                    Sound("폭풍 앞");
                    var during = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    during.Tick += (_, _) => Sound("폭풍 중");
                    during.Start();
                    window.StormSceneForTest();
                    during.Stop();
                    Sound("폭풍 뒤");
                    var after = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    int left = 3;
                    after.Tick += (_, _) => { Sound("폭풍 뒤 " + (4 - left) * 2 + "초"); if (--left == 0) after.Stop(); };
                    after.Start();
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=landbattle-sound — 육상전이 곡을 다루는 차례(전투 곡 → 끝맺음 숨죽임 → 듣던 곡)와 해전 · 일기토의 승패 소리
            // 멈춤(Pause → Resume)을 그대로 밟으며 1초마다 곡 상태 · 장치 음량 자취를 적는다(버그 리포트 #46 · #47). 흐름이 끊기면 State 가 Idle 이 된다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") == "landbattle-sound")
                window.Dispatcher.BeginInvoke(() =>
                {
                    var bgm = window.CurrentGame.Bgm;
                    void Sound(string when) => Log($"소리 {when} bgm={bgm.Describe()} | device={SoundDiagnostics.Collect()["device"]}"
                                                   + $" | deviceChanges={SoundDiagnostics.Collect()["deviceChanges"]}");
                    int was = bgm.Track;
                    var steps = new (string Name, Action Act)[]
                    {
                        ("육상전 시작(전투 곡)", () => bgm.Play(BgmPlayer.BattleTrack)),
                        ("싸우는 중", () => { }),
                        ("끝맺음(숨죽임 · 알림 창)", () => bgm.Hush()),
                        ("알림 창 1초", () => { }),
                        ("알림 창 2초", () => { }),
                        ("육상전 끝(듣던 곡)", () => LandBattleScene.RestoreMusic(bgm, was)),
                        ("승패 소리 멈춤(Pause)", () => bgm.Pause()),
                        ("승패 소리 1초", () => { }),
                        ("이음(Resume)", () => bgm.Resume()),
                        ("끝", () => { }),
                    };
                    int at = 0;
                    Sound("앞");
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    timer.Tick += (_, _) =>
                    {
                        var (name, act) = steps[at];
                        act();
                        Sound(name);
                        if (++at == steps.Length) timer.Stop();
                    };
                    timer.Start();
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=mybugs — 버그 리포트의 「내 리포트」 창을 띄운다(받는 곳은 CDS_STATS_URL).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") == "mybugs")
                window.Dispatcher.BeginInvoke(() =>
                {
                    Log("내 리포트 창을 띄운다");
                    // 창은 주인 가운데(CenterOwner)가 아니라 실제 화면 가운데에 선다 — 주인이 -8000 이라 스냅숏 밖이다. 뜬 뒤 주인 안으로 옮긴다.
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                            if (w is MyBugReportsDialog) { w.Left = window.Left + 80; w.Top = window.Top + 80; }
                    }, DispatcherPriority.ApplicationIdle);
                    MyBugReportsDialog.Open(window);
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=livemap — 도시락 「세계지도」 창을 띄운다(모달이 아니다 — CDS_SAIL 과 함께 쓰면 배를 따라가는지 본다).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") == "livemap")
                window.Dispatcher.BeginInvoke(() =>
                {
                    Log("세계지도 창을 띄운다");
                    GameSettings.ShowLiveMapMenu = true;   // 뱃길은 이 모드가 켜져 있어야 적힌다
                    window.OpenLiveMapForTest();
                    foreach (Window w in window.OwnedWindows)
                        if (w is LiveMapWindow) { w.Left = window.Left + 60; w.Top = window.Top + 60; }
                }, DispatcherPriority.ApplicationIdle);
            // CDS_FOUND=<이름>[,<이름>…] — 그 이름의 발견물들을 찾은 것으로 적는다(사람 발견물 그림이 지도에 서는지 본다).
            if (Environment.GetEnvironmentVariable("CDS_FOUND") is { Length: > 0 } found)
                window.Dispatcher.BeginInvoke(() =>
                    Log("찾은 것으로 적었다 — " + string.Join(", ", window.DiscoverForTest(found.Split(',')))),
                    DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=areapick[:x1,y1,x2,y2[,land]] — 사람 발견물 창의 「지도에서 고르기…」 창(세계지도 고르기 모드)을 띄운다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } pickDialog && (pickDialog == "areapick" || pickDialog.StartsWith("areapick:")))
                window.Dispatcher.BeginInvoke(() =>
                {
                    var n = pickDialog.Length > 9 ? pickDialog[9..].Split(',').Select(t => int.TryParse(t, out int v) ? v : -1).ToArray() : [];
                    DiscoveryAreaPicker.Area? start = n.Length >= 4 ? new(n[0], n[1], n[2], n[3], n.Length > 4 && n[4] == 1) : null;
                    Log($"자리 고르기 창을 띄운다 — 처음 범위 {start?.ToString() ?? "(없음)"}");
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                            if (w is LiveMapWindow) { w.Left = window.Left + 20; w.Top = window.Top + 20; }
                    }, DispatcherPriority.ApplicationIdle);
                    var got = window.PickAreaForTest(start);
                    Log($"자리 고르기 창이 닫혔다 — {got?.ToString() ?? "(그만둠)"}");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=mod[:찾는 말] — 모드 창을 띄우고 찾기 칸에 그 말을 넣는다. 창은 주인 안으로 옮겨 스냅숏에 들게 한다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } modDialog && (modDialog == "mod" || modDialog.StartsWith("mod:")))
                window.Dispatcher.BeginInvoke(() =>
                {
                    string? query = modDialog.Length > 4 ? modDialog[4..] : null;
                    Log($"모드 창을 띄운다 — 찾는 말 {query ?? "(없음)"}");
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                            if (w is ModDialog) { w.Left = window.Left + 20; w.Top = window.Top + 20; }
                    }, DispatcherPriority.ApplicationIdle);
                    window.OpenModForTest(query);
                    Log("모드 창이 닫혔다");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=acc[:del:<자리>][,hide:<자리>] — 누적 캐릭터를 등장시킨 게임으로 두고 누적 캐릭터 창을 띄운다. del 이면 그 자리(0~4)
            // 사람을 묻지 않고 지운 뒤 json · 인물 276~280 · 행적 대본을 적고, 세상을 새로 지어(불러오기와 같은 길) 되살아나지 않는지 본다.
            // hide 면 그 자리 사람의 「등장 안 함」을 켠다(json 깃발만 — 지금 게임은 그대로).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } accDialog && (accDialog == "acc" || accDialog.StartsWith("acc:")))
                window.Dispatcher.BeginInvoke(() =>
                {
                    string Names() => string.Join(", ", Engine.AccData.Load().Select(c => $"{c.Name}[{c.Id}]{(c.Hidden ? "(숨김)" : "")}"));
                    Log($"누적 캐릭터 파일 {Engine.AccData.Path} — {Names()}");
                    Log("앉힘 " + window.CumulativeStateForTest(open: true));
                    var ops = accDialog.Length > 4 ? accDialog[4..].Split(',') : [];
                    int Slot(string op) => ops.Select(o => o.Split(':')).Where(p => p.Length == 2 && p[0] == op)
                                              .Select(p => int.TryParse(p[1], out int n) ? n : -1).DefaultIfEmpty(-1).First();
                    int slot = Slot("del"), hideSlot = Slot("hide");
                    window.OpenCumulativeForTest(dialog =>
                    {
                        // 주인이 -8000 이라 CenterOwner 가 실제 화면 가운데에 세운다 — 자리를 다 잡은 뒤 주인 안으로 옮겨 스냅숏에 들게 한다.
                        window.Dispatcher.BeginInvoke(() => { dialog.Left = window.Left + 40; dialog.Top = window.Top + 20; },
                                                      DispatcherPriority.ApplicationIdle);
                        var seats = window.CurrentGame.Player.AccSeats;
                        string? SeatId(int at) => seats != null && at >= 0 && at < seats.Count ? seats[at] : null;
                        if (SeatId(hideSlot) is { } hideId)
                        {
                            dialog.SetHidden(hideId, true);
                            Log($"자리 {hideSlot} 등장 안 함 [{hideId}] — 파일 {Names()} · 새 게임 자리 {string.Join(",", Engine.AccData.SeatsForNewGame())}");
                        }
                        if (slot < 0) return;
                        string? id = SeatId(slot);
                        Log($"자리 {slot} 지우기 [{id}] — {(id != null && dialog.Delete(id, ask: false) ? "지웠다" : "못 지웠다")}");
                        Log($"지운 뒤 파일 — {Names()}");
                        Log("지운 뒤 " + window.CumulativeStateForTest(open: false));
                        window.RebuildWorldForTest();
                        Log("다시 지은 뒤 " + window.CumulativeStateForTest(open: false));
                    });
                    Log("누적 캐릭터 창이 닫혔다");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=dev:<탭> — 개발 창을 그 탭(「에디터」 따위)으로 띄운다. 창은 주인 안으로 옮겨 스냅숏에 들게 한다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } devDialog && devDialog.StartsWith("dev:"))
                window.Dispatcher.BeginInvoke(() =>
                {
                    Log($"개발 창을 띄운다 — 탭 {devDialog[4..]}");
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                            if (w is DevDialog) { w.Left = window.Left + 40; w.Top = window.Top + 20; }
                    }, DispatcherPriority.ApplicationIdle);
                    window.OpenDevForTest(devDialog[4..]);
                    Log("개발 창이 닫혔다");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=userdiscovery[:<탭>] — 커스텀 발견물 창을 그 탭으로 띄운다. CDS_ITEMPIC=<그림> 이면 고른 아이템의 그림 칸에 올린다(끌기 전 첫 자리).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } userDialog && (userDialog == "userdiscovery" || userDialog.StartsWith("userdiscovery:")))
                window.Dispatcher.BeginInvoke(() =>
                {
                    string? tab = userDialog.Length > 14 ? userDialog[14..] : null;
                    Log($"커스텀 발견물 창을 띄운다 — 탭 {tab ?? "(발견물)"}");
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                            if (w is UserDiscoveryDialog found)
                            {
                                (w.Left, w.Top) = (window.Left + 10, window.Top + 10);
                                if (Environment.GetEnvironmentVariable("CDS_ITEMPIC") is { Length: > 0 } picture)
                                    Log($"아이템 그림 올리기 — {found.LoadItemPicture(picture)}");
                            }
                    }, DispatcherPriority.ApplicationIdle);
                    window.OpenUserDiscoveriesForTest(tab);
                    Log("커스텀 발견물 창이 닫혔다");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=childname — 자녀 이름 다시 짓기 창(선명입력 창)을 띄우고, 뜬 뒤 그 창의 초점이 어디 갔는지 적은 다음 닫는다.
            // 화면 밖 창은 활성이 안 되므로 글쇠 초점(Keyboard)이 아니라 창이 쥔 논리 초점(FocusManager)을 본다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") == "childname")
                window.Dispatcher.BeginInvoke(() =>
                {
                    Log("자녀 이름 창을 띄운다");
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                            if (w is ShipNameDialog)
                            {
                                var held = System.Windows.Input.FocusManager.GetFocusedElement(w);
                                string text = held is System.Windows.Controls.TextBox box ? $" 「{box.Text}」 고른 {box.SelectionLength}자" : "";
                                Log($"자녀 이름 창 초점 — {held?.GetType().Name ?? "(없음)"}{text}");
                                w.Close();
                            }
                    }, DispatcherPriority.ApplicationIdle);
                    var got = ShipNameDialog.Ask(window, "후안", ["후안", "카를로스"], "아들의 이름", 8);
                    Log($"자녀 이름 창이 닫혔다 — {got ?? "(중단)"}");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=trade:<도시> — 그 도시 교역소 매매 창을 짐을 실은 채 띄운다. CDS_START 로 그 도시 앞바다에 든 뒤에 쓴다.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } dialog && dialog.StartsWith("trade:")
                && int.TryParse(dialog[6..], out int tradeCity))
                window.Dispatcher.BeginInvoke(() =>
                {
                    Log($"매매 창을 띄운다 — 도시 {tradeCity}");
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                            if (w is TradePostDialog) { w.Left = window.Left + 40; w.Top = window.Top + 40; }
                    }, DispatcherPriority.ApplicationIdle);
                    string error = window.OpenTradeForTest(tradeCity);
                    if (error.Length > 0) Log($"매매 창 실패 — {error}");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=library:<도시>[:menu[:<글쇠>]] — 그 도시 도서관 서가를 띄운다. menu 면 오른쪽 단추를 눌러
            // 「서적 선택」 차림표를 내고, 글쇠를 주면 차림표에 그 글쇠를 한 번 보낸다(Up 이면 맨 끝 줄로 감겨 굴러간다).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } lib && lib.StartsWith("library:")
                && lib.Split(':') is { Length: >= 2 } libParts && int.TryParse(libParts[1], out int libCity))
                window.Dispatcher.BeginInvoke(() =>
                {
                    Log($"도서관 서가를 띄운다 — 도시 {libCity}");
                    window.Dispatcher.BeginInvoke(() =>
                    {
                        foreach (Window w in window.OwnedWindows)
                        {
                            if (w is not LibraryDialog shelf) continue;
                            shelf.Left = window.Left + 40;
                            shelf.Top = window.Top + 40;
                            if (libParts.Length < 3 || libParts[2] != "menu") continue;
                            window.Dispatcher.BeginInvoke(() =>
                            {
                                foreach (Window c in shelf.OwnedWindows)
                                {
                                    c.Left = shelf.Left + 120;      // 화면 밖이면 가운데 놓기가 (0, 0) 으로 빗나간다
                                    c.Top = shelf.Top + 60;
                                    Log($"차림표가 떴다 — {c.GetType().Name} · 자리 ({c.Left:0}, {c.Top:0}) · 크기 {c.ActualWidth:0}x{c.ActualHeight:0} · 보임 {c.IsVisible}");
                                    if (libParts.Length >= 4 && Enum.TryParse(libParts[3], true, out System.Windows.Input.Key libKey))
                                        Press(c, libKey);
                                }
                            }, DispatcherPriority.ApplicationIdle);
                            Log("서가에 오른쪽 단추를 누른다");
                            shelf.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                                                                                         System.Windows.Input.MouseButton.Right)
                                { RoutedEvent = UIElement.MouseRightButtonUpEvent });
                        }
                    }, DispatcherPriority.ApplicationIdle);
                    string error = window.OpenLibraryForTest(libCity);
                    if (error.Length > 0) Log($"도서관 서가 실패 — {error}");
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=patrons[:<명>][:story:<명>][:year:<해>][:city:<도시>] — 후원자를 그만큼 만난 것으로 두고(story 는 대본처럼 표 이름으로 더)
            // 스폰서 일람을 띄운 뒤, 휠 · 글쇠 · 굴림대 화살표로 아래 줄까지 가는지 굴림 자리를 적고 걸음마다 창을 찍는다(버그 리포트 #48).
            // city 면 도시 커맨드 길, 아니면 바다 「정보」 길.
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } patronDialog
                && (patronDialog == "patrons" || patronDialog.StartsWith("patrons:")))
                window.Dispatcher.BeginInvoke(() =>
                {
                    var parts = patronDialog.Split(':');
                    int meet = parts.Length > 1 && int.TryParse(parts[1], out int m) ? m : 15;
                    int Opt(string key) => Array.IndexOf(parts, key) is int at and > 0 && at + 1 < parts.Length
                                           && int.TryParse(parts[at + 1], out int v) ? v : -1;
                    window.Dispatcher.BeginInvoke(() => ProbeList(window, "patrons", 10), DispatcherPriority.ApplicationIdle);
                    Log("스폰서 일람 — " + window.OpenPatronsForTest(meet, Opt("city"), Math.Max(0, Opt("story")), Opt("year")));
                }, DispatcherPriority.ApplicationIdle);
            // CDS_DIALOG=minigame:<fish|cube>[:city:<도시>][:secs:<초>] — 미니 게임을 띄워 두고 1초마다 WPF 프레임 · 지도 프레임 · CPU 를 적는다(버그 리포트 #49).
            if (Environment.GetEnvironmentVariable("CDS_DIALOG") is { } miniDialog && miniDialog.StartsWith("minigame:"))
                window.Dispatcher.BeginInvoke(() => RunMinigameProbe(window, miniDialog), DispatcherPriority.ApplicationIdle);
        }, DispatcherPriority.ApplicationIdle);
    }

    // ── 미니 게임 프레임 ────────────────────────────────────────────────────

    /// <summary>
    /// <c>CDS_DIALOG=minigame:&lt;fish|cube&gt;[:city:&lt;도시&gt;][:secs:&lt;초&gt;]</c>(버그 리포트 #49) — 미니 게임을 띄워 두고 1초마다
    /// WPF 프레임 수 · 가장 긴 프레임 틈 · 33ms 넘은 틈 수 · 지도(D3D) 프레임 수 · Background 우선도 지연(미니 게임 타이머가 서는 줄) ·
    /// CPU 를 적는다. 설명 · 결과 알림은 저절로 닫고, 입방체는 0.7초마다 방향키 · 스페이스를 보낸다. 그 초가 지나면 창을 닫는다.
    /// </summary>
    private static void RunMinigameProbe(WorldMapWindow window, string spec)
    {
        var parts = spec.Split(':');
        string which = parts.Length > 1 ? parts[1] : "fish";
        int Opt(string key, int fallback) => Array.IndexOf(parts, key) is int at and > 0 && at + 1 < parts.Length
                                             && int.TryParse(parts[at + 1], out int v) ? v : fallback;
        int city = Opt("city", -1), secs = Opt("secs", 12);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan lastCpu = proc.TotalProcessorTime, lastRender = TimeSpan.Zero;
        double lastSecond = 0, maxGap = 0, maxLag = 0, sumGap = 0;
        int frames = 0, slow = 0, second = 0;
        long lastMap = window.MapHost.FrameCount;
        bool pinging = false;
        var all = new List<double>();
        // 미니 게임 그림이 실제로 바뀐 횟수 — 캔버스 그림들의 자리 · 다각형 점 묶음이 바뀌면 한 번으로 센다.
        int moves = 0;
        double lastMove = -1, maxStill = 0;
        long lastSig = 0;
        var stills = new List<double>();
        long Signature(Window w)
        {
            long sig = 17;
            void Walk(DependencyObject node)
            {
                for (int i = 0, n = VisualTreeHelper.GetChildrenCount(node); i < n; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is UIElement { Visibility: Visibility.Visible } ui && VisualTreeHelper.GetParent(ui) is System.Windows.Controls.Canvas)
                    {
                        double l = System.Windows.Controls.Canvas.GetLeft(ui), t = System.Windows.Controls.Canvas.GetTop(ui);
                        sig = sig * 31 + (double.IsNaN(l) ? 0 : (long)(l * 16));
                        sig = sig * 31 + (double.IsNaN(t) ? 0 : (long)(t * 16));
                        if (ui is System.Windows.Shapes.Polygon poly) sig = sig * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(poly.Points);
                        if (ui is System.Windows.Controls.Image img && img.Source is { } src) sig = sig * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(src);
                    }
                    Walk(child);
                }
            }
            Walk(w);
            return sig;
        }

        void OnRender(object? sender, EventArgs e)
        {
            var at = ((RenderingEventArgs)e).RenderingTime;
            if (at == lastRender) return;
            if (lastRender != TimeSpan.Zero)
            {
                double gap = (at - lastRender).TotalMilliseconds;
                frames++; sumGap += gap; all.Add(gap);
                maxGap = Math.Max(maxGap, gap);
                if (gap > 33.5) slow++;
            }
            lastRender = at;

            // Background 우선도 일이 얼마나 늦게 도는지 — DispatcherTimer 의 기본 우선도가 이것이다.
            if (!pinging)
            {
                pinging = true;
                long sent = clock.ElapsedTicks;
                window.Dispatcher.BeginInvoke(() =>
                {
                    maxLag = Math.Max(maxLag, (clock.ElapsedTicks - sent) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    pinging = false;
                }, DispatcherPriority.Background);
            }

            double now = clock.Elapsed.TotalSeconds;
            if (Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w is FishingGameDialog or CubePuzzleDialog) is { IsVisible: true } game)
            {
                long sig = Signature(game);
                if (sig != lastSig)
                {
                    if (lastMove >= 0)
                    {
                        double still = (now - lastMove) * 1000;
                        maxStill = Math.Max(maxStill, still);
                        if (still < 300) stills.Add(still);   // 손을 놓고 쉬는 틈(입방체 단추 사이)은 빼고 잰다
                    }
                    lastSig = sig; lastMove = now; moves++;
                }
            }
            else lastMove = -1;
            if (now - lastSecond < 1.0) return;
            proc.Refresh();
            var cpu = proc.TotalProcessorTime;
            long map = window.MapHost.FrameCount;
            bool open = Application.Current.Windows.OfType<Window>().Any(w => w is FishingGameDialog or CubePuzzleDialog);
            Log($"프레임 {++second}초 — 미니 게임 {(open ? "열림" : "닫힘")} · WPF {frames} · 평균 {(frames > 0 ? sumGap / frames : 0):0.0}ms · 최장 {maxGap:0.0}ms"
                + $" · 33ms 넘음 {slow} · 지도 {map - lastMap} · Background 지연 최장 {maxLag:0.0}ms · CPU {(cpu - lastCpu).TotalMilliseconds / ((now - lastSecond) * 10):0}%"
                + $" · 그림 바뀜 {moves} · 그림 멎음 최장 {maxStill:0}ms");
            lastCpu = cpu; lastMap = map; lastSecond = now;
            frames = 0; slow = 0; maxGap = 0; maxLag = 0; sumGap = 0; moves = 0; maxStill = 0;
        }
        CompositionTarget.Rendering += OnRender;

        // 설명 · 결과 알림은 저절로 닫는다. 입방체는 글쇠를 차례로 보낸다.
        System.Windows.Input.Key[] keys =
            [System.Windows.Input.Key.Left, System.Windows.Input.Key.Up, System.Windows.Input.Key.Right,
             System.Windows.Input.Key.Down, System.Windows.Input.Key.Space];
        int sentKeys = 0;
        double lastKey = 0;
        var driver = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(200) };
        driver.Tick += (_, _) =>
        {
            foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
            {
                if (w is ConfirmDialog && w.IsVisible) { Log($"알림을 닫는다 — [{TextOf(w)}]"); w.Close(); }
                else if (w is CubePuzzleDialog cube && w.IsVisible && clock.Elapsed.TotalSeconds - lastKey > 0.7)
                {
                    lastKey = clock.Elapsed.TotalSeconds;
                    var key = keys[sentKeys++ % keys.Length];
                    if (PresentationSource.FromVisual(cube) is { } source)
                        cube.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, key)
                        { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                }
            }
            if (clock.Elapsed.TotalSeconds < secs) return;
            foreach (var w in Application.Current.Windows.OfType<Window>().ToList())
                if (w is FishingGameDialog or CubePuzzleDialog) { Log($"{secs}초 — 미니 게임 창을 닫는다"); w.Close(); }
        };
        driver.Start();

        var all0 = all;
        Log($"미니 게임 [{which}] 을 띄운다 — 도시 {city} · {secs}초");
        // 한 판이 일찍 끝나면(낚시가 잡어에 걸리면) 그 초가 찰 때까지 다시 띄운다. none 이면 띄우지 않고 그대로 잰다.
        var results = new List<string>();
        if (which == "none")
        {
            if (city >= 0) window.PlayMinigameForTest(which, city);
            var frame = new DispatcherFrame();
            var stop = new DispatcherTimer(TimeSpan.FromSeconds(secs), DispatcherPriority.Normal, (_, _) => frame.Continue = false, window.Dispatcher);
            Dispatcher.PushFrame(frame);
            stop.Stop();
        }
        else
            for (int round = 0; clock.Elapsed.TotalSeconds < secs; round++)
                results.Add(window.PlayMinigameForTest(which, round == 0 ? city : -1));
        string result = string.Join(" / ", results);
        driver.Stop();
        CompositionTarget.Rendering -= OnRender;
        all0.Sort();
        double P(double q) => all0.Count == 0 ? 0 : all0[Math.Min(all0.Count - 1, (int)(q * all0.Count))];
        Log($"미니 게임 끝 — {result} · 프레임 {all0.Count} · 중앙 {P(0.5):0.0}ms · 95% {P(0.95):0.0}ms · 99% {P(0.99):0.0}ms · 최장 {P(1):0.0}ms");
        stills.Sort();
        double Q(double q) => stills.Count == 0 ? 0 : stills[Math.Min(stills.Count - 1, (int)(q * stills.Count))];
        Log($"그림 바뀜 사이 — {stills.Count}번 · 중앙 {Q(0.5):0.0}ms · 95% {Q(0.95):0.0}ms · 99% {Q(0.99):0.0}ms");
    }

    // ── 바로 시작 ───────────────────────────────────────────────────────────

    /// <summary><c>sea:&lt;도시&gt;:&lt;선체 key&gt;</c> 를 풀어 바다로 든다.</summary>
    private static void RunStart(WorldMapWindow window, string start)
    {
        var parts = start.Split(':');
        // land:<도시>:<선체 key>:<x>:<y> — 배는 그 도시 앞바다에 대 두고 말을 그 칸에 세운다(CDS_HEADING · CDS_SAIL 이면 걸어간다).
        if (parts.Length == 5 && parts[0] == "land" && int.TryParse(parts[1], out int landCity)
            && double.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out double landX)
            && double.TryParse(parts[4], System.Globalization.CultureInfo.InvariantCulture, out double landY))
        {
            string landError;
            try { landError = window.StartAshoreForTest(landCity, parts[2], landX, landY, (OffscreenTest.Heading ?? 0) * 2, OffscreenTest.Sail); }
            catch (Exception ex) { landError = $"{ex.GetType().Name}: {ex.Message}"; }
            Log(landError.Length == 0 ? $"육지에 섰다 — ({landX}, {landY}) · 걷기 {OffscreenTest.Sail}" : $"육지 시작 실패 — {landError}");
            return;
        }
        if (parts.Length != 3 || parts[0] != "sea" || !int.TryParse(parts[1], out int city))
        {
            Log($"CDS_START 를 못 읽었다 — [{start}] (꼴: sea:<도시 번호>:<선체 key> · land:<도시 번호>:<선체 key>:<x>:<y>)");
            return;
        }
        // 모드 「추가 선박」을 켜 둔다 — 시험 폴더(CDS_SAVEDIR)일 때만. 사람 설정은 안 건드린다(선체는 꺼져 있어도 찾힌다).
        if (OffscreenTest.SaveDir != null && !GameSettings.ExtraShips) GameSettings.ExtraShips = true;

        int heading = (OffscreenTest.Heading ?? 0) * 2;   // 8방위 → 16방위
        string error;
        try { error = window.StartAtSeaForTest(city, parts[2], heading, OffscreenTest.Sail); }
        catch (Exception ex) { error = $"{ex.GetType().Name}: {ex.Message}"; }
        Log(error.Length == 0
            ? $"바다로 들었다 — 도시 {city} · 선체 {parts[2]} · 뱃머리 {ShipRegistry.DirectionNames[heading / 2]} · 항해 {OffscreenTest.Sail}"
            : $"바로 시작 실패 — {error}");
    }

    // ── 네비게이션 ──────────────────────────────────────────────────────────

    /// <summary><c>CDS_NAV</c> — 그 도시로 자동항해를 걸고 1초마다 배 자리 · 어귀 · 뜬 창을 적는다. 창은 닫지 않는다.</summary>
    private static void RunNav(WorldMapWindow window, int city)
    {
        var host = window.MapHost;
        host.AutoSailEnded += message => Log($"자동항해 끝 — {message} · 어귀 [{string.Join(",", host.TownsAt())}]");
        string error = window.NavForTest(city);
        Log(error.Length == 0 ? $"네비게이션 — 도시 {city} 로 자동항해를 걸었다" : $"네비게이션 실패 — {error}");
        var seen = new HashSet<Window>();
        int seconds = 0;
        var watch = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        watch.Tick += (_, _) =>
        {
            seconds++;
            var at = host.ShipCell;
            Log($"{seconds}초 — 배 {at?.CellX:0.0},{at?.CellY:0.0} · 자동항해 {host.AutoSailing} · 마디 {host.AutoWaypointIndex + 1}/{host.AutoRouteCount}"
                + $" · 어귀 [{string.Join(",", host.TownsAt())}] · {window.ScreenStateForTest()}");
            foreach (Window w in Application.Current.Windows)
                if (!ReferenceEquals(w, window) && w.IsVisible && seen.Add(w))
                    Log($"창이 떴다 — {w.GetType().Name} · 글 [{TextOf(w)}]");
        };
        watch.Start();
    }

    // ── 타이틀 글쇠 ─────────────────────────────────────────────────────────

    /// <summary><c>CDS_TITLE</c> 의 글쇠를 차례로 누르고, 뜨는 창을 지켜본다(<c>CDS_AUTOKEY</c>).</summary>
    private static void RunTitleKeys(WorldMapWindow window, string spec)
    {
        var keys = new Queue<System.Windows.Input.Key>();
        foreach (string name in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse(name, true, out System.Windows.Input.Key key)) keys.Enqueue(key);
            else Log($"CDS_TITLE 글쇠를 못 읽었다 — [{name}]");
        System.Windows.Input.Key? auto = Environment.GetEnvironmentVariable("CDS_AUTOKEY") is { } autoName
                                         && Enum.TryParse(autoName, true, out System.Windows.Input.Key a) ? a : null;

        Log($"세이브 자리 — {Engine.GameSave.Census()}");
        var seen = new HashSet<Window>();
        int ticks = 0;
        var watch = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        watch.Tick += (_, _) =>
        {
            ticks++;
            foreach (Window w in Application.Current.Windows)
            {
                if (ReferenceEquals(w, window) || !w.IsVisible || !seen.Add(w)) continue;
                Log($"창이 떴다 — {w.GetType().Name} · 제목 [{w.Title}] · 주인 {w.Owner?.GetType().Name ?? "없음"} · 자리 ({w.Left:0}, {w.Top:0}) · 이름 [{System.Windows.Automation.AutomationProperties.GetName(w).Replace('\n', ' ')}] · 글 [{TextOf(w)}]");
                if (auto is { } send)
                {
                    var target = w;
                    var later = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(700) };
                    later.Tick += (_, _) =>
                    {
                        later.Stop();
                        if (!target.IsVisible) return;
                        Log($"{target.GetType().Name} 에 {send} 를 보낸다");
                        window.Dispatcher.BeginInvoke(() => Press(target, send));
                    };
                    later.Start();
                }
            }
            if (ticks % 2 == 0)
            {
                if (keys.Count > 0)
                {
                    var key = keys.Dequeue();
                    Log($"타이틀 글쇠 {key}");
                    // 엔터가 띄우는 창(ShowDialog)은 닫힐 때까지 안 돌아온다 — 이 시계가 멎지 않게 따로 부른다.
                    window.Dispatcher.BeginInvoke(() => Log($"타이틀 글쇠 {key} 가 돌아왔다 → {window.TitleKeyForTest(key)}"));
                }
                else Log($"화면 — {window.ScreenStateForTest()} · 마지막 불러오기 [{Engine.GameSave.LastLoadOutcome}]");
            }
        };
        watch.Start();
    }

    /// <summary>창에 글쇠 하나를 미리보기 · 누름 차례로 쏜다(창이 활성이 아니어도 그 창의 처리기가 받는다).</summary>
    private static void Press(Window w, System.Windows.Input.Key key)
    {
        var source = PresentationSource.FromVisual(w);
        if (source == null) return;
        var target = (IInputElement?)System.Windows.Input.Keyboard.FocusedElement is DependencyObject d && Window.GetWindow(d) == w
            ? (IInputElement)System.Windows.Input.Keyboard.FocusedElement : w;
        var preview = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, key)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (preview.Handled) return;
        target.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source, 0, key)
            { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
    }

    /// <summary>
    /// 떠 있는 목록 창(<see cref="ListPickDialog"/>)을 주인 안으로 옮기고 굴림 자리를 잰다 — 처음 · 휠 세 번 · 맨 위로 돌려 아래
    /// 글쇠 열네 번 · 맨 위로 돌려 굴림대 아래 화살표 다섯 번. 걸음마다 창을 <c>&lt;이름&gt;-&lt;걸음&gt;.png</c> 로 찍고, 끝에 Esc 로 닫는다.
    /// 창이 아직 안 떴으면 0.5초마다 <paramref name="tries"/> 번 더 찾는다.
    /// </summary>
    private static void ProbeList(WorldMapWindow window, string name, int tries)
    {
        var dlg = Application.Current.Windows.OfType<ListPickDialog>().FirstOrDefault(w => w.IsVisible);
        if (dlg == null)
        {
            if (tries <= 0) { Log("목록 창을 못 찾았다"); return; }
            // 도시에 들 때 뜨는 그림 · 쪽지 창이 막고 있으면 Enter 로 넘긴다(도시 커맨드 길).
            var blocking = Application.Current.Windows.OfType<Window>()
                .LastOrDefault(w => w.IsVisible && w is not WorldMapWindow && w is not CityPicView && w.GetType().Name != "FleetLabelWindow");
            Log($"목록 창을 기다린다 — 떠 있는 창 {string.Join(",", Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible).Select(w => w.GetType().Name))}"
                + (blocking != null ? $" · {blocking.GetType().Name} 에 Enter" : ""));
            if (blocking != null) Press(blocking, System.Windows.Input.Key.Enter);
            var again = new DispatcherTimer(DispatcherPriority.ApplicationIdle, window.Dispatcher) { Interval = TimeSpan.FromSeconds(0.5) };
            again.Tick += (_, _) => { again.Stop(); ProbeList(window, name, tries - 1); };
            again.Start();
            return;
        }
        dlg.Left = window.Left + 60;
        dlg.Top = window.Top + 30;

        var view = Descendants(dlg).OfType<System.Windows.Controls.ScrollViewer>().FirstOrDefault();
        if (view == null) { Log("목록 창에 두루마리 칸이 없다"); return; }
        var bar = (view.Parent as System.Windows.Controls.Grid)?.Children.OfType<FrameworkElement>().FirstOrDefault(e => !ReferenceEquals(e, view));
        var down = (bar as System.Windows.Controls.Grid)?.Children.OfType<FrameworkElement>().LastOrDefault();
        string State() =>
            $"창 {dlg.ActualWidth:0}x{dlg.ActualHeight:0} · 주인 {dlg.Owner?.GetType().Name}"
            + $" · 줄 {((view.Content as System.Windows.Controls.Border)?.Child as System.Windows.Controls.Panel)?.Children.Count} · 칸 높이 {view.ViewportHeight:0} / 전체 {view.ExtentHeight:0}"
            + $" · 굴림 {view.VerticalOffset:0} / {view.ScrollableHeight:0} · 굴림대 {bar?.Visibility}";

        var steps = new List<(string Step, Action Act)>
        {
            ("처음", () => { }),
            ("휠", () =>
            {
                for (int i = 0; i < 3; i++)
                    ((UIElement)view.Content).RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                        { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
            }),
            ("글쇠", () =>
            {
                view.ScrollToTop();
                for (int i = 0; i < 14; i++) Press(dlg, System.Windows.Input.Key.Down);
            }),
            ("화살표", () =>
            {
                view.ScrollToTop();
                for (int i = 0; i < 5; i++)
                    down?.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                                                                                    System.Windows.Input.MouseButton.Left)
                        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            }),
        };
        int k = 0;
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, window.Dispatcher) { Interval = TimeSpan.FromSeconds(0.6) };
        timer.Tick += (_, _) =>
        {
            if (k > 0)
            {
                // 앞 걸음의 결과 — 그림이 다 그려진 뒤에 잰다.
                Log($"{name} {steps[k - 1].Step} — {State()}");
                SavePng(dlg, Path.Combine(OffscreenTest.OutputFolder, $"{name}-{k - 1}-{steps[k - 1].Step}.png"));
            }
            if (k == steps.Count)
            {
                timer.Stop();
                Press(dlg, System.Windows.Input.Key.Escape);
                Log($"{name} 창을 닫았다");
                return;
            }
            steps[k++].Act();
        };
        timer.Start();
    }

    /// <summary>창 하나만 PNG 로 적는다.</summary>
    private static void SavePng(Window w, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(Render(w)));
            png.Save(stream);
        }
        catch (Exception ex) { Log($"창 그림 실패 {path} — {ex.Message}"); }
    }

    /// <summary>눈에 보이는 나무를 다 훑는다.</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>창 안의 글(TextBlock · 글 상자)을 이어 붙인다 — 기록용.</summary>
    private static string TextOf(DependencyObject root)
    {
        var parts = new List<string>();
        void Walk(DependencyObject node)
        {
            switch (node)
            {
                case System.Windows.Controls.TextBlock t when !string.IsNullOrWhiteSpace(t.Text): parts.Add(t.Text.Trim()); break;
                case System.Windows.Controls.TextBox b when !string.IsNullOrWhiteSpace(b.Text): parts.Add(b.Text.Trim()); break;
            }
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
        Walk(root);
        string all = string.Join(" | ", parts).Replace('\n', ' ');
        return all.Length > 400 ? all[..400] + "…" : all;
    }

    // ── 찍고 끄기 ───────────────────────────────────────────────────────────

    /// <summary><c>CDS_SHOT</c> 의 차례대로 찍고, 마지막 것을 찍으면 창을 닫는다.</summary>
    private static void ScheduleShots(WorldMapWindow window)
    {
        if (OffscreenTest.Shot is not { } spec) return;
        var shots = new List<(string Path, double Seconds)>();
        foreach (string item in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // 경로에 드라이브 콜론(C:)이 있으니 마지막 콜론으로 가른다.
            int at = item.LastIndexOf(':');
            if (at <= 1 || !double.TryParse(item[(at + 1)..], System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                Log($"CDS_SHOT 을 못 읽었다 — [{item}] (꼴: <png 경로>:<초>)");
                continue;
            }
            shots.Add((item[..at], seconds));
        }
        shots.Sort((a, b) => a.Seconds.CompareTo(b.Seconds));
        if (shots.Count == 0) return;

        var clock = System.Diagnostics.Stopwatch.StartNew();
        int next = 0;
        void Arm()
        {
            var (path, seconds) = shots[next];
            var wait = TimeSpan.FromSeconds(Math.Max(0, seconds - clock.Elapsed.TotalSeconds));
            var timer = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = wait };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Save(window, path, () =>
                {
                    if (++next < shots.Count) { Arm(); return; }
                    Log("CDS_SHOT 을 다 찍었다 — 창을 닫는다");
                    window.Close();
                });
            };
            timer.Start();
        }
        Arm();
    }

    // ── 스냅샷 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 지금 화면을 PNG 로 적는다. 지도가 돌고 있으면 다음 프레임의 백버퍼를 받아 겹친다.
    /// 다 적으면 <paramref name="done"/>.
    /// </summary>
    public static void Save(WorldMapWindow window, string path, Action? done)
    {
        var host = window.MapHost;
        bool finished = false;
        void Finish(byte[]? map, int w, int h)
        {
            if (finished) return;
            finished = true;
            try
            {
                Compose(window, map, w, h, path);
                Log($"스냅샷 {path}{(map is { Length: > 0 } ? $" (지도 {w}x{h})" : " (지도 없음)")}");
            }
            catch (Exception ex) { Log($"스냅샷 실패 {path} — {ex.GetType().Name}: {ex.Message}"); }
            done?.Invoke();
        }

        if (!host.IsVisible || host.FrameCount == 0 || host.ActualWidth <= 0)
        {
            Finish(null, 0, 0);
            return;
        }
        var timeout = new DispatcherTimer(DispatcherPriority.Normal, window.Dispatcher) { Interval = GrabWait };
        timeout.Tick += (_, _) => { timeout.Stop(); Finish(null, 0, 0); };
        timeout.Start();
        // 프레임 안(CompositionTarget.Rendering)에서 불린다 — 그리는 일은 한 박자 뒤로 미룬다.
        host.GrabNextFrame((pixels, w, h) => window.Dispatcher.BeginInvoke(() =>
        {
            timeout.Stop();
            Finish(pixels, w, h);
        }));
    }

    /// <summary>창 그림 + 지도 백버퍼 + 딸린 창을 한 장에 겹쳐 적는다(임시 파일에 적고 옮겨 반쯤 적힌 파일이 안 보이게).</summary>
    private static void Compose(WorldMapWindow window, byte[]? map, int mapW, int mapH, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        double width = window.ActualWidth, height = window.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(window.Background ?? Brushes.Black, null, new Rect(0, 0, width, height));
            dc.DrawImage(Render(window), new Rect(0, 0, width, height));

            // 지도는 자식 창(D3D)이라 WPF 그림에는 비어 나온다 — 그 자리에 백버퍼를 얹는다. 실제 화면도 지도가 WPF 위에 뜬다.
            if (map is { Length: > 0 } && mapW > 0 && mapH > 0 && window.MapHost.IsVisible)
            {
                var image = BitmapSource.Create(mapW, mapH, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                                                PixelFormats.Bgr32, null, map, mapW * 4);
                var at = window.MapHost.TransformToAncestor(window).Transform(new Point(0, 0));
                dc.DrawImage(image, new Rect(at.X, at.Y, mapW / dpi.DpiScaleX, mapH / dpi.DpiScaleY));
            }

            // 딸린 창(도시 화면 · 대화 창 · 쪽지)은 제 자리에 — 주인 창에 견준 자리로 놓는다.
            foreach (var owned in Owned(window))
            {
                if (owned.ActualWidth <= 0 || owned.ActualHeight <= 0) continue;
                dc.DrawImage(Render(owned), new Rect(owned.Left - window.Left, owned.Top - window.Top,
                                                     owned.ActualWidth, owned.ActualHeight));
            }
        }

        var shot = new RenderTargetBitmap((int)Math.Ceiling(width * dpi.DpiScaleX), (int)Math.Ceiling(height * dpi.DpiScaleY),
                                          96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        shot.Render(visual);

        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = full + ".part";
        using (var stream = File.Create(temp))
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(shot));
            png.Save(stream);
        }
        File.Move(temp, full, overwrite: true);
    }

    /// <summary>창 하나를 실픽셀로 그린다.</summary>
    private static BitmapSource Render(Window window)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX)),
                                            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY)),
                                            96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        return bitmap;
    }

    /// <summary>보이는 딸린 창 — 주인부터 깊이 딸린 것 차례(위에 뜨는 것이 나중).</summary>
    private static IEnumerable<Window> Owned(Window owner)
    {
        foreach (Window w in owner.OwnedWindows)
        {
            if (!w.IsVisible) continue;
            yield return w;
            foreach (var sub in Owned(w)) yield return sub;
        }
    }

    // ── 포커스 안 뺏기 ─────────────────────────────────────────────────────

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const int WhCbt = 5, HcbtActivate = 5;

    private static HookProc? _cbt;     // 대리자가 GC 에 걷히지 않게 들고 있는다
    private static IntPtr _cbtHook;

    /// <summary>
    /// 이 실(UI 스레드)의 창이 <b>활성이 되지 않게</b> 한다 — CBT 훅이 <c>HCBT_ACTIVATE</c> 를 막는다. 대화 창 · 도시 화면이
    /// <c>Activate()</c> 를 불러도 사람이 쓰던 창의 포커스가 그대로다. 창이 활성이 아니니 게임도 커서로 배를 몰지 않는다(SyncMouse).
    /// </summary>
    private static void BlockActivation()
    {
        if (_cbtHook != IntPtr.Zero) return;
        _cbt = (code, wParam, lParam) => code == HcbtActivate ? 1 : CallNextHookEx(_cbtHook, code, wParam, lParam);
        _cbtHook = SetWindowsHookExW(WhCbt, _cbt, IntPtr.Zero, GetCurrentThreadId());
        if (_cbtHook == IntPtr.Zero) Log($"활성 막기 훅을 못 걸었다 (Win32 {Marshal.GetLastWin32Error()})");
    }

    // ── 기록 ──────────────────────────────────────────────────────────────

    /// <summary>시험 기록 한 줄 — <c>OutputFolder\cds_test.log</c>.</summary>
    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(OffscreenTest.OutputFolder);
            File.AppendAllText(Path.Combine(OffscreenTest.OutputFolder, "cds_test.log"),
                               $"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {line}{Environment.NewLine}");
        }
        catch { /* 기록을 못 남겨도 시험은 돈다 */ }
    }
}
