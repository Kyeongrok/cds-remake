using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 모드 창 — <b>원본에 없는 편의 기능</b>을 켜고 끈다.
/// </summary>
/// <remarks>
/// 개발 창에 섞여 있던 것 가운데 <b>놀 때 쓰는 것</b>만 따로 뽑아 왔다. 개발 창은 값을
/// 손으로 밀어 넣어 시험하는 데고, 여기는 판을 그대로 두고 보기를 거드는 데다.
///
/// 줄은 <b>왼쪽</b>에 늘어놓고, 커서를 올리거나 고른 줄의 설명이 <b>오른쪽</b>에 뜬다 —
/// 처음 보면 이름만으로는 무엇인지 알 수 없어 풍선말 대신 붙박이 설명 칸을 두었다.
/// </remarks>
public sealed class ModDialog : GameWindow
{
    /// <summary>모드 창이 만지는 것들.</summary>
    public sealed class Options
    {
        /// <summary>제독 컨디션(HP) 상자.</summary>
        public Func<bool> ConditionOn { get; init; } = () => false;
        public Action<bool> SetCondition { get; init; } = _ => { };

        /// <summary>미니맵 — 발견물 지도를 작게 잘라 배를 따라간다.</summary>
        public Func<bool> MiniMapOn { get; init; } = () => false;
        public Action<bool> SetMiniMap { get; init; } = _ => { };
        public Func<double> MiniMapOpacity { get; init; } = () => 0.75;
        public Action<double> SetMiniMapOpacity { get; init; } = _ => { };

        /// <summary>바람·해류 화살표 — 원본에 없는 덧그림이다.</summary>
        public Func<bool> ArrowsOn { get; init; } = () => false;
        public Action<bool> SetArrows { get; init; } = _ => { };
    }

    /// <summary>줄 목록과 설명 칸의 너비.</summary>
    private const double ListWidth = 250, TipWidth = 330;

    /// <summary>아무 줄에도 커서가 없을 때 설명 칸에 적는 글.</summary>
    private const string Greeting =
        "원본에 없는 편의 기능을 켜고 끄는 창입니다.\n\n왼쪽 줄에 커서를 올리면 여기에 설명이 뜹니다.";

    /// <summary>오른쪽 설명 칸의 이름 줄.</summary>
    private readonly TextBlock _tipName = new()
    {
        Foreground = GameUi.Text,
        FontWeight = FontWeights.Bold,
        FontSize = 15,
        Margin = new Thickness(0, 0, 0, 6),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>오른쪽 설명 칸의 본문.</summary>
    private readonly TextBlock _tipText = new()
    {
        Text = Greeting,
        Foreground = GameUi.Text,
        FontSize = 13,
        LineHeight = 20,
        TextWrapping = TextWrapping.Wrap,
    };

    private ModDialog(Options options)
    {
        Title = "모드";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        var rows = new StackPanel { Width = ListWidth, Margin = new Thickness(12, 10, 8, 4) };

        // 컨디션 — 제독 HP(0x005B60D8)를 지도 왼쪽 아래에 막대로 띄운다. 300·100 문턱도 같이 그린다.
        rows.Children.Add(Toggle("컨디션", options.ConditionOn(), options.SetCondition,
            "제독 컨디션(HP, 0~2000)을 지도 왼쪽 아래에 띄웁니다. 300·100 아래면 부관이 쉬라고 하고, 0 이면 쓰러집니다."));

        // 미니맵 — D 로 여는 발견물 지도를 항해·뭍 이동 중에 오른쪽 아래에 작게 띄운다.
        rows.Children.Add(MiniMapControls(options));

        // 바람·해류 화살표 — 원본은 물결로만 흐름을 보인다. 개발 창에 있던 것을 여기로 옮겼다.
        rows.Children.Add(Toggle("바람·해류 화살표", options.ArrowsOn(), options.SetArrows,
            "원본에 없는 덧그림입니다 — 바람과 해류의 방위를 지도 위에 화살표로 얹습니다."));

        // 발견물 지도 — 햄버거 줄과 단축키를 함께 여닫는다. 원본 항해지도는 표식을 안 찍는다.
        rows.Children.Add(Toggle("발견물 지도", GameSettings.ShowDiscoveryMapMenu,
            on => GameSettings.ShowDiscoveryMapMenu = on,
            "햄버거에 「발견물 지도」 줄을 냅니다. 어디에 무엇이 있는지 표식으로 찍어 보여 줍니다."
            + " 끄면 줄도 단축키도 안 먹습니다."));

        // 여급 수첩 — 낯을 튼 여급과 궁합을 모아 본다. 원본에는 없는 창이다.
        rows.Children.Add(Toggle("여급 수첩", GameSettings.ShowBarmaidBookMenu,
            on => GameSettings.ShowBarmaidBookMenu = on,
            "햄버거에 「여급 수첩」 줄을 냅니다. 낯을 튼 여급의 친밀도와 궁합을 모아 봅니다."));

        // 인물 이동 — 누가 어느 도시로 가고 있는지 늘어놓는 창.
        rows.Children.Add(Toggle("인물 이동", GameSettings.ShowPersonMoveMenu,
            on => GameSettings.ShowPersonMoveMenu = on,
            "햄버거에 「인물 이동」 줄을 냅니다. 인물이 어느 도시로 가고 있는지 늘어놓습니다."));

        // Ctrl+클릭 배 놓기 — 지도를 찍은 자리로 배가 뛴다. 켠 채로 시작한다.
        rows.Children.Add(Toggle("Ctrl+클릭 배 놓기", GameSettings.PlaceShipByCtrlClick,
            on => GameSettings.PlaceShipByCtrlClick = on,
            "Ctrl 을 짚고 지도를 찍으면 배를 그 자리에 놓습니다. 끄면 여느 클릭처럼 닻만 오르내립니다."));

        // 계약 힌트 — 기능·언어 쪽지 위에 현재 계약의 힌트 이름을 띄운다.
        rows.Children.Add(Toggle("현재 계약 힌트", GameSettings.ShowContractHintOverlay,
            on => GameSettings.ShowContractHintOverlay = on,
            "도시에 들어가면 현재 계약을 맺은 힌트 이름을 기능·언어 쪽지 위에 띄웁니다."));

        rows.Children.Add(Toggle("현재 함대 선박 이름", GameSettings.ShowFleetOverlay,
            on => GameSettings.ShowFleetOverlay = on,
            "도시에 들어가면 현재 함대의 선박 이름과 선체를 도시 창 옆에 띄웁니다."));

        rows.Children.Add(Toggle("현재 힌트 목록", GameSettings.ShowHintOverlay,
            on => GameSettings.ShowHintOverlay = on,
            "현재 남아 있는 힌트를 최대 10개까지 함대 선박 이름 아래에 띄웁니다."));

        // 기능·언어 — 켜 두면 도시에 들어갈 때 도시 그림 왼쪽에 쪽지로 뜬다.
        rows.Children.Add(Toggle("기능·언어", GameSettings.ShowSkillOverlay,
            on => GameSettings.ShowSkillOverlay = on,
            "도시에 들어가면 제독과 부하 넷의 기능·언어를 도시 그림 왼쪽에 띄웁니다. 끌어 옮기면 그 자리를 기억합니다."));

        // 배 빌림 묻기 — 원본은 배가 있으면 계약 자리에서 늘 묻는다(0x00410724).
        rows.Children.Add(Toggle("배 빌림 묻기", GameSettings.AskLendShips,
            on => GameSettings.AskLendShips = on,
            "계약을 맺을 때 내 배가 한 척이라도 있으면 후원자가 「배를 빌리겠습니까?」를 묻습니다(원본 그대로)."
            + " 끄면 묻지 않고 안 빌린 것으로 넘어갑니다 — 배를 이미 갖춘 판에서 물음이 성가실 때 씁니다."));

        // 자동저장 — 원본에 없다. 손으로 적는 자리(SAVEDATA.CDS)는 안 건드리고 따로 적는다.
        rows.Children.Add(Toggle("도시 자동저장", GameSettings.AutoSaveOnPort,
            on => GameSettings.AutoSaveOnPort = on,
            "원본에 없는 것입니다 — 도시에 들어설 때마다 자동저장 파일(AUTOSAVE.CDS)에 적습니다."
            + " 배로 입항하든 뭍으로 성문을 지나든 마찬가지라, 항구가 없는 내륙 마을에서도 적힙니다."
            + " 손으로 적어 둔 세이브(SAVEDATA.CDS)는 건드리지 않습니다."
            + " 첫 화면의 「CONTINUE」가 이 파일을 엽니다."));

        // 마을·항구에 들고 날 때 보내는 날수. 원본은 열흘씩이라 오가는 시험이 더디다.
        rows.Children.Add(Select("출입 일수",
            [.. Enumerable.Range(GameSettings.MinPortDays,
                                 GameSettings.MaxPortDays - GameSettings.MinPortDays + 1)
                          .Select(n => n == GameSettings.DefaultPortDays ? $"{n}일 (원본)" : $"{n}일")],
            GameSettings.PortDays - GameSettings.MinPortDays,
            i => GameSettings.PortDays = i + GameSettings.MinPortDays,
            $"항구·마을에 들어가고 나올 때 각각 지나는 날수. 원본 기본값 {GameSettings.DefaultPortDays}일입니다."
            + " 바꾼 값은 다음 출입부터 곧바로 듭니다."));

        // 인물 이동 — 떠날지 굴리는 때와 확률. 원본은 매월 1일 5분의 1이다.
        // 첫 줄(0)이 원본 「매월 1일」이고, 그 뒤 줄 번호가 곧 날수다.
        rows.Children.Add(Select("이동 주기",
            ["매월 1일 (원본)", .. Enumerable.Range(1, GameSettings.MaxPersonRollDays).Select(n => $"{n}일마다")],
            GameSettings.PersonRollDays,
            i => GameSettings.PersonRollDays = i,
            "인물(14~200번)이 떠날지 굴리는 때. 원본은 매월 1일입니다. N일마다는 1480년 1월 1일부터 셉니다."
            + " 역사 항해자 대본은 늘 매월 1일입니다."));
        rows.Children.Add(Select("떠날 확률",
            [.. Enumerable.Range(GameSettings.MinPersonMoveOdds,
                                 GameSettings.MaxPersonMoveOdds - GameSettings.MinPersonMoveOdds + 1)
                          .Select(n => n == 1 ? "1분의 1 (반드시)" : $"{n}분의 1")],
            GameSettings.PersonMoveOdds - GameSettings.MinPersonMoveOdds,
            i => GameSettings.PersonMoveOdds = i + GameSettings.MinPersonMoveOdds,
            $"굴릴 때마다 떠날 확률. 원본은 {GameSettings.DefaultPersonMoveOdds}분의 1입니다."));

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("모드", Close);
        GameUi.EnableDrag(this, title);

        // 오른쪽 설명 칸 — 줄 이름과 설명을 한 판에 담는다.
        var tip = new StackPanel();
        tip.Children.Add(_tipName);
        tip.Children.Add(_tipText);

        var side = new Border
        {
            Width = TipWidth,
            Margin = new Thickness(0, 10, 12, 4),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)),
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(1),
            Child = tip,
        };

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(rows);
        body.Children.Add(side);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(body);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    /// <summary>그 줄의 설명을 오른쪽 칸에 건다. 커서가 떠나도 마지막 것을 남긴다.</summary>
    private void Watch(FrameworkElement row, string label, string tip)
    {
        void Show()
        {
            _tipName.Text = label;
            _tipText.Text = tip;
        }

        row.MouseEnter += (_, _) => Show();
        row.GotKeyboardFocus += (_, _) => Show();
        row.PreviewMouseLeftButtonDown += (_, _) => Show();
    }

    /// <summary>켜고 끄는 줄 하나.</summary>
    private CheckBox Toggle(string label, bool on, Action<bool> set, string tip)
    {
        var box = new CheckBox
        {
            Content = label,
            IsChecked = on,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Margin = new Thickness(0, 8, 0, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        Watch(box, label, tip);
        return box;
    }

    private UIElement MiniMapControls(Options options)
    {
        var box = Toggle("미니맵", options.MiniMapOn(), options.SetMiniMap,
            "항해·뭍 이동 중에 발견물 지도를 지도 오른쪽 아래에 작게 띄웁니다. 배를 가운데 두고 따라갑니다"
            + " (빨강 찾음 · 회색 아직 · 파랑 내 자리).");

        var value = new TextBlock
        {
            Width = 48,
            Foreground = GameUi.Text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var slider = new Slider
        {
            Minimum = 0.1,
            Maximum = 1.0,
            TickFrequency = 0.1,
            IsSnapToTickEnabled = true,
            Width = 150,
            Margin = new Thickness(18, 0, 0, 0),
            IsEnabled = box.IsChecked == true,
            Value = Math.Clamp(options.MiniMapOpacity(), 0.1, 1.0),
        };
        void ShowValue() => value.Text = $"{slider.Value:P0}";
        slider.ValueChanged += (_, _) =>
        {
            ShowValue();
            options.SetMiniMapOpacity(slider.Value);
        };
        box.Checked += (_, _) => slider.IsEnabled = true;
        box.Unchecked += (_, _) => slider.IsEnabled = false;
        ShowValue();

        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
        line.Children.Add(new TextBlock
        {
            Text = "투명도",
            Width = 64,
            Foreground = GameUi.Text,
            Margin = new Thickness(18, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        line.Children.Add(slider);
        line.Children.Add(value);
        Watch(line, "미니맵 투명도", "미니맵을 켠 상태에서 투명도를 조절합니다. 체크를 끄면 조절할 수 없습니다.");

        var group = new StackPanel();
        group.Children.Add(box);
        group.Children.Add(line);
        return group;
    }

    /// <summary>고르는 줄 하나 — 이름과 펼침 상자. 고르면 곧바로 설정에 남긴다.</summary>
    private UIElement Select(string label, IReadOnlyList<string> items, int selected,
                             Action<int> set, string tip)
    {
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 2),
        };
        line.Children.Add(new TextBlock
        {
            Text = label,
            Width = 64,
            Foreground = GameUi.Text,
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var box = new ComboBox
        {
            Width = 160,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(6, 3, 6, 3),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        foreach (string item in items) box.Items.Add(item);
        box.SelectedIndex = Math.Clamp(selected, 0, items.Count - 1);
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) set(box.SelectedIndex); };

        line.Children.Add(box);
        Watch(line, label, tip);
        Watch(box, label, tip);
        return line;
    }

    public static void Show(Window owner, Options options) =>
        new ModDialog(options) { Owner = owner }.ShowDialog();
}
