using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 지도 위에 내리는 <b>비·눈</b> — EVANIME 장면 0(비)·1(눈)을 0.1초마다 한 걸음씩 그린다.
/// </summary>
/// <remarks>
/// 게임은 지도를 다 그린 뒤 구름 → 비 → 눈 차례로 얹는다(<c>0x0048AA6E</c> · <c>0x0048AAC2</c> →
/// <c>0x0049ABB0</c>). 셈은 게임 점(지도 칸 하나 = 16점)으로 하고 그릴 때 늘린다.
/// <code>
///   비 0x00496D60   파트 0 32x32 한 장, 방울 75
///     놓기  x = rand(W/20) + (i%10)(W/10),  y = -(i/10)96 - rand(16) - (i&amp;1)64
///     걸음  m = i%3:  x -= 16(m+1),  y += 16(m+2)       — 왼쪽 아래로 비스듬히
///     감기  x &lt; 0 이면 x = W - rand(16), y += rand(16) · y ≥ H 면 x += rand(16), y = -rand(16)
///   눈 0x00497070   파트 1 16x16 세 장(i%3), 송이 50
///     놓기  x = rand(W/40) + (i%20)(W/20),  y = -(i/10)H/5 - rand(16) - (i&amp;1)32, 결 = rand(6)
///     걸음  p = 결&amp;7:  0~2 이면 x += 5m-14, 3~5 면 x += 14-5m, 6·7 이면 그대로 · y += 8(4-m) · 결 += 1
///     감기  y ≥ H 면 y = -rand(16)
///   들어설 때  걸음 ≤5 면 셋에 하나, ≤15 면 반쯤, 그 뒤로 다 (안 그리는 방울은 안 움직인다)
///   그칠 때    비 &lt;10 홀수 · &lt;15 i%3==2 · &lt;18 i%4==3 · 끝 / 눈 &lt;10 짝수 · &lt;15 i%3==1 · 끝
/// </code>
/// </remarks>
public sealed class WeatherView : FrameworkElement
{
    private const int RainDrops = 75, SnowFlakes = 50;

    private readonly Random _rng = new();
    private BitmapSource? _rain;
    private BitmapSource[]? _snow;

    private Engine.Sea.SeaWeather.Kind _kind;
    private bool _fading;
    private int _ramp, _fade;
    private int[] _x = [], _y = [], _phase = [];
    private int _w, _h;
    private double _scale = 1;

    /// <summary>그리는 것이 남아 있는지 — 그치고 흐려짐까지 다 끝나면 거짓이다.</summary>
    public bool Busy => _kind != Engine.Sea.SeaWeather.Kind.None;

    public WeatherView()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>그림을 읽는다. 못 읽으면 아무것도 안 그린다.</summary>
    public void Load(EventAnimation? art)
    {
        if (art?.TryGetWeather(Local.Helpers.EventAnimation.Rain, 32, 32) is { } rain)
            _rain = ToBitmap(rain, 0);
        if (art?.TryGetWeather(Local.Helpers.EventAnimation.Snow, 16, 16) is { } snow)
            _snow = [.. Enumerable.Range(0, Math.Min(3, snow.Count)).Select(f => ToBitmap(snow, f))];
    }

    private static BitmapSource ToBitmap(EventAnimation.Strip strip, int frame)
    {
        int n = strip.Width * strip.FrameHeight;
        var px = new uint[n];
        Array.Copy(strip.Bgra, frame * n, px, 0, n);
        var bmp = BitmapSource.Create(strip.Width, strip.FrameHeight, 96, 96, PixelFormats.Bgra32, null, px,
                                      strip.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>게임 점 하나가 몇 WPF 단위인지와 판 크기를 잡는다.</summary>
    public void Resize(double width, double height, double scale)
    {
        Width = width;
        Height = height;
        _scale = Math.Max(0.25, scale);
        int w = (int)(width / _scale), h = (int)(height / _scale);
        if (w == _w && h == _h) return;
        _w = w;
        _h = h;
        if (Busy) Seed();
    }

    /// <summary>오기 시작한다(<c>0x00488E90</c> · <c>0x00488EC0</c>).</summary>
    public void Start(Engine.Sea.SeaWeather.Kind kind)
    {
        if (kind == Engine.Sea.SeaWeather.Kind.None) { Stop(); return; }
        if (_kind == kind && !_fading) return;
        _kind = kind;
        _fading = false;
        _ramp = 0;
        _fade = 0;
        Seed();
    }

    /// <summary>그치기 시작한다 — 흐려지며 사라진다(<c>0x00488EA0</c>).</summary>
    public void Stop()
    {
        if (_kind == Engine.Sea.SeaWeather.Kind.None) return;
        _fading = true;
        _fade = 0;
    }

    /// <summary>곧바로 지운다 — 해전·육상전처럼 흐려짐 없이 끊는 자리.</summary>
    public void Clear()
    {
        _kind = Engine.Sea.SeaWeather.Kind.None;
        _fading = false;
        InvalidateVisual();
    }

    private void Seed()
    {
        int n = _kind == Engine.Sea.SeaWeather.Kind.Rain ? RainDrops : SnowFlakes;
        _x = new int[n];
        _y = new int[n];
        _phase = new int[n];
        int w = Math.Max(40, _w), h = Math.Max(40, _h);
        for (int i = 0; i < n; i++)
        {
            if (_kind == Engine.Sea.SeaWeather.Kind.Rain)
            {
                _x[i] = _rng.Next(Math.Max(1, w / 20)) + i % 10 * (w / 10);
                _y[i] = -(i / 10) * 96 - _rng.Next(16) - (i & 1) * 64;
            }
            else
            {
                _x[i] = _rng.Next(Math.Max(1, w / 40)) + i % 20 * (w / 20);
                _y[i] = -(i / 10) * h / 5 - _rng.Next(16) - (i & 1) * 32;
                _phase[i] = _rng.Next(6);
            }
        }
    }

    /// <summary>0.1초 한 걸음(<c>0x0049ABB0</c>).</summary>
    public void Step()
    {
        if (!Busy) return;
        if (_fading && ++_fade >= (_kind == Engine.Sea.SeaWeather.Kind.Rain ? 18 : 15))
        {
            Clear();
            return;
        }
        if (!_fading) _ramp += 2;   // 원본 걸음 셈이 한 번에 둘씩 오른다

        int w = Math.Max(40, _w), h = Math.Max(40, _h);
        for (int i = 0; i < _x.Length; i++)
        {
            if (!Drawn(i)) continue;
            int m = i % 3;
            if (_kind == Engine.Sea.SeaWeather.Kind.Rain)
            {
                _x[i] -= 16 * (m + 1);
                _y[i] += 16 * (m + 2);
                if (_x[i] < 0) { _x[i] = w - _rng.Next(16); _y[i] += _rng.Next(16); }
                if (_y[i] >= h) { _x[i] += _rng.Next(16); _y[i] = -_rng.Next(16); }
            }
            else
            {
                int p = _phase[i] & 7;
                if (p <= 2) _x[i] += 5 * m - 14;
                else if (p <= 5) _x[i] += 14 - 5 * m;
                _y[i] += 8 * (4 - m);
                _phase[i]++;
                if (_y[i] >= h) _y[i] = -_rng.Next(16);
            }
        }
        InvalidateVisual();
    }

    /// <summary>이 걸음에 그 방울을 그리는지 — 들어설 때와 그칠 때 수를 줄인다.</summary>
    private bool Drawn(int i)
    {
        bool rain = _kind == Engine.Sea.SeaWeather.Kind.Rain;
        if (_fading)
            return rain
                ? _fade < 10 ? i % 2 == 1 : _fade < 15 ? i % 3 == 2 : i % 4 == 3
                : _fade < 10 ? i % 2 == 0 : i % 3 == 1;
        if (_ramp <= 5) return i % 3 == 0;
        if (_ramp <= 15) return rain ? i % 2 == 0 : i % 3 != 2;
        return true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!Busy) return;
        for (int i = 0; i < _x.Length; i++)
        {
            if (!Drawn(i)) continue;
            BitmapSource? img = _kind == Engine.Sea.SeaWeather.Kind.Rain ? _rain : _snow?[i % _snow.Length];
            if (img == null) continue;
            dc.DrawImage(img, new Rect(_x[i] * _scale, _y[i] * _scale,
                                       img.PixelWidth * _scale, img.PixelHeight * _scale));
        }
    }
}
