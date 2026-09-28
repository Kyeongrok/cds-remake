using System.Windows;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

internal sealed partial class EventAnimationPopup
{
    /// <summary>
    /// 16·17 갈매기 — 세 마리가 날아들고 앉았다가 떼 지어 떠난다(객체 <c>0x00585970</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   0x004186C0  파트 0x14(96 x 3456 = 96x96 서른여섯 장), 팔레트 0x2E
    ///   0x004186E0  처음 불릴 때 읽고 첫자리(0x004184A0)를 놓은 뒤 소리 0x31(되풀이)
    ///   0x00416E70  한 걸음 — 세 마리를 상태(+0x24)대로 그리고 옮긴다. 상태 둘이 한 손이다(0x00416EF4)
    ///                 0·1 날아든다(0x00416F00)   2·3 떠난다(0x00417450)   4·5 · 6·7 앉아 있다(0x00417370 · 0x00417290)
    ///                 8·9 떼 지어 지나간다(0x004179F0)   10·11 18번 장면 것 — 부르는 곳이 없어 안 옮긴다
    ///               세 마리가 모두 -1 이면 끝(0x00418250) — 0x0049B287 이 치우고 소리 0x31 을 끈다
    ///   상태가 짝수면 오른쪽을 본다(0x004182D0 — 표 0x00418318)
    /// </code>
    /// 16 은 한 마리가 위에서 내려앉고 나머지는 이미 앉아 있다(0x004184A0). 앉은 새가 다 날면
    /// (날아드는 새가 없을 때, 0x00418130) 모두 떠나고, 셋이 다 화면 밖(상태 12)이면 떼로 돌아와
    /// 지나간다(0x004181E0). 17 은 처음부터 떼다. 자리 셈은 게임 점 정수 그대로다.
    /// </remarks>
    private sealed class GullScene(int scene) : Scene
    {
        private const int Side = 0x60, Birds = 3, Away = 12;

        private BitmapSource[] _art = [];
        private readonly int[] _x = new int[Birds], _y = new int[Birds], _n = new int[Birds], _s = new int[Birds];
        private int _flip;                 // [+0x38] — 떠나는 쪽을 가른다
        private int _w, _h, _c;
        private Random _rng = new();

        public override int SoundPart => 0x31 - WaveBank.FirstSoundId;
        public override bool EndsOnClick => scene == EventAnimation.Gulls;

        public override bool Load(EventAnimation anims)
        {
            _art = Frames(anims, 0x14, Side, Side, 0x2E) ?? [];
            return _art.Length >= 0x24;
        }

        public override void Start(int w, int h, Point? ship, Random rng)
        {
            _w = w; _h = h; _rng = rng;
            if (scene == EventAnimation.Gulls) Depart();
            else
            {
                // 17 — 셋이 한 방향으로 지나간다(0x0041859B).
                int s = R(2) == 0 ? 9 : 8;
                for (int i = 0; i < Birds; i++) _s[i] = s;
                Flock();
            }
            Array.Clear(_n);                                   // 0x00418676
        }

        public override bool Step(int count, List<Draw> draws)
        {
            _c = count;
            for (int i = 0; i < Birds; i++)
            {
                switch (_s[i])
                {
                    case 0 or 1: Approach(i, draws); break;
                    case 2 or 3: Leave(i, draws); break;
                    case 4 or 5: Sit(i, 0x20, draws); break;
                    case 6 or 7: Sit(i, 0x1C, draws); break;
                    case 8 or 9: Pass(i, draws); break;
                }
            }
            return _s.All(s => s == -1);
        }

        private int R(int n) => _rng.Next(Math.Max(1, n));

        /// <summary>오른쪽을 보는가 — 짝수 상태(<c>0x004182D0</c>).</summary>
        private bool Even(int i) => _s[i] is 0 or 2 or 4 or 6 or 8 or 10;

        /// <summary>떠나는 새가 제 쪽으로 가는가(<c>0x00418290</c>).</summary>
        private bool Toward(int i) => _flip == 0 ? Even(i) : !Even(i);

        private void Put(int i, int f, List<Draw> draws)
        {
            if (f >= 0 && f < _art.Length) draws.Add(new Draw(_art[f], _x[i], _y[i]));
        }

        // ── 첫자리 ───────────────────────────────────────────────────────────

        /// <summary>16 — 한 마리는 날아들고 둘은 앉아 있다(<c>0x004184A0</c> · <c>0x00418330</c>).</summary>
        private void Depart()
        {
            int m = R(3);
            _s[m] = R(2) == 0 ? 1 : 0;
            for (int k = 0; k < Birds; k++)
            {
                if (k == m) continue;
                bool any = Even(m) ? k != 2 : k != 0;
                _s[k] = any
                    ? R(4) switch { 0 => 6, 1 => 7, 2 => 4, _ => 5 }
                    : Even(m) ? (R(2) == 0 ? 4 : 6) : (R(2) == 0 ? 5 : 7);
            }
            _flip = Even(m) ? 0 : 1;

            for (int k = 0; k < Birds; k++)
            {
                if (_s[k] == 0) { _x[k] = _w * 9 / 10 + 0xC0 * k; _y[k] = -0x60; }
                else if (_s[k] == 1) { _x[k] = _w / 10 + 0xC0 * k - 0x180; _y[k] = -0x60; }
                else { _x[k] = _w / 2 - 0x90 + R(0x30) + 0x120 * k / 2; _y[k] = _h - 0x4F; }
            }
        }

        /// <summary>떼의 첫자리 — 화면 끝에서 0x90 씩 벌려 선다(<c>0x004183E0</c>).</summary>
        private void Flock()
        {
            int x = Even(0) ? _w : 0, t = _h / 10, step = Even(0) ? 0x90 : -0x90;
            for (int k = 0; k < Birds; k++)
            {
                _x[k] = x;
                int e = (R(3) == 0 ? -1 : 1) * R(k + 1) + 2 * k;
                _y[k] = t + 12 * e;
                x += step;
            }
        }

        // ── 0·1 날아든다 ─────────────────────────────────────────────────────

        private void Approach(int i, List<Draw> draws)
        {
            Put(i, ApproachFrame(i), draws);
            ApproachMove(i);
        }

        /// <summary><c>0x00416F60</c> — 높이 날 때는 날갯짓, 내려앉을 참에는 발을 내린다.</summary>
        private int ApproachFrame(int i)
        {
            int y = _y[i];
            if ((_h - 0x60) / 10 * 5 >= y) return Even(i) ? 0 : 5;
            if (_h - 0x4F > y)
            {
                int f = (_c + i) % 4 < 2 ? 0x10 : 0x11;
                return _s[i] == 1 ? f + 4 : f;
            }
            int g = 0x10 + _n[i] % 4;
            if (!Even(i)) g += 4;
            _n[i]++;
            return g;
        }

        /// <summary><c>0x00417030</c> — 아래로 갈수록 느려지다 바닥(H − 0x4F)에 앉는다.</summary>
        private void ApproachMove(int i)
        {
            int a = _w / 2 / 20, b = (_h - 0x60) / 20, dir = Even(i) ? 1 : -1;
            int jx = ((i + R(2)) & 1) != 0 && R(2) != 0 ? a / 2 : 0;
            int jy = ((i + R(2)) & 1) != 0 && R(2) != 0 ? b / 2 : 0;
            int y = _y[i];
            if (y + 0x60 < 0) { _x[i] -= a / 4 * dir; _y[i] += b / 2; return; }
            if (6 * b > y) { _x[i] -= (jx + a * 3 / 2) * dir; _y[i] += b + jy; }
            else if (10 * b > y) { _x[i] -= (a + jx) * dir; _y[i] += b + jy; }
            else if (12 * b > y) { _x[i] -= (jx + a * 3 / 4) * dir; _y[i] += b + jy; }
            else if (16 * b > y) { _x[i] -= a / 2 * dir; _y[i] += b * 3 / 4; }
            else if (20 * b > y)
            {
                _x[i] -= a / 4 * dir;
                _y[i] += b / 2;
                if (_y[i] >= _h - 0x4F) _y[i] = _h - 0x4F;
            }
            else
            {
                _y[i] = _h - 0x4F;
                if (_n[i] >= 4) { _s[i] = Even(i) ? 6 : 7; _n[i] = 0; }   // 0x00417269 — 앉는다
            }
        }

        // ── 4·5 · 6·7 앉아 있다 ─────────────────────────────────────────────

        /// <summary>
        /// <c>0x004173C0</c>(4·5, 장 0x20) · <c>0x004172E0</c>(6·7, 장 0x1C) — 걸음 8 에 셋에 하나, 13 이면 반드시
        /// 날갯짓 장(0x13 · 0x17)을 내고 자세를 바꾸거나 모두 날아오른다(<c>0x00418130</c>).
        /// </summary>
        private void Sit(int i, int pose, List<Draw> draws)
        {
            int n = _n[i], f;
            if (n < 8 || n > 8 && n < 13)
            {
                f = n % 4 < 2 ? pose : pose + 1;
                if (!Even(i)) f += 2;
            }
            else
            {
                f = Even(i) ? 0x13 : 0x17;
                if (n >= 13 || R(3) == 0) { Stir(i); _n[i] = -1; }
            }
            _n[i]++;
            Put(i, f, draws);
        }

        /// <summary><c>0x00418130</c> — 날아드는 새가 없으면 모두 떠나고, 있으면 이 새만 자세를 바꾼다.</summary>
        private void Stir(int i)
        {
            if (!_s.Any(s => s is 0 or 1) && _s[i] != -1)
                for (int k = 0; k < Birds; k++) _s[k] = Even(k) ? 2 : 3;
            switch (_s[i])
            {
                case 4 or 5: _s[i] = Even(i) ? 6 : 7; break;
                case 6 or 7: _s[i] = Even(i) ? 4 : 5; break;
            }
        }

        // ── 2·3 떠난다 ───────────────────────────────────────────────────────

        private void Leave(int i, List<Draw> draws)
        {
            Put(i, Toward(i) ? LeaveFrame(i) : TurnFrame(i), draws);
            if (Toward(i)) LeaveMove(i); else TurnMove(i);
        }

        /// <summary><c>0x004174E0</c>.</summary>
        private int LeaveFrame(int i)
        {
            int t = (_h - 0x60) / 10, y = _y[i];
            if (8 * t <= y) return Flap((_c + i) % 2 == 0 ? 0x18 : 0x19, i);
            if (5 * t < y) return Flap((_c + i) % 4 < 2 ? 0x18 : 0x19, i);
            return Even(i) ? 0 : 5;
        }

        /// <summary><c>0x00417590</c> — 돌아서 날다가 높이 오르면 뒤로 돈다(상태 짝을 바꾼다).</summary>
        private int TurnFrame(int i)
        {
            int t = (_h - 0x60) / 10, y = _y[i];
            if (8 * t <= y) return Flap((_c + i) % 2 == 0 ? 0x18 : 0x19, i);
            if (6 * t < y) return Even(i) ? 0x0B : 0x0E;
            if (4 * t < y) return 0x0A;
            if (2 * t < y) return Even(i) ? 0x0E : 0x0B;
            _s[i] = Even(i) ? 3 : 2;
            return Even(i) ? 0 : 5;
        }

        private int Flap(int f, int i) => Even(i) ? f : f + 2;

        /// <summary><c>0x00417680</c>.</summary>
        private void LeaveMove(int i)
        {
            int a = _w / 2 / 20, b = (_h - 0x60) / 20, dir = Even(i) ? 1 : -1;
            int jx = (i & 1) != 0 && R(2) != 0 ? a : 0;
            int y = _y[i];
            if (16 * b <= y) { _x[i] -= a / 2 * dir; _y[i] -= b * 3 / 2; }
            else if (12 * b < y) { _x[i] -= (a / 2 + jx) * dir; _y[i] -= b * 3 / 2; }
            else if (8 * b < y) { _x[i] -= (a + jx) * dir; _y[i] -= b * 3 / 2; }
            else if (4 * b < y) { _x[i] -= (a * 3 / 2 + jx) * dir; _y[i] -= b * 3 / 2; }
            else { _x[i] -= (jx + 2 * a) * dir; _y[i] -= b; }
            Gone(i, a, b);
        }

        /// <summary><c>0x00417840</c>.</summary>
        private void TurnMove(int i)
        {
            int a = _w / 2 / 20, b = (_h - 0x60) / 20, dir = Even(i) ? 1 : -1;
            int jx = (i & 1) != 0 && R(2) != 0 ? a : 0;
            int y = _y[i];
            if (16 * b <= y) { _x[i] -= a / 2 * dir; _y[i] -= b * 3 / 2; }
            else if (12 * b < y) { _x[i] -= a / 2 * dir; _y[i] -= b; }
            else if (8 * b < y) { _x[i] += (14 * b < y ? -1 : 1) * (a / 4) * dir; _y[i] -= b; }
            else if (4 * b < y) { _x[i] += (jx + a / 2) * dir; _y[i] -= b * 3 / 2; }
            else { _x[i] += (jx + a * 3 / 2) * dir; _y[i] -= b; }
            Gone(i, a, b);
        }

        /// <summary>
        /// 화면 밖으로 나갔으면 — 16 은 상태 12 로 기다렸다가 셋이 다 나가면 떼로 돌아오고(<c>0x004181E0</c>),
        /// 그 밖은 사라진다(<c>0x004177EB</c>).
        /// </summary>
        private void Gone(int i, int a, int b)
        {
            if (_x[i] + a + 0x60 >= 0 && _y[i] + b + 0x60 >= 0 && _w + a + 0x60 >= _x[i]) return;
            if (scene != EventAnimation.Gulls) { _s[i] = -1; return; }

            _s[i] = Away;
            if (!_s.All(s => s == Away)) return;
            _flip = _flip == 0 ? 1 : 0;
            for (int k = 0; k < Birds; k++) _s[k] = _flip == 0 ? 8 : 9;
            Flock();
            for (int k = 0; k < Birds; k++) _x[k] += Even(k) ? _w / 2 : -(_w / 2);
        }

        // ── 8·9 떼 지어 지나간다 ─────────────────────────────────────────────

        /// <summary><c>0x004179F0</c> — 화면 밖에서는 그리지 않고 W/20 씩 다가온다.</summary>
        private void Pass(int i, List<Draw> draws)
        {
            int x = _x[i];
            if (x + 0x60 < 0 && _s[i] == 9) { _x[i] = x + _w / 20; return; }
            if (_w < x && _s[i] == 8) { _x[i] = x - _w / 20; return; }

            // 날갯짓 여덟 걸음(0x00417AB0) — 오른쪽이면 장 0~4, 왼쪽이면 5~9.
            int[] beat = [0, 1, 2, 1, 3, 0, 4, 3];
            Put(i, (Even(i) ? 0 : 5) + beat[(_c + i) % 8], draws);

            if (i % 3 == 1) PassWave(i); else PassGlide(i);
        }

        /// <summary><c>0x00417B80</c> — 여섯 걸음마다 한 번 크게 나가고, 그 사이는 오르내린다.</summary>
        private void PassGlide(int i)
        {
            int a = _w / 30, b = _h / 20, dir = Even(i) ? 1 : -1;
            int jx = (i != 0 ? a / 2 : 0) + R(a);
            if ((_c + i) % 6 == 0) _x[i] -= (a * 3 / 2 + jx) * dir;
            else
            {
                _x[i] -= (jx + a) * dir;
                _y[i] += (_c + i) / 6 % 2 != 0 ? -b : b;
            }
            Passed(i);
        }

        /// <summary><c>0x00417CA0</c> — 가운데 새는 높이에 따라 내려가며 지난다.</summary>
        private void PassWave(int i)
        {
            int a = _w / 30, b = _h / 20, dir = Even(i) ? 1 : -1, y = _y[i];
            if (6 * b > y) { _x[i] -= a * 3 / 2 * dir; _y[i] += b; }
            else if (10 * b > y) { _x[i] -= dir * a; _y[i] += b / 2; }
            else if (14 * b > y) { _x[i] -= a * 3 / 2 * dir; _y[i] += b; }
            else
            {
                _x[i] -= a * 3 / 2 * dir;
                if (_x[i] < 4 * a || _x[i] > 16 * a) _x[i] -= dir * a;
            }
            if (R(3) == 0) _x[i] -= a / 2 * dir;
            Passed(i);
        }

        /// <summary>가는 쪽 끝을 넘으면 사라진다(<c>0x00417C55</c>).</summary>
        private void Passed(int i)
        {
            if (_x[i] + 0x60 < 0 && Even(i) || _x[i] > _w && !Even(i)) _s[i] = -1;
        }
    }
}
