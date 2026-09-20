using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 조합 → 수련 에서 뜨는 "습득가능 기술" 창. 한 줄을 고르고 결정을 누르면 조합장이 값과
/// 걸리는 달을 이르고, 그래도 좋다면 배운다.
/// </summary>
/// <remarks>
/// 한 자리 올리는 데 <see cref="Skill.Price"/> 닢이 들고, 걸리는 달은 자리마다 다르다
/// (0→1 석 달 · →2 여섯 달 · →3 열두 달). 배우고 나면 그만큼 날이 간다.
///
/// 줄은 게임 <b>비트맵 글꼴</b>로 찍고 아래 두 단추는 <b>베이지 띠</b>다
/// (<see cref="BandStyle.Button"/>) — 힌트 일람과 같은 벌이다.
/// </remarks>
public sealed class SkillLearnDialog : GameWindow
{
    /// <summary>줄 속 칸 — "이름 ( LVn )" 하나다. 게임은 오른쪽맞춤으로 낸다.</summary>
    private static readonly GameListColumn[] Columns =
    [
        new(GameListDock.Fill, new Thickness(10, 0, 10, 0), Align: HorizontalAlignment.Right),
    ];

    /// <summary>목록 바닥 폭과, 넘치면 굴리기 시작하는 키. 게임 갈무리를 재어 맞췄다.</summary>
    private const double ListWidth = 300, ListMaxHeight = 220;

    private readonly Func<string, int> _levelOf;
    private readonly Func<string, bool> _learn;
    private readonly IReadOnlyList<string> _skills;
    private readonly GameList _list;
    private readonly GameButton _decide;

    /// <summary>이 창에서 하나라도 배웠는지. 조합장이 나가는 말을 고르는 데 쓴다.</summary>
    private bool _learned;

    private SkillLearnDialog(IReadOnlyList<string> skills, Func<string, int> levelOf, Func<string, bool> learn)
    {
        _levelOf = levelOf;
        _learn = learn;
        _skills = skills;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _list = new GameList(Columns, i => [RowText(_skills[i])], _skills.Count,
                             maxHeight: ListMaxHeight)
        {
            Margin = new Thickness(0),
            BorderBrush = GameUi.Edge,
        };

        _decide = new GameButton("결정", Decide, width: ButtonWidth) { On = false };
        _list.SelectionChanged += () => _decide.On = _list.Selected >= 0;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        buttons.Children.Add(_decide);
        buttons.Children.Add(new GameButton("종료", Close, width: ButtonWidth));

        var title = GameUi.TitleBar("습득가능 기술", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel { MinWidth = ListWidth };
        stack.Children.Add(title);
        stack.Children.Add(_list);
        stack.Children.Add(buttons);

        Content = GameUi.DialogEdge(stack);

        KeyDown += OnKey;
        MouseRightButtonUp += (_, _) => Close();
    }

    /// <summary>단추 하나의 폭.</summary>
    private const double ButtonWidth = 110;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_list.HandleKey(e.Key)) { e.Handled = true; return; }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter when _list.Selected >= 0:
                Decide();
                e.Handled = true;
                break;
        }
    }

    /// <summary>줄에 찍는 글.</summary>
    private string RowText(string name) => $"{name} ( LV{_levelOf(name)} )";

    /// <summary>
    /// 결정 — 가르치는 쪽(<see cref="TrainingMenu"/>)이 값·기간을 묻고 배운다. <b>하나 배우면 창을 닫는다</b>
    /// (게임 <c>0x00491436</c> 이 수련 차림을 끝낸다). 물리거나 못 배웠으면 목록에 남는다.
    /// </summary>
    private void Decide()
    {
        int at = _list.Selected;
        if (at < 0 || at >= _skills.Count) return;

        if (!_learn(_skills[at])) { _list.Refresh(); return; }
        _learned = true;
        Close();
    }

    /// <summary>
    /// 습득가능 기술 창을 띄운다. <paramref name="skills"/> 는 그 건물이 가르치는 것이다 —
    /// 게임은 건물 표의 비트마스크로 도시마다 다르게 준다.
    /// </summary>
    /// <returns>하나라도 배웠으면 true. 부르는 쪽이 나가는 말을 고르는 데 쓴다.</returns>
    public static bool Show(Window owner, IReadOnlyList<string> skills,
                            Func<string, int> levelOf, Func<string, bool> learn)
    {
        if (skills.Count == 0) return false;

        var dlg = new SkillLearnDialog(skills, levelOf, learn) { Owner = owner };
        dlg.ShowDialog();
        return dlg._learned;
    }
}
