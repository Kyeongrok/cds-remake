using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 「함대정보」 — 가진 배를 죽 늘어놓고 함대 전체의 형편을 보여 주는 판.
/// 커맨드 → 정보 → 함대정보.
/// </summary>
/// <remarks>
/// 게임의 <c>0x0046F340</c> 이다. 화면을 재어 맞췄다(갈무리 1.7778배).
/// <code>
///   판 속     375 x 382
///   배 단추   304 x 24 · 가운데 · 위에서 17 부터 붙여 쌓는다
///   글 줄     왼쪽 16 부터 · 값과 막대는 86 부터 · 줄마다 16
///   막대      154 x 5 · 검정 바탕에 붉은 살
///   아래 줄   대열 · 짐 · 취소
/// </code>
/// <b>배 하나하나의 자세한 것은 이 판에 없다</b> — 이름 단추를 누르면 그 배의 판이 열린다
/// (<see cref="ShipInfoDialog"/>). 게임도 목록이 먼저고 배는 그 다음이다.
///
/// 「대열」은 함대배치도(<c>0x0046F619</c> → <c>0x00433F30</c>)를 열고, 「짐」은 보급물자와 교역품일람을
/// 늘어놓는 판(<see cref="CargoView"/>)이다.
/// </remarks>
internal sealed class FleetInfoDialog : InfoDialog
{
    /// <summary>판 크기. 게임 화면에서 잰 그림 점 그대로다.</summary>
    private const double BoardWidth = 375, BoardHeight = 382;

    /// <summary>배 단추 폭. 마구리 둘에 가운데 서른네 칸이다(16+8*34+16).</summary>
    private const double ShipWidth = 304;

    /// <summary>글 줄의 왼쪽 여백과 값이 서는 자리.</summary>
    private const double RowInset = 16, ValueLeft = 70;

    /// <summary>막대 크기.</summary>
    private const double BarWidth = 154, BarHeight = 5;

    /// <summary>막대의 빈 쪽과 찬 쪽. 화면에서 그대로 뽑았다.</summary>
    private static readonly Brush BarBack = Frozen(Color.FromRgb(0, 0, 0));
    private static readonly Brush BarFill = Frozen(Color.FromRgb(135, 21, 10));

    /// <inheritdoc/>
    protected override Brush Board => Steel;

    /// <inheritdoc/>
    protected override Brush BoardEdge => SteelEdge;

    private FleetInfoDialog(Player player, string coord, ItemTable? items, Func<Player.Cargo, string>? cargoName,
                            Action<Window, Player.Cargo>? cargoInfo)
    {
        var rows = new StackPanel();

        // 배 목록. 이름 단추를 붙여 쌓는다 — 게임도 줄 사이가 벌어져 있지 않다.
        var ships = new StackPanel
        {
            Width = ShipWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 0),
        };
        for (int i = 0; i < player.Ships.Count; i++)
        {
            int at = i;
            ships.Children.Add(new GameButton($"{player.Ships[at].Name}호",
                                              () => ShipInfoDialog.Show(this, player, at, items))
            {
                Margin = default,
            });
        }
        rows.Children.Add(ships);

        rows.Children.Add(Gap(13));
        rows.Children.Add(Row("국적", Text(player.NationName)));
        // 자리를 모르면(도시 안) 「위도 ---도  경도 ---도」다(0x00571320).
        rows.Children.Add(Row("함대좌표", Text(coord.Length > 0 ? coord : "위도 ---도  경도 ---도")));
        rows.Children.Add(Row("피로도", Bar(player.Fatigue, Player.MaxFatigue)));
        rows.Children.Add(Row("총승원수", Bar(player.Crew, player.MaxCrew)));
        rows.Children.Add(Row("짐용량", Bar(player.LoadedBarrels, player.Capacity)));
        rows.Children.Add(Row("짐중량", Bar(player.LoadedWeight, player.Tonnage)));

        Build("함대정보", rows, BoardWidth, BoardHeight,
              new GameButton("대열", () => FormationDialog.Show(this, player)),
              new GameButton("짐", () => CargoView.Show(this, player, cargoName, cargoInfo)),
              new GameButton("취소", Close));
    }

    /// <summary>이름 한 칸에 값 한 칸인 줄. 값 자리는 줄마다 같다.</summary>
    private static UIElement Row(string name, UIElement value)
    {
        var line = new Grid { Height = GameUi.ItemTextHeight, Margin = new Thickness(RowInset, 0, 0, 0) };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ValueLeft) });
        line.ColumnDefinitions.Add(new ColumnDefinition());

        var label = Label(name);
        label.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(label);

        Grid.SetColumn(value, 1);
        line.Children.Add(value);
        return line;
    }

    private static UIElement Text(string text)
    {
        var label = Label(text);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    /// <summary>
    /// 찬 만큼 붉게 물드는 막대와 그 옆의 숫자.
    /// </summary>
    /// <remarks>
    /// 숫자는 게임처럼 자릿수를 맞춰 적는다 — 게임 글꼴은 ASCII 가 8점으로 고정이라
    /// 빈칸으로 밀면 줄마다 자리가 딱 맞는다.
    /// </remarks>
    private static UIElement Bar(int now, int max)
    {
        double part = max > 0 ? Math.Clamp((double)now / max, 0, 1) : 0;

        var fill = new Border
        {
            Width = BarWidth * part,
            Height = BarHeight,
            Background = BarFill,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var gauge = new Grid
        {
            Width = BarWidth,
            Height = BarHeight,
            Background = BarBack,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { fill },
        };

        var number = Label($"{now,6}/{max,-6}");
        number.VerticalAlignment = VerticalAlignment.Center;
        number.Margin = new Thickness(4, 0, 0, 0);

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(gauge);
        line.Children.Add(number);
        return line;
    }

    /// <summary>함대정보 판을 연다.</summary>
    /// <param name="coord">함대좌표에 적을 글. 도시 안이면 비워 둔다 — 「위도 ---도  경도 ---도」가 선다.</param>
    /// <param name="items">아이템 표. 배 정보의 선수상 이름을 여기서 낸다.</param>
    /// <param name="cargoName">교역품 한 칸의 이름 — 「%s산」 과 품목 이름(<c>0x0042E310</c>).</param>
    /// <param name="cargoInfo">짐 판에서 교역품 단추를 눌렀을 때 — 그 교역품 창(그림·분류·개체중량)을 띄운다.</param>
    public static void Show(Window owner, Player player, string coord = "",
                            ItemTable? items = null, Func<Player.Cargo, string>? cargoName = null,
                            Action<Window, Player.Cargo>? cargoInfo = null) =>
        new FleetInfoDialog(player, coord, items, cargoName, cargoInfo) { Owner = owner }.ShowDialog();

    /// <summary>
    /// 「짐」 판 — 보급물자 한 줄과 교역품일람(<c>0x0046F97D</c> ~ <c>0x0046FC6D</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   보급물자                                               0x00571390
    ///   식량%4d통    물  %4d통    자재%4d통    탄약%4d통        0x005713A0 (식량·물은 (값+9)/10 통)
    ///   교역품일람                                             0x005713D8
    ///   칸마다 「%s산」 + 품목 이름 — 띠 단추, 누르면 교역품 창    0x0042E310
    ///   취소                                                   0x005713E8
    /// </code>
    /// </remarks>
    private sealed class CargoView : InfoDialog
    {
        private CargoView(Player player, Func<Player.Cargo, string>? cargoName,
                          Action<Window, Player.Cargo>? cargoInfo)
        {
            var rows = new StackPanel { Margin = new Thickness(RowInset, 0, RowInset, 0) };
            rows.Children.Add(Label("보급물자"));
            rows.Children.Add(Label($"식량{player.SupplyOf(SupplyKind.Food),4}통    "
                                    + $"물  {player.SupplyOf(SupplyKind.Water),4}통    "
                                    + $"자재{player.SupplyOf(SupplyKind.Material),4}통    "
                                    + $"탄약{player.SupplyOf(SupplyKind.Ammo),4}통"));
            rows.Children.Add(Gap(8));
            rows.Children.Add(Label("교역품일람"));
            // 칸마다 판 너비 띠 단추다 — 누르면 그 교역품 창(그림·분류·개체중량)이 뜬다.
            foreach (var cargo in player.CargoHold)
            {
                var item = cargo;
                rows.Children.Add(new GameButton(cargoName?.Invoke(item) ?? $"교역품 {item.Kind}",
                                                 () => cargoInfo?.Invoke(this, item))
                {
                    Margin = new Thickness(0, 4, 0, 0),
                });
            }

            Build("", rows, BoardWidth, BoardHeight, new GameButton("취소", Close));
        }

        public static void Show(Window owner, Player player, Func<Player.Cargo, string>? cargoName,
                                Action<Window, Player.Cargo>? cargoInfo) =>
            new CargoView(player, cargoName, cargoInfo) { Owner = owner }.ShowDialog();
    }
}
