using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 육상전 <b>모의전</b>을 차리는 창 — 미니 게임에서만 연다.
/// </summary>
/// <remarks>
/// 도시도 나라도 없이 싸움만 돌려 본다. 양쪽 여섯 자리의 병종과 병력 합을 골라
/// <see cref="LandBattle"/> 의 모의전 생성자에 그대로 넘긴다.
///
/// 게임에 없는 화면이라 밤색 판이 아니라 <b>여느 개발 창</b>의 꼴로 짓는다 —
/// 부대 편성 창(<see cref="LandFormationDialog"/>)과 같은 결이다.
/// </remarks>
internal sealed class LandSparDialog : GameWindow
{
    /// <summary>한 쪽이 세울 수 있는 자리 수.</summary>
    private const int Slots = LandBattle.PerSide;

    /// <summary>고르는 칸의 폭과 병력 칸의 폭.</summary>
    private const double PickWidth = 150, MenWidth = 90;

    private readonly ComboBox[] _mine = new ComboBox[Slots];
    private readonly ComboBox[] _theirs = new ComboBox[Slots];
    private readonly TextBox _myMen = new() { Width = MenWidth, Text = "300" };
    private readonly TextBox _foeMen = new() { Width = MenWidth, Text = "300" };
    private readonly ComboBox _terrain = new() { Width = PickWidth };
    private readonly ComboBox _culture = new() { Width = PickWidth };
    private readonly ComboBox _sort = new() { Width = PickWidth };
    private readonly ComboBox _target = new() { Width = 190 };

    /// <summary>상대 칸에 늘어놓은 도시 번호 — 갈래가 마을 공략일 때 쓴다.</summary>
    private readonly List<int> _cityIds = [];

    /// <summary>고르고 나면 그 짜임. 물렀으면 null.</summary>
    private Setup? _made;

    /// <summary>
    /// <b>지난번에 차렸던 짜임</b> — 창을 다시 열면 이대로 되편다.
    /// </summary>
    /// <remarks>
    /// 게임의 부대배치 화면에도 「전회」 단추가 있어 지난번 배치(<c>0x0056EAB8</c> 여섯 칸)를
    /// 그대로 되편다(<see cref="LandDeployDialog"/>). 모의전은 게임에 없는 화면이지만 같은
    /// 결로 둔다 — 같은 짜임으로 여러 판을 굴려 보는 자리이기 때문이다.
    ///
    /// 게임의 그 여섯 칸은 세이브에 안 적혀 놀이를 새로 열면 죄다 −1 이지만, 이쪽은
    /// <b>앱을 껐다 켜도 남긴다</b>(<c>game-settings.json</c>). 놀이 안의 배치가 아니라
    /// 같은 짜임으로 여러 판을 굴려 보는 <b>시험 자리</b>라, 켤 때마다 여섯 칸을 손으로
    /// 다시 고르게 할 까닭이 없다.
    /// </remarks>
    private static Setup? Last
    {
        get => GameSettings.LandSpar is { } saved
            ? new Setup(saved.Mine ?? [], saved.Theirs ?? [], saved.MyMen, saved.FoeMen,
                        saved.Culture, saved.Terrain)
            : null;
        set => GameSettings.LandSpar = value is { } made
            ? new LandSparData
            {
                Mine = made.Mine,
                Theirs = made.Theirs,
                MyMen = made.MyMen,
                FoeMen = made.FoeMen,
                Culture = made.Culture,
                Terrain = made.Terrain,
            }
            : null;
    }

    /// <summary>모의전 한 판의 짜임.</summary>
    /// <param name="Mine">아군 여섯 자리의 병종. −1 이면 빈 자리다.</param>
    /// <param name="Theirs">적 여섯 자리.</param>
    /// <param name="Target">
    /// 상대 — 마을 공략이면 <b>도시 번호</b>, 들싸움이면 <see cref="LandFieldFoes.All"/>
    /// 의 자리다. −1 이면 안 골랐다.
    /// </param>
    internal readonly record struct Setup(int[] Mine, int[] Theirs, int MyMen, int FoeMen,
                                          int Culture, int Terrain, int Sort = LandBattle.Town,
                                          int Target = -1);

    /// <summary>
    /// 싸움 갈래 둘 — 차례가 <see cref="Sorts"/> 다.
    /// </summary>
    /// <remarks>
    /// 열 턴을 넘겼을 때가 갈린다 — 마을 공략은 이길 길이 없이 물러나고, 들싸움은
    /// 버텨 내면 적이 물러간다(<c>0x00449420</c>).
    /// </remarks>
    private static readonly (string Name, int Value)[] Sorts =
        [("마을 공략", LandBattle.Town), ("들에서 마주침", LandBattle.Field)];

    /// <summary>마을 공략의 싸움터는 늘 도시다(<c>0x0044A5B0</c> 의 다섯째 인자 7).</summary>
    private const int TownField = 0;

    /// <summary>싸움터 그림 넷 — <see cref="LandBattle.Terrain"/> 차례다.</summary>
    private static readonly string[] Fields = ["도시", "초지", "숲", "황무지"];

    /// <summary>「상대」 칸에 늘어놓을 도시들. 표를 못 열면 빈 목록이다.</summary>
    private readonly IReadOnlyList<CityTable.Entry> _towns;

    private LandSparDialog(IReadOnlyList<CityTable.Entry> towns)
    {
        _towns = towns;
        Title = "육상전 모의전";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var page = new StackPanel { Margin = new Thickness(14) };
        page.Children.Add(Head("싸울 자리와 부대를 고르고 「싸운다」를 누른다."));

        var top = new StackPanel { Orientation = Orientation.Horizontal,
                                   Margin = new Thickness(0, 10, 0, 6) };
        top.Children.Add(Label("싸움터", 52));
        top.Children.Add(_terrain);
        top.Children.Add(Label("문화권", 60));
        top.Children.Add(_culture);
        page.Children.Add(top);

        var next = new StackPanel { Orientation = Orientation.Horizontal,
                                    Margin = new Thickness(0, 0, 0, 6) };
        next.Children.Add(Label("갈래", 52));
        next.Children.Add(_sort);
        next.Children.Add(new TextBlock
        {
            Text = "  들싸움은 열 턴을 버티면 이긴다",
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
        });
        page.Children.Add(next);

        foreach (var (name, _) in Sorts) _sort.Items.Add(name);
        _sort.SelectionChanged += (_, _) => FillTargets();
        _sort.SelectedIndex = 0;

        var pick = new StackPanel { Orientation = Orientation.Horizontal,
                                    Margin = new Thickness(0, 0, 0, 6) };
        pick.Children.Add(Label("상대", 52));
        pick.Children.Add(_target);
        page.Children.Add(pick);
        FillTargets();

        foreach (string field in Fields) _terrain.Items.Add(field);
        _terrain.SelectedIndex = 0;

        // 문화권은 적 그림을 가른다(LandUnitArt.PartOf) — 이름은 도시 표의 것을 쓴다.
        for (int i = 0; i < CultureNames.Length; i++) _culture.Items.Add($"{i} {CultureNames[i]}");
        _culture.SelectedIndex = 0;

        var sides = new StackPanel { Orientation = Orientation.Horizontal };
        sides.Children.Add(Side("아군", _mine, _myMen, friend: true));
        sides.Children.Add(new Border { Width = 20 });
        sides.Children.Add(Side("적군", _theirs, _foeMen, friend: false));
        page.Children.Add(sides);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var fight = new Button { Content = "싸운다", Padding = new Thickness(16, 3, 16, 3),
                                 Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        fight.Click += (_, _) => Decide();
        buttons.Children.Add(fight);

        var stop = new Button { Content = "그만둔다", Padding = new Thickness(16, 3, 16, 3),
                                IsCancel = true };
        stop.Click += (_, _) => Close();
        buttons.Children.Add(stop);
        page.Children.Add(buttons);

        Content = page;

        // 지난번에 차렸던 것이 있으면 그대로 되편다.
        if (Last is { } was) Restore(was);
    }

    /// <summary>지난번 짜임을 칸에 되편다.</summary>
    private void Restore(Setup was)
    {
        for (int i = 0; i < Slots; i++)
        {
            _mine[i].SelectedIndex = Row(was.Mine, i, friend: true);
            _theirs[i].SelectedIndex = Row(was.Theirs, i, friend: false);
        }

        _myMen.Text = Sane(was.MyMen).ToString();
        _foeMen.Text = Sane(was.FoeMen).ToString();
        _culture.SelectedIndex = Math.Clamp(was.Culture, 0, CultureNames.Length - 1);
        _terrain.SelectedIndex = Math.Clamp(was.Terrain, 0, Fields.Length - 1);
        _sort.SelectedIndex = Math.Max(0, Array.FindIndex(Sorts, s => s.Value == was.Sort));
        FillTargets();
        int at = _cityIds.Count > 0 ? _cityIds.IndexOf(was.Target) : was.Target;
        if (at >= 0 && at < _target.Items.Count) _target.SelectedIndex = at;
    }

    /// <summary>
    /// 그 자리의 병종을 고르는 칸 번호로 — 표 밖이면 「빈 자리」(0)다.
    /// </summary>
    /// <remarks>
    /// 아군 자리에 못 세우는 병종이 적혀 있으면 빈 자리로 돌린다 — 막기 전에 적어 둔
    /// 짜임이 설정 파일에 남아 있을 수 있다.
    /// </remarks>
    private static int Row(int[] kinds, int at, bool friend)
    {
        int kind = at < kinds.Length ? kinds[at] : -1;
        if (kind < 0 || kind >= LandUnits.Count) return 0;
        return friend && !CanBeMine(kind) ? 0 : kind + 1;
    }

    /// <summary>문화권 이름 열하나. 적 그림과 진형이 이것으로 갈린다.</summary>
    private static readonly string[] CultureNames =
    [
        "서유럽", "북유럽", "동유럽", "이슬람", "인도", "동남아시아",
        "동아시아", "일본", "아프리카", "중남미", "오세아니아",
    ];

    /// <summary>
    /// 아군으로 세울 수 있는 병종인지 — <b>여덟뿐</b>이다.
    /// </summary>
    /// <remarks>
    /// 게임이 배치 화면에 미리 읽어 두는 부대 그림이 파트 8~15 여덟 장이고
    /// (<c>0x004A020B</c>), <see cref="LandUnitArt.DeploySheet"/> 가 그 자리를 낸다 —
    /// 기병 · 중장기병 · 화승총대 · 머스켓총대 · 포병 · 캐논포병 · 제독 · 무적제독이다.
    /// 곧 <b>플레이어가 낼 수 있는 병종은 그 여덟이 전부</b>다.
    ///
    /// 나머지 열여섯은 <see cref="LandUnitArt.PartOf"/> 가 아군·적에 <b>같은 파트</b>를
    /// 내주는데, 그 그림이 적 자리에서 보는 쪽을 향해 있다 — 창병 · 인디오 · 영주는 물론
    /// 사무라이 · 코끼리병도 그렇다. 아군 자리에 세우면 <b>아래를 보고 서서</b> 등을 돌린 채
    /// 싸운다. 모의전은 시험 자리라 아예 못 고르게 막는다.
    /// </remarks>
    private static bool CanBeMine(int kind) => LandUnitArt.DeploySheet(kind) >= 0;

    /// <summary>
    /// 갈래에 맞춰 「상대」 칸을 다시 채운다.
    /// </summary>
    /// <remarks>
    /// 마을 공략이면 <b>도시</b>를 고른다 — 그 도시의 나라·문화권·규모로 판을 세우고
    /// 적은 게임처럼 진형표에서 지어낸다. 들싸움이면 <b>뭍에서 마주치는 부대 열여섯</b>
    /// (<see cref="LandFieldFoes"/>) 가운데 하나를 고르고, 그 벌의 병력을 굴려 넣는다.
    /// </remarks>
    private void FillTargets()
    {
        bool town = Sorts[Math.Max(0, _sort.SelectedIndex)].Value == LandBattle.Town;

        _target.Items.Clear();
        _cityIds.Clear();

        if (town)
        {
            foreach (var city in _towns.OrderBy(c => c.Id))
            {
                _cityIds.Add(city.Id);
                _target.Items.Add($"{city.Id,3} {city.Name}");
            }
        }
        else
        {
            foreach (var party in LandFieldFoes.All)
                _target.Items.Add($"{party.Name} ({party.Where})");
        }

        if (_target.Items.Count > 0) _target.SelectedIndex = 0;
    }

    /// <summary>고른 상대 — 마을 공략이면 도시 번호, 들싸움이면 부대 자리다.</summary>
    private int PickedTarget()
    {
        int at = _target.SelectedIndex;
        if (at < 0) return -1;
        return _cityIds.Count > 0 ? (at < _cityIds.Count ? _cityIds[at] : -1) : at;
    }

    /// <summary>한 쪽의 여섯 자리와 병력 칸.</summary>
    /// <param name="friend">아군 쪽인지 — 그러면 <see cref="FoeOnly"/> 가 흐리다.</param>
    private UIElement Side(string title, ComboBox[] slots, TextBox men, bool friend)
    {
        var box = new StackPanel { Width = 290 };
        box.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 6, 0, 4),
        });

        for (int i = 0; i < Slots; i++)
        {
            // 칸 번호가 곧 병종 번호 + 1 이므로 <b>줄을 빼지 않고 흐리게</b>만 한다.
            var pick = new ComboBox { Width = PickWidth, Margin = new Thickness(0, 0, 0, 3) };
            pick.Items.Add(new ComboBoxItem { Content = "— 빈 자리 —" });
            for (int kind = 0; kind < LandUnits.Names.Length; kind++)
                pick.Items.Add(new ComboBoxItem
                {
                    Content = LandUnits.Names[kind],
                    IsEnabled = !friend || CanBeMine(kind),
                });

            // 처음에는 아군 셋 · 적 셋을 세워 둔다 — 열자마자 싸울 수 있게.
            pick.SelectedIndex = i < 3 ? Opening[i] + 1 : 0;
            slots[i] = pick;

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Label($"{i + 1}", 20));
            row.Children.Add(pick);
            box.Children.Add(row);
        }

        var last = new StackPanel { Orientation = Orientation.Horizontal,
                                    Margin = new Thickness(0, 6, 0, 0) };
        last.Children.Add(Label("병력", 20));
        last.Children.Add(men);
        last.Children.Add(new TextBlock { Text = " 명", VerticalAlignment = VerticalAlignment.Center });
        box.Children.Add(last);
        return box;
    }

    /// <summary>열 때 세워 두는 병종 셋 — 제독 · 기병 · 화승총대다.</summary>
    private static readonly int[] Opening =
        [LandUnits.Admiral, LandUnits.Horse, LandUnits.Matchlock];

    private static TextBlock Label(string text, double width) => new()
    {
        Text = text,
        Width = width,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Head(string text) => new()
    {
        Text = text,
        Foreground = Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
    };

    private void Decide()
    {
        var mine = Picked(_mine);
        var theirs = Picked(_theirs);

        if (mine.All(k => k < 0) || theirs.All(k => k < 0))
        {
            MessageBox.Show(this, "양쪽 다 적어도 한 자리는 세워야 합니다.", Title,
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _made = new Setup(mine, theirs, Men(_myMen), Men(_foeMen),
                          _culture.SelectedIndex, _terrain.SelectedIndex,
                          Sorts[Math.Max(0, _sort.SelectedIndex)].Value, PickedTarget());
        Last = _made;                            // 다음에 열 때 이대로 되편다
        Close();
    }

    /// <summary>고른 여섯 자리. 「빈 자리」는 −1 이다.</summary>
    private static int[] Picked(ComboBox[] slots) =>
        [.. slots.Select(s => s.SelectedIndex - 1)];

    /// <summary>병력 칸. 숫자가 아니거나 0 이하면 백으로 친다.</summary>
    private static int Men(TextBox box) => Sane(int.TryParse(box.Text, out int n) ? n : 0);

    /// <summary>병력 한 값을 쓸 만한 사이로 민다 — 손으로 고친 설정 파일이 들어와도 버틴다.</summary>
    private static int Sane(int men) => men > 0 ? Math.Min(men, 9999) : 100;

    /// <summary>
    /// 모의전을 차리고 그대로 싸운다.
    /// </summary>
    public static void Play(Window owner, Engine.Game game)
    {
        var setup = new LandSparDialog(game.CityTable.Cities) { Owner = owner };
        setup.ShowDialog();
        if (setup._made is not { } made) return;

        // 싸움 쪽은 제 주사위를 쓴다 — 성문에서 들어갈 때와 같은 결이다.
        var dice = new GameRandom(Environment.TickCount);

        var player = game.Player;
        var aide = player.Mates.Count > 0 && player.Mates[0].Length > 0
            ? player.MateInfoOf(player.Mates[0]) : null;

        // 마을 공략은 <b>그 도시로 진짜 판을 세운다</b> — 나라·문화권·규모를 도시에서
        // 떠 오고 적은 진형표에서 지어낸다(0x004A1320). 모의전이라 값은 안 치른다.
        LandBattle field;
        if (made.Sort == LandBattle.Town && made.Target >= 0)
        {
            var rows = game.CityRows;
            field = new LandBattle(made.Mine, player, aide,
                                   rows?.ScaleOf(made.Target) ?? 3,
                                   rows?.NationOf(made.Target) ?? -1,
                                   rows?.CultureOf(made.Target) ?? made.Culture,
                                   TownField, dice, made.MyMen, mock: true)
            { MyCulture = game.MyCulture };
        }
        else
        {
            // 들싸움은 고른 부대의 병력을 굴려 넣는다(0x0048BF8B) — 편성은 창에서 고른 것이다.
            int foeMen = made.Target >= 0 ? LandFieldFoes.MenOf(made.Target, dice) : made.FoeMen;
            int culture = made.Target >= 0
                ? LandFieldFoes.All[made.Target].Culture : made.Culture;
            field = new LandBattle(made.Mine, made.Theirs, made.MyMen, foeMen,
                                   player, aide, culture, made.Terrain, dice, made.Sort)
            { MyCulture = game.MyCulture };
        }

        LandBattleScene.Run(owner, game, field, dice);
    }
}
