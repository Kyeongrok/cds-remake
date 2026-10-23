using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dho.Render;
using Vortice.Mathematics;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// DHO 배 모형을 D3D11 로 바로 그리는 미리보기 — 「DHO 배 찍기」 창 왼쪽 칸. 끌면 돌고 휠로 다가선다.
/// </summary>
/// <remarks>
/// 그리는 것은 DHO 저장소에서 옮겨 온 렌더러(<c>CdsHelper.Main/Dho</c> — <see cref="ShipModel"/> · <see cref="SceneRenderer"/>)다.
/// 질감 · 조명 · 장식이 DHO 화면 그대로 나오고, 값이 바뀔 때만 다시 그린다.
///
/// 각도는 찍기 스크립트(<c>tools/dho_ship_render.py</c>)와 같은 값으로 둔다 — <see cref="Yaw"/> −45°·i 가 게임 방향 i
/// (0 북에서 반시계), <see cref="Pitch"/> 는 내려다보는 각도다. 미리보기에서 찾은 각도를 그대로 찍기에 넘긴다.
/// 자식 창은 STATIC 이라 마우스가 WPF 로 그대로 넘어온다(<c>WorldMapHost</c> 와 같다).
/// </remarks>
internal sealed class DhoShipView : HwndHost
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
                                                 int x, int y, int w, int h,
                                                 IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private const int WsChild = 0x40000000, WsVisible = 0x10000000, WsClipSiblings = 0x04000000;

    private IntPtr _hwnd;
    private Gfx? _gfx;
    private SceneRenderer? _scene;
    private ShipModel? _ship;
    private string _shipKey = "";
    private bool _dirty = true;
    private double _yaw, _pitch = 20, _zoom = 1;

    /// <summary>모형을 못 세웠을 때의 까닭. 잘 섰으면 빈 글.</summary>
    public string Error { get; private set; } = "";

    /// <summary>끌어서 각도가 바뀌었다 — 창이 슬라이더를 맞춘다.</summary>
    public event Action? Turned;

    /// <summary>돌리기(도) — −45°·i 가 게임 방향 i 다. −180 ~ 180.</summary>
    public double Yaw
    {
        get => _yaw;
        set { _yaw = ((value + 180) % 360 + 360) % 360 - 180; _dirty = true; }
    }

    /// <summary>내려다보는 각도(도).</summary>
    public double Pitch
    {
        get => _pitch;
        set { _pitch = Math.Clamp(value, 0, 89); _dirty = true; }
    }

    /// <summary>모형과 돛 자리를 정한다. 같으면 그대로 둔다.</summary>
    public void SetShip(int model, IReadOnlySet<int> skip)
    {
        string key = $"{model}|{string.Join(",", skip.Order())}";
        if (key == _shipKey) return;
        _shipKey = key;
        _pending = (model, skip);
        _dirty = true;
    }

    private (int Model, IReadOnlySet<int> Skip)? _pending;

    public DhoShipView()
    {
        // 끌기 · 휠 — 자식 창이 마우스를 통과시키므로 WPF 쪽에서 받는다.
        Point last = default;
        bool dragging = false;
        MouseLeftButtonDown += (_, e) => { dragging = true; last = e.GetPosition(this); CaptureMouse(); e.Handled = true; };
        MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var now = e.GetPosition(this);
            Yaw = _yaw - (now.X - last.X) * 0.6;
            Pitch = _pitch + (now.Y - last.Y) * 0.3;
            last = now;
            Turned?.Invoke();
        };
        MouseLeftButtonUp += (_, _) => { dragging = false; ReleaseMouseCapture(); };
        MouseWheel += (_, e) => { _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 0.9 : 1.1), 0.3, 3); _dirty = true; };
        Cursor = Cursors.SizeAll;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hwnd = CreateWindowExW(0, "STATIC", null, WsChild | WsVisible | WsClipSiblings, 0, 0, 1, 1,
                                hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"미리보기 창을 만들지 못했습니다 (Win32 {Marshal.GetLastWin32Error()})");
        CompositionTarget.Rendering += OnFrame;
        return new HandleRef(this, _hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        CompositionTarget.Rendering -= OnFrame;
        _ship?.Dispose();
        _scene?.Dispose();
        _gfx?.Dispose();
        (_ship, _scene, _gfx) = (null, null, null);
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmPaint = 0x000F;
        if (msg == WmPaint) _dirty = true;   // 가려졌다 드러나면 한 번 다시 그린다
        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_hwnd == IntPtr.Zero || ActualWidth < 2 || ActualHeight < 2) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX), h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        try
        {
            if (_gfx == null)
            {
                _gfx = new Gfx(_hwnd, w, h) { ClearColor = new Color4(40 / 255f, 60 / 255f, 90 / 255f, 1) };
                _scene = new SceneRenderer(_gfx);
                _dirty = true;
            }
            else if (w != _gfx.Width || h != _gfx.Height)
            {
                _gfx.Resize(w, h);
                _dirty = true;
            }
            if (_pending is { } want)
            {
                _pending = null;
                _ship?.Dispose();
                _ship = null;
                try { _ship = new ShipModel(_gfx, want.Model, want.Model, want.Skip); Error = ""; }
                catch (Exception ex) { Error = $"모형 {want.Model} 을 못 세웠습니다 — {ex.Message}"; }
            }
            if (!_dirty) return;
            _dirty = false;
            Draw();
        }
        catch (Exception ex)
        {
            Error = $"그리지 못했습니다 — {ex.Message}";
            _ship?.Dispose();
            _scene?.Dispose();
            _gfx?.Dispose();
            (_ship, _scene, _gfx) = (null, null, null);
            CompositionTarget.Rendering -= OnFrame;   // 장치가 안 서는 PC 면 매 프레임 다시 터지지 않게 멈춘다
        }
    }

    private void Draw()
    {
        if (_gfx == null || _scene == null) return;
        var noon = Sky.At(0.5);
        var frame = new FrameConstants
        {
            SunDirection = noon.LightDirection, SunColor = noon.LightColor, Ambient = noon.Ambient,
            HorizonColor = noon.Horizon, ZenithColor = noon.Zenith, WaterColor = noon.Water,
            SunAngle = MathF.PI / 2, FogDensity = 0,
        };
        if (_ship is not { } ship)
        {
            _gfx.Begin(frame);
            _gfx.Present();
            return;
        }

        // 배는 그 자리에서 돌리고 카메라는 +z 쪽 위에서 내려다본다. 이 렌더러에서는 돌리지 않으면 뱃머리가 화면 안쪽(북)을 본다 —
        // 돌리기 −45°·i 가 게임 방향 i(0 북 · 2 서 · 4 남 · 6 동)가 되게 맞춘 것이다(찍기 스크립트와 같은 값, 화면으로 확인).
        var centre = ship.Center;
        float pitch = (float)(_pitch * Math.PI / 180), spin = (float)(_yaw * Math.PI / 180);   // 이 렌더러는 화면 오른쪽이 모형의 −x 라 돌리기와 같은 쪽으로 돈다
        const float Fov = 0.35f;
        float reach = ship.Radius / MathF.Tan(Fov / 2) * 1.05f * (float)_zoom;
        var eye = centre + new Vector3(0, MathF.Sin(pitch), MathF.Cos(pitch)) * reach;
        var look = Matrix4x4.CreateLookAt(eye, centre, Vector3.UnitY)
                   * Matrix4x4.CreatePerspectiveFieldOfView(Fov, _gfx.Width / (float)_gfx.Height, reach * 0.05f, reach * 4);
        Matrix4x4.Invert(look, out var inverse);
        frame.ViewProjection = look;
        frame.InverseViewProjection = inverse;
        frame.CameraPosition = eye;

        _gfx.Begin(frame);
        _scene.BeginMeshes();
        ship.Draw(_scene, Matrix4x4.CreateTranslation(-centre) * Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateTranslation(centre));
        _gfx.Present();
    }
}
