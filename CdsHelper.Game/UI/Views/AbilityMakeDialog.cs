using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// NEW GAME 둘째 걸음 — 굴린 능력치를 보너스 포인트로 손보고 직업을 고른다.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0045D6C0</c> 이고, 굴리는 것은 <c>0x0045D450</c> 이다.
/// <code>
///   0x00560A88  능력치 여섯 — 체력 지력 무력 매력 운 (신앙심은 안 보인다)
///   0x00560AA8  직업 여덟   — 화면에는 탐험가 발굴자 사냥꾼 정복자 넷만
///   0x0051ACA0  얼굴 번호(+0xBC)마다 32바이트 — 능력치 보정(0x0045D473)
///   0x005472C0  생일마다 32바이트 — 이레만 값이 있다
///   45d568      값 = 생일보정 + rand(나이) + 직업보정 + 나이보정 + 50, 20~100 으로 자른다
///   45d5d5      보너스 = 합으로 갈린다 — 잘 굴렸을수록 덜 준다
/// </code>
/// <b>직업을 바꿔도 다시 굴리지 않는다</b> — 게임 그대로다. 굴리는 <c>0x0045D450</c> 을 부르는 데는
/// <c>0x0045D022</c> 한 곳뿐인데, 그 자리는 <b>앞 걸음(신상)의 끝</b>이다 — 이 화면에
/// 들어오기 전에 이미 굴려 놓는다는 뜻이다. 그래서 직업 보정표는 새 놀이에서는
/// 늘 0번 줄(탐험가, 값이 다 0)로 걸리고, 표의 나머지 줄은 부하·NPC 쪽에서만 쓰인다.
/// 직업 단추는 <b>기본 기술</b>만 정한다(다음 걸음, 고르는 자리 <c>0x0045D8DA</c>).
///
/// 다시 굴리고 싶으면 게임처럼 "취소" 로 신상 걸음까지 물러났다가 다시 오면 된다.
/// </remarks>
internal sealed class AbilityMakeDialog : InfoDialog
{
    /// <summary>
    /// 판 크기(그림 점). 잰 값이 <b>1.75배로 늘어난 화면</b>에서 나온 것이라 도로 나눴다 —
    /// 띠 단추만 제 크기(24)로 그려져 있어 혼자 작아 보였다.
    /// </summary>
    /// <summary>게임 갈무리에 이 화면들에는 닫기(X)가 없다.</summary>
    protected override bool ShowClose => false;

    /// <summary>판을 원본 좌표 그대로 캔버스에 짓는다 — 여백은 테(8점)가 다 맡는다.</summary>
    protected override Thickness BoardPad => new(0);

    protected override Thickness ButtonPad => new(0);

    /// <remarks>
    /// 원본 창은 <b>288x208</b>(<c>0x0045D6ED</c>, 테 포함)이다. 자리는 창 <b>속</b>(<c>[창+0x54]</c>,
    /// 테 8점 안쪽)에서 잰 값이라 그대로 캔버스 좌표가 된다 — 캔버스는 272x192 다.
    /// 계약 정보 창(<c>0x0047F1E0</c>)으로 맞춰 봤다: 제목이 속 (0,8) 인데 갈무리에서 창 끝으로부터
    /// 8점 안쪽에 있다.
    /// <code>
    ///   0x0045F009  이름·값 "%-6s%4d"   (8, 16 + 24i)        반각 8점 — 값은 x=88 에서 끝난다
    ///   0x0045D75D  위 화살표 16x16      (104, 16 + 24i)      ID 0x15 + 2i
    ///   0x0045D7B3  아래 화살표          (120, 16 + 24i)      ID 0x16 + 2i
    ///   0x0045D829  직업 단추 96x24      (168, 16 + 32k)      고른 것 무늬 1, 나머지 2
    ///   0x0045F0C1  보너스 상자 128x32   (8, 144)             "보너스" (8,144) · "포인트:%4d" (24,160)
    ///   0x0045BE13  취소·다음 48x24      창 오른쪽 아래에서 (112,40) · (64,40)
    /// </code>
    /// </remarks>
    private const double BoardWidth = 272, BoardHeight = 192;

    /// <summary>모드에서 계산기(16)와 바라는 값 세 칸(24)이 들어갈 만큼 판을 넓히는 폭.</summary>
    private const double TargetRoom = 40;

    /// <summary>모드 쓰는 법. 판 폭(312점 = 반각 39칸)에 들게 줄을 끊었다.</summary>
    private static readonly string[] HelpLines =
    [
        "[모드] 직업을 누르면 다시 굴린다.",
        "계산기로 바라는 값을 적는다.",
        "적은 값을 다 채운 판이면 멈춘다.",
        "적은 값과 신상은 3시간 기억한다.",
    ];

    /// <summary>설명 줄 사이와 설명이 차지하는 높이.</summary>
    private const double HelpStep = 18, HelpRoom = 8 + 4 * HelpStep;

    /// <summary>원본 좌표에서 뺄 값. 원본 자리가 이미 테 안쪽 기준이라 0 이다.</summary>
    private const double Frame = 0;

    /// <summary>게임 반각 한 칸.</summary>
    private const double Cell = 8;

    /// <summary>줄 사이(능력치 24 · 직업 32)와 첫 줄 자리.</summary>
    private const double RowStep = 24, JobStep = 32, FirstRow = 16;

    /// <summary>화살표 칸 폭(조각을 못 읽었을 때만 쓴다)과 직업·아래 단추 폭.</summary>
    private const double ArrowWidth = 16, JobWidth = 96, FootWidth = 48;

    private readonly int _age;

    private readonly GameUi.GameLabel[] _values = new GameUi.GameLabel[Ability.Shown];
    private readonly GameUi.GameLabel _bonus = new(GameFont.WhiteColor)
    {
        Bold = false,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Right,
    };
    private readonly List<GameButton> _jobs = [];

    private int[] _stats;
    private int _left, _job;
    private bool _ok;

    /// <summary>직업을 바꿀 때 다시 굴리려고 들고 있는 것들.</summary>
    private readonly Random _rng;
    private readonly int _birthMonth, _birthDay, _fortune;

    /// <param name="spare">
    /// 0 이상이면 <b>다시 굴리지 않는다</b> — 앞서 손본 능력치를 그대로 이어받고 남은
    /// 보너스도 이 값으로 둔다. 기술 화면에서 되돌아왔을 때가 그렇다.
    /// </param>
    private AbilityMakeDialog(Player player, Random rng, int spare)
    {
        _age = player.Age;
        _job = player.JobIndex;
        _rng = rng;
        _birthMonth = player.BirthMonth;
        _birthDay = player.BirthDay;
        _fortune = player.Fortune;

        // 되돌아온 걸음이면 굴리지 않는다 — 굴려 버리면 손본 것이 죄다 날아간다.
        // 담아 둔 값은 보이는 값에서 1 을 뺀 것이라 도로 더한다 — 안 더하면 되돌아올 때마다 1 씩 깎인다.
        // 넣은 보너스도 잇는다: 안 이으면 도로 뺄 수 없고 굴린 지력(RolledMind)도 넣은 만큼 부푼다.
        bool again = spare >= 0;
        _stats = again
            ? [.. player.Abilities.Select(Ability.Display)]
            : Ability.Roll(Ability.BiasOf(player.Fortune), _age, player.BirthMonth, player.BirthDay, rng);
        _left = again ? spare : Ability.BonusFor(_stats, rng);
        if (again) Array.Copy(LastAdded, _added, _added.Length);
        _initial = [.. _stats];

        // 세 시간 안에 적어 둔 바라는 값이 있으면 그대로 채운다(CharacterMakeDialog 와 한 벌).
        if (GameSettings.RerollOnJob && GameSettings.CharacterDraft?.Targets is { } saved)
            for (int i = 0; i < Math.Min(saved.Length, _targets.Length); i++) _targets[i] = saved[i];

        // 굴리는 자리(0x0045D450)에서 컨디션·소지금·명성·악명도 함께 정해 둔다 — 컨디션은 보너스를 얹기 전 체력이다.
        if (again)
            (_condition, _gold, _fame, _infamy) = (player.Condition, player.Gold, player.Fame, player.Infamy);
        else
        {
            _condition = Ability.ConditionFor(_stats[Ability.Body]);
            _gold = Ability.GoldRoll(_age, rng);
            _fame = Ability.FameRoll(_age, rng);
            _infamy = Ability.InfamyRoll(_age, rng);
        }

        // 모드 「직업 누르면 다시 굴림」이면 계산기와 바라는 값 칸이 들어갈 만큼 판을 넓히고 오른쪽 것들을 민다.
        double wide = GameSettings.RerollOnJob ? TargetRoom : 0;
        double tall = GameSettings.RerollOnJob ? HelpRoom : 0;
        var body = new Canvas { Width = BoardWidth + wide, Height = BoardHeight + tall };

        for (int i = 0; i < Ability.Shown; i++)
        {
            int which = i;
            double y = FirstRow + i * RowStep;

            // "%-6s%4d" — 이름 여섯 칸 뒤에 값 네 칸을 오른쪽으로 맞춘다.
            Put(body, Text(Ability.Names[i]), 8, y);
            _values[i] = Text("");
            _values[i].HorizontalAlignment = HorizontalAlignment.Right;
            Put(body, new Grid { Width = 4 * Cell, Children = { _values[i] } }, 8 + 6 * Cell, y);

            // 게임은 화살표 둘을 세로로 쌓지 않고 나란히 놓는다.
            Put(body, Arrow(up: true, () => Move(which, +1)), 104, y);
            Put(body, Arrow(up: false, () => Move(which, -1)), 120, y);

            // 모드 「직업 누르면 다시 굴림」이면 화살표 오른쪽에 계산기와 적어 둔 바라는 값을 단다 —
            // 바라는 값을 다 채운 판이 나오면 직업을 또 눌러도 안 굴린다(ChooseJob). 원본에 없는 칸이다.
            if (GameSettings.RerollOnJob)
            {
                Put(body, GameUi.CalcButton(() => AskTarget(which), ArrowWidth), 140, y);
                _targetLabels[i] = Text("");
                _targetLabels[i].HorizontalAlignment = HorizontalAlignment.Right;
                Put(body, new Grid { Width = 3 * Cell, Children = { _targetLabels[i] } }, 160, y);
            }
        }

        // 보너스 상자는 글씨보다 먼저 깐다 — 글씨가 테 위에 올라앉는다(원본도 테에 붙어 있다).
        Put(body, new Border
        {
            BorderBrush = Ink,
            BorderThickness = new Thickness(1),
            Width = 128,
            Height = 32,
        }, 8, 144);
        Put(body, Text("보너스"), 8, 144);
        Put(body, Text("포인트:"), 24, 160);
        Put(body, new Grid { Width = 4 * Cell, Children = { _bonus } }, 24 + 7 * Cell, 160);

        for (int i = 0; i < Job.Choosable; i++)
        {
            int pick = i;
            var cell = new GameButton(Job.All[i].Name, () => ChooseJob(pick), BandStyle.Button, JobWidth)
            {
                Margin = new Thickness(0),
            };
            _jobs.Add(cell);
            Put(body, cell, 168 + wide, FirstRow + i * JobStep);
        }

        Put(body, new GameButton("취소", Close, width: FootWidth) { Margin = new Thickness(0) }, 288 - 112 + wide, 208 - 40);
        Put(body, new GameButton("다음", Next, width: FootWidth) { Margin = new Thickness(0) }, 288 - 64 + wide, 208 - 40);

        // 모드를 켰으면 판 아래에 쓰는 법을 적는다 — 원본에 없는 칸이라 무엇을 하는지 모르면 못 쓴다.
        if (GameSettings.RerollOnJob)
            for (int i = 0; i < HelpLines.Length; i++)
                Put(body, Text(HelpLines[i]), 8, BoardHeight + 8 + i * HelpStep);

        Build("", body, BoardWidth + wide, BoardHeight + tall);

        Sync();
    }

    /// <summary>원본 창 좌표(테 포함)로 캔버스에 놓는다.</summary>
    private static void Put(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x - Frame);
        Canvas.SetTop(element, y - Frame);
        canvas.Children.Add(element);
    }

    /// <summary>판 위의 밝은 글씨 한 줄.</summary>
    private static GameUi.GameLabel Text(string text) => new(GameFont.WhiteColor)
    {
        Text = text,
        Bold = false,
        FallbackBrush = Ink,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// 위·아래 화살표. 게임 조각(<c>MISC.CDS</c> 파트 3)을 그대로 건다 —
    /// 원본은 16x16 짜리 칸이고, 못 누를 때 쓰는 X 칸도 같은 줄에 있다.
    /// </summary>
    private UIElement Arrow(bool up, Action run)
    {
        if (GameUi.GameIcon(up ? UiSprites.IconUp : UiSprites.IconDown) is { } art)
        {
            art.Margin = new Thickness(0);
            art.Cursor = Cursors.Hand;
            art.VerticalAlignment = VerticalAlignment.Center;
            Hold(art, run);
            return art;
        }

        // 조각을 못 읽었으면 글자 화살표로 물러선다.
        var box = new Border
        {
            Background = GameUi.ItemFill,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(1),
            Width = ArrowWidth,
            Height = ArrowWidth,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                // 화살표는 게임 비트맵 글꼴에 없는 글자라 윈도 글꼴로 찍는다.
                Text = up ? "↑" : "↓",
                Foreground = Brushes.Black,
                FontWeight = FontWeights.Bold,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Hold(box, run);
        return box;
    }

    /// <summary>누르는 동안 잇달아 도는 참 — 처음 한 박자 쉬고, 그 뒤로는 빨리 돈다.</summary>
    private static readonly TimeSpan HoldFirst = TimeSpan.FromMilliseconds(400),
                                     HoldNext = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// 누르면 한 번 돌고, <b>누르고 있으면 잇달아</b> 돈다.
    /// </summary>
    /// <remarks>
    /// 눌림을 여기서 막아야 한다 — 안 막으면 창 끌기(<see cref="GameUi.EnableDrag"/>)가
    /// 마우스를 채 가서 뗌이 안 온다.
    ///
    /// 마우스를 붙들어(<see cref="UIElement.CaptureMouse"/>) 두므로 손이 칸 밖으로 나가도
    /// 뗄 때까지 이어진다. 창이 닫힐 때도 참을 세운다.
    /// </remarks>
    private void Hold(UIElement box, Action run)
    {
        var timer = new DispatcherTimer { Interval = HoldFirst };
        timer.Tick += (_, _) =>
        {
            timer.Interval = HoldNext;
            run();
        };

        box.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            run();
            box.CaptureMouse();
            timer.Start();
        };
        box.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            timer.Stop();
            timer.Interval = HoldFirst;
            box.ReleaseMouseCapture();
        };
        Closed += (_, _) => timer.Stop();
    }

    /// <summary>
    /// 보너스 포인트를 능력치에 넣거나 도로 뺀다 — 빼는 것은 <b>넣은 만큼만</b>이라 굴린 값 밑으로는 안 내려간다
    /// (<c>0x0045D971</c> 이 칸마다 넣은 수 <c>[+0x124]</c> 가 0 보다 클 때만 뺀다).
    /// </summary>
    private void Move(int which, int by)
    {
        if (by > 0)
        {
            if (_left <= 0 || _stats[which] >= Ability.Max) return;
            _stats[which]++;
            _added[which]++;
            _left--;
        }
        else
        {
            if (_added[which] <= 0) return;
            _stats[which]--;
            _added[which]--;
            _left++;
        }
        Sync();
    }

    /// <summary>이 창을 열 때의 능력치 — 계산기가 처음 보여 줄 값이다.</summary>
    private readonly int[] _initial;

    /// <summary>칸마다 바라는 값. 계산기로 적은 칸만 있다 — 적은 칸이 다 이만큼 나온 판이면 더 안 굴린다.</summary>
    private readonly int?[] _targets = new int?[Ability.Names.Length];

    /// <summary>계산기 옆에 적어 둔 바라는 값을 보이는 글. 모드를 안 켰으면 비어 있다.</summary>
    private readonly GameUi.GameLabel?[] _targetLabels = new GameUi.GameLabel?[Ability.Shown];

    /// <summary>그 칸의 바라는 값을 계산기로 받는다. 처음에는 창을 열 때의 값이 들어 있다.</summary>
    private void AskTarget(int which)
    {
        if (NumberPadDialog.Ask(this, _targets[which] ?? _initial[which], Ability.Min, Ability.Max) is { } n)
        {
            _targets[which] = n;
            Sync();
        }
    }

    /// <summary>
    /// 적어 둔 바라는 값을 다 채웠는지 — 보너스로 넣은 것은 빼고 굴린 값으로 잰다. 하나도 안 적었으면 거짓이다.
    /// </summary>
    private bool TargetsMet()
    {
        bool any = false;
        for (int i = 0; i < _targets.Length; i++)
        {
            if (_targets[i] is not { } want) continue;
            any = true;
            if (_stats[i] - _added[i] < want) return false;
        }
        return any;
    }

    /// <summary>칸마다 보너스로 넣은 수.</summary>
    private readonly int[] _added = new int[Ability.Names.Length];

    /// <summary>
    /// 직업을 고른다 — 원본은 능력치를 <b>다시 안 굴린다</b>(<c>0x0045D8DA</c>). 굴림은 이 화면에 들어오기 전에
    /// 한 번(<c>0x0045D450</c>)이고 보정 줄은 직업이 아니라 얼굴 자리다(<see cref="Ability.FaceBias"/>).
    /// 모드 창의 「직업 누르면 다시 굴림」을 켜면 누를 때마다 같은 식으로 통째로 새로 굴린다 — 넣은 보너스도 도로 걷힌다.
    /// 계산기로 적은 바라는 값을 다 채운 판이 나오면 그때부터는 안 굴린다(<see cref="TargetsMet"/>).
    /// </summary>
    private void ChooseJob(int pick)
    {
        _job = pick;
        // 바라는 값을 다 채운 판이면 <b>주사위를 아예 안 굴린다</b> — 칸마다 굳히면 값을 높게 적는 것이
        // 곧 손으로 정하는 것과 같아지므로, 여섯을 한 판으로 통째로 굴려 한꺼번에 채워야 멎는다.
        if (GameSettings.RerollOnJob && !TargetsMet())
        {
            _stats = Ability.Roll(Ability.BiasOf(_fortune), _age, _birthMonth, _birthDay, _rng);
            _left = Ability.BonusFor(_stats, _rng);
            Array.Clear(_added);
            _condition = Ability.ConditionFor(_stats[Ability.Body]);
        }
        Sync();
    }

    private void Sync()
    {
        for (int i = 0; i < Ability.Shown; i++) _values[i].Text = $"{_stats[i]}";
        _bonus.Text = $"{_left}";
        for (int i = 0; i < _targetLabels.Length; i++)
            if (_targetLabels[i] is { } label) label.Text = _targets[i] is { } want ? $"{want}" : "";

        // 고른 직업은 <b>띠 무늬를 갈아</b> 알린다. 게임은 <b>고른 것이 밝은 베이지</b>고
        // 안 고른 것이 어두운 쪽이다 — 우리가 거꾸로 걸고 있었다.
        for (int i = 0; i < _jobs.Count; i++)
            _jobs[i].Band = i == _job ? BandStyle.Button : BandStyle.Alt;
    }

    /// <summary>"다음" — 보너스를 다 안 썼으면 게임처럼 한 번 묻는다.</summary>
    private void Next()
    {
        if (_left > 0 && !ConfirmDialog.Ask(this,
                $"보너스 포인트가 {_left} 남아 있습니다만{Environment.NewLine}" +
                "다음 설정으로 이동해도 괜찮습니까?"))
            return;

        // 바라는 값도 다음에 새로 지을 때 채우게 적어 둔다.
        if (GameSettings.RerollOnJob)
            GameSettings.EditCharacterDraft(d => d.Targets = [.. _targets]);

        _ok = true;
        Close();
    }

    /// <summary>
    /// 능력치 화면을 띄운다. "다음" 을 누르면 <paramref name="player"/> 에 적고, 남은
    /// 보너스 포인트를 낸다(무른 것이면 -1).
    /// </summary>
    /// <param name="spare">
    /// 앞서 남긴 보너스. 0 이상이면 능력치를 <b>다시 안 굴리고</b> 그 자리에서 잇는다.
    /// </param>
    public static int Show(Window owner, Player player, Random rng, int spare = -1)
    {
        var dialog = new AbilityMakeDialog(player, rng, spare) { Owner = owner };
        dialog.ShowDialog();
        if (!dialog._ok) return -1;

        player.JobIndex = dialog._job;
        // 굴린 값은 <b>보이는 값</b>이라 담을 때 1 을 뺀다(0x0045E47A 의 dec edx).
        player.SetAbilities([.. dialog._stats.Select(Ability.Store)]);
        // 마무리(0x0045E485 ~ 0x0045E4B1)가 옮겨 박는다. 초심자 주인공은 따로다(Beginner).
        player.SetCondition(dialog._condition);
        player.SetGold(dialog._gold);
        player.Fame = dialog._fame;
        player.Infamy = dialog._infamy;
        RolledMind = dialog._stats[Ability.Mind] - dialog._added[Ability.Mind];
        LastAdded = [.. dialog._added];
        return dialog._left;
    }

    /// <summary>마지막으로 「다음」을 눌렀을 때 칸마다 넣어 둔 보너스 — 기술 화면에서 되돌아오면 잇는다.</summary>
    private static int[] LastAdded = new int[Ability.Names.Length];

    /// <summary>
    /// 굴린 지력(<c>[+0x110]</c>) — 보너스로 넣은 것을 뺀 값이다. 기술 화면의 상한이 이것으로 선다(<c>0x0045DFF6</c>).
    /// </summary>
    public static int RolledMind { get; private set; } = Ability.Base;

    /// <summary>굴릴 때 정해 둔 컨디션 · 소지금 · 명성 · 악명.</summary>
    private int _condition, _gold, _fame, _infamy;
}
