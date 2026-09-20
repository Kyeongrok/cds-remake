using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 성문 앞 — 막힌 도시에 다가서면 <b>그림은 그려지고</b> 문지기에게 문에서 막힌다.
/// </summary>
/// <remarks>
/// 들어가겠다고 대답하면 게임은 도시 그림부터 편다. 그 위에서 문지기가
/// <c>0x00551D28</c> "외국인은 들어올 수 없다." 를 말하고, 그제야 공격·잠입·교섭·떠난다가
/// 뜬다(<c>0x004A5210</c> 이 차림표 <c>0x004A56F0</c> 의 앞머리다).
///
/// <b>말을 못 알아들으면 글자가 아니라 ×로 나온다.</b> 그리고 대원 중에도 아는 이가 없으면
/// 한 마디 덧붙는다 — 마을이면 <c>0x00551D48</c>, 항구면 <c>0x00551D90</c> 이라
/// <b>문구가 갈린다</b>. 인자가 0 이 아닐 때만 「마을」이라는 말이 들어가는 이 갈림이
/// 곧 차림표 인자의 뜻을 밝혀 준다(1 이 마을, 0 이 항구).
///
/// 이 창은 그림만 깐다 — 말과 차림표는 이 창을 임자로 삼아 그 위에 뜬다.
/// </remarks>
internal sealed class GateScene : GameWindow
{
    /// <summary>
    /// 한 장이 머무는 참. <b>벌마다 따로 잡는다.</b>
    /// </summary>
    /// <remarks>
    /// 게임은 벌마다 「몇 걸음에 한 장」인지가 다른데(<see cref="EffectAnim.HeartStep"/> 2 ·
    /// <see cref="EffectAnim.CoinStep"/> 1) 그 걸음이 몇 밀리초인지는 아직 못 짚었다 —
    /// 창 고리가 도는 대로 세기 때문이다(<c>0x004A5DDE</c>).
    ///
    /// 그래서 <b>눈으로 보며 맞춘 값</b>이다. 하트는 걸음 비율대로 두면 알맞은데
    /// (한 장 140ms, 열 장 1.4초) 동전은 그 비율대로 70ms 면 팽이처럼 돌아 따라가기가
    /// 어려웠다. 동전만 100ms 로 늦춰 스물세 장에 2.3초로 두었다.
    /// </remarks>
    private static readonly TimeSpan HeartSpan = TimeSpan.FromMilliseconds(140);

    private static readonly TimeSpan CoinSpan = TimeSpan.FromMilliseconds(100);

    /// <summary>그 벌의 한 장이 머무는 참.</summary>
    private static TimeSpan SpanOf(int anim) => anim == EffectAnim.Coin ? CoinSpan : HeartSpan;

    private readonly Canvas _layer = new() { IsHitTestVisible = false };
    private readonly Engine.Game _game;
    private readonly int _scale;
    private bool _playing;

    private GateScene(Engine.Game game, BitmapSource picture, int scale, Rect mapArea)
    {
        _game = game;
        _scale = scale;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = Brushes.Black;
        SizeToContent = SizeToContent.Manual;

        double fullW = CityPictures.Width * scale, fullH = CityPictures.Height * scale;
        Width = fullW;
        Height = fullH;
        if (mapArea.Width > 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = mapArea.X + (mapArea.Width - fullW) / 2;
            Top = mapArea.Y + (mapArea.Height - fullH) / 2;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        var art = new Image { Source = picture, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(art, GameUi.SpriteScaling);

        // 그림 위에 애니메이션을 얹을 자리를 하나 깔아 둔다 — 교섭할 때 하트가 여기서 돈다.
        Content = new Grid { Children = { art, _layer } };
    }

    /// <summary>
    /// 교섭하는 하트를 돌린다(<c>0x004A6360</c> → <c>0x004A6140(3, 성공여부, -1)</c>).
    /// </summary>
    /// <remarks>
    /// 게임은 <b>굴림을 하고 나서</b> 이것을 돌린다 — 그래서 하트가 깨지면 이미 진 것이다.
    /// 파트 <b>3</b> 이 하트고(<see cref="EffectAnim.Heart"/>), 소리는 없다.
    /// </remarks>
    public void PlayHeart(bool won) => Play(EffectAnim.Heart, won);

    /// <summary>
    /// 잠입하는 동전을 돌린다(<c>0x004A53D0</c> → <c>0x004A6380</c> → 파트 <b>4</b>).
    /// </summary>
    /// <remarks>
    /// 하트와 달리 <b>한 걸음에 한 장</b>이라 두 배 빠르게 돌고, 스무 걸음 남짓 돌다가
    /// 되면 첫째 장 · 어그러지면 넷째 장에서 멎는다(<see cref="EffectAnim.CoinFrames"/>).
    /// 게임도 굴림을 하고 나서 돌리므로 멎은 쪽이 곧 결과다.
    /// </remarks>
    public void PlayCoin(bool won) => Play(EffectAnim.Coin, won);

    /// <summary>
    /// 들킨 뒤 달아나는 벌을 돌린다(<c>0x004A5419</c> → <c>0x004A6120</c> → 파트 <b>0</b>).
    /// </summary>
    public void PlayEscape(bool won) => Play(EffectAnim.Load, won);

    /// <summary>동그란 애니메이션 한 벌을 그림 한가운데에서 돌린다.</summary>
    private void Play(int anim, bool won)
    {
        int[] order = EffectAnim.Frames(anim, won);
        var span = SpanOf(anim);

        if (_playing) return;                       // 도는 동안 또 부르면 겹친다
        if (_game.Effects is not { } effects) return;

        double side = EffectAnim.Size * _scale;
        var image = new Image { Width = side, Height = side, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        Canvas.SetLeft(image, (CityPictures.Width * _scale - side) / 2);
        Canvas.SetTop(image, (CityPictures.Height * _scale - side) / 2);
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
                    if (effects.TryGetBgra(anim, f) is not { } bgra) continue;

                    var bmp = BitmapSource.Create(EffectAnim.Size, EffectAnim.Size, 96, 96,
                                                  PixelFormats.Bgra32, null, bgra,
                                                  EffectAnim.Size * 4);
                    bmp.Freeze();
                    art[f] = bmp;
                }
                image.Source = art[f];
                Wait(span);
            }
        }
        finally
        {
            _layer.Children.Remove(image);
            _playing = false;
        }
    }

    /// <summary>그동안 화면이 멎지 않게 하면서 한 참 기다린다.</summary>
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
    /// 그 도시의 그림을 편다. 그림을 못 구하면 null — 그때는 차림표만 뜬다.
    /// </summary>
    public static GateScene? Open(Window owner, Engine.Game game, int cityId, Rect mapArea)
    {
        if (game.CityPics is not { } pictures) return null;
        if (pictures.TryGetBgra(cityId) is not { } bgra) return null;

        var picture = BitmapSource.Create(CityPictures.Width, CityPictures.Height, 96, 96,
                                          PixelFormats.Bgra32, null, bgra,
                                          CityPictures.Width * 4);
        picture.Freeze();

        double areaW = mapArea.Width > 0 ? mapArea.Width : owner.ActualWidth;
        double areaH = mapArea.Height > 0 ? mapArea.Height : owner.ActualHeight;
        int scale = Math.Max(1, Math.Min(4, (int)Math.Min(areaW * 0.6 / CityPictures.Width,
                                                          areaH * 0.7 / CityPictures.Height)));

        var scene = new GateScene(game, picture, scale, mapArea) { Owner = owner };
        scene.Show();
        return scene;
    }
}
