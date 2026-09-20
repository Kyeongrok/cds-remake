using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Inn;
using CdsHelper.Game.Engine.Market;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Engine.Models;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Engine.Menu;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 입항한 도시의 그림(CITYCG.CDS)을 지도 한가운데에 띄운다. 게임처럼 건물(항구·조선소)을
/// 누르면 그 건물의 명령 창이 열린다.
/// </summary>
/// <remarks>
/// 물음창들과 같은 수를 쓴다 — 창(HWND)을 따로 쓰므로 D3D 자식 창 위에
/// 제대로 뜬다(airspace 를 안 탄다). 그림은 400x320 도트 그림이라 정수배로만 늘린다.
///
/// 건물 자리·이름·가르치는 기능은 게임 EXE 의 건물 표(<see cref="CityBuildingTable"/>)에서
/// 그대로 온다. 표에 항구가 없는 도시라면 그림 아무 데나 눌러도 항구 명령 창이 열리게 해
/// 두었다 — 출항할 길은 어디서나 있어야 한다.
/// </remarks>
public sealed class CityPicView : GameWindow, ITownScreen
{
    /// <summary>건물 이름표와 명령 창을 얹는 자리. 그림과 같은 격자 칸에 둔다.</summary>
    private readonly Canvas _layer = new();

    /// <summary>
    /// 도시 창 <b>옆</b>에 따로 띄우는 내 함대 쪽지 — 배마다 한 줄 「배 이름(선체)」.
    /// 게임 화면에는 없는 덧그림이라 그림을 가리지 않게 창 밖에 두고, 끌어 옮길 수 있다.
    /// </summary>
    private FleetLabelWindow? _shipNote;

    /// <summary>기능·언어 쪽지. 개발 창에서 켜 두었을 때만 뜬다(<see cref="GameSettings.ShowSkillOverlay"/>).</summary>
    private SkillOverlayWindow? _skillNote;

    /// <summary>쪽지 글자 크기 — 함대 쪽지와 같게 그림 배율을 따른다.</summary>
    private double _noteFontSize = 13;

    /// <summary>
    /// 사건이 도는 동안 그림을 덮는 <b>파란 막</b>.
    /// </summary>
    /// <remarks>
    /// 발견을 보고하는 동안 게임 화면이 파래진다 — 바다에서 발견할 때 지도가 파래지는 것과
    /// 같은 몫이고(<see cref="Rendering.ShipMapHost.Shaded"/>), 도시 그림에도 있어야
    /// 후원자에게 보고하는 대목이 게임과 같아진다.
    /// </remarks>
    private readonly System.Windows.Shapes.Rectangle _shade = new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(0x88, 0x18, 0x30, 0x70)),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };

    /// <summary>그림을 파랗게 덮거나 걷는다. 함대 쪽지도 같이 감췄다 낸다.</summary>
    public void Shade(bool on)
    {
        _shade.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        _shipNote?.Shade(on);
        _skillNote?.Shade(on);
    }

    /// <summary>건물 이름표들. 명령 창이 열리면 다 감춘다.</summary>
    private readonly List<Border> _tags = [];

    /// <summary>이 판. 게임 폴더 · 표 · 소리를 여기서 얻는다.</summary>
    private readonly Engine.Game _game;

    /// <summary>배를 사면 여기서 돈이 빠진다. 판이 든 것을 그대로 쓴다.</summary>
    private readonly Player _player;

    /// <summary>건물에 들어갈 때 곡을 바꾼다. 없으면(시험용) 아무것도 안 한다.</summary>
    private readonly BgmPlayer? _bgm;

    /// <summary>건물 표. 가르치는 기능을 이름으로 풀 때 쓴다.</summary>
    private readonly CityBuildingTable _table;

    /// <summary>초상화(게임 자료). 설득할 때에야 연다.</summary>

    // 도서관 열람에 쓴다. 책 표를 못 읽었으면 열람 줄이 흐린 채로 남는다.
    private readonly BookTable? _library;
    private readonly string _gameDirectory;
    private readonly string _cityName;
    private readonly int _cityId;

    /// <summary>이 도시의 문화권("이슬람", "북유럽" …). 건물 사진을 고르는 데 쓴다.</summary>
    private string _culture;

    /// <summary>
    /// 이 마을 문화권 번호(0~10). 시설의 화자 얼굴이 여기 따라 갈린다 —
    /// 같은 조선소라도 리스본과 이스탄불에 딴 사람이 앉는다.
    /// </summary>
    /// <remarks>
    /// 도구 창에서 이 마을 문화권을 갈면 창을 열어 둔 채로 바뀐다
    /// (<see cref="CityCultureEdits"/>) — 그래서 <c>readonly</c> 가 아니다.
    /// </remarks>
    private int _cultureNo;

    /// <summary>그림 배율. 건물 사진도 같은 배율로 놓아야 자리가 맞는다.</summary>
    private readonly int _scale;

    /// <summary>
    /// 이 도시에서 도는 곡. 문화권마다 다르다 — 시설에서 나오면 이 곡으로 돌아간다.
    /// </summary>
    private readonly int _cityTrack;

    /// <summary>출항을 골랐는지. 창을 그냥 닫으면 false.</summary>
    public bool Sailed { get; private set; }

    /// <summary>
    /// 성문에서 <b>탐험을 떠난다</b> 를 골랐는지.
    /// </summary>
    /// <remarks>
    /// 마을이 닫히고 뭍으로 나선다 — 배는 항구에 대 둔 채 말을 타고 걷는다. 되돌아오는
    /// 것은 뭍 커맨드의 "승선" 이 맡는다(<see cref="Rendering.ShipMapHost.Embark"/>).
    /// </remarks>
    public bool Explored { get; private set; }

    /// <summary>
    /// 바다로 닿아 뜬 항구 차림표에서 곧장 출항했는지 — 그때는 나서는 열흘이 없다
    /// (<c>0x00477310</c> 이 건물 <c>+0x98</c>「닿아서 들어옴」이 0 일 때만 열흘을 보낸다).
    /// </summary>
    public bool SailedOnArrival { get; private set; }

    /// <summary>
    /// 지금 열려 있는 항구·성문 — 나설 때(마지막 줄·ESC) 부관이 한마디 한다. 딴 건물이면 null.
    /// </summary>
    private FacilityKind? _gateway;

    /// <summary>
    /// 그 항구·성문에 <b>밖에서 닿아</b> 들어왔는지(건물 <c>+0x98</c>, <c>0x004A258A</c>).
    /// 바다로 닿으면 항구 차림표가 먼저 뜨고 마지막 줄이 「마을에 들어간다」다(<c>0x0047797E</c>).
    /// </summary>
    private bool _arrived;

    /// <summary>펼치기 시작하는 크기(제 크기의 몇 곱).</summary>
    private const double OpenFrom = 0.1;

    /// <summary>다 펼쳐졌을 때의 자리와 크기.</summary>
    private double _openLeft, _openTop, _openWidth, _openHeight;

    /// <summary>
    /// 도시 그림이 가운데서 펼쳐지며 열린다.
    /// </summary>
    /// <remarks>
    /// 창(HWND)을 따로 쓰므로 창의 크기와 자리를 함께 움직인다 — 크기만 키우면 왼쪽 위가
    /// 붙박이라 한쪽으로 자라는 것처럼 보인다. 네 값을 같은 박자로 움직여야 가운데가 안 흔들린다.
    ///
    /// 끝나면 <b>애니메이션을 떼고</b> 값을 손으로 박는다. 안 떼면 그 값이 물려 있어
    /// 나중에 <see cref="GameUi.CarryOwnedWindows"/> 가 창을 옮기려 해도 먹히지 않는다.
    /// </remarks>
    private void PlayOpening(CityOpenEffect effect)
    {
        var span = TimeSpan.FromMilliseconds(effect switch
        {
            CityOpenEffect.Expand => 220,
            CityOpenEffect.Zoom => 320,     // 넘쳤다 돌아올 참이 있어야 해서 조금 길다
            _ => 250,
        });
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        DoubleAnimation To(double from, double to) =>
            new(from, to, span) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };

        // 끝나면 물려 있던 값을 떼고 손으로 박는다.
        void Settle()
        {
            foreach (var prop in new[] { WidthProperty, HeightProperty, LeftProperty, TopProperty, OpacityProperty })
                BeginAnimation(prop, null);
            Width = _openWidth;
            Height = _openHeight;
            Left = _openLeft;
            Top = _openTop;
            Opacity = 1;
        }

        DoubleAnimation lead;
        switch (effect)
        {
            case CityOpenEffect.Expand:
                double w0 = _openWidth * OpenFrom, h0 = _openHeight * OpenFrom;
                lead = To(w0, _openWidth);
                BeginAnimation(HeightProperty, To(h0, _openHeight));
                BeginAnimation(LeftProperty, To(_openLeft + (_openWidth - w0) / 2, _openLeft));
                BeginAnimation(TopProperty, To(_openTop + (_openHeight - h0) / 2, _openTop));
                lead.Completed += (_, _) => Settle();
                BeginAnimation(WidthProperty, lead);
                break;

            case CityOpenEffect.Slide:
                lead = To(SystemParameters.VirtualScreenWidth, _openLeft);
                lead.Completed += (_, _) => Settle();
                BeginAnimation(LeftProperty, lead);
                break;

            case CityOpenEffect.Fade:
                lead = To(0, 1);
                lead.Completed += (_, _) => Settle();
                BeginAnimation(OpacityProperty, lead);
                break;

            case CityOpenEffect.Zoom:
                ZoomIn(span);
                break;
        }
    }

    /// <summary>
    /// 파워포인트의 "확대/축소" 처럼 그림만 키운다 — 커지면서 흐림이 걷히고 끝에서 살짝 넘친다.
    /// </summary>
    /// <remarks>
    /// 창은 건드리지 않고 안의 그림에만 <see cref="ScaleTransform"/> 을 건다. 창의 크기·자리를
    /// 움직이지 않으므로 <see cref="GameUi.CarryOwnedWindows"/> 와도 부딪히지 않는다.
    /// 넘쳤다 돌아오는 맛은 <see cref="BackEase"/> 가 낸다.
    /// </remarks>
    /// <remarks>
    /// <b>깜빡이지 않게 하는 요령이 둘 있다.</b>
    ///
    /// 하나, 애니메이션을 <see cref="FillBehavior.HoldEnd"/> 로 둔다. <c>Stop</c> 으로 두면
    /// 끝나는 순간 값이 <i>처음 값</i>으로 되돌아간다 — 흐림처럼 짧게 끝나는 것을 <c>Stop</c>
    /// 으로 두면 중간에 그림이 한 번 사라졌다 돌아온다.
    ///
    /// 둘, 다 끝난 뒤 값을 먼저 박고 그 다음에 애니메이션을 뗀다. 차례가 바뀌면 떼는 순간
    /// 한 틱 동안 옛 값이 드러난다.
    ///
    /// 창 바탕도 건드리지 않는다. <c>AllowsTransparency</c> 가 켜진 창은 바탕을 갈면 통째로
    /// 다시 그려져 눈에 띈다 — 다 자란 그림이 창을 꽉 채우므로 비워 둔 채로 두어도 된다.
    /// </remarks>
    private void ZoomIn(TimeSpan span)
    {
        if (Content is not FrameworkElement box) return;

        var scale = new ScaleTransform(ZoomFrom, ZoomFrom);
        box.RenderTransformOrigin = new Point(0.5, 0.5);
        box.RenderTransform = scale;
        box.Opacity = 0;

        var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        DoubleAnimation Grow() => new(ZoomFrom, 1, span) { EasingFunction = ease };

        var lead = Grow();
        lead.Completed += (_, _) =>
        {
            // 값부터 박고 나서 뗀다.
            box.Opacity = 1;
            box.BeginAnimation(OpacityProperty, null);
            box.RenderTransform = Transform.Identity;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, lead);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Grow());
        // 흐림은 먼저 걷힌다 — 끝까지 끌면 넘쳤다 돌아오는 동안 반투명해 보인다.
        box.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(span.TotalMilliseconds * 0.6)));
    }

    /// <summary>확대/축소가 시작하는 배율.</summary>
    private const double ZoomFrom = 0.3;

    private CityPicView(Engine.Game game, string cityName, BitmapSource picture, int scale,
                          int cityId, Rect mapArea, int cityTrack, string culture)
    {
        // 판이 든 것을 그대로 든다 — 표를 여기서 따로 열지 않는다.
        _game = game;
        _player = game.Player;
        _bgm = game.Bgm;
        _table = game.Buildings!;      // 부르는 쪽에서 없으면 창을 아예 안 연다
        _library = game.Books;
        _gameDirectory = game.Directory;

        _culture = culture;
        // 문화권 번호는 열 때 한 번만 묻는다 — 시설들이 제 화자 얼굴을 찾는 데 쓴다.
        _cultureNo = game.CityRows?.CultureOf(cityId) ?? 0;
        _scale = scale;
        _cityTrack = cityTrack;
        _cityName = cityName;
        _cityId = cityId;

        // 도구 창에서 문화권을 갈면 그 자리에서 따라간다 — 창을 닫았다 열 것 없이
        // 조선소 얼굴이 바뀌는지 보려는 것이 그 기능의 뜻이다.
        CityCultureEdits.Changed += OnCultureChanged;
        Closed += (_, _) => CityCultureEdits.Changed -= OnCultureChanged;

        Title = cityName;                 // 화면에는 안 나온다 — 창 목록에서만 쓴다
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;       // 그림에 가려 안 보인다

        // 부수기 전에 주인 창(함대 창)을 띄워 둔다 — 안 그러면 초점이 다른 앱으로
        // 샜다가 돌아온다. 도시 창은 테도 없고 작업표시줄에도 없어서 윈도가 다음에
        // 띄울 창을 못 고르기 때문이다.
        Closing += (_, _) => Owner?.Activate();

        // 창 크기는 그림 크기 그대로다. 제목 줄이 없어(WindowStyle.None) 테가 붙지 않는다.
        double fullW = CityPictures.Width * scale, fullH = CityPictures.Height * scale;

        // 지도를 덮는 남색 막은 이 창이 아니라 지도(D3D) 쪽에서 씌운다 — 그래야 이 그림을
        // 끌어 옮겨도 막이 따라오지 않는다. 게임도 막과 그림이 따로다.
        // 처음 자리는 게임처럼 지도 한가운데다(옮기는 것은 손으로).
        if (mapArea.Width > 0 && mapArea.Height > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            // 다 펼쳐졌을 때의 자리. 펼치는 동안에는 이 한가운데를 축으로 커진다.
            _openLeft = mapArea.X + (mapArea.Width - fullW) / 2;
            _openTop = mapArea.Y + (mapArea.Height - fullH) / 2;
            _openWidth = fullW;
            _openHeight = fullH;

            // 효과는 개발 창에서 고른다. 크기를 움직이려면 SizeToContent 를 꺼야 한다.
            SizeToContent = SizeToContent.Manual;
            Width = fullW;
            Height = fullH;
            Left = _openLeft;
            Top = _openTop;

            var effect = GameSettings.CityOpenEffect;
            if (effect == CityOpenEffect.Expand)
            {
                Width = fullW * OpenFrom;
                Height = fullH * OpenFrom;
                Left = _openLeft + (fullW - Width) / 2;
                Top = _openTop + (fullH - Height) / 2;
            }
            else if (effect == CityOpenEffect.Slide)
            {
                Left = SystemParameters.VirtualScreenWidth;      // 화면 오른쪽 바깥
            }
            else if (effect == CityOpenEffect.Fade)
            {
                AllowsTransparency = true;   // 이것을 켜야 Opacity 가 창에 먹는다
                Opacity = 0;
            }
            else if (effect == CityOpenEffect.Zoom)
            {
                // 창은 제 크기 그대로 두고 그림만 키운다. 창 바탕이 검으면 다 자라기 전에
                // 검은 네모가 먼저 보이므로, 바탕을 비우고 그림만 뜨게 한다.
                AllowsTransparency = true;
                Background = Brushes.Transparent;
            }

            if (effect != CityOpenEffect.None) Loaded += (_, _) => PlayOpening(effect);
        }
        else
        {
            // 지도 자리를 모르면 owner 한가운데에 제 크기로 띄운다(펼치지 않는다).
            //
            // 크기를 <b>손으로 박는다</b>. 예전에는 SizeToContent 에 맡겼는데, 이 창의 속은
            // Viewbox(Stretch.Fill) 라 창에 맞춰 늘어난다 — 크기를 속에서 재고 속을 다시
            // 창에 맞추는 두 규칙이 서로를 밀어, 창이 화면만 하게 부풀어 오르는 일이 있었다
            // (도시 그림이 전체 화면으로 뜨던 것이 이것이다).
            SizeToContent = SizeToContent.Manual;
            Width = fullW;
            Height = fullH;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // 펼침 효과는 안 쓰지만, 뒤에 무엇이 이 값을 보더라도 창 크기와 어긋나지 않게 둔다.
            _openWidth = fullW;
            _openHeight = fullH;
        }

        var image = new Image
        {
            Source = picture,
            Width = CityPictures.Width * scale,
            Height = CityPictures.Height * scale,
            Stretch = Stretch.Fill,
        };
        // 도트 그림이라 늘릴 때 섞으면 뭉개진다 — 게임 화면처럼 각을 살린다.
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);

        // 사건이 도는 동안 그림을 파랗게 덮는 막. 지도 쪽의 ShipMapHost.Shaded 와 같은 몫이다.
        _shade.Width = image.Width;
        _shade.Height = image.Height;

        var picBox = new Grid
        {
            Width = image.Width,
            Height = image.Height,
            Children = { image, _layer, _shade },
        };

        // 함대 쪽지는 이 창이 자리를 잡은 뒤에야 옆에 붙일 수 있다.
        Loaded += (_, _) =>
        {
            _shipNote = FleetLabelWindow.Attach(this, 13 * scale);
            _noteFontSize = 13 * scale;
            RefreshShipLabel();
            SyncSkillNote();
        };
        // 개발 창에서 켜고 끄면 떠 있는 도시 창에도 곧바로 든다.
        GameSettings.ShowSkillOverlayChanged += SyncSkillNote;
        // 도시 창이 닫히면 쪽지도 같이 접는다(주인 창이 닫히면 따라 닫히지만, 펼침 효과로
        // 미끄러지는 동안 닫는 자리도 있어 손으로 짚어 둔다).
        Closed += (_, _) =>
        {
            GameSettings.ShowSkillOverlayChanged -= SyncSkillNote;
            _shipNote?.Close(); _shipNote = null;
            _skillNote?.Close(); _skillNote = null;
        };

        // 조선소에서 배를 사거나 이름을 바꾸고 돌아오면 이 창이 다시 활성화된다 — 그때 고쳐 쓴다.
        // 술집에서 부하를 들이고 돌아와도 같다 — 기능·언어 쪽지도 새로 채운다.
        Activated += (_, _) =>
        {
            RefreshShipLabel();
            _skillNote?.Refresh(_player);
        };

        // 도시 그림에 그려진 마을 사람들. <b>건물보다 먼저</b> 깐다 — 자리가 겹치면 건물이 이긴다(0x00491DC0).
        foreach (var folk in _game.TownFolk?.InCity(cityId) ?? []) AddFolk(folk, scale);

        // 게임 건물 표에 적힌 그대로 얹는다 — 그 도시에 있는 건물만, 게임이 쓰는 자리에.
        // 코드가 <b>큰</b> 것부터 깔아 낮은 것이 위에 오게 한다 — 겹치면 낮은 코드가 이긴다(0x00491D58).
        bool harborPlaced = false;
        foreach (var building in Enumerable.Reverse(Standing(cityId)))
        {
            AddSpot(building, scale);
            if (building.Kind == "항구") harborPlaced = true;
        }

        // 표에 항구가 없는 도시는 아무 데나 눌러도 항구 명령 창이 열린다(건물 판이 먼저 먹는다).
        _harborPlaced = harborPlaced;
        if (!harborPlaced)
        {
            picBox.Cursor = Cursors.Hand;
            picBox.MouseLeftButtonUp += (_, _) =>
            {
                if (MenuOpen) return;   // 창이 떠 있으면 그림을 눌러도 안 열린다
                OpenBareHarbor(arrived: false);
            };
        }

        // 게임 화면에는 제목 줄도 안내 줄도 없다. 그림 한 장이 곧 창이다.
        // 펼치는 동안 창이 작아지므로 그림도 같이 줄어야 한다 — Viewbox 가 창에 맞춰 준다.
        // 다 펼쳐지면 창과 그림이 같은 크기라 배율이 1 이 되어, 건물 누르는 자리도 그대로 맞는다.
        Content = new Viewbox { Child = picBox, Stretch = Stretch.Fill };

        // 제목 줄이 없어도 옮길 수는 있어야 한다 — 그림의 아무 데나 잡으면 끌린다.
        // 건물 판과 명령 창은 누르는 자리라 제 몫으로 삼키므로 여기까지 오지 않는다.
        // 항구를 못 찾은 그림은 그림 전체가 누르는 자리라 끌기를 달지 않는다.
        if (harborPlaced)
            picBox.MouseLeftButtonDown += (_, _) =>
            {
                if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
            };

        // 그림을 옮기면 옆에 붙은 커맨드 창도 같이 옮긴다. 함대 창이 옮겨져 이 그림이
        // 끌려갈 때에도 같은 길로 이어진다.
        GameUi.CarryOwnedWindows(this);

        // 오른쪽 단추는 게임처럼 도시 커맨드 창을 연다.
        //
        // ESC 는 <b>열려 있는 창만</b> 닫는다. 예전에는 그림 창까지 닫아 버려서 아무것도
        // 안 열린 채로 ESC 를 누르면 그대로 바다로 나갔다 — 게임에는 그런 길이 없다.
        // 도시를 나가는 길은 항구의 "출항" 하나뿐이다(HarborMenu.ConfirmSail).
        KeyDown += (_, e) =>
        {
            if (e.Key is not Key.Escape) { e.Handled = PickByKey(e.Key); return; }
            e.Handled = true;
            // 창이 초점을 쥐고 있으면 그쪽이 제 ESC 로 닫힌다(MenuWindow). 여기까지 온 것은
            // 그림이 초점을 쥔 자리라, 열려 있는 것이 있으면 대신 닫아 준다.
            Menus.CloseOpen();
        };
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowCityMenu(cityName, ToScreen(e.GetPosition(this)));
        };
        Closed += (_, _) => CloseCityMenu();   // 그림 창을 닫으면 커맨드 창도 같이 닫는다
    }

    /// <summary>
    /// 건물 하나를 누를 수 있게 한다. 커서를 올리면 이름표가 밑에 뜨고, 누르면 명령 창이 열린다.
    /// </summary>
    private void AddSpot(CityBuildingTable.Building building, int scale)
    {
        var facility = Facility.For(building.Kind, building.Code);
        var tag = GameUi.NameTag(building.Kind);   // 지도 이름표에는 종류가 뜬다("술집")
        _layer.Children.Add(tag);
        _tags.Add(tag);

        // 누를 자리는 상자(96x80)의 가운데 절반이다 — 48x40(0x004733E0).
        var a = new Rect(building.HitX, building.HitY, building.HitWidth, building.HitHeight);
        var spot = new Border
        {
            Width = a.Width * scale,
            Height = a.Height * scale,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        Canvas.SetLeft(spot, a.X * scale);
        Canvas.SetTop(spot, a.Y * scale);
        _spots.Add((building, tag, a, scale));
        spot.MouseEnter += (_, _) =>
        {
            ShowTag(tag, a, scale);
            _pickedCode = building.Code;   // 커서를 올리면 글쇠 고름도 그리로 간다(0x00491C7B)
        };
        spot.MouseLeave += (_, _) => tag.Visibility = Visibility.Collapsed;
        // 건물을 누른 것은 여기서 삼킨다 — 안 그러면 그림 끌기가 먼저 걸려 메뉴가 안 열린다.
        spot.MouseLeftButtonDown += (_, e) => e.Handled = true;
        spot.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (MenuOpen) return;   // 명령 창이 떠 있으면 딴 건물은 안 눌린다
            Enter(building);
        };
        // 오른쪽 단추로 건물을 누르면 곧장 들지 않고 묻는다(0x004918D6 이 자리에 0x100 을 얹고,
        // 0x004934E0 이 그 건물 이름을 제목으로 「안으로 들어간다 · 도시로 돌아간다」를 낸다).
        // 빈 자리를 오른쪽으로 누르면 도시 커맨드다(0x110) — 그것은 창이 받는다.
        spot.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (MenuOpen) return;
            AskEnter(building);
        };
        _layer.Children.Add(spot);
    }

    /// <summary>
    /// 곧장 들지 않고 묻는다 — 오른쪽 단추와 글쇠(Enter · Space)가 이 길이다(<c>0x004934E0</c>).
    /// </summary>
    private void AskEnter(CityBuildingTable.Building building)
    {
        string title = building.Name.Length > 0 ? building.Name : building.Kind;
        if (ChoiceDialog.Pick(this, title, ["안으로 들어간다", "도시로 돌아간다"]) == 0) Enter(building);
    }

    /// <summary>그림에 올린 건물들 — 글쇠로 고를 때 차례와 이름표 자리를 여기서 찾는다.</summary>
    private readonly List<(CityBuildingTable.Building Building, Border Tag, Rect Area, int Scale)> _spots = [];

    /// <summary>글쇠로 고른 건물 코드(<c>[+0x1E0]</c>). 닿은 건물(항구·성문)이나 마지막에 든 건물에서 시작한다.</summary>
    private int _pickedCode = -1;

    /// <summary>
    /// 방향 글쇠로 건물을 고른다(<c>0x00491981</c>) — ↑·← 는 앞, →·↓ 는 뒤 코드로 돌며 선 건물만 밟고,
    /// 고른 건물에 이름표를 띄운다(<c>0x0044C150</c>). Enter · Space 는 그 건물을 묻는다(<c>0x0049195D</c>).
    /// </summary>
    private bool PickByKey(Key key)
    {
        if (_spots.Count == 0 || MenuOpen) return false;
        var order = _spots.OrderBy(sp => sp.Building.Code).ToList();
        int at = order.FindIndex(sp => sp.Building.Code == _pickedCode);

        switch (key)
        {
            case Key.Up or Key.Left:
                at = at < 0 ? order.Count - 1 : (at - 1 + order.Count) % order.Count;
                break;
            case Key.Right or Key.Down:
                at = at < 0 ? 0 : (at + 1) % order.Count;
                break;
            case Key.Enter or Key.Space:
                if (at < 0) return false;
                AskEnter(order[at].Building);
                return true;
            // 「0」 글쇠는 오른쪽 클릭과 같다 — 도시 커맨드 창을 그림 왼쪽 위에 낸다(0x00491CCA → 0x00491D01).
            case Key.D0 or Key.NumPad0:
                ShowCityMenu(_cityName, ToScreen(new Point(0, 0)));
                return true;
            default:
                return false;
        }

        _pickedCode = order[at].Building.Code;
        foreach (var tag in _tags) tag.Visibility = Visibility.Collapsed;
        ShowTag(order[at].Tag, order[at].Area, order[at].Scale);
        return true;
    }

    /// <summary>
    /// 그림에 서 있는 마을 사람 하나를 누를 수 있게 한다(<c>0x00491DC0</c>).
    /// </summary>
    /// <remarks>
    /// 사람 그림은 도시 그림에 이미 있고 표는 누를 자리만 준다. 건물 자리와 겹치면 건물이 먼저다 —
    /// 건물 자리를 뒤에 얹지 않고 여기서 먼저 깔아 두는 것으로 갈음한다.
    /// </remarks>
    private void AddFolk(TownFolkTable.Folk folk, int scale)
    {
        var a = new Rect(folk.HitX, folk.HitY, folk.HitWidth, folk.HitHeight);

        // 커서를 올리면 「남」·「여」 이름표가 뜬다(0x00491BB4 — 갈래 200 위가 여자다).
        var tag = GameUi.NameTag(folk.Kind >= TownFolkTable.FemaleKind ? "여" : "남");
        _layer.Children.Add(tag);
        _tags.Add(tag);
        var spot = new Border
        {
            Width = a.Width * scale,
            Height = a.Height * scale,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        Canvas.SetLeft(spot, a.X * scale);
        Canvas.SetTop(spot, a.Y * scale);
        spot.MouseEnter += (_, _) => ShowTag(tag, a, scale);
        spot.MouseLeave += (_, _) => tag.Visibility = Visibility.Collapsed;
        spot.MouseLeftButtonDown += (_, e) => e.Handled = true;
        spot.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (MenuOpen) return;
            TalkToFolk(folk);
        };
        _layer.Children.Add(spot);
    }

    /// <summary>
    /// 마을 사람이 한 마디 한다(<c>0x00492DC4</c>) — <b>얼굴도 이름도 없는</b> 창이다.
    /// </summary>
    /// <remarks>
    /// 1493년부터는 둘째 말이 있으면 그것을 한다. 갈래 100~102 는 반쯤 「이곳은 %s입니다.」·
    /// 「이곳은 %s의 도시입니다.」로 도시·나라 이름만 말한다(<c>0x00492E40</c>).
    /// 그 고장 말을 모르면 글자가 뭉개진다(<see cref="StrangerTalk.Garble"/>) — 부하가 더 잘하면
    /// 「[…]라고 말하고 있는 것 같습니다.」로 옮겨 준다.
    /// </remarks>
    /// <summary>멕시코의 도시 번호 — 마을 사람 말이 따로 박혀 있다(<c>0x00492E5B</c> 의 <c>cmp eax, 0xC9</c>).</summary>
    private const int MexicoCity = 201;

    private void TalkToFolk(TownFolkTable.Folk folk)
    {
        string words = folk.WordsOn(_player.Date.Year);
        // 멕시코(201)는 표 말을 안 쓰고 붙박이 넉 줄이다(0x00492E5B) — 갈래 100 은 반기는 말,
        // 나머지는 황금 이야기 가운데 rand(2) 로 하나.
        if (_cityId == MexicoCity)
            words = folk.Kind == 100
                ? _random.Next(2) == 0 ? "황금도시 멕시코에 잘 오셨습니다." : "이곳은 멕시코란 도시에요."
                : _random.Next(2) == 0 ? "이 도시에는 황금이 많이 있어요." : "이 도시는 멕시코라는 이름이에요.";
        else if (folk.Kind < 200 && _random.Next(2) != 0)
        {
            string nation = _game.Nations?.Find(_game.CityRows?.NationOf(_cityId) ?? -1)?.Name ?? "";
            words = _random.Next(2) == 0 || nation.Length == 0
                ? $"이곳은 {_cityName}입니다."
                : $"이곳은 {nation}의 도시입니다.";
        }

        // 말은 그 도시 나라 말이다 — 제독 수준으로 뭉개 들린다(0x004780E0).
        int language = _game.Nations?.Find(_game.CityRows?.NationOf(_cityId) ?? -1)?.Language ?? -1;
        int mine = language >= 0 && language < Skill.Languages.Length
            ? _player.TongueOf(Skill.Languages[language]) : Skill.MaxLevel;

        // 부하 가운데 그 말을 더 잘 아는 사람이 있으면 그 사람이 옮겨 준다(0x00492F47 → 0x0047CD20) —
        // 그 사람 수준으로 뭉갠 글을 그 얼굴로 「[%s]라고 말하고 있는 것 같습니다.」(0x0053BDE8) 한다.
        if (language >= 0 && BestTongue(language) is var (name, level) && level > mine)
        {
            var who = _game.MateInfo(name);
            var face = who is { Face: >= 0 and < 0xFFFF } m
                ? _game.Faces?.TryGetBgra(m.Face, female: false) : null;
            TalkDialog.Say(this, face, "",
                           $"[{StrangerTalk.Garble(words, level, _random)}]라고 말하고 있는 것 같습니다.");
            return;
        }

        NoticeDialog.Show(this, StrangerTalk.Garble(words, mine, _random));
    }

    /// <summary>그 말을 가장 잘 아는 부하와 그 수준(<c>0x0047CD20</c>). 아무도 없으면 빈 이름에 0 이다.</summary>
    private (string Name, int Level) BestTongue(int language)
    {
        string best = "";
        int level = 0;
        if (_game.World?.People is not { } people) return (best, level);

        for (int slot = 0; slot < Player.MaxMates; slot++)
        {
            string mate = _player.MateAt(slot);
            if (mate.Length == 0) continue;
            if (people.FirstOrDefault(r => r.Name == mate) is not { } row) continue;
            if (language >= row.Languages.Length || row.Languages[language] <= level) continue;
            best = mate;
            level = row.Languages[language];
        }
        return (best, level);
    }

    /// <summary>
    /// 건물이나 도시 명령 창이 떠 있는지. 게임은 창이 열린 채로는 <b>다른 건물을 못 누른다</b> —
    /// 먼저 창을 닫아야 한다. 예전에는 눌러지는 대로 그 건물 창으로 갈아탔다.
    /// </summary>
    private bool MenuOpen => Menus.FacilityWindow != null || _cityMenu.IsOpen;

    /// <summary>
    /// 건물 하나에 들어간다. 그림에서 눌러도, 커맨드의 "맵 포인트에 들어간다" 로 골라도
    /// 이 길을 지난다.
    /// </summary>
    private void Enter(CityBuildingTable.Building building, bool arrived = false)
    {
        // 초심자 개인 이야기(이야기0/1)가 <b>맨 먼저</b>다 — 게임은 들어서자마자 0x004AB5A0 으로
        // 건물 사건을 보고, 장면이 돌았으면 보복·문간 관문·차림표를 다 건너뛰고 건물을 나선다
        // (0x004A266A → 0x004A26BC). 그래서 명성이 모자라도 이야기의 저택에는 불려 들어간다.
        if (CheckStory(building.Code)) return;

        // 배신한 후원자의 나라에서는 건물에 들어서다 보복을 당한다(0x004A267D → 0x00450140).
        if (Ambushed()) return;

        var facility = Facility.For(building.Kind, building.Code);
        if (!PassFameGate(building, facility)) return;   // 문 앞에서 돌아섰다
        Discover(building);                              // 이 건물이 곧 발견물일 수 있다
        Greet(facility, building, arrived);
        ShowPhoto(facility.Kind, building.Code);
        if (arrived) facility = ArrivalHarbor(facility);
        _openKind = facility.Kind;
        _pickedCode = building.Code;
        // 명령 창 제목은 건물 이름이다 — 게임도 "베렌의 탑", "홍경정" 으로 낸다.
        ShowMenu(() => BuildMenu(facility, building.Name, building.Code, building.TeachMask,
                                 building.Kind),
                 BuildingTrack(building.Code));
        MarkGateway(facility.Kind, arrived);
    }

    /// <summary>왕궁의 건물 코드 — 문간 관문의 배수가 x100 이다(<c>0x00470AC0</c>).</summary>
    private const int PalaceCode = 2;

    /// <summary>교회의 건물 코드.</summary>
    private const int ChurchCode = 3;

    /// <summary>술집의 건물 코드.</summary>
    private const int TavernCode = 4;

    /// <summary>
    /// 건물에 들어가 있는 동안 도는 곡. 없으면(null) 도시 곡이 그대로 돈다.
    /// </summary>
    /// <remarks>
    /// 게임은 시설 갈래가 아니라 <b>건물 코드</b>로 가른다(<c>0x004929C4</c>) — 왕궁 2 는 소리
    /// <c>0x12</c>, 교회 3 은 <c>0x0E</c>, 술집 4 는 <c>0x14</c> 다. 왕궁과 술집은 도시 문화권이
    /// 0·1·2(이베리아·북유럽·지중해)일 때만 바뀌고(<c>0x00492B1B</c> · <c>0x00492B35</c> 의
    /// <c>test edi, edi</c>), 그 밖의 문화권에서는 문화권 곡이 그대로 돈다. 교회만 문화권을
    /// 안 가린다(<c>0x00492B2C</c>). 나설 때 문화권 곡으로 되돌리는 것은
    /// <c>0x00492BC1</c> 이다.
    /// </remarks>
    private int? BuildingTrack(int code) => code switch
    {
        PalaceCode => European ? BgmPlayer.PalaceTrack : null,
        ChurchCode => BgmPlayer.ChurchTrack,
        TavernCode => European ? BgmPlayer.TavernTrack : null,
        _ => null,
    };

    /// <summary>문화권이 유럽 셋(이베리아·북유럽·지중해)인지 — 왕궁·술집 곡의 조건이다.</summary>
    private bool European => _cultureNo is 0 or 1 or 2;

    /// <summary>지금 들어와 있는 시설 갈래 — 교회의 설득은 들머리 관문이 하나 더 있다(<c>0x004AE1F0</c>).</summary>
    private FacilityKind? _openKind;

    /// <summary>건물 표에 항구가 서 있는지 — 없으면 그림 아무 데나 눌러 항구에 든다.</summary>
    private readonly bool _harborPlaced;

    /// <summary>
    /// 건물 표에 항구가 없는 도시의 항구 차림표를 연다 — 그림 아무 데나 누르면 이리 온다.
    /// </summary>
    private void OpenBareHarbor(bool arrived)
    {
        var harbor = Facility.For("항구");
        GreetHarbor(arrived);
        var shown = arrived ? ArrivalHarbor(harbor) : harbor;
        ShowMenu(() => BuildMenu(shown, harbor.Name, HarborCode, 0, harbor.Name), BuildingTrack(HarborCode));
        MarkGateway(FacilityKind.Harbor, arrived);
    }

    /// <summary>
    /// 바다로 닿은 항구의 차림표 — 마지막 줄이 「마을로 돌아간다」 대신 「마을에 들어간다」다
    /// (<c>0x00477979</c>: 건물 <c>+0x98</c> 이 서 있으면 <c>0x00545368</c>, 아니면 <c>0x00545378</c>).
    /// </summary>
    private static Facility ArrivalHarbor(Facility harbor) =>
        harbor.Kind == FacilityKind.Harbor
            ? harbor with { Menu = [.. harbor.Menu[..^1], "마을에 들어간다"] }
            : harbor;

    /// <summary>항구·성문이 열렸으면 나설 때 할 말을 위해 적어 둔다. 딴 건물이면 지운다.</summary>
    private void MarkGateway(FacilityKind kind, bool arrived)
    {
        _gateway = kind is FacilityKind.Harbor or FacilityKind.Gate ? kind : null;
        _arrived = _gateway != null && arrived;
    }

    /// <summary>
    /// 도시에 닿았다 — <b>바다로 왔으면 항구 차림표가 먼저 뜬다</b>. 뭍으로 왔으면 성문을
    /// 저절로 지나 부관 인사만 하고 도시 그림이다.
    /// </summary>
    /// <remarks>
    /// 게임은 닿을 때 들어온 건물을 「닿아서 들어옴」(<c>+0x98</c>)으로 돌린다(<c>0x004A2530</c>).
    /// 성문(코드 10)이면 차림표 없이 곧장 나서기 갈래(<c>0x004A2740</c> → 칸 10 <c>0x00468790</c>)를
    /// 돌고(<c>0x004A2612</c>), 항구면 차림표를 연다. 그 차림표에서 「마을에 들어간다」를 골라야
    /// 같은 칸 10 이 돌아 부관이 「이 마을에서 잠깐 쉽시다」 한다.
    /// </remarks>
    public void Arrive(bool bySea)
    {
        if (!bySea)
        {
            _pickedCode = GateCode;   // 글쇠 고르기는 들어온 성문에서 시작한다(0x00492CCA)
            LeaveGateway(FacilityKind.Gate, arrived: true);
            return;
        }

        foreach (var building in Standing(_cityId))
            if (Facility.For(building.Kind, building.Code).Kind == FacilityKind.Harbor)
            {
                Enter(building, arrived: true);
                return;
            }
        if (!_harborPlaced) OpenBareHarbor(arrived: true);
    }

    /// <summary>
    /// 항구·성문을 나서 마을로 든다 — 칸 10(<c>0x00468790</c>)이다. 부관이 있을 때만 말한다(<c>0x004696B0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   닿아서 들어왔으면(+0x98)
    ///     0x004687B0  항구이고 모항이면  rand(2) 로 0x00551960 · 0x00551980
    ///     0x004687E5  그 밖이면          0x005519A0 「제독, 이 마을에서 잠깐 쉽시다.」
    ///     0x0046885F  모항 악명 3000 넘으면 병사가 막는다(0x0046B980)
    ///   아니면(마을에서 걸어 들어왔다 나간다)
    ///     0x00468874  성문이면 0x005519C0 「출발할 때는…」, 항구면 0x005519F0 「출항할 때에는…」
    /// </code>
    /// 적대 도시 차림표(<c>0x004687FD</c>)는 우리 쪽이 도시 그림을 열기 전에 이미 돈다(<c>ShipMapWindow.PassGate</c>).
    /// </remarks>
    private void LeaveGateway(FacilityKind kind, bool arrived)
    {
        bool harbor = kind == FacilityKind.Harbor;
        if (!arrived)
        {
            if (_game.AideFace is { } face)
                TalkDialog.Say(this, face, "", harbor
                    ? "출항할 때에는 말해 주십시오. 곧 준비하겠습니다."
                    : "출발할 때는 말해 주십시오. 곧 준비하겠습니다.");
            return;
        }

        if (_game.AideFace is { } aideFace)
            TalkDialog.Say(this, aideFace, "",
                harbor && _cityId == _player.HomePort
                    ? _game.Random.Next(2) == 0
                        ? "제독, 역시 모항이 좋군요." : "모항에 돌아오면 안심되는군요."
                    : "제독, 이 마을에서 잠깐 쉽시다.");

        // 악명이 3000 을 넘으면 <b>모항</b>에 닿을 때 병사가 막아선다(0x0046885D).
        if (_player.Infamy > Standoff.VillainInfamy && _cityId == _player.HomePort)
        {
            Villain(harbor, harbor ? HarborCode : GateCode);
            return;
        }

        AmbientFolk();
    }

    /// <summary>
    /// 닿아서 마을에 들면 <b>누르지 않아도</b> 마을 사람이 한 마디 할 때가 있다(<c>0x00492BD0</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x00492BDC  닿음(0x005B6384)이 서 있고 나온 건물이 항구(0)·성문(10)일 때
    ///   0x00492C01  rand(5) == 0
    ///   0x00492C5B  그 고장 말 수준(0x00478050)이 2 이상
    ///   0x00492C60  갈래 표 0x0056A0D0(100·101·102·200·201·202) 가운데 이 도시에 있는 것에서 rand
    /// </code>
    /// 그 뒤는 누른 것과 같다(<see cref="TalkToFolk"/>). 말 수준은 누를 때처럼 제독 것으로 본다.
    /// </remarks>
    private void AmbientFolk()
    {
        if (_random.Next(5) != 0) return;

        int language = _game.Nations?.Find(_game.CityRows?.NationOf(_cityId) ?? -1)?.Language ?? -1;
        int mine = language >= 0 && language < Skill.Languages.Length
            ? _player.TongueOf(Skill.Languages[language]) : Skill.MaxLevel;
        if (mine < 2) return;

        // 갈래마다 표에서 처음 걸리는 사람이다(0x00473800).
        var here = (_game.TownFolk?.InCity(_cityId) ?? [])
            .GroupBy(f => f.Kind).OrderBy(g => g.Key).Select(g => g.First()).ToList();
        if (here.Count == 0) return;
        TalkToFolk(here[_random.Next(here.Count)]);
    }


    /// <summary>
    /// 뭍의 추격 — 배신한 후원자의 보복(<c>0x00450140</c>). 건물에 못 들어가게 됐으면 true.
    /// </summary>
    /// <remarks>
    /// 뒤쫓는 후원자(원래 기한이 지난 배신) 가운데 <b>이 도시와 나라가 같은</b> 사람만 본다.
    /// <code>
    ///   그중 성미[2] == 2(집착)가 있으면 도둑 (0x00450060)
    ///     rand(3) == 0 이고 보관품이나 예금이 있으면 — 보관품은 칸마다 1/3 로 사라지고 예금은 30% 만 남는다
    ///     「여보, 미안해요. 내가 없는 동안 도둑이 들었어요!」 / 「도둑이 들었습니다. 스폰서의 보복이겠지요.」
    ///   아니면 현상금 사냥꾼 (0x0044FEB0) — rand(100) &gt; 운 + 1 이면
    ///     「어이... 저 자, 벽보의...」「확실히...」 부관 「왠지 분위기가 않좋군요, 도망칩시다.」
    ///     r = rand(100) — r ≤ 96 이고 체력 + 1 &gt; r 이면 달아난다 「후우~, 더 이상 쫓아오지 않는군요 …」
    ///     r &gt; 96 이면 곧바로 붙잡힌다, 아니면 일기토(인물 268) — 지면 붙잡힌다
    ///     붙잡히면 「좋아, 상금 걸린 자를 붙잡았다!」 → 후원자 「정신이 드나? …」 → GAME OVER
    /// </code>
    /// 일기토에 져서 베이면(결과 3) 붙잡는 말 없이 곧바로 놀이가 끝난다(<c>0x0044FFBE</c> 의 <c>0x0044AF40(4)</c>).
    /// </remarks>
    private bool Ambushed()
    {
        int nation = _game.CityRows?.NationOf(_cityId) ?? -1;
        if (nation < 0) return false;

        var hunters = _player.Pursuers
            .Select(b => (Betrayal: b, Sponsor: _game.Sponsors?.FindByName(b.Sponsor)))
            .Where(p => p.Sponsor is { } s && s.Nation == nation)
            .ToList();
        if (hunters.Count == 0) return false;

        var dice = _game.Random;
        var aide = _game.AideFace;

        // 가) 집착하는 후원자가 있으면 도둑을 보낸다.
        if (hunters.Any(h => PatronMenu.SponsorFortune(h.Sponsor)[2] == 2))
        {
            if (dice.Next(3) != 0 || (_player.Stored.Count == 0 && _player.Savings == 0)) return false;

            for (int i = _player.Stored.Count - 1; i >= 0; i--)
                if (dice.Next(3) == 0) _player.LoseStored(i);
            _player.LoseSavings(30);

            if (_player.Spouse.Length > 0)
                TalkDialog.Say(this, null, _player.Spouse, "여보, 미안해요. 내가 없는 동안 도둑이 들었어요!");
            else
                TalkDialog.Say(this, aide, "", "도둑이 들었습니다. 스폰서의 보복이겠지요.");
            return true;
        }

        // 나) 현상금 사냥꾼.
        if (dice.Next(100) <= _player.AbilityOf(Ability.Luck) + 1) return false;

        GameDialog.Show(this, "어이... 저 자, 벽보의...");
        GameDialog.Show(this, "확실히...");
        TalkDialog.Say(this, aide, "", "왠지 분위기가 않좋군요, 도망칩시다.");

        int r = dice.Next(100);
        if (r <= 96 && _player.AbilityOf(Ability.Body) + 1 > r)
        {
            TalkDialog.Say(this, aide, "", "후우~, 더 이상 쫓아오지 않는군요. 제독, 여긴 너무 위험합니다. 빨리 마을을 떠납시다.");
            return true;
        }

        if (r <= 96)
        {
            // 판 결과 0·1 이면 달아나고, 2(졌지만 살았다)면 붙잡히고, 3(베였다)이면 붙잡는 말도 없이
            // 곧바로 놀이가 끝난다(0x0044FFAF → 0x0044FFBE 의 0x0044AF40(4)).
            switch (HunterDuel())
            {
                case null or true:
                    TalkDialog.Say(this, aide, "", "후우~, 더 이상 쫓아오지 않는군요. 제독, 여긴 너무 위험합니다. 빨리 마을을 떠납시다.");
                    return true;
                case false when _huntSlain:
                    EndGame();
                    return true;
            }
        }

        var boss = hunters[dice.Next(hunters.Count)].Sponsor!.Value;
        GameDialog.Show(this, "좋아, 상금 걸린 자를 붙잡았다!");

        // 붙잡혀 깨어났을 때의 말도 말투 세 벌이다(0x0045000A · 0x00450005 · 0x00450000).
        var sponsorRow = _game.Sponsors?.FindByName(boss.Name);
        int style = sponsorRow is { IsFemale: true } ? 1
                  : sponsorRow is { JobCode: >= 18 and <= 21 } ? 2 : 0;
        TalkDialog.Say(this, _game.Faces?.TryGetBgra(boss.Face, boss.IsFemale), "", style switch
        {
            1 => "정신이 드십니까? 용서를 빌면 눈감아 드리려 했건만 안된 일이라고 생각합니다.",
            2 => "겨우 정신이 들었나? 어이없군, 한마디 용서를 빌었다면 끝났을 일을..., 정말 성가시게도 했군.",
            _ => "정신이 드나? 바보같은 녀석, 얌전히 용서를 빌었더라면 도와 주었을 것을..., 쓸데없는 수고를 하게 하다니!",
        });
        GameOverDialog.Show(this, _game.EventStills, GameOverDialog.MutinyLost, bgm: _game.Bgm);
        if (Owner is ShipMapWindow map) Dispatcher.BeginInvoke(map.ReturnToTitle);
        return true;
    }

    /// <summary>
    /// 모항이 등을 돌린다(<c>0x0046B980</c>) — 악명 3000 을 넘으면 병사가 일기토를 건다.
    /// </summary>
    /// <remarks>
    /// 막는 말은 둘 가운데 굴려 고르고 <b>문지기 얼굴</b>로 나온다. 지면(판 결과 2·3) 놀이가 끝나고
    /// (<c>0x0046B894</c> 가 상태 4 로 끝낸다), 이기면 처형·놓아 준다·모두 뺏는다를 고른다 — 처형(결과 0)이면
    /// 악명이 말없이 500 오르고(<c>0x004697C0(1, 500)</c>), 그 밖(결과 1)은 악명이 안 오른다.
    /// </remarks>
    private void Villain(bool harbor, int code)
    {
        var dice = _game.Random;
        var face = _game.SpeakerFace(code, _cultureNo);
        TalkDialog.Say(this, face, "", Standoff.VillainWords[dice.Next(Standoff.VillainWords.Length)]);

        var (body, might, sword, luck) = Standoff.SoldierOf(dice);
        var foe = new Engine.Town.Duel.Fighter(Standoff.SoldierName(harbor), body, might, sword, luck, 0, 0);
        var mine = new Engine.Town.Duel.Fighter(_player.Name.Length > 0 ? _player.Name : "제독",
            _player.AbilityOf(Ability.Body), _player.AbilityOf(Ability.Might),
            _player.LevelOf(Skill.Names[Skill.Sword]), _player.AbilityOf(Ability.Luck), 0, 0);

        var duel = new Engine.Town.Duel(mine, foe, _player.Items.Contains(Engine.Town.Duel.EdithShieldId),
                                        Environment.TickCount);
        bool won = DuelDialog.Show(this, duel, new GameRandom(Environment.TickCount), face,
                                   _game.Fighters, FighterSprites.SetForCulture(_cultureNo),
                                   myFace: _game.Faces?.TryGetBgra(
                                       PortraitAges.At(_player.Face, _player.Age, false, _game.Faces),
                                       female: false),
                                   arena: DuelArt.Field, bgm: _game.Bgm);
        _player.Hurt(duel.BodyLost);

        if (won)
        {
            // 성문 앞 판은 무대 1(초원)이라 「처형한다」 한 줄뿐이다(0x004A847A).
            if (Guests.Triumph(TavernMenu.BrawlPerson, face, new GameRandom(Environment.TickCount),
                               indoors: false) == 0)
                _player.Infamy += Standoff.VillainInfamyUp;
            return;
        }

        // 성문 앞 병사와의 판은 종류 6 이라 도망도 용서도 없다(0x004A9EDE).
        TavernMenu.LostDuel(this, _player, duel, face, new GameRandom(Environment.TickCount),
                            mateFought: false, canFlee: false, canSpare: false);
        GameOverDialog.Show(this, _game.EventStills, GameOverDialog.MutinyLost, bgm: _game.Bgm);
        if (Owner is ShipMapWindow map) Dispatcher.BeginInvoke(map.ReturnToTitle);
    }

    /// <summary>
    /// 현상금 사냥꾼(인물 268)과 일기토. 이기면 true, 지면 false, 판을 못 열면 null.
    /// 지면 여느 일기토처럼 도망·용서·죽음 말이 나고, 죽음이면 <see cref="_huntSlain"/> 이 선다.
    /// </summary>
    private bool? HunterDuel()
    {
        const int hunter = Engine.Sea.Encounter.ChaserLeader;
        var row = PersonTable.Open()?.Find(hunter);
        if (row == null || row.Stats.Length < 5) return null;

        int sword = row.Skills.Length > Skill.Sword ? row.Skills[Skill.Sword] : 0;
        var foe = new Engine.Town.Duel.Fighter(row.Name, row.Stats[0], row.Stats[2], sword, row.Stats[4], 0, 0);
        var mine = new Engine.Town.Duel.Fighter(_player.Name.Length > 0 ? _player.Name : "제독",
            _player.AbilityOf(Ability.Body), _player.AbilityOf(Ability.Might),
            _player.LevelOf(Skill.Names[Skill.Sword]), _player.AbilityOf(Ability.Luck), 0, 0);

        var dice = new GameRandom(Environment.TickCount);
        var duel = new Engine.Town.Duel(mine, foe, _player.Items.Contains(Engine.Town.Duel.EdithShieldId),
                                        Environment.TickCount);
        var face = _game.PersonTemplates?.Find(hunter) is { } t ? _game.Faces?.TryGetBgra(t.Face, female: false) : null;
        DuelDialog.Show(this, duel, dice, face, _game.Fighters, FighterSprites.SetForCulture(_cultureNo),
                        myFace: _game.Faces?.TryGetBgra(PortraitAges.At(_player.Face, _player.Age, false, _game.Faces),
                                                        female: false),
                        // 추격대는 든 건물에 따라 무대가 갈린다(0x004A2D92) — 술집·여관이면
                        // 문화권 배경이고 그 밖에는 초원이다.
                        arena: _openKind == FacilityKind.Tavern || _openKind == FacilityKind.Inn
                                   ? DuelArt.TavernFor(_cultureNo) : DuelArt.Field,
                        bgm: _game.Bgm);
        _huntSlain = false;
        if (duel.Won != true && TavernMenu.LostDuel(this, _player, duel, face, dice, mateFought: false))
        {
            _huntSlain = true;
            return false;
        }
        _player.Hurt(duel.BodyLost);
        return duel.Won;
    }

    /// <summary>현상금 사냥꾼에게 져서 베였다 — 판 결과 3 이다.</summary>
    private bool _huntSlain;

    /// <summary>
    /// 이 마을 자택으로 곧바로 들어선다. 새 판이 시작될 때 쓴다 —
    /// <b>게임도 판을 열면 자택 명령 창이 이미 떠 있다.</b>
    /// </summary>
    /// <remarks>자택이 없는 마을이면 아무 일도 없다(고향이 아닌 데서 부를 일은 없다).</remarks>
    public void EnterHome()
    {
        foreach (var building in _table.InCity(_cityId))
            if (Facility.For(building.Kind, building.Code).Kind == FacilityKind.Home) { Enter(building); return; }
    }

    /// <summary>
    /// 건물 자체가 발견물이면 들어서는 그 자리에서 발견한다 — 세빌리아 교회가 51번
    /// 히랄다탑이다.
    /// </summary>
    /// <remarks>
    /// 발견물 번호도 그림 번호도 <b>건물 표</b>가 들고 있다(<c>+0x1C</c>, <c>+0x14</c>).
    /// 바다에서 하는 판정(<see cref="DiscoveryLog.At"/>)과는 길이 아주 다르다 — 이런 것들은
    /// 지도에 사각형이 없어(<c>-1</c>) 자리로는 영영 안 잡힌다.
    ///
    /// 힌트로 열리는 것도 있으므로 <see cref="DiscoveryLog.IsOpen"/> 을 거친다.
    /// </remarks>
    private void Discover(CityBuildingTable.Building building)
    {
        if (!building.IsDiscovery) return;
        if (_game.Discoveries is not { } log) return;
        if (log.Table.Find(building.Discovery) is not { } row) return;
        if (_player.HasFound(row.Id)) return;
        if (!log.IsOpen(_player, row)) return;

        // 발견 대본(DISEV)이 있으면 <b>그것이 다 한다</b> — 동영상 · 대사 · 육상전까지. 바다·뭍 발견
        // (ShipMapWindow.CheckDiscovery)과 같은 길이다. 예전에는 건물 발견만 그림 한 장으로 끝내서
        // 파르테논 신전에서 동영상도 육상전도 안 났다.
        bool scripted = Engine.Disev.DisevRunner.Run(this, _game, row.Id);
        // 대본이 게임 오버로 끝났으면(파르테논 육상전에서 전멸하거나 물러나 저주를 받으면) 발견을
        // 적지 않고 놀이를 끝낸다 — 바다·뭍 발견과 같은 차례다(ShipMapWindow.CheckDiscovery).
        if (Engine.Disev.DisevRunner.LastEndedInGameOver)
        {
            GameOverDialog.Show(this, _game.EventStills, Engine.Disev.DisevRunner.LastGameOverPicture, bgm: _game.Bgm);
            if (Owner is ShipMapWindow map) Dispatcher.BeginInvoke(map.ReturnToTitle);
            return;
        }

        // 대본이 돌았으면 발견·물건은 대본의 01 0B 가 준다(0x0048D3F0 은 따로 안 적는다).
        if (!scripted) log.Discover(_player, row.Id);

        // 대본이 없을 때만 그림 한 장으로 알린다.
        // 게임 문구는 "%s%s 발견했다!"(0x00544720) 다 — 이름 뒤에 을/를 이 붙는다.
        if (!scripted)
            DiscoveryDialog.Show(this, _game.Stills, building.Picture,
                                 $"{row.Name}{GameUi.Josa(row.Name, "을", "를")} 발견했다!");

    }

    /// <summary>
    /// 초심자(EASY) 캐릭터의 개인 퀘스트라인(이야기0·이야기1) 한 장면을 체크한다.
    /// </summary>
    /// <remarks>
    /// 건물에 들어서는 <b>맨 첫머리</b>에서 건다(게임의 <c>0x004AB5A0</c>). 장면이 결과 코드 1(<c>4D</c>)로
    /// 끝났으면 true — 그러면 부르는 쪽은 문간 관문도 차림표도 안 연다.
    /// 이야기0/1 은 발견물 표에 없는 대신 <see cref="Engine.Discovery.StoryLog"/> 가 건물·도시·
    /// 연도·명성 같은 조건을 그때그때 살펴 지금 틀 장면을 찾아 준다. 새로운 주인공(NORMAL)은
    /// <see cref="Player.ActiveStoryBook"/> 이 없어 곧장 지나간다.
    /// </remarks>
    private bool CheckStory(int building)
    {
        if (_player.ActiveStoryBook is not { } book) return false;
        if (Engine.Discovery.StoryLog.NextPart(_player, _game, building) is not { } part) return false;

        Engine.Disev.DisevRunner.Run(this, _game, book, part, building);
        Engine.Discovery.StoryLog.Advance(_player, _game, book, part);

        if (Engine.Disev.DisevRunner.LastEndedInGameOver)
        {
            GameOverDialog.Show(this, _game.EventStills, Engine.Disev.DisevRunner.LastGameOverPicture, bgm: _game.Bgm);
            if (Owner is ShipMapWindow map) Dispatcher.BeginInvoke(map.ReturnToTitle);
            return true;
        }
        // 결과 코드가 1(4D)일 때만 건물에 못 든다(0x004AB4AC). 도서관 안내처럼 말만 하고 끝나면 그대로 들어간다.
        return Engine.Disev.DisevRunner.LastResult == 1;
    }

    /// <summary>한 장이 머무는 참. 다섯 장을 이으면 1.1초쯤 된다.</summary>
    private static readonly TimeSpan FrameSpan = TimeSpan.FromMilliseconds(220);

    private bool _playing;

    /// <summary>
    /// 후원자가 앉은 건물의 <b>명성 관문</b>. 통과하면 true, 문 앞에서 돌아섰으면 false 다.
    /// </summary>
    /// <remarks>
    /// 명성이 모자라면 설득이 엎어지고 <b>명령 창이 아예 안 열린다</b> — 게임도 그 자리에서
    /// 도시 그림으로 돌아가고 집사가 돌려보내는 소리(효과음 파트 1)를 낸다.
    ///
    /// <b>왕궁과 교회는 뺀다.</b> 그 둘은 후원자를 못 만나도 건물 자체에는 들어간다 —
    /// 왕궁에는 알현·의뢰 같은 제 줄이 있고 교회는 수련하는 데다. 막히는 것은 후원자만
    /// 앉아 있는 곳(총독부·상관·저택 따위)이다.
    /// </remarks>
    private bool PassFameGate(CityBuildingTable.Building building, Facility facility)
    {
        var patron = PatronAt(building.Kind);
        if (patron == null) return true;

        // <b>교회만</b> 문에서 안 본다. 교회는 수련하는 데라 후원자를 못 만나도 들어가고,
        // 명성 관문은 "설득" 을 누를 때 집사가 대신 본다(<see cref="PatronMenu.Persuade"/> ·
        // 게임 0x004AE1F0).
        //
        // <b>왕궁은 막는다.</b> 후원자가 앉은 건물들과 한 벌이다 — 게임도 그 다섯 벌의
        // 가상함수표 첫 칸이 다 0x0040D370(문간 관문)이다(볼트 22 참고). 한때 왕궁까지
        // 빼 두었는데 그것이 틀렸다.
        if (facility.Kind is FacilityKind.Church) return true;

        // <b>이미 만난 후원자면 문을 안 본다.</b> 게임의 관문(0x0044E740)은 후원자 +0x28 비트 15(첫 알현 전)가
        // 서 있을 때만 명성을 잰다(vtbl+0x34 = 0x004AD800). 첫 알현(0x004AE595)이나 이야기 대본의 38 12 가
        // 그 비트를 지운다 — 라몬의 파브리스, 에밀리오의 에란쪼가 명성 없이 열리는 까닭이다.
        if (_player.HasMet(patron.Name) ||
            _game.Sponsors?.FindByName(patron.Name) is { } known && _player.HasMet(known.Name))
            return true;

        // 안목에 거는 배수가 건물마다 다르다 — 왕궁(코드 2)은 0x00470AC1 의 x100, 저택(12~15)은
        // 0x0040D371 의 x70 이다(0x0044E740: 안목 x 배수 ≤ 명성이면 통과). patrons.json 의 fame 은 x70 값이다.
        int eye = _game.Sponsors?.FindByName(patron.Name)?.Eye ?? patron.Fame / 70;
        bool passed = _player.Fame >= eye * (building.Code == PalaceCode ? 100 : 70);

        PlayFameCheck(passed);

        if (passed) return true;

        _game.Sfx?.Play(SoundBank.TurnedAwayPart);

        // 문지기가 내쫓는다. 얼굴은 그 건물의 화자 그대로다 — 게임도 시설 객체가 든
        // 같은 얼굴을 쓴다(0x0040D385 가 +0x84 를 넘긴다).
        ConfirmDialog.Tell(this, "너 같은 녀석이 들어올 장소가 아니다! 꺼지지 못할까!",
                           face: _game.SpeakerFace(building.Code, _cultureNo));
        // 「명성치가 모자랍니다」(0x00544BF0 · 0x00544BA0)는 힌트 패널(0x0040E0A0)로만 가는 안 보이는 기록이라
        // 화면에 내지 않는다(0x0040D39B · 0x00470AE3 — 디버그 깃발 [0x00580C6C]&2 뒤).
        return false;
    }

    /// <summary>
    /// 후원자가 앉은 건물에 들어설 때 도는 <b>설득 애니메이션</b>(MPEFFECT 5번).
    /// </summary>
    /// <remarks>
    /// 게임은 이것을 명성 관문 안에서 돌린다 — <c>0x0044E740</c> 이 후원자의 필요 명성과 내
    /// 명성을 견주고, 그 결과를 그대로 애니메이션의 인자로 넘긴다(우리도 <paramref name="passed"/>
    /// 로 받는다). 그림 넉 장이 곧 결말까지
    /// 담고 있어서, <b>통과면 청을 들어주는 셋째 장에서 멈추고 모자라면 엎어지는 끝 장까지</b>
    /// 간다. 자세한 것은 볼트 <c>22.분석-애니메이션(MPEFFECT·EVANIME)</c> 참고.
    ///
    /// 한 번 만난 뒤에는 게임도 관문을 건너뛴다(후원자 비트 15) — <see cref="PassFameGate"/> 가
    /// 그것을 보므로 이 애니메이션도 <b>첫 알현 때만</b> 돈다.
    /// </remarks>
    public void PlayFameCheck(bool passed) =>
        PlayEffect(EffectAnim.Persuade, [.. Plead, passed ? Granted : Refused]);

    /// <summary>
    /// 자택 "후손을 남긴다" 의 애니메이션 — <b>MPEFFECT 2번(대포)</b>이다.
    /// </summary>
    /// <remarks>
    /// 게임도 그렇다(<c>0x004613E3</c> 이 <c>0x004A6340</c> 을 부른다). 그 껍데기는 인자가
    /// 1 이면 소리 <c>0x2A</c>, 아니면 <c>0x2B</c> 를 함께 낸다 — 되고 안 되고가 곧 소리다.
    /// </remarks>
    public void PlayHeir(bool born) =>
        PlayEffect(EffectAnim.Cannon, [.. Plead, born ? Granted : Refused]);

    /// <summary>
    /// 후원자의 마음이 동하는지 — <b>MPEFFECT 3번(하트)</b>이다.
    /// </summary>
    /// <remarks>
    /// 이야기를 고르고 나서 돈다(<c>0x004AE7B7</c> · <c>0x004AE815</c>). 굴림에 이기면
    /// 하트가 커지고, 지면 깨진다 — 넷째 장이 곧 깨진 하트다.
    /// </remarks>
    public void PlayHeart(bool won) =>
        PlayEffect(EffectAnim.Heart, [.. Plead, won ? Granted : Refused]);

    /// <summary>
    /// 동그란 애니메이션 한 벌을 도시 그림 한가운데에서 돌린다.
    /// </summary>
    /// <remarks>
    /// <b>도는 동안 시설 명령 창을 접는다.</b> 명령 창은 제 창(HWND)이라 도시 그림 <b>위에</b>
    /// 뜨는데, 애니메이션은 그림 위에 얹히므로 창에 통째로 가려 안 보였다 — 후원자에게
    /// 이야기를 내밀 때 도는 하트가 그래서 한 번도 안 나왔다. 게임도 이때는 명령 창을 지운다.
    /// </remarks>
    private void PlayEffect(int anim, int[] order)
    {
        if (_playing) return;                       // 도는 동안 또 누르면 겹친다

        var effects = _game.Effects;
        if (effects == null) return;

        bool wasShown = Menus.FacilityWindow is { Visibility: Visibility.Visible };
        if (wasShown) HideMenu(true);

        double side = EffectAnim.Size * _scale;
        var image = new Image { Width = side, Height = side, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        // 그림 한가운데에 놓는다. 예전에는 누른 건물 위에 놓았는데, 건물이 구석에 있으면
        // 애니메이션도 구석으로 밀려 났다 — 게임은 늘 화면 가운데에서 돈다.
        Canvas.SetLeft(image, (CityPictures.Width * _scale - side) / 2);
        Canvas.SetTop(image, (CityPictures.Height * _scale - side) / 2);
        Panel.SetZIndex(image, 30);
        _layer.Children.Add(image);

        // 같은 장이 두 번 나오므로 한 번만 풀어 둔다.
        var art = new BitmapSource?[EffectAnim.FrameCount];

        _playing = true;
        try
        {
            foreach (int f in order)
            {
                if (art[f] == null)
                {
                    var bgra = effects.TryGetBgra(anim, f);
                    if (bgra == null) continue;

                    var bmp = BitmapSource.Create(EffectAnim.Size, EffectAnim.Size, 96, 96,
                                                  PixelFormats.Bgra32, null, bgra, EffectAnim.Size * 4);
                    bmp.Freeze();
                    art[f] = bmp;
                }
                image.Source = art[f];
                Wait(FrameSpan);
            }
        }
        finally
        {
            _layer.Children.Remove(image);
            _playing = false;
            if (wasShown) HideMenu(false);
        }
    }

    /// <summary>
    /// 청하는 두 장. 이것을 두 번 되풀이해 흔든 뒤 결말 장으로 넘어간다(모두 0부터 센다).
    /// </summary>
    private static readonly int[] Plead = [0, 1, 0, 1];

    /// <summary>결말 장 — 받아 드는 셋째 장과 엎어지는 넷째 장.</summary>
    private const int Granted = 2, Refused = 3;

    /// <summary>
    /// 그동안 화면이 멎지 않게 하면서 한 참 기다린다. 애니메이션을 다 돌리고 나서 명령 창을
    /// 열어야 하는데, <c>Thread.Sleep</c> 으로 막으면 그림이 아예 안 바뀐다.
    /// </summary>
    private static void Wait(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(span, DispatcherPriority.Normal,
                                        (_, _) => frame.Continue = false,
                                        Dispatcher.CurrentDispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    /// <summary>
    /// "수련" — 맡은 사람이 먼저 묻고, 창을 닫을 때 아무것도 안 배웠으면 한마디 한다.
    /// </summary>
    /// <remarks>
    /// <b>건물마다 사람도 말도 다르다.</b> 게임은 문구를 <c>0x00490D90(교회, 조합, 그밖)</c>
    /// 으로 고른다 — 건물 종류로 셋 중 하나를 집는 갈래표다(<c>0x00490DDC</c>).
    /// <code>
    ///   0x0055A7E8  교회    "주의 배움의 터전에 잘 오셨습니다. 어떤 학문, 기능을 배우고 싶습니까?"
    ///   0x0055A830  조합    "기술을 습득하고 싶나?"
    ///   0x0055A848  그 밖   "가르쳐 드릴 것은 한가지 밖에 없습니다만."   (배울 것이 하나일 때)
    ///   0x0055A878          "무엇을 배우고 싶은가?"
    /// </code>
    /// 우리가 "수련" 을 내는 곳은 교회와 조합 둘이라 그 둘만 갈랐다.
    /// </remarks>

    /// <summary>항구에 들어설 때의 말 — 부관 물음과 빌린 배 인사(<see cref="HarborMenu.Greet"/>).</summary>
    private void GreetHarbor(bool arrived) => Port.Greet(arrived);

    /// <summary>건물에 들어설 때 그 시설 사람이 건네는 한마디.</summary>
    /// <remarks>
    /// 인사말도 얼굴도 <b>시설이 든다</b> — 게임도 시설 객체마다 제 인사 자리가 있다
    /// (조선소 <c>0x0044B4A0</c>). 도시 창이 하는 일은 어느 시설인지 가르는 것뿐이고,
    /// 시설은 이 창이 든 문화권(<see cref="_cultureNo"/>)으로 제 화자를 찾는다.
    ///
    /// 문구를 아직 못 찾은 시설은 창이 없거나 조용하다.
    /// </remarks>
    private void Greet(Facility facility, CityBuildingTable.Building building, bool arrived = false)
    {
        // 항구·성문의 부관 두 마디(「이 마을에서 잠깐 쉽시다」·「출항할 때에는…」)와 모항 병사는
        // 들어설 때가 아니라 <b>나설 때</b>다 — 칸 10(0x00468790)이 나서기(0x004A2740)에서 돈다.
        // 여기는 칸 2(항구 0x004770A0 · 성문 0x0048F1D0)다. 성문은 들어설 때 아무 말이 없다.

        switch (facility.Kind)
        {
            case FacilityKind.Harbor: GreetHarbor(arrived); break;
            case FacilityKind.Shipyard: Yard.Greet(); break;
            case FacilityKind.Library: Books.Greet(); break;
            case FacilityKind.Tavern: Guests.Greet(); break;
            case FacilityKind.Market: Shop.Greet(); break;
            case FacilityKind.TradingPost: TradePostDialog.Greet(this, _game, _cultureNo); break;
            case FacilityKind.Home: HomeRooms.Greet(); break;

            // 조합(0x004AC840) — 부관이 있으면 부관이, 없으면 조합 사람이 말한다.
            case FacilityKind.Guild:
                if (_game.AideFace is { } trainer)
                    TalkDialog.Say(this, trainer, "", "제독, 조합에 무슨 일이십니까?");
                else
                    ConfirmDialog.Tell(this, "훌륭한 선원이 되고 싶다면 여기서 수행하고 가게.",
                                       face: _game.SpeakerFace(building.Code, _cultureNo));
                break;
        }

        MeetFamily(facility, building);
    }

    /// <summary>
    /// 모항의 시설에서 아내·아이와 마주친다(<c>0x004A1EB0</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 인사 다음, <b>후원자가 앉아 있지 않을 때만</b> 이 자리를 본다 — 왕궁에 후원자가
    /// 앉은 날에는 가족 대사 여섯 줄이 안 나온다. 규칙과 문구는 <see cref="FamilyVisit"/> 에 있다.
    /// </remarks>
    private void MeetFamily(Facility facility, CityBuildingTable.Building building)
    {
        // 말하는 시설은 다섯뿐이다. 그 밖은 vtbl+0x10 이 기본 구현이라 아무 일도 안 난다.
        if (facility.Kind is not (FacilityKind.TradingPost or FacilityKind.Inn
                                  or FacilityKind.Shipyard or FacilityKind.Palace
                                  or FacilityKind.Market)) return;

        // 후원자가 앉은 건물이면 그쪽이 먼저다(slot7 이 1 을 돌려주면 slot8 이 아예 안 불린다).
        if (building.Kind is { Length: > 0 } kind && PatronAt(kind) != null) return;

        if (!FamilyVisit.Due(_player, _cityId, _game.Random)) return;
        if (FamilyVisit.MetChild(_player, _game.Random) is not { } child) return;

        var face = _game.Faces?.TryGetBgra(Home.FaceOf(child, child.AgeOn(_player.Date)),
                                           child.Daughter);
        bool girl = child.Daughter;
        var dice = _game.Random;

        // 시장만 아내가 먼저 말을 걸고 아이가 받는다 — 아내가 없으면 한 줄도 안 나온다.
        if (facility.Kind == FacilityKind.Market)
        {
            if (_player.Spouse.Length == 0) return;
            var (wife, said) = FamilyVisit.MarketWords(girl, dice);
            TalkDialog.Say(this, null, _player.Spouse, wife);
            TalkDialog.Say(this, face, child.Name, said);
            return;
        }

        TalkDialog.Say(this, face, child.Name, facility.Kind switch
        {
            FacilityKind.TradingPost => FamilyVisit.TradePostWord(girl, dice),
            FacilityKind.Inn => FamilyVisit.InnWord(girl, dice),
            FacilityKind.Shipyard => FamilyVisit.ShipyardWord(girl, dice),
            _ => FamilyVisit.PalaceWord(girl, dice),
        });
    }

    /// <summary>지금 떠 있는 건물 사진. 명령 창을 닫으면 같이 걷는다.</summary>
    private BuildingPhotoWindow? _photoWindow;

    /// <summary>
    /// 게임 640x480 화면에서 타원 사진이 앉는 자리. 도시 그림은 (0,0)~(400,320) 이고
    /// 사진은 (320,240) 부터라 <b>오른쪽 아래 모서리만</b> 겹친다.
    /// </summary>
    private const int PhotoLeft = 320, PhotoTop = 240;

    /// <summary>
    /// 그 건물의 타원 사진을 오른쪽 아래에 띄운다(<see cref="BuildingPhoto"/>).
    /// 술집·여관이면 사진 앞에 손님도 세운다. 사진을 못 구하면 조용히 넘어간다 —
    /// 사진은 덤이고 명령 창은 이미 열린다.
    /// </summary>
    private void ShowPhoto(FacilityKind kind, int buildingCode)
    {
        _photoWindow?.Close();
        _photoWindow = null;

        var photos = _game.Photos;
        if (photos == null) return;

        int k = photos.Pick(_culture, buildingCode);
        if (k < 0) return;

        var people = kind == FacilityKind.Home ? WifeArt() : Guests.GuestArt(kind);
        _photoWindow = BuildingPhotoWindow.Show(this, photos.TryGetBgra(k), people, _scale,
                                                new Point(Left + PhotoLeft * _scale,
                                                          Top + PhotoTop * _scale));
    }

    /// <summary>
    /// 자택 사진 앞에 세우는 <b>아내</b>. 아내가 없거나 얼굴을 못 읽으면 빈 목록이다.
    /// </summary>
    /// <remarks>
    /// 게임도 자택 인물 목록에는 <b>아내 하나</b>만 넣고(<c>0x004A19D0</c> 이 건물 11 일 때만),
    /// 아이는 아예 못 누른다. 눌렀을 때 도는 것은 <c>0x00414AD0</c> → <c>0x004149F0</c> 이다.
    /// </remarks>
    private IReadOnlyList<BuildingPhotoWindow.GuestArt> WifeArt()
    {
        if (_player.Spouse.Length == 0) return [];
        if (_game.Barmaids?.Find(_player.SpouseId) is not { } her) return [];
        if (_game.Faces?.TryGetBgra(her.Face, female: true) is not { } bgra) return [];
        return [new(bgra, PortraitW, PortraitH, _player.Spouse, TalkToWife)];
    }

    /// <summary>초상화 크기(FEMALE.CDS 한 장).</summary>
    private const int PortraitW = 80, PortraitH = 96;

    /// <summary>
    /// 아내에게 말을 건다(<c>0x004149F0</c>).
    /// </summary>
    /// <remarks>
    /// 아이가 하나라도 있으면 <b>아내 한 줄 · 아이 한 줄</b>이고, 없으면 아내 혼잣말 한 줄이다.
    /// 끼는 아이와 대본은 <see cref="Home.TalkerOf"/> · <see cref="Home.TalkWith"/> 에 있다.
    /// </remarks>
    private void TalkToWife()
    {
        var dice = _game.Random;
        var face = _game.Barmaids?.Find(_player.SpouseId) is { } her
            ? _game.Faces?.TryGetBgra(her.Face, female: true) : null;

        if (Home.TalkerOf(_player, dice) is not { } child)
        {
            TalkDialog.Say(this, face, _player.Spouse,
                           Home.WifeAlone[dice.Next(Home.WifeAlone.Length)]);
            return;
        }

        int age = child.AgeOn(_player.Date);
        var talk = Home.TalkWith(child.Daughter, age, dice);
        // 「%s%s 크면」의 조사는 은/는이다(0x004147F7 의 0x004281B0(이름, 1)).
        TalkDialog.Say(this, face, _player.Spouse,
                       string.Format(talk.Wife, child.Name, GameUi.Josa(child.Name, "은", "는")));
        TalkDialog.Say(this, _game.Faces?.TryGetBgra(Home.FaceOf(child, age), child.Daughter),
                       child.Name, talk.Child);
    }

    /// <summary>
    /// 건물 <b>위에</b> 이름표를 띄운다. 게임은 밑에 붙이는데, 우리 커서는 이름표를 덮고
    /// 앉아 글자가 가린다 — 커서가 누르는 자리는 늘 이름표 아래가 되게 위로 올렸다.
    /// 그림 꼭대기에 붙은 건물이라 위로 넘칠 때만 밑으로 돌린다.
    /// </summary>
    private void ShowTag(Border tag, Rect area, int scale)
    {
        tag.Visibility = Visibility.Visible;
        tag.UpdateLayout();
        double w = tag.ActualWidth > 0 ? tag.ActualWidth : 52;
        double h = tag.ActualHeight > 0 ? tag.ActualHeight : UiSprites.BandHeight;
        double x = (area.X + area.Width / 2) * scale - w / 2;
        Canvas.SetLeft(tag, Math.Clamp(x, 0, Math.Max(0, CityPictures.Width * scale - w)));

        // 게임은 이름표를 건물 <b>아래</b>에 붙인다. 그림 밑단을 넘칠 때만 위로 올린다.
        double below = (area.Y + area.Height) * scale + 2;
        double bottom = CityPictures.Height * scale;
        Canvas.SetTop(tag, below + h <= bottom ? below : Math.Max(0, area.Y * scale - h - 2));
    }

    /// <summary>
    /// 이 화면이 띄우는 창 둘 — 시설 명령 창과 도시 커맨드 창.
    /// </summary>
    /// <remarks>
    /// 창을 언제 어디에 내고 닫을 때 무엇을 되돌리는지는 <see cref="CityMenus"/> 가 든다.
    /// 여기서는 그때 되돌릴 것(사진 · 곡 · 이름표)만 일러 준다.
    /// </remarks>
    private CityMenus Menus => _menus ??= new CityMenus(this,
        onFacilityOpening: () =>
        {
            foreach (var tag in _tags) tag.Visibility = Visibility.Collapsed;
        },
        onFacilityClosed: () =>
        {
            _photoWindow?.Close();
            _photoWindow = null;
            _bgm?.Play(_cityTrack);

            // 항구·성문을 마지막 줄(ESC 도 같다)로 나서 마을에 들면 부관이 한마디 한다 — 출항·탐험은
            // 그 줄에서 먼저 지워 여기 안 온다.
            if (_gateway is { } kind)
            {
                bool arrived = _arrived;
                _gateway = null;
                _arrived = false;
                LeaveGateway(kind, arrived);
            }
        });

    private CityMenus? _menus;

    /// <summary>시설 명령 창. 줄에 손을 달아 주는 곳들이 이것을 쓴다.</summary>
    private GameMenuHost Menu => Menus.Facility;

    /// <summary>
    /// 명령 창을 연다. 건물마다 도는 곡이 다르면 <paramref name="track"/> 으로 준다 —
    /// 안 주면 도시 곡으로 돌아간다(다른 건물로 옮겨 갈 때 술집 곡이 따라오지 않게).
    /// </summary>
    private void ShowMenu(Func<GameMenu> build, int? track = null)
    {
        Menus.ShowFacility(build);
        _bgm?.Play(track ?? _cityTrack);
    }

    /// <summary>명령 창을 닫고 도시로 돌아간다 — 곡도 도시 것으로 되돌린다.</summary>
    private void CloseMenu() => Menus.CloseFacility();

    /// <summary>
    /// 시설에서 "기능" 을 골랐을 때 뜨는 창. 제목이 없고 줄만 넷이다 —
    /// 저장·로드·게임 종료·게임 재개를 <see cref="GameSystemMenu"/> 가 든다.
    /// </summary>
    /// <summary>
    /// 자택·여관의 "기능" 줄 — 저장·로드·게임 종료다. 도시 일이 아니라 판 일이라
    /// <see cref="GameSystemMenu"/> 가 든다.
    /// </summary>
    private GameMenu SystemMenu() => GameSystemMenu.Build(this, _game, Menu);

    /// <summary>
    /// 도시 커맨드 창 — 줄 차례와 이름은 <see cref="CityCommandMenu"/> 가 든다.
    /// 여기서는 줄마다 할 일만 채워 준다.
    /// </summary>
    private GameMenu CityMenu(string cityName) =>
        CityCommandMenu.Build(cityName, new CityCommandMenu.Actions(
            EnterMapPoint: EnterMapPoint,
            ShowPerson: ShowPerson,
            ShowFleet: _player.Ships.Count > 0 ? ShowFleet : null,
            ShowBelongings: ShowBelongings,
            ShowCityInfo: ShowCityInfo,
            ShowHints: ShowHints,
            ShowContract: ShowContract,
            ShowPatrons: () => KeepCityMenu(Patrons.ShowPatrons),
            ShowMap: () => _cityMenu.Push(MapMenu),
            Quit: () => GameSystemMenu.Quit(this, _game, Menu),
            Cancel: CloseCityMenu));

    /// <summary>
    /// 인물 정보. <b>부하가 하나라도 있으면</b> 게임처럼 누구를 볼지 먼저 묻고,
    /// 아무도 없으면 곧바로 제독의 판을 낸다.
    /// </summary>
    /// <remarks>도시 안이라 함대좌표는 게임처럼 <c>---</c> 다.</remarks>
    private void ShowPerson() => PersonInfoMenu.Show(this, _game, _cityMenu);

    /// <summary>함대 정보 판.</summary>
    private void ShowFleet() => KeepCityMenu(() => FleetInfoDialog.Show(this, _player, items: _game.Items,
                                                                    cargoName: c => GameInfo.CargoLabel(_game, c)));

    /// <summary>
    /// 정보 판 하나를 띄우는 동안 도시 커맨드 창을 감춰 두었다가 <b>도로 편다</b> — 게임은 판을 닫으면 차림표를
    /// 다시 낸다(<c>0x004934A3</c> 가 <c>0x110</c> 을 적어 <c>0x00492FE7</c> 로 되돌아간다). 창이 닫히는 것은
    /// 취소 · 맵 포인트 · 게임 종료뿐이다.
    /// </summary>
    private void KeepCityMenu(Action show)
    {
        var menu = _cityMenu.Window;
        if (menu != null) menu.Visibility = Visibility.Hidden;
        try { show(); }
        finally
        {
            if (menu != null && menu.IsLoaded)
            {
                menu.Visibility = Visibility.Visible;
                menu.Activate();
            }
        }
    }

    /// <summary>
    /// 「맵 포인트에 들어간다」 — 이 도시의 건물을 늘어놓고 고른 데로 들어간다.
    /// </summary>
    /// <remarks>
    /// 게임 커맨드의 그 줄이다(<c>0x0053BE10</c>). 누르면 <b>"어디로 들어 가시겠습니까?"</b>
    /// (<c>0x0053BF38</c>) 창이 뜨고 그 도시의 건물이 줄줄이 선다 — 고르면 그 건물의 명령
    /// 창이 열린다. 그림에서 작은 건물을 눈으로 찾아 누르지 않아도 되는 길이다.
    /// 건물이 하나도 없으면 <b>빈 목록</b>이다 — 「맵 포인트 데이터가 없습니다」(<c>0x0053A7FB</c>)를 내는
    /// 자리는 EXE 안에 없다.
    /// </remarks>
    private void EnterMapPoint()
    {
        var spots = Standing(_cityId);

        // 줄은 건물 이름이다 — "베렌의 탑" 처럼 그 도시만의 이름이 뜨고, 없으면 종류를 낸다.
        int at = MapPointDialog.Ask(this,
            [.. spots.Select(b => b.Name.Length > 0 ? b.Name : b.Kind)]);
        if (at < 0 || at >= spots.Count) return;

        CloseCityMenu();
        Enter(spots[at]);
    }

    /// <summary>「지도를 본다」 한 겹 — 항해지도 · 취소(<c>0x0049328E</c>).</summary>
    private GameMenu MapMenu() => CityCommandMenu.Map(
        wide: LookAtChart,
        back: _cityMenu.Pop);

    /// <summary>
    /// 항해지도 창을 띄운다 — 바다와 <b>같은</b> 모달 창이다. 창을 그리는 것은 함대 창이 맡는다.
    /// </summary>
    /// <remarks>
    /// 창이 떠 있는 동안 커맨드 창은 감춰 두고, 닫으면 「지도를 본다」 한 겹을 도로 낸다
    /// (<c>0x0049334C</c> 가 취소가 아니면 메뉴를 다시 띄운다).
    /// </remarks>
    private void LookAtChart()
    {
        if (Owner is not ShipMapWindow map) { CloseCityMenu(); return; }
        map.ShowSeaChart(this, _cityMenu.Window);
    }

    /// <summary>도시 정보 창을 낸다. 표를 못 읽어도 열린다 — 그 줄만 비는 채로 뜬다.</summary>
    private void ShowCityInfo() => KeepCityMenu(() =>
        CityInfoDialog.Show(this, _cityName, _cityId, _game.CityRows,
                            _game.Nations, _game.Goods, _game.ItemPictures,
                            Market?.Rates ?? _game.Rates));

    /// <summary>
    /// 여관에 묵는다. 게임 차례 그대로 — 값을 부르고, YES 면 그때서야 돈을 본다.
    /// </summary>
    /// <remarks>
    /// 묵고 나면 한 달이 가므로 상단 띠의 날짜가 그만큼 넘어간다.
    /// </remarks>
    private void Stay()
    {
        var inn = _lodging ??= new Lodging(_game.CityRows, _game.Rates);
        int price = inn.PriceAt(_cityId);

        // 여관 주인이 얼굴을 띄우고 묻는다(0x0047FC5B — 시설 +0x80).
        if (!ConfirmDialog.Ask(this, $"선불이네. 우리 집은 한 달에 금화 {price}닢인데, 머물고 갈텐가?",
                               face: _game.SpeakerFace(InnCode, _cultureNo)))
            return;

        if (!_player.CanAfford(price))
        {
            NoticeDialog.Show(this, "소지금이 모자랍니다");
            return;
        }

        // 차례는 원본 그대로다(0x0047FC78~): 화면을 덮고 값을 치르고 서른 날을 보낸 뒤 밝히고,
        // 말을 조금 배우고(0x0047FAE0), 모항이면 능력이 오를 때가 있고(0x0047FB80), 일어난 말, HP 다.
        DayPass.Blackout(this, () => inn.Stay(_player, _cityId));
        TellTongue(inn.LearnTongue(_player, _cityId, _game.Nations, _random));
        HomeInnBonus();
        NoticeDialog.Show(this, Lodging.WakeWord(_random));
        // 한 달 묵으면 HP 가 30~59 찬다(0x0047FCFF).
        _player.SetCondition(_player.Condition + Vitality.InnRest(_random));
    }

    /// <summary>
    /// 모항 여관에 묵으면 가끔 능력이 오른다(<c>0x0047FB80</c>) — <c>rand(100) &lt;= 5</c> 일 때 체력·지력·무력·매력·운
    /// 가운데 <c>rand(5)</c> 로 하나를 1 올린다(<c>0x00432C50</c>, 100 에서 자름). 알리는 말은 없다.
    /// </summary>
    private void HomeInnBonus()
    {
        if (_cityId != _player.HomePort || _random.Next(100) > 5) return;
        int which = _random.Next(5);
        if (_player.Abilities[which] < Ability.Max) _player.AdjustAbility(which, 1);
    }

    private Lodging? _lodging;

    /// <summary>셋 중 하나를 고르는 데 쓴다. 게임도 rand(3) 으로 고른다.</summary>
    private readonly Random _random = new();

    /// <summary>
    /// 여관 「허드렛일」(<c>0x0047FD60</c>) — 주머니가 가벼울 때만 나오는 줄이다.
    /// </summary>
    /// <remarks>
    /// YES 면 <b>한 해</b>가 통째로 가고(피로가 다 풀리고 규율이 가득 찬다) 컨디션이 10~19 차며,
    /// 그 고장 말을 배울 수도 있다(<see cref="Lodging.LearnTongue"/>). 삯은 시세를 먹인 1200 닢이다.
    /// 게임은 <b>삯을 알리고 나서</b> 돈을 넣는데, 셈이 같으니 여기서는 알림 뒤에 넣는다.
    /// NO 면 아무 말 없이 끝난다.
    /// </remarks>
    private void OddJob()
    {
        var inn = _lodging ??= new Lodging(_game.CityRows, _game.Rates);
        var face = _game.SpeakerFace(InnCode, _cultureNo);
        if (!ConfirmDialog.Ask(this, Lodging.OddJobAsk, face: face)) return;

        int pay = inn.OddJobPay(_cityId);
        // 한 해가 가는 동안 화면이 덮였다 밝는다(0x004A5AE0(0x14, 1)).
        DayPass.Blackout(this, () => _player.AdvanceDays(Lodging.OddJobDays));
        _player.SetCondition(_player.Condition + Lodging.OddJobRest(_random));
        TellTongue(inn.LearnTongue(_player, _cityId, _game.Nations, _random));

        ConfirmDialog.Tell(this, Lodging.OddJobDone, face: face);
        NoticeDialog.Show(this, $"금화 {pay}닢을 손에 넣었다!");
        _player.Earn(pay);
    }

    /// <summary>여관 건물 코드(화자표) — 주인은 여자다.</summary>
    private const int InnCode = 5;

    /// <summary>말을 한 조각 배웠으면 알린다(<c>0x005443B8</c>).</summary>
    private void TellTongue(string? tongue)
    {
        if (tongue is { Length: > 0 })
            NoticeDialog.Show(this, $"{tongue}{GameUi.Josa(tongue, "을", "를")} 조금 습득했다!");
    }

    /// <summary>
    /// 교역소 「회화」(<c>0x00481A10</c>) — 도시 상태로 비싸게 팔리는 것이 있으면 금화 1닢에 일러 주고,
    /// 없으면 그 도시 특산품을 자랑한다.
    /// </summary>
    /// <remarks>
    /// 특산품 자랑은 (도시 번호 + (해-1480)/8) 을 씨로 네 갈래 중 하나다(노예면 늘 권하는 말). 재고가 있는지로
    /// 말끝이 갈린다. 넷째 갈래의 둘째 이름은 원본이 「리스본산와인」을 만든 버퍼를 곧바로 교역품 이름으로
    /// 덮어써서(<c>0x0042E310</c> 이 한 버퍼를 쓴다) 결국 교역품 이름만 나온다 — 그대로 옮겼다.
    /// </remarks>
    private void TradeTalk()
    {
        if (TradeRules is not { } rules || _game.Goods is not { } goods) return;
        var owner = Menu.Window ?? this;
        var face = _game.SpeakerFace(TradePostDialog.TradingPostCode, _cultureNo);
        void Say(string text) => ConfirmDialog.Tell(owner, text, face: face);

        if (rules.TipOf(_player, _cityId, _random) is { } tip)
        {
            if (!ConfirmDialog.Ask(owner, "나리, 돈벌이 이야기가 있는데, 금화 1닢으로 어떠신가?", face: face))
            {
                Say("듣고 싶지 않다면 그만두게나.");
                return;
            }
            if (_player.Gold < 1)
            {
                Say("응? 빈털터리인가?");
                return;
            }
            Say($"지금, 이 마을에서는 {tip.Name}{NameToken.Of(tip.Name, 0)} 비싸게 팔리네.");
            _player.SetGold(_player.Gold - 1);
            return;
        }

        int special = rules.SpecialOf(_player, _cityId);
        if (goods.Find(special) is not { } item)
        {
            Say("지금은 아무 흥미있는 이야기거리가 없네.");
            return;
        }

        bool stocked = rules.StockOf(_player, _cityId)[TradePost.SpecialCell] > 0;
        string g = item.Name, city = _cityName;
        string recommend = $"으-음, 그렇군. 우리집에서 취급하고 있는 것 중에는, {g}{NameToken.Of(g, 2)} "
                           + (stocked ? "권하고 싶네." : "권하고 싶네마는.");
        if (g == "노예")
        {
            Say(recommend);
            return;
        }

        Say(TradePost.BoastKind(_cityId, _player.Date.Year) switch
        {
            0 => $"{city}{NameToken.Of(city, 9)} {g}의 " + (stocked ? "본산지라네. 다른 것에 비할 수 없네." : "본산지네. 빨리 사 놓고 또 오게나."),
            1 => $"{city}에 와서 {g}{NameToken.Of(g, 2)} " + (stocked ? "사지 않으면 손해보네." : "사야지! 지금은 품절이지만."),
            2 => recommend,
            _ => $"우리집에서 취급하고 있는 {g}{NameToken.Of(g, 1)} 다른 것에 뒤지지 않네. {g}"
                 + (stocked ? $"{NameToken.Of(g, 1)} 최고지." : $"{NameToken.Of(g, 2)} 원한다면 다음에 들여올 때까지 기다리게."),
        });
    }

    /// <summary>
    /// 소지품 정보 창을 낸다. 아이템 표를 못 읽어도 열린다 — 이름이 번호로 나올 뿐이다.
    /// </summary>
    private void ShowBelongings() => KeepCityMenu(() =>
        BelongingsDialog.Show(this, _player, _game.Items, _game.ItemText, _game.ItemPictures,
                              GameInfo.DiscoveryNames(_game), _game));

    /// <summary>
    /// 계약 정보 창을 낸다. 계약이 없으면 빈 판이 뜬다.
    /// </summary>
    /// <remarks>
    /// 증거품은 계약 중 발견한 것이 준 물건 가운데 <b>아직 지니고 있는</b> 것만 센다 —
    /// 팔아 버렸으면 내밀 증거가 없다. 판에 채울 것은 <see cref="GameInfo.ContractSheetOf"/>
    /// 가 짓는다(지도 창과 한 벌이다).
    /// </remarks>
    private void ShowContract() => KeepCityMenu(() =>
    {
        // 계약이 없으면 얼굴 없는 상자로 물린다(0x00493266 → 0x0053BF58).
        var sheet = GameInfo.ContractSheetOf(_game);
        if (sheet.Contract == null)
        {
            NoticeDialog.Show(this, "계약을 맺지 않았습니다");
            return;
        }
        ContractDialog.Show(this, sheet.Contract, _player.Date,
                            sheet.HintName, sheet.Found, sheet.Evidence,
                            _game.Sponsors?.FindByName(sheet.Contract?.Sponsor ?? "")?.Name);
    });

    /// <summary>힌트 이름. 판이 게임 표 · DB · 번호 차례로 물러서며 찾아 준다.</summary>
    private string HintNameOf(int id) => _game.HintName(id);

    /// <summary>얻은 힌트를 늘어놓는다. 이름은 판이 찾아 준다.</summary>
    private void ShowHints()
    {
        // 게임은 그냥 늘어놓기만 하지 않는다 — 한 줄을 고르고 결정하면 그 이야기를 편다.
        //
        // 보고까지 마친 힌트는 여기서 빠진다(원본 힌트 상태 15). 발견만 해서는 안 빠지는
        // 것이 옳다 — 까닭은 DiscoveryLog.IsHintDone 에 적어 두었다.
        var owner = _cityMenu.Window ?? this;
        var ids = _game.Discoveries?.LiveHints(_player) ?? [.. _player.Hints.Order()];

        while (true)
        {
            // 힌트가 없으면 게임도 설득 때와 같은 「설득 가능한 힌트가 없습니다」를 낸다.
            int at = HintListDialog.Pick(owner, [.. ids.Select(id => GameInfo.HintLabel(_game, id))]);
            if (at < 0 || at >= ids.Count) return;
            if (_game.Hints?.Find(ids[at]) is not { } hint) return;

            HintDetailDialog.Show(owner, hint, _game.Hints.CategoryOf(hint.Category),
                                  _player.Fame, _game.MateSpeaks, _player.Contract?.Hint == hint.Id);
        }
    }

    /// <summary>기능·언어 쪽지를 설정대로 붙이거나 걷는다.</summary>
    private void SyncSkillNote()
    {
        if (!IsLoaded) return;
        if (GameSettings.ShowSkillOverlay)
            _skillNote ??= SkillOverlayWindow.Attach(this, _player, _noteFontSize);
        else
        {
            _skillNote?.Close();
            _skillNote = null;
        }
    }

    /// <summary>
    /// 함대 쪽지를 지금 함대로 채운다 — 배마다 한 줄 「배 이름(선체)」, 함대 차례대로.
    /// 배가 없으면 쪽지가 안 뜬다.
    /// </summary>
    private void RefreshShipLabel() =>
        _shipNote?.Set(string.Join(Environment.NewLine,
                                   _player.Ships.Select(s => $"{s.Name}({s.Hull.Name})")));

    /// <summary>
    /// 시설의 명령 창을 짓는다 — 줄과 손은 <see cref="TownMenu"/> 가 짝지어 주고,
    /// 여기서는 <b>이 도시의 형편</b>만 채워 준다.
    /// </summary>
    private GameMenu BuildMenu(Facility facility, string title, int code, uint teachMask,
                               string kind)
    {
        var patron = PatronAt(kind);

        return TownMenu.Build(facility, title, code, teachMask, patron,
            new TownWorks.TownState(
                Teaches: TownWorks.Teaches(teachMask),
                Poor: _player.Gold <= Lodging.OddJobMaxGold,   // 0x0047FE70 의 jle — 100닢이면 뜬다
                // 게임도 알릴 것이 있고 <b>모항</b>일 때만 줄을 켠다(0x00476DE0).
                CanAnnounce: _cityId == _player.HomePort && Port.Announceable().Count > 0,
                PatronRow: patron == null ? null : Patrons.PatronRow(patron),
                PatronBribe: patron != null && Patrons.CanBribe(patron),
                PatronBorrow: patron != null && Patrons.CanBorrow(patron, KindsHere.Contains("항구")),
                Commented: Commented(code),
                Drinks: facility.Kind == FacilityKind.Tavern ? DrinkNames : null,
                Contracted: facility.Kind == FacilityKind.Tavern && Guests.HasRumor,
                HasHeir: Home.HasBornChild(_player),
                HasSon: Home.EldestSon(_player) != null,
                HasBooks: _game.Books?.InLibrary(_cityId, _player.Date.Year).Count > 0,
                Wed: Home.CanLeaveHeir(_player)),
            this);
    }

    /// <summary>
    /// 그 건물에 <b>해설</b> 줄이 붙는지 — 발견물인 건물을 이미 발견했고 해설 글이 있을 때다.
    /// </summary>
    private bool Commented(int code) =>
        BuildingAt(code) is { } b && b.IsDiscovery && b.Comment.Length > 0
        && _player.HasFound(b.Discovery);

    /// <summary>
    /// 지금 서 있는 건물들 — 건물 표에 있어도 건물 낱말 비트가 꺼져 있으면 뺀다(<see cref="CityExeTable.HasBuilding"/>).
    /// 스톡홀름·이스파한·우르겐치·카슈가르 왕궁은 역사 대본이 세우기 전까지 안 들어가진다.
    /// </summary>
    /// <remarks>
    /// 차례는 <b>건물 코드 차례</b>다 — 게임은 도시 낱말(<c>+0x1C</c>)의 비트 0~15 를 훑어(<c>0x00491D58</c>)
    /// 자리가 겹치면 <b>코드가 낮은 건물</b>을 집는다. 맵 포인트 목록도 같은 차례다.
    /// </remarks>
    private List<CityBuildingTable.Building> Standing(int cityId) =>
        [.. _table.InCity(cityId)
                  .Where(b => _game.CityRows?.HasBuilding(cityId, b.Code) ?? true)
                  .OrderBy(b => b.Code)];

    /// <summary>그 자리의 건물 줄. 못 찾으면 null.</summary>
    private CityBuildingTable.Building? BuildingAt(int code)
    {
        foreach (var b in _table.InCity(_cityId))
            if (b.Code == code) return b;
        return null;
    }

    /// <summary>발견한 건물의 해설 — 그림과 글을 함께 낸다.</summary>
    private void ShowComment(int code)
    {
        if (BuildingAt(code) is not { } b) return;
        DiscoveryDialog.Show(this, _game.Stills, b.Picture, b.Comment, title: b.Name);
    }

    /// <summary>
    /// 도구 창에서 문화권이 갈렸다 — 이 마을 것을 다시 묻고, 그 값을 품고 있는 시설을 놓는다.
    /// </summary>
    /// <remarks>
    /// 시설 창은 한 번 지으면 그대로 두고 쓴다(<see cref="Yard"/> 따위). 문화권을 지을 때
    /// 받아 들고 있으므로 놓아 주지 않으면 <b>옛 얼굴이 그대로</b> 나온다. 놓기만 하면
    /// 다음에 그 시설을 누를 때 새 문화권으로 다시 지어진다.
    /// </remarks>
    private void OnCultureChanged()
    {
        _cultureNo = _game.CityRows?.CultureOf(_cityId) ?? 0;
        _culture = _game.CultureOf(_cityId);

        _yard = null;      // 조선소 — 화자 얼굴
        _books = null;     // 도서관 — 사서 얼굴
        _guests = null;    // 술집·여관 — 주인 얼굴과 손님 그림
        _shop = null;      // 시장 — 장사꾼 얼굴
    }

    /// <summary>항구의 건물 코드. 배에서 곧바로 항구 창을 열 때 쓴다.</summary>
    private const int HarborCode = 0;

    /// <summary>성문의 건물 코드(<c>0x004A260D</c> 이 10 과 견준다).</summary>
    private const int GateCode = 10;

    /// <summary>
    /// 이 건물의 수련 자리 — 조합 · 교회 · 학자 저택. 건물마다 사람도 말도 달라
    /// 그 건물의 코드로 짓는다.
    /// </summary>
    private TrainingMenu Training(int buildingCode) =>
        new(this, _game, buildingCode, _cultureNo, _table, _cityId, PatronFaceAt(buildingCode));

    /// <summary>
    /// 그 건물에 앉은 후원자의 얼굴. 없으면 null — 그때는 화자표의 시설 사람이 선다.
    /// </summary>
    /// <remarks>
    /// 게임은 대사 얼굴을 시설 객체 <c>+0x88</c> 에서 꺼내는데, 후원자가 앉은 건물이면 그 자리가 <b>후원자</b>다 —
    /// 교회 수련에서 신부 대신 그 교회 후원자(페르난 마르틴스 따위)가 말한다.
    /// </remarks>
    private uint[]? PatronFaceAt(int buildingCode)
    {
        var building = _table.InCity(_cityId).FirstOrDefault(b => b.Code == buildingCode);
        if (building.Kind is not { Length: > 0 } kind || PatronAt(kind) is not { } patron) return null;
        return _game.Sponsors?.FindByName(patron.Name) is { } sponsor
            ? _game.Faces?.TryGetBgra(sponsor.Face, sponsor.IsFemale)
            : null;
    }

    /// <summary>이 마을 도서관 — 사서 인사와 서가는 도서관이 든다.</summary>
    private LibraryMenu Books => _books ??=
        new LibraryMenu(this, _game, _cityId, _cityName, _cultureNo, _table);

    private LibraryMenu? _books;

    /// <summary>이 마을 후원자 — 설득·보고·계약은 자리가 아니라 사람이 든다.</summary>
    private PatronMenu Patrons => _patronMenu ??=
        new PatronMenu(this, _game, _cityName, Menu, _cityMenu, _cityTrack, _cultureNo, _cityId);

    private PatronMenu? _patronMenu;

    /// <summary>이 마을 술집·여관에 앉은 사람들 — 말을 거는 일은 사람 쪽이 든다.</summary>
    private TavernMenu Guests => _guests ??=
        new TavernMenu(this, _game, _cityId, _culture, _cultureNo, HideMenu, CloseMenu);

    /// <summary>
    /// 손님과 이야기하는 동안 시설 명령 창을 감춘다.
    /// </summary>
    /// <remarks>
    /// 게임은 손님을 누르면 <b>명령 창을 지우고 그 자리에</b> "말을 건다 · 무시한다" 를
    /// 낸다. 우리는 창이 따로라 겹쳐 보였다 — 이야기하는 동안만 접어 둔다.
    /// </remarks>
    private void HideMenu(bool hide)
    {
        if (Menus.FacilityWindow is { } window)
            window.Visibility = hide ? Visibility.Hidden : Visibility.Visible;
    }

    private TavernMenu? _guests;

    /// <summary>이 마을 시장 — 인사도 사고파는 창도 시장이 든다.</summary>
    private MarketMenu Shop => _shop ??=
        new MarketMenu(this, _game, _cityId, _cultureNo, Market);

    private MarketMenu? _shop;

    /// <summary>이 마을 항구 — 함대·선원·발표는 도시가 아니라 항구가 한다.</summary>
    private HarborMenu Port => _port ??= new HarborMenu(this, _game, Menu, _cityId, _cultureNo);

    private HarborMenu? _port;

    /// <summary>이 마을 자택 — 휴양·저금은 도시가 아니라 집이 한다.</summary>
    private HomeMenu HomeRooms => _home ??= new HomeMenu(this, _game, Menu);

    private HomeMenu? _home;

    /// <summary>
    /// 이 마을 조선소. 배를 손보는 일은 도시가 아니라 조선소가 한다
    /// (<see cref="ShipyardMenu"/>) — 이 창은 명령 창과 시세만 대 준다.
    /// </summary>
    private ShipyardMenu Yard => _yard ??=
        new ShipyardMenu(this, _game, Menu, _cityId, _cultureNo,
                         Market?.Rates.Of(_cityId) ?? 100);

    private ShipyardMenu? _yard;

    /// <summary>도시 커맨드 창. 그림 안이 아니라 제 창으로 띄운다.</summary>
    /// <summary>도시 커맨드 창. 줄 안에서 겹치고 되돌아갈 때 쓴다.</summary>
    private GameMenuHost _cityMenu => Menus.City;

    /// <summary>도시 커맨드 창을 누른 자리에 띄운다.</summary>
    private void ShowCityMenu(string cityName, Point at) =>
        Menus.ShowCity(() => CityMenu(cityName), at);

    /// <summary>창 안의 자리를 화면 자리(WPF 단위)로. 창을 그 자리에 띄울 때 쓴다.</summary>
    private Point ToScreen(Point at)
    {
        var device = PointToScreen(at);
        var source = PresentationSource.FromVisual(this);
        return source == null
            ? device
            : source.CompositionTarget.TransformFromDevice.Transform(device);
    }

    private void CloseCityMenu() => Menus.CloseCity();

    /// <summary>
    /// 시장에서 사고파는 규칙. 처음 쓸 때 한 번만 짓는다 — 아이템 표를 못 읽으면 null 이고,
    /// 그러면 "구입" 줄이 흐린 채로 남는다.
    /// </summary>
    private Market? Market
    {
        get
        {
            if (_market != null || _marketTried) return _market;
            _marketTried = true;

            var items = _game.Items;
            if (items == null)
            {
                System.Diagnostics.Debug.WriteLine($"[City] 아이템 표 없음: {ItemTable.LastError}");
                return null;
            }
            _market = new Market(items, _game.Rates, _game.CityRows);
            return _market;
        }
    }

    private Market? _market;
    private bool _marketTried;

    /// <summary>교역소 매매 규칙. 교역소 표 · 교역품 표를 못 읽으면 null 이고 「매매」 줄이 흐리다.</summary>
    private TradePost? TradeRules =>
        _tradePost ??= _game.Trade is { } trade && _game.Goods is { } goods
            ? new TradePost(trade, goods, _game.Rates, _game.CityRows, _game.Nations, _game.Discoveries?.Table)
            : null;

    private TradePost? _tradePost;

    /// <summary>이 건물에 앉아 있는 후원자. 없으면 null.</summary>
    private Patron? PatronAt(string kind) => Patrons.At(kind, KindsHere);

    /// <summary>이 도시에 있는 건물 종류들. 후원자를 앉힐 자리를 고를 때 쓴다.</summary>
    private HashSet<string> KindsHere =>
        _kindsHere ??= [.. _table.InCity(_cityId).Select(b => b.Kind)];

    private HashSet<string>? _kindsHere;

    /// <summary>이 고장 술집이 파는 술. 도시 표의 지역 무리로 고른다.</summary>
    private List<DrinkTable.Drink> Drinks =>
        _drinks ??= _game.Drinks is { } table && _game.CityRows is { } rows
            ? table.InRegion(rows.RegionOf(_cityId))
            : [];

    private List<DrinkTable.Drink>? _drinks;

    /// <summary>술집 명령 창에 적을 술 이름들.</summary>
    private List<string> DrinkNames => [.. Drinks.Select(DrinkNameOf)];

    /// <summary>
    /// 술 줄에 적을 이름. 그 고장 말을 둘 이상 알아야 진짜 이름이 나온다 —
    /// 모르면 "붉은 술" 처럼 겉모습으로 부른다(<c>0x0042FB20</c>).
    /// </summary>
    private string DrinkNameOf(DrinkTable.Drink drink) => DrinkTable.NameFor(drink, CityTongue);

    /// <summary>이 도시 말을 얼마나 아는지. 나라를 모르면 0 이다.</summary>
    private int CityTongue
    {
        get
        {
            if (_game.CityRows is not { } rows || _game.Nations is not { } nations) return 0;
            if (nations.Find(rows.NationOf(_cityId)) is not { } nation) return 0;
            return nation.Language >= 0 && nation.Language < Skill.Languages.Length
                ? _player.TongueOf(Skill.Languages[nation.Language])
                : 0;
        }
    }

    /// <summary>그 줄이 술이면 어느 술인지. 아니면 null.</summary>
    private DrinkTable.Drink? DrinkAt(string item)
    {
        foreach (var drink in Drinks)
            if (DrinkNameOf(drink) == item) return drink;
        return null;
    }

    /// <summary>
    /// 성문의 「탐험을 떠난다」 관문 — 나서도 좋으면 true.
    /// </summary>
    /// <remarks>
    /// 게임의 <c>0x00468900</c> 이다.
    /// <code>
    ///   468903  편성 안 된 배가 있나(0x004688A0)   — 있으면 못 나간다
    ///   468910  함대 선원 수(0x0040E360) &gt; 0      — 있으면 그대로 나간다
    ///   468919  부하 첫 자리가 찼나(0x00468EF0)
    ///     찼으면  "제독, 저희들만으로 탐험을 하는 것은 무모한 짓입니다! …"  (0x00551AF0)
    ///     비었으면 "성밖은 위험하다. 통행을 금지한다."                     (0x00551B50)
    /// </code>
    /// 선원 수는 함대 물건(<c>0x005B3928</c>)을 훑어 배마다 탄 사람을 더한 값이고,
    /// 부하 자리는 제독 물건(<c>0x005B60A0</c>)의 0번 자리다
    /// (<c>0x0047CC60</c> 이 그 자리의 인물을 찾아 준다 — 항구에서 인사하는 그 부관이다).
    ///
    /// 맨 앞의 「편성돼 있지 않은 선박」은 이 마을에 <b>맡겨 둔 배</b>가 있을 때다
    /// (<c>0x004688E1</c>) — 출항 쪽과 한 함수이고 글만 갈린다
    /// (<see cref="HarborMenu.ConfirmSail"/>).
    /// </remarks>
    /// <param name="buildingCode">성문의 건물 코드 — 막아서는 병사 얼굴을 화자표에서 집는다.</param>
    private bool CanExplore(int buildingCode)
    {
        // 맡겨 둔 배가 있으면 탐험대를 못 모은다(0x004688E1) — 부관 있고 없고로 두 벌이다.
        // 모항에서는 안 본다(0x004688A8 의 도시 +0x1D 비트 8).
        if (_cityId != _player.HomePort && _player.DockedAt(_cityId).Count > 0)
        {
            const string word = "항구에 편성돼 있지 않은 선박이 있습니다! 탐험대를 모집할 수 없습니다.";
            if (Port.MateFace() is { } who)
                ConfirmDialog.Tell(this, "제독, " + word, face: who);
            else
                ConfirmDialog.Tell(this, word);
            return false;
        }

        if (_player.Crew > 0) return true;

        // 부하가 있으면 부하가 말리고, 없으면 성문 병사가 막아선다.
        if (Port.MateFace() is { } mate)
        {
            ConfirmDialog.Tell(this, "제독, 저희들만으로 탐험을 하는 것은 무모한 짓입니다! "
                                   + "항구에서 사람을 모집한 후로 합시다.", face: mate);
            return false;
        }

        ConfirmDialog.Tell(this, "성밖은 위험하다. 통행을 금지한다.",
                           face: _game.SpeakerFace(buildingCode, _cultureNo));
        return false;
    }

    // ── ITownScreen — 시설 명령 창이 이 화면에 시키는 일들 ──────────────────
    //
    // 어느 줄이 무슨 일인지는 TownWorks 가 알고, 그 일에 손을 달아 주는 것은 TownMenu 다.
    // 여기 있는 것은 <b>실제로 창을 띄우고 값을 세는</b> 몫뿐이다.

    // 함대가 이 도시에 닻을 내려야 한다 — 걸어 들어온 마을에서는 출항·보급·선원편성이 흐리다
    // (0x00476CBB · 0x00476D09 → 0x0040E1C0(도시, 1)).
    bool ITownScreen.HasShips => _player.FleetHere(_cityId, 1);
    bool ITownScreen.HasCrew => _player.Crew > 0;
    // 보관은 지닌 것이든 맡긴 것이든 하나라도 있으면 켜진다(0x00462407 — 소지품 · 보관품 둘 다 본다).
    bool ITownScreen.HasItems => _player.Items.Count > 0 || _player.Stored.Count > 0;
    bool ITownScreen.HasMates => _player.MateCount > 0;
    bool ITownScreen.CanBuyGoods => Market != null;
    bool ITownScreen.CanSellGoods => Market != null && _game.Items != null;
    bool ITownScreen.CanFormFleet => _player.FleetHere(_cityId) && Port.CanFormFleet;   // 0x0046A1CC
    bool ITownScreen.CanRepairShip => Yard.CanRepair;
    // 줄은 함대가 이 도시에 닿아 있는지만 본다(0x0044BD60) — 한 척뿐이면 눌러서 「기함을 처분하는 일은 불가능합니다!」(0x0044B96F).
    bool ITownScreen.CanSellShip => _player.FleetHere(_cityId, 1);
    bool ITownScreen.CanRefitShip => _player.FleetHere(_cityId, 1);                              // 0x0044BD69
    bool ITownScreen.CanRead => Books.CanRead;
    bool ITownScreen.CanLeaveHeir => Home.CanLeaveHeir(_player);
    bool ITownScreen.CanSucceed => Home.EldestSon(_player) != null;
    bool ITownScreen.CanEducate => _player.Children.Count > 0;

    void ITownScreen.CloseMenu() => CloseMenu();

    /// <summary>
    /// 놀이를 끝낸다(<c>0x0044AF40</c>) — 그림 0x0B 와 CONTINUE? 뒤 첫 화면으로 돌아간다.
    /// 끝난 까닭 0·4·5·6 은 그림 0x0B, 1·2 는 0x0C, 3 은 0x0D 다(<c>0x00410CA1</c> 뜀표).
    /// </summary>
    internal void EndGame()
    {
        CloseMenu();
        GameOverDialog.Show(this, _game.EventStills, GameOverDialog.MutinyLost, bgm: _game.Bgm);
        if (Owner is ShipMapWindow map) Dispatcher.BeginInvoke(map.ReturnToTitle);
    }

    void ITownScreen.LeaveTavern()
    {
        Guests.Challenged();
        CloseMenu();
    }

    // 수련을 눌러도 인사는 다시 안 한다 — 조합 인사(0x004AC840)는 들어설 때 한 번뿐이다(0x004AC880 → 0x00491470).
    void ITownScreen.Train(int buildingCode, uint teachMask) => Training(buildingCode).Teach(teachMask);

    void ITownScreen.OpenSystemMenu() => Menu.Push(SystemMenu);

    /// <summary>
    /// 설득 — 끝나면 <b>그 건물에서 나온다</b>.
    /// </summary>
    /// <remarks>
    /// 게임도 설득이 끝나면 명령 창으로 안 돌아가고 도시 그림으로 물러선다. 시설의
    /// "고른 것 처리"(<c>0x0044ED1A</c>)가 <b>설득 본체가 돌려준 값을 그대로 제 반환값으로
    /// 내보내므로</b>(<c>0x0044ED28</c> → <c>0x0044ED30</c> → <c>0x0044EE05</c>) 설득이
    /// 화면을 접을 수 있다 — 다른 줄들은 제 값을 따로 만들어 낸다.
    ///
    /// 문전박대도 마찬가지다. 명성이 모자라 집사가 돌려보내면 게임은 그 자리에서 도시
    /// 그림으로 돌아간다(<see cref="PatronMenu.Persuade"/>).
    /// </remarks>
    void ITownScreen.Persuade(Patron patron)
    {
        Patrons.Persuade(patron, church: _openKind == FacilityKind.Church);
        CloseMenu();
    }

    void ITownScreen.HearInfo() => Guests.HearInfo();
    bool ITownScreen.CanPlayPoker => Engine.Town.Poker.CanPlayIn(_cultureNo);
    void ITownScreen.PlayPoker() => Guests.PlayPoker();
    void ITownScreen.Report(Patron patron) => Patrons.Report(patron);
    void ITownScreen.BreakContract(Patron patron) => Patrons.BreakContract(patron);
    void ITownScreen.BribeInspector(Patron patron) => Patrons.BribeInspector(patron);
    void ITownScreen.BorrowShips(Patron patron) => Patrons.BorrowShips(patron);

    void ITownScreen.Sail()
    {
        if (!Port.ConfirmSail()) return;
        Sailed = true;
        SailedOnArrival = _arrived;
        _gateway = null;
        Close();
    }

    void ITownScreen.Explore(int buildingCode)
    {
        if (!CanExplore(buildingCode)) return;

        // 관문을 넘으면 한 번 더 묻는다(0x0046894F). 글 둘을 넘기지만 0x00469680 이
        // <b>부관 있고 없고로 하나만</b> 고른다 — 제목 띠가 아니다.
        // 준비하는 열흘은 성문을 나설 때 도는 PassPortDays 가 그대로 쓴다.
        if (!ConfirmDialog.Ask(this, _game.Player.MateAt(0).Length > 0
                ? "탐험을 떠납니까? 준비하는데 10일 걸립니다. 좋습니까?"
                : "탐험하러 출발하십니까?")) return;

        Explored = true;
        _gateway = null;
        Close();
    }

    void ITownScreen.ShowComment(int buildingCode) => ShowComment(buildingCode);

    void ITownScreen.OpenFleetForm() => Menu.Push(Port.FleetMenu);
    void ITownScreen.OpenCrewForm() => Port.CrewForm();
    void ITownScreen.ShowPortCityInfo() => Port.CityInfo();

    void ITownScreen.Announce()
    {
        Port.Announce();
        // 다 알리고 나면 그 줄이 아예 사라져야 한다 — 줄 목록을 다시 지어 그리게 한다.
        Menu.Refresh();
    }

    void ITownScreen.Supply() =>
        SupplyDialog.Show(Menu.Window ?? this, _player, Market?.Rates.Of(_cityId) ?? 100,
                          ((_game.CityRows?.FlagsOf(_cityId) ?? 0) & 8) != 0,
                          Port.MateFace());

    void ITownScreen.BuyShip() => Yard.BuyShip();
    void ITownScreen.SellShip() => Yard.SellShip();
    void ITownScreen.RepairShip() => Yard.RepairShip();
    void ITownScreen.RefitShip() => Yard.RefitShip();

    void ITownScreen.BuyGoods() => Shop.Buy();
    void ITownScreen.SellGoods() => Shop.Sell();
    bool ITownScreen.CanTrade => TradeRules != null;

    void ITownScreen.Trade()
    {
        // 함대가 없으면 매매가 안 열린다(0x004819BB → 0x00469680).
        if (!_player.FleetHere(_cityId, 1))
        {
            if (_player.MateAt(0).Length > 0 && _game.AideFace is { } aide)
                TalkDialog.Say(Menu.Window ?? this, aide, "", "제독, 배가 없습니다");
            else
                NoticeDialog.Show(Menu.Window ?? this, "배가 없습니다");
            return;
        }
        if (TradeRules is { } rules)
            TradePostDialog.Show(Menu.Window ?? this, _game, rules, _cityId, _player.CityName, _cultureNo);
    }

    void ITownScreen.TradeTalk() => TradeTalk();

    void ITownScreen.Stay() => Stay();

    void ITownScreen.OddJob() => OddJob();
    void ITownScreen.ShowMates() => MateRosterDialog.Show(this, _player);

    void ITownScreen.LeaveHeir() => HomeRooms.LeaveHeir();
    void ITownScreen.Succeed() => HomeRooms.Succeed();
    void ITownScreen.Educate() => HomeRooms.Educate();
    void ITownScreen.OpenRestMenu() => Menu.Push(HomeRooms.RestMenu);
    void ITownScreen.OpenSavingsMenu()
    {
        // 소지금도 저금도 없으면 창을 안 연다(0x00460A01).
        if (!HomeRooms.HasMoneyToBank) { GameDialog.Show(this, HomeMenu.NoMoneyAtAll); return; }
        Menu.Push(HomeRooms.SavingsMenu);
    }
    void ITownScreen.OpenStorage() => StorageDialog.Show(Menu.Window ?? this, _player, _game.Items);

    void ITownScreen.ShowEncyclopedia() =>
        EncyclopediaDialog.Show(Menu.Window ?? this, _game);

    void ITownScreen.ShowChronicle() =>
        ChronicleDialog.ShowChronicle(Menu.Window ?? this, _player, _game.Discoveries?.Table);

    void ITownScreen.Retire()
    {
        if (!HomeRooms.Retire()) return;
        CloseMenu();
        Close();
        if (Owner is ShipMapWindow map) Dispatcher.BeginInvoke(map.ReturnToTitle);
    }

    void ITownScreen.ReadBooks() =>
        Books.Read(Menu.Window ?? this, text => (Owner as ShipMapWindow)?.Say(text));


    bool ITownScreen.IsDrink(string item) => DrinkAt(item) != null;

    void ITownScreen.Drink(string item)
    {
        if (DrinkAt(item) is not { } drink) return;
        Guests.Drink(drink, DrinkNameOf(drink));
    }

    /// <summary>
    /// 도시 그림 창을 연다. 그림을 못 풀면 null 이다 — 그림이 없다고 입항까지 막을 일은 아니다.
    /// </summary>
    /// <remarks>
    /// <b>모달로 띄우지 않는다.</b> 모달이면 같은 앱의 다른 창이 입력을 못 받아 함대 창
    /// 제목 줄(옮기기·닫기)이 죽어 버린다. 배는 부르는 쪽에서 멈춰 두고, 창이 닫히면
    /// <see cref="Window.Closed"/> 로 풀어 준다.
    /// </remarks>
    /// <param name="mapArea">
    /// 지도가 놓인 자리(화면 좌표, WPF 단위). 그 자리를 통째로 덮는다. 비워 두면 그림 크기에
    /// 맞춰 owner 한가운데에 띄운다.
    /// </param>
    public static CityPicView? Open(Window owner, Engine.Game game, int cityId, string cityName,
                                      Rect mapArea = default,
                                      int cityTrack = BgmPlayer.CityTrack,
                                      string culture = "")
    {
        // 그림도 건물 표도 없으면 열지 않는다 — 건물 자리를 모르면 도시 안에서 할 일이 없다.
        if (game.CityPics is not { } pictures || game.Buildings == null) return null;

        var bgra = pictures.TryGetBgra(cityId);
        if (bgra == null) return null;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra, CityPictures.Width * 4);
        picture.Freeze();

        double areaW = mapArea.Width > 0 ? mapArea.Width : owner.ActualWidth;
        double areaH = mapArea.Height > 0 ? mapArea.Height : owner.ActualHeight;

        var dlg = new CityPicView(game, cityName, picture, PickScale(areaW, areaH), cityId,
                                    mapArea, cityTrack, culture)
        {
            Owner = owner,
        };
        dlg.Show();
        // 닫을 때 초점이 앱 밖으로 새지 않게 붙든다.
        FocusWatch.KeepInApp(dlg);
        // 초점이 어디로 가는지 보려고 둔 진단(FocusWatch). 다 잡고 나면 지운다.
        dlg.Closed += (_, _) => FocusWatch.After("도시그림창 닫힘");
        dlg.Deactivated += (_, _) => FocusWatch.After("도시그림창 초점 잃음");
        return dlg;
    }

    /// <summary>
    /// 그림 배율. 게임처럼 지도의 반쯤을 덮는 크기로 잡는다(자리가 좁아도 1배는 쓴다).
    /// </summary>
    private static int PickScale(double areaWidth, double areaHeight)
    {
        int scale = (int)Math.Min(areaWidth * 0.6 / CityPictures.Width,
                                  areaHeight * 0.7 / CityPictures.Height);
        return Math.Max(1, Math.Min(scale, 4));
    }
}
