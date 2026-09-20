using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 기능·언어 한눈에 보기 — 제독과 부하 넷 가운데 <b>누구 것이 제일 높은지</b>를 색으로 낸다.
/// </summary>
/// <remarks>
/// <b>게임에는 없는 창이다.</b> 도구 앱(<c>CdsHelper.Main</c>)의 「스킬」 칸을 놀이 쪽으로
/// 옮긴 것이라 그림도 그쪽 것(<see cref="SkillGroupBox"/>)을 그대로 쓴다 — 게임 글꼴로
/// 다시 짓지 않는다.
///
/// 게임은 기능을 <b>함대에서 가장 높은 값</b>으로 쓴다(<c>0x0047CCA0</c>) — 항해술은
/// 항해사, 측량은 측량사, 언어는 통역이 맡는 식이라 <b>누가 채우고 있는지</b>가 그대로
/// 놀이에 든다. 그 자리를 한 판에 보려고 둔 창이다.
///
/// 부하의 기능 열셋·언어 열넷은 우리 세이브에 안 적힌다(<see cref="Player.MateInfo"/> 는
/// 검술·사격술·포술 셋뿐이다). 그래서 <b>인물 표에서 그 사람 줄을 찾아</b> 편다.
/// </remarks>
public sealed class SkillBoardDialog : Window
{
    private SkillBoardDialog(Player player, PersonTable? people)
    {
        Title = "기능·언어";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        var board = BuildBoard(player, people, new Thickness(10));

        var close = new Button
        {
            Content = "닫기",
            Width = 90,
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        close.Click += (_, _) => Close();

        var stack = new StackPanel();
        stack.Children.Add(board);
        stack.Children.Add(close);
        Content = stack;

        KeyDown += (_, e) => { if (e.Key is System.Windows.Input.Key.Escape) Close(); };
    }

    /// <summary>기능·언어 판 — 창과 도시 쪽지(<see cref="SkillOverlayWindow"/>)가 같이 쓴다.</summary>
    internal static SkillGroupBox BuildBoard(Player player, PersonTable? people, Thickness margin) => new()
    {
        Header = "스킬",
        Margin = margin,
        Skills = Build(player, people, language: false),
        Languages = Build(player, people, language: true),
        Column1Label = "제독",
        Column2Label = Player.MateRoles[0],
        Column3Label = Player.MateRoles[1],
        Column4Label = Player.MateRoles[2],
        Column5Label = Player.MateRoles[3],
    };

    /// <summary>
    /// 줄 하나씩 — 제독과 부하 넷의 자리를 나란히 채운다.
    /// </summary>
    /// <param name="language">참이면 언어 열넷, 아니면 기능 열셋이다.</param>
    internal static List<SkillDisplayItem> Build(Player player, PersonTable? people, bool language)
    {
        var names = language ? Skill.Languages : Skill.Names;
        var mates = new PersonTable.Row?[Player.MaxMates];
        for (int slot = 0; slot < Player.MaxMates; slot++)
            mates[slot] = RowOf(people, player.MateAt(slot));

        var rows = new List<SkillDisplayItem>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            rows.Add(new SkillDisplayItem
            {
                Name = names[i],
                PlayerLevel = (byte)(language ? player.TongueOf(names[i]) : player.LevelOf(names[i])),
                AdjutantLevel = LevelOf(mates[0], i, language),
                NavigatorLevel = LevelOf(mates[1], i, language),
                SurveyorLevel = LevelOf(mates[2], i, language),
                InterpreterLevel = LevelOf(mates[3], i, language),
            });
        }
        return rows;
    }

    /// <summary>이름으로 인물 줄을 찾는다. 빈 자리거나 표가 없으면 null.</summary>
    private static PersonTable.Row? RowOf(PersonTable? people, string name) =>
        people == null || name.Length == 0
            ? null
            : people.People.FirstOrDefault(r => r.Name == name);

    /// <summary>그 사람의 그 자리 값. 없으면 0.</summary>
    private static byte LevelOf(PersonTable.Row? who, int at, bool language)
    {
        if (who == null) return 0;
        var list = language ? who.Languages : who.Skills;
        return at >= 0 && at < list.Length ? (byte)Math.Clamp(list[at], 0, byte.MaxValue) : (byte)0;
    }

    /// <summary>창을 연다.</summary>
    public static void Show(Window owner, Player player, PersonTable? people) =>
        new SkillBoardDialog(player, people) { Owner = owner }.ShowDialog();
}
