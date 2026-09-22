using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 함대 창 설정. 배경음악과 효과음을 따로 켜고 끈다.
/// </summary>
/// <remarks>
/// 켜고 끈 것은 <see cref="GameSettings.BgmEnabled"/> 에 적혀 다음에 켤 때도 그대로다.
/// </remarks>
public sealed class SettingsDialog : GameWindow
{
    private readonly BgmPlayer _bgm;

    /// <summary>켜고 끄는 칸이 앉는 자리. 뒤집을 때마다 칸을 통째로 갈아 끼운다.</summary>
    private readonly Border _bgmRow = new() { Padding = new Thickness(8, 8, 8, 2) };
    private readonly Border _sfxRow = new() { Padding = new Thickness(8, 2, 8, 2) };
    private readonly Border _bgmVolRow = new() { Padding = new Thickness(8, 2, 8, 2) };
    private readonly Border _sfxVolRow = new() { Padding = new Thickness(8, 2, 8, 2) };
    private readonly Border _sizeRow = new() { Padding = new Thickness(8, 2, 8, 2) };
    private readonly Border _mapRow = new() { Padding = new Thickness(8, 2, 8, 2) };

    /// <summary>해상 지도 배율을 바꿨을 때 지도에 곧바로 먹이는 손. 없으면 다음에 켤 때 든다.</summary>
    private readonly Action<double>? _onMapScale;

    private SettingsDialog(BgmPlayer bgm, Action<double>? onMapScale)
    {
        _bgm = bgm;
        _onMapScale = onMapScale;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        _bgmRow.Child = BgmToggle();
        _sfxRow.Child = SfxToggle();
        _bgmVolRow.Child = VolumeRow("배경음악", GameSettings.BgmVolume, StepBgm);
        _sfxVolRow.Child = VolumeRow("효과음  ", GameSettings.SfxVolume, StepSfx);
        _sizeRow.Child = SizeRow();
        _mapRow.Child = MapRow();

        var title = GameUi.TitleBar("설정", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(_bgmRow);
        stack.Children.Add(_bgmVolRow);
        stack.Children.Add(_sfxRow);
        stack.Children.Add(_sfxVolRow);
        stack.Children.Add(_sizeRow);
        stack.Children.Add(_mapRow);
        stack.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 10),
            Children = { GameUi.PushButton("닫기", Close) },
        });

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
        MouseRightButtonUp += (_, _) => Close();
    }

    // 저장·지도 글쇠 줄은 햄버거의 「단축키」 창(ShortcutDialog)으로 옮겨 여기서 걷었다.

    /// <summary>줄 글자. 두 줄의 폭이 어긋나지 않게 이름을 같은 길이로 맞춰 둔다.</summary>
    private static string Label(string name, bool on) => $"{name}   {(on ? "켬" : "끔")}";

    private UIElement BgmToggle() =>
        new GameButton(Label("배경음악", GameSettings.BgmEnabled), FlipBgm);

    private UIElement SfxToggle() =>
        new GameButton(Label("효과음  ", GameSettings.SfxEnabled), FlipSfx);

    /// <summary>켜고 끄기를 뒤집는다 — 곡은 그 자리에서 멈추거나 다시 돈다.</summary>
    /// <remarks>
    /// 글자만 갈아 끼우지 않고 칸을 <b>다시 짓는다</b>. 칸의 속 모양이 한 가지가 아니기
    /// 때문이다 — 게임 원본 조각을 읽었으면 띠 그림 위에 비트맵 글씨를 얹은 <c>Grid</c> 가
    /// 들어 있고, 못 읽었을 때만 <c>TextBlock</c> 이다. 속을 아는 척하고 형변환하면
    /// 원본 조각이 있는 자리에서 반드시 깨진다.
    /// </remarks>
    private void FlipBgm()
    {
        GameSettings.BgmEnabled = !GameSettings.BgmEnabled;
        _bgm.Enabled = GameSettings.BgmEnabled;
        _bgmRow.Child = BgmToggle();
    }

    /// <summary>
    /// 효과음을 켜고 끈다. 배경음악과 따로 논다 — 곡은 두고 소리만 끄고 싶을 때가 있다.
    /// 실제로 가르는 자리는 <see cref="SoundBank.Play"/> 다.
    /// </summary>
    private void FlipSfx()
    {
        GameSettings.SfxEnabled = !GameSettings.SfxEnabled;
        _sfxRow.Child = SfxToggle();
    }

    /// <summary>소리 크기 한 줄 — <c>◀ 100 ▶</c> 로 열씩 오르내린다.</summary>
    private static UIElement VolumeRow(string name, int volume, Action<int> step)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new GameButton("◀", () => step(-GameSettings.VolumeStep),
                                        BandStyle.Button, StepWidth));
        row.Children.Add(Value($"{name} {volume,3}"));
        row.Children.Add(new GameButton("▶", () => step(GameSettings.VolumeStep),
                                        BandStyle.Button, StepWidth));
        return row;
    }

    /// <summary>소리 크기 줄의 칸 폭.</summary>
    private const double StepWidth = 32, NumberWidth = 120;

    /// <summary>
    /// ◀ ▶ 사이에 값을 보여 주는 칸. <b>누르는 데는 아니지만 글씨는 검정</b>이다.
    /// </summary>
    /// <remarks>
    /// <see cref="GameButton"/> 은 할 일이 없는 줄을 흐린 회색(색인 21)으로 찍는다. 여기는
    /// 못 누르는 것이 아니라 <b>보여 주기만 하는</b> 칸이라 <see cref="GameButton.Lit"/> 를
    /// 세워 여느 단추와 같은 먹색으로 둔다.
    /// </remarks>
    private static GameButton Value(string text) =>
        new(text, null, BandStyle.Button, NumberWidth) { Lit = true };

    /// <summary>
    /// 게임 창 크기 한 줄 — <c>◀ 1200 x 800 ▶</c> 로 고른다.
    /// </summary>
    /// <remarks>
    /// 원본은 640x480 한 가지지만 우리 지도는 넓으면 넓은 만큼 더 보이는 창이라
    /// 몇 가지를 열어 두었다(<see cref="GameSettings.Resolutions"/>). 고르면
    /// <b>그 자리에서</b> 창이 바뀐다 — 다시 켜지 않아도 된다.
    /// </remarks>
    private UIElement SizeRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new GameButton("◀", () => StepSize(-1), BandStyle.Button, StepWidth));
        row.Children.Add(Value($"화면 {GameSettings.WindowSize.Name}"));
        row.Children.Add(new GameButton("▶", () => StepSize(+1), BandStyle.Button, StepWidth));
        return row;
    }

    /// <summary>크기를 한 칸 옮기고 그 자리에서 창에 먹인다. 끝에서 다시 처음으로 돈다.</summary>
    private void StepSize(int by)
    {
        int count = GameSettings.Resolutions.Length;
        GameSettings.Resolution = (GameSettings.Resolution + by + count) % count;
        _sizeRow.Child = SizeRow();
        Apply(Owner);
    }

    /// <summary>
    /// 고른 크기를 창에 먹인다. 창을 처음 띄울 때도 이 손을 쓴다.
    /// </summary>
    /// <remarks>
    /// 폭이 0 이면 전체 화면이다 — 테 없는 최대화로 낸다. 창 크기로 돌아올 때는
    /// 최대화를 풀고 크기를 박은 뒤 화면 한가운데로 다시 앉힌다.
    /// </remarks>
    public static void Apply(Window? window)
    {
        if (window == null) return;

        var (_, width, height) = GameSettings.WindowSize;
        if (width == 0)
        {
            window.WindowState = WindowState.Maximized;
            return;
        }

        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;

        // 크기를 키우면 창이 화면 밖으로 밀려날 수 있다. 가운데로 다시 앉힌다.
        var area = SystemParameters.WorkArea;
        window.Left = area.Left + (area.Width - width) / 2;
        window.Top = area.Top + (area.Height - height) / 2;
    }

    private void StepBgm(int by)
    {
        GameSettings.BgmVolume += by;
        _bgm.Volume = GameSettings.BgmVolume / (double)GameSettings.MaxVolume;
        _bgmVolRow.Child = VolumeRow("배경음악", GameSettings.BgmVolume, StepBgm);
    }

    private void StepSfx(int by)
    {
        GameSettings.SfxVolume += by;
        _sfxVolRow.Child = VolumeRow("효과음  ", GameSettings.SfxVolume, StepSfx);
    }

    /// <summary>
    /// 해상 지도 배율 한 줄 — <c>◀ 지도 0.75 ▶</c> 로 0.25 씩 0.5~1.5 를 오간다. 끝에서 멈춘다.
    /// </summary>
    private UIElement MapRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new GameButton("◀", () => StepMap(-1), BandStyle.Button, StepWidth));
        row.Children.Add(Value($"지도 {GameSettings.MapScale:0.00}"));
        row.Children.Add(new GameButton("▶", () => StepMap(+1), BandStyle.Button, StepWidth));
        return row;
    }

    /// <summary>배율을 한 칸 옮기고 지도에 곧바로 먹인다.</summary>
    private void StepMap(int by)
    {
        double next = GameSettings.MapScale + by * GameSettings.MapScaleStep;
        GameSettings.MapScale = next;
        _mapRow.Child = MapRow();
        _onMapScale?.Invoke(GameSettings.MapScale);
    }

    /// <param name="onMapScale">해상 지도 배율을 바꿨을 때 지도에 먹이는 손.</param>
    public static void Show(Window owner, BgmPlayer bgm, Action<double>? onMapScale = null) =>
        new SettingsDialog(bgm, onMapScale) { Owner = owner }.ShowDialog();
}
