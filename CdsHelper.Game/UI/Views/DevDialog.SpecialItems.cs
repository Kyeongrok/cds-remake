using System.Windows;
using System.Windows.Controls;
using CdsHelper.Game.Engine.Land;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「특수템추가」 탭 — 다 빈치 무기 셋과 특수 선수상을 지금 게임 주인공 소지품에 바로 넣는다(버그 리포트 #37, 원본에 없는 것이다).
/// </summary>
/// <remarks>
/// 세이브 파일을 고쳐 넣는 「테스트」 탭의 「아이템 넣기…」(<see cref="Engine.GameSave.AddItem"/>)와 달리 떠 있는 게임의
/// <see cref="CdsHelper.Support.Local.Models.Player"/> 에 곧장 넣는다 — 다음 저장 때 세이브에 남는다.
/// <code>
///   무기     0 잠수폭탄(SeaBattle.MineItem) · 1 속사포(SeaBattle.RapidFireItem) · 2 작렬탄(LandBattle.ShellItem)
///   선수상   239 여신상 ~ 248 마왕상 — 선수상 표(0x0054A0A0) 0x1A~0x23 + 213. 싸움 셈에 효과가 붙는 것들이다
///            (해신 · 수룡 · 불꽃신 · 청룡 · 백호 · 불사조 · 천마 이동력 +1(0x0044CA30) · 사신 · 마왕). 213~238 은 효과가 없는 여느 선수상이다
///   칸       Player.Take — 원본 열여섯 칸(+0x118) + 작위 혜택. 꽉 차면 원본 말 「이 이상 가질 수 없습니다!」(0x00544830)
/// </code>
/// 넣으면 다른 개발 기능처럼 개발 기록에 「dev:item」으로 남고, 하드코어 게임이면 랭킹 기록에도 더한다
/// (<see cref="Engine.HardcoreRules.Mark"/>). 타이틀처럼 게임을 불러오지 않았으면 단추가 꺼진다.
/// </remarks>
public sealed partial class DevDialog
{
    /// <summary>이 탭이 넣은 것을 적는 개발 기록 열쇠 — 「아이템 넣기…」와 같다.</summary>
    private const string ItemMark = "dev:item";

    /// <summary>특수 선수상의 아이템 번호 — 선수상 번호(0x1A 여신 ~ 0x23 마왕)에 213 을 더한 값.</summary>
    private const int FigureheadBase = 213;

    /// <summary>탭이 늘어놓는 무기 셋.</summary>
    private static readonly int[] SpecialWeapons = [SeaBattle.MineItem, SeaBattle.RapidFireItem, LandBattle.ShellItem];

    /// <summary>탭이 늘어놓는 특수 선수상 — 여신상 ~ 마왕상.</summary>
    private static readonly int[] SpecialFigureheads =
        [.. Enumerable.Range(FigureheadBase + SeaBattle.Goddess, SeaBattle.DemonKing - SeaBattle.Goddess + 1)];

    /// <summary>표를 못 읽었을 때 쓰는 이름(아이템 표 0 · 1 · 2 · 239~248 그대로).</summary>
    private static readonly Dictionary<int, string> SpecialNames = new()
    {
        [0] = "잠수폭탄", [1] = "속사포", [2] = "작렬탄",
        [239] = "여신상", [240] = "해신상", [241] = "수룡상", [242] = "불꽃신상", [243] = "청룡상",
        [244] = "백호상", [245] = "불사조상", [246] = "천마상", [247] = "사신상", [248] = "마왕상",
    };

    /// <summary>「특수템추가」 탭 판.</summary>
    private FrameworkElement SpecialItemsPage(ItemTable? table, bool loaded)
    {
        var page = new StackPanel { Margin = new Thickness(12, 10, 12, 4), Width = 560 };
        var refreshers = new List<Action>();
        void RefreshAll() { foreach (var r in refreshers) r(); }

        TextBlock Label(string text, double width = 0, double size = 15, bool bold = true)
        {
            var t = new TextBlock
            {
                Text = text, Foreground = GameUi.Text, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            };
            if (width > 0) t.Width = width;
            return t;
        }

        page.Children.Add(Label("주인공 소지품에 잠수폭탄 · 속사포 · 작렬탄과 특수 선수상을 넣습니다. " +
                                "넣으면 개발 기록에 남고, 하드코어 게임이면 랭킹 기록에도 남습니다.", 0, 13, bold: false));

        var bag = Label("", 0, 14);
        bag.Margin = new Thickness(0, 6, 0, 0);
        refreshers.Add(() => bag.Text = loaded
            ? string.Format("소지품 {0} / {1}칸", _player.Items.Count, _player.ItemLimit)
            : "게임을 불러온 뒤에 넣을 수 있습니다.");
        page.Children.Add(bag);

        var said = Label("", 0, 13, bold: false);
        said.Margin = new Thickness(0, 2, 0, 0);
        page.Children.Add(said);

        void Give(int id, string name)
        {
            if (!loaded) return;
            if (!_player.Take(id))
            {
                NoticeDialog.Show(this, "소지품이 꽉 찼습니다 — 이 이상 가질 수 없습니다!");
                return;
            }
            Engine.HardcoreRules.Mark(ItemMark, _player);
            said.Text = string.Format("소지품에 「{0}」 하나를 넣었습니다.", name);
            RefreshAll();
        }

        void Group(string head, int[] ids)
        {
            var title = Label(head, 0, 16);
            title.Margin = new Thickness(0, 10, 0, 2);
            page.Children.Add(title);
            var grid = new WrapPanel();
            foreach (int id in ids)
            {
                string name = table?.Find(id)?.Name is { Length: > 0 } n ? n : SpecialNames[id];
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 6, 3), Width = 270 };
                line.Children.Add(Label(name, 100));
                var held = Label("", 64, 13, bold: false);
                refreshers.Add(() => held.Text = string.Format("{0}개", _player.Items.Count(i => i == id)));
                held.ToolTip = "지금 지닌 수입니다";
                var button = GameUi.PushButton("넣기", loaded ? () => Give(id, name) : null, 70);
                button.Margin = new Thickness(4, 0, 4, 0);
                line.Children.Add(button);
                line.Children.Add(held);
                grid.Children.Add(line);
            }
            page.Children.Add(grid);
        }

        Group("다 빈치 무기", SpecialWeapons);
        Group("특수 선수상", SpecialFigureheads);

        RefreshAll();
        return page;
    }
}
