"""DHO(대항해시대 온라인) 배 모형을 그림으로 찍는다 — 편집기 「DHO 배 찍기」 창(DhoShipShotDialog)이 부른다.

    python -I dho_ship_render.py --gvo <DHO>/tools/gvo --model 1 --out <폴더> --sprites [--hd]
    python -I dho_ship_render.py --gvo <DHO>/tools/gvo --model 1 --out <폴더> --frames 60 [--width 240 --height 176]
    python -I dho_ship_render.py --gvo <DHO>/tools/gvo --model 1 --out <폴더> --serve

모형 읽기는 DHO 저장소의 도구(tools/gvo — pack · model · texture · ships)를 그대로 빌려 쓴다. 그 도구는 설치된
GV Online 클라이언트(환경 변수 GVO_DIR, 없으면 기본 설치 자리)를 읽는다.

모형 번호 하나는 배 모형 표(0001\\0002.bin)의 한 줄이고, 그 줄의 선체 자원 하나와 돛대 · 돛 자원 다섯 벌씩을
색인(갈래 0x300)으로 묶음 항목에 잇는다 — DHO 의 ShipModel(Gfx, model) 과 같은 차례다.

  --sprites  게임 항해 지도의 8방향 그림 ship_0.png ~ ship_7.png(48x48). 번호는 0 북에서 반시계(0 북 · 2 서 · 4 남 · 6 동).
             --hd 면 같은 그림을 192x192 로도 ship_{n}_hd.png 에 남긴다. 여덟 장은 같은 배율이다.
  --frames   한 바퀴 도는 장면 frame_000.png … 을 frames 장. 배경색을 깔고 배율은 장마다 같다.
  --serve    모형을 한 번 읽어 두고 표준 입력으로 한 줄씩 받는다(편집기의 「돌려 보며 찍기」):
               view <yaw> <elev> <png>        미리보기 한 장(256x256, 배경 투명)
               shot <yaw> <elev> <방향> <hd>   그 각도로 방향 칸 ship_{방향}.png(와 hd 면 _hd)를 찍는다
             줄마다 'ok …' 또는 'error …' 한 줄로 답한다. 배율은 --sprites 와 같다(여덟 표준 각도로 잰 네모).
  --skip-slots  뺄 돛 자리(0 ~ 4). 한 돛대에 가로돛 · 삼각돛이 겹쳐 서는 모형이 있어 고르게 한다.
  --info     그 모형의 돛 자리 번호만 알린다.
  --sail-turn · --billow  돛을 돛대 둘레로 틀고(기본 35°, 그림은 각도마다 왼쪽 · 오른쪽 가운데 돛이 넓게 보이는 쪽) 돛천을 부풀린다(기본 0.15) — 앞뒤에서 봐도 돛이 납작하지 않게.

음영은 정점 법선을 보간해 칠하고 빛은 화면 기준 왼쪽 위 앞에서 온다 — 배가 어느 쪽을 보든 같은 쪽이 밝다.
작은 그림은 원본 배 그림처럼 가장자리 한 줄을 어둡게 둘러 바다 위에서 떠 보이게 한다.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image, ImageEnhance


def model_row(gvo_dir, model_no):
    sys.path.insert(0, gvo_dir)
    import ships
    table = ships.model_table()
    if model_no not in table:
        raise SystemExit(f"모형 {model_no} 이 모형 표에 없습니다")
    return table[model_no]


def sail_slots(row):
    """돛 자리 번호들 — 모형 줄의 돛대 · 돛 다섯 벌 가운데 무엇이든 든 자리."""
    return [k for k in range(5) if row["bone_res"][k] != 0xFFFF or row["sail_res"][k] != 0xFFFF]


def load_parts(gvo_dir, model_no, skip=(), sail_turn=0.0, billow=0.0):
    """[(정점, 법선|None, UV|None, 삼각형, 텍스처|None, 빛깔)] — 선체 · 돛대 · 돛 조각.

    sail_turn 이면 돛 자원(활대 + 돛천)을 가장 가까운 돛대 축 둘레로 그 각도만큼 튼다 — 삼각돛은 배의 중심선과 나란해서
    앞뒤에서 보면 선 하나로 납작했다. billow 면 돛천 가운데를 돛 크기의 그 몫만큼 불룩하게 해 곡면 음영이 서게 한다.
    """
    sys.path.insert(0, gvo_dir)
    import texture as T
    import ships
    from model import Model
    from pack import Pack

    row = model_row(gvo_dir, model_no)
    index = ships.resource_index()

    base = T.decode(T.load(bytes(Pack("0001/sh0004.bin").entry(0)))[0])
    sail = T.decode(T.load(bytes(Pack("0001/sa0000.bin").entry(0)))[0])

    parts = []
    groups = []      # 조각마다 (돛 자원 번호 | None, 돛천인지)

    def part(resource, hull):
        if resource == 0xFFFF or (0x300, resource) not in index:
            return
        pack, entry = index[(0x300, resource)]
        data = bytes(Pack(pack).entry(entry))
        if data[:4] != b"XKMD":
            return
        m = Model(data)
        for s in m.subsets:
            vb = m.vbs[s["vb"]]
            if "pos" not in vb:
                continue
            tris = m.triangles(s)
            nrm = vb.get("normal")
            pos = np.array(vb["pos"], float)
            if hull:
                parts.append((pos, nrm, vb.get("uv0"), tris, base, (150, 120, 80)))
                groups.append((None, False))
            elif vb["fvf"] == 0x316:
                parts.append((pos, nrm, vb.get("uv0"), tris, sail, SAIL_COLOR))
                groups.append((resource, True))
            else:
                parts.append((pos, nrm, None, tris, None, (120, 95, 60)))
                groups.append((resource, False))

    part(row["hull_res"], True)
    for mast in range(5):
        # 돛 자리를 뺄 수 있다 — 한 돛대에 가로돛 · 삼각돛이 겹쳐 서는 모형(바사 따위)이 있다.
        if mast in skip:
            continue
        part(row["bone_res"][mast], False)
        part(row["sail_res"][mast], False)
    if not parts:
        raise SystemExit(f"모형 {model_no} 의 조각을 못 읽었습니다")
    if sail_turn or billow:
        parts = shape_sails(parts, groups, sail_turn, billow)
    return parts


def vertex_normals(pos, tris):
    """삼각형 넓이로 가중한 정점 법선."""
    n = np.zeros_like(pos)
    t = np.asarray(tris)
    face = np.cross(pos[t[:, 1]] - pos[t[:, 0]], pos[t[:, 2]] - pos[t[:, 0]])
    for k in range(3):
        np.add.at(n, t[:, k], face)
    return n / np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)


def mast_axes(hull):
    """선체에서 높이 솟은 정점(돛대)을 z 로 무리 지어 돛대마다 (x, z) 하나."""
    ymax = hull[:, 1].max()
    tall = hull[hull[:, 1] > 0.55 * ymax]
    if len(tall) == 0:
        return [(0.0, 0.0)]
    order = tall[np.argsort(tall[:, 2])]
    axes, start = [], 0
    for i in range(1, len(order) + 1):
        if i == len(order) or order[i, 2] - order[i - 1, 2] > 250:
            chunk = order[start:i]
            axes.append((float(np.median(chunk[:, 0])), float(np.median(chunk[:, 2]))))
            start = i
    return axes


def shape_sails(parts, groups, turn, billow):
    hull = parts[0][0]
    axes = mast_axes(hull)
    out = list(parts)
    resources = {g for g, _ in groups if g is not None}
    for res in resources:
        idx = [i for i, (g, _) in enumerate(groups) if g == res]
        pts = np.concatenate([out[i][0] for i in idx])
        cz = float(pts[:, 2].mean())
        ax, az = min(axes, key=lambda m: abs(m[1] - cz))
        a = np.radians(turn)
        rot = np.array([[np.cos(a), 0, np.sin(a)], [0, 1, 0], [-np.sin(a), 0, np.cos(a)]])
        for i in idx:
            pos, nrm, uv, tris, tex, col = out[i]
            p = pos - [ax, 0, az]
            p = p @ rot.T + [ax, 0, az]
            n = None if nrm is None else np.asarray(nrm) @ rot.T
            if groups[i][1] and billow and uv is not None and len(p) >= 3:
                # 돛천 평면의 법선(가장 작은 특이 벡터) 쪽으로, UV 가운데가 가장 불룩하게 민다.
                centered = p - p.mean(0)
                normal = np.linalg.svd(centered, full_matrices=False)[2][2]
                lo, hi = uv.min(0), uv.max(0)
                un = (uv - lo) / np.maximum(hi - lo, 1e-9)
                size = float(np.ptp(centered @ np.linalg.svd(centered, full_matrices=False)[2][:2].T, axis=0).max())
                bulge = billow * size * np.sin(np.pi * un[:, 0]) * np.sin(np.pi * un[:, 1])
                p = p + bulge[:, None] * normal
                n = vertex_normals(p, tris)
            out[i] = (p, n, uv, tris, tex, col)
    return out


# 빛 — 화면 기준(오른쪽 · 위 · 카메라 쪽). 왼쪽 위 앞에서 비춘다.
LIGHT_VIEW = np.array([-0.45, 0.75, 0.5]) / np.linalg.norm([-0.45, 0.75, 0.5])
AMBIENT, DIFFUSE = 0.42, 0.78


def render(parts, yaw_deg, elev_deg, width, height, scale, center_y):
    """정사영으로 그린다. 좌표는 y 가 위, 뱃머리가 +z. yaw 0 이면 뱃머리가 화면 안쪽(북)이다. RGBA 를 돌려준다."""
    a, e = np.radians(yaw_deg), np.radians(elev_deg)
    # 화면 축을 모형 좌표로 — 오른쪽 r, 위 u, 카메라 쪽 v(내려다본다: 먼 쪽이 화면 위, 높은 데 · 가까운 데가 앞)
    r = np.array([np.cos(a), 0.0, np.sin(a)])
    u = np.array([-np.sin(a) * np.sin(e), np.cos(e), np.cos(a) * np.sin(e)])
    v = np.array([np.sin(a) * np.cos(e), np.sin(e), -np.cos(a) * np.cos(e)])
    light = LIGHT_VIEW[0] * r + LIGHT_VIEW[1] * u + LIGHT_VIEW[2] * v

    img = np.zeros((height, width, 4), np.uint8)
    zb = np.full((height, width), -1e30)
    for pos, nrm, uv, tris, tex, col in parts:
        sx = (pos @ r) * scale + width / 2
        sy = height / 2 - (pos @ u - center_y * np.cos(e)) * scale
        depth = pos @ v
        for t in tris:
            p3 = pos[t]
            face = np.cross(p3[1] - p3[0], p3[2] - p3[0])
            fl = np.linalg.norm(face)
            if fl == 0:
                continue
            face = face / fl
            xs, ys, zs = sx[t], sy[t], depth[t]
            x0, x1 = int(max(0, np.floor(xs.min()))), int(min(width - 1, np.ceil(xs.max())))
            y0, y1 = int(max(0, np.floor(ys.min()))), int(min(height - 1, np.ceil(ys.max())))
            if x1 < x0 or y1 < y0:
                continue
            gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            dd = (ys[1] - ys[2]) * (xs[0] - xs[2]) + (xs[2] - xs[1]) * (ys[0] - ys[2])
            if abs(dd) < 1e-9:
                continue
            w0 = ((ys[1] - ys[2]) * (gx - xs[2]) + (xs[2] - xs[1]) * (gy - ys[2])) / dd
            w1 = ((ys[2] - ys[0]) * (gx - xs[2]) + (xs[0] - xs[2]) * (gy - ys[2])) / dd
            w2 = 1 - w0 - w1
            m = (w0 >= -1e-4) & (w1 >= -1e-4) & (w2 >= -1e-4)
            z = w0 * zs[0] + w1 * zs[1] + w2 * zs[2]
            sub = zb[y0:y1 + 1, x0:x1 + 1]
            m &= z > sub
            if not m.any():
                continue
            # 정점 법선을 보간한다 — 없으면 면 법선. 카메라에서 등진 쪽이면 뒤집어 앞면으로 친다(돛 · 얇은 판은 양면이다).
            if nrm is not None:
                n = w0[..., None] * nrm[t[0]] + w1[..., None] * nrm[t[1]] + w2[..., None] * nrm[t[2]]
                n /= np.maximum(np.linalg.norm(n, axis=-1, keepdims=True), 1e-9)
            else:
                n = np.broadcast_to(face, gx.shape + (3,)).copy()
            n *= np.where((n @ v) < 0, -1.0, 1.0)[..., None]
            shade = AMBIENT + DIFFUSE * np.clip(n @ light, 0, 1)
            if tex is not None and uv is not None:
                uu = w0 * uv[t[0], 0] + w1 * uv[t[1], 0] + w2 * uv[t[2], 0]
                vv = w0 * uv[t[0], 1] + w1 * uv[t[1], 1] + w2 * uv[t[2], 1]
                th, tw = tex.shape[:2]
                c = tex[np.floor(vv * th).astype(int) % th, np.floor(uu * tw).astype(int) % tw]
                m &= c[..., 3] > 40
                base = c[..., :3].astype(float)
            else:
                base = np.broadcast_to(np.array(col, float), gx.shape + (3,))
            rgb = np.clip(base * shade[..., None], 0, 255)
            sub[m] = z[m]
            blk = img[y0:y1 + 1, x0:x1 + 1]
            blk[m, :3] = rgb[m].astype(np.uint8)
            blk[m, 3] = 255
    return Image.fromarray(img, "RGBA")


def bounds(parts):
    allp = np.concatenate([p[0] for p in parts])
    radius = float(np.linalg.norm(allp[:, [0, 2]], axis=1).max())
    return radius, float(allp[:, 1].min()), float(allp[:, 1].max())


def finish(cut, size):
    """작업 그림을 한 장으로 줄인다 — 알파는 끊고(0 · 255) 가장자리 한 줄을 어둡게 둘러 볼륨을 세운다."""
    small = cut.resize((size, size), Image.LANCZOS)
    small = ImageEnhance.Contrast(small.convert("RGB")).enhance(1.12).convert("RGBA")
    alpha = np.array(cut.resize((size, size), Image.LANCZOS).getchannel("A")) > 110
    px = np.array(small)
    px[..., 3] = np.where(alpha, 255, 0)
    # 가장자리 — 속인데 네 이웃 가운데 하나라도 바깥인 칸. 큰 그림은 두께를 크기에 맞춘다.
    width = max(1, size // 64)
    inner = alpha.copy()
    for _ in range(width):
        pad = np.pad(inner, 1)
        inner = inner & pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:]
    edge = alpha & ~inner
    px[edge, :3] = (px[edge, :3].astype(float) * 0.45).astype(np.uint8)
    return Image.fromarray(px, "RGBA")


SAIL_COLOR = (230, 225, 210)   # 돛천 조각의 빛깔 — 돛천을 가려낼 때 쓴다


def sail_area(parts, yaw, elev):
    """그 각도에서 화면에 비친 돛천 넓이 — 돛을 어느 쪽으로 틀지 고를 때 쓴다."""
    a, e = np.radians(yaw), np.radians(elev)
    r = np.array([np.cos(a), 0.0, np.sin(a)])
    u = np.array([-np.sin(a) * np.sin(e), np.cos(e), np.cos(a) * np.sin(e)])
    total = 0.0
    for pos, _, _, tris, _, col in parts:
        if col != SAIL_COLOR:
            continue
        t = np.asarray(tris)
        x, y = pos @ r, pos @ u
        total += float(np.abs((x[t[:, 1]] - x[t[:, 0]]) * (y[t[:, 2]] - y[t[:, 0]])
                              - (x[t[:, 2]] - x[t[:, 0]]) * (y[t[:, 1]] - y[t[:, 0]])).sum() / 2)
    return total


class Shooter:
    """모형 하나를 같은 배율로 찍는다 — 표준 여덟 각도로 잰 네모로 잘라 어느 각도로 찍어도 크기가 같다.

    돛을 양쪽으로 튼 두 벌(variants)을 받으면 각도마다 돛천이 화면에 더 넓게 비치는 쪽을 쓴다 — 한쪽으로만 틀면
    앞뒤는 나아져도 대각선 둘에서 돛이 옆선이 되어 납작했다. 삼각돛은 바람에 따라 양쪽으로 넘기므로 어색하지 않다.
    """

    def __init__(self, variants, elev, work=384):
        self.variants, self.elev, self.work = variants, elev, work
        parts = variants[0]
        radius, self.ymin, self.ymax = bounds(parts)
        self.scale = (work / 2 - 4) / max(radius, self.ymax - self.ymin)
        self.raw = [self.render(-45 * i, elev) for i in range(8)]
        box = None
        for im in self.raw:
            b = im.getbbox()
            if b is not None:
                box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
        if box is None:
            raise SystemExit("그린 것이 없습니다")
        # 여덟 장을 같은 네모로 자른다 — 둘레를 조금 남겨 붙박이 배 그림(48 안에 30 남짓)과 크기를 맞춘다.
        cx, cy = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
        side = max(box[2] - box[0], box[3] - box[1]) * 1.3
        self.crop = (int(cx - side / 2), int(cy - side / 2 + side * 0.04), int(cx + side / 2), int(cy + side / 2 + side * 0.04))

    def render(self, yaw, elev):
        parts = max(self.variants, key=lambda v: sail_area(v, yaw, elev))
        return render(parts, yaw, elev, self.work, self.work, self.scale, (self.ymin + self.ymax) / 2)

    def save(self, im, out, direction, hd):
        cut = im.crop(self.crop)
        finish(cut, 48).save(os.path.join(out, f"ship_{direction}.png"))
        if hd:
            finish(cut, 192).save(os.path.join(out, f"ship_{direction}_hd.png"))


def sprites(variants, out, elev, hd):
    # 게임 방향 i(0 북부터 반시계)는 렌더 각도 −45°·i 다 — yaw 90 이면 뱃머리가 오른쪽(동)이다.
    shooter = Shooter(variants, elev)
    for i, im in enumerate(shooter.raw):
        shooter.save(im, out, i, hd)
        print(f"sprite {i + 1}/8", flush=True)


def serve(variants, out, elev):
    shooter = Shooter(variants, elev)
    print("ready", flush=True)
    for line in sys.stdin:
        words = line.split()
        if not words:
            continue
        try:
            if words[0] == "view":
                yaw, el, path = float(words[1]), float(words[2]), " ".join(words[3:])
                finish(shooter.render(yaw, el).crop(shooter.crop), 256).save(path)
                print(f"ok {path}", flush=True)
            elif words[0] == "shot":
                yaw, el, direction, hd = float(words[1]), float(words[2]), int(words[3]), words[4] == "1"
                shooter.save(shooter.render(yaw, el), out, direction, hd)
                print(f"ok shot {direction}", flush=True)
            elif words[0] == "quit":
                break
            else:
                print(f"error 모르는 명령 {words[0]}", flush=True)
        except Exception as ex:  # 한 줄이 틀려도 다음 줄을 받는다
            print(f"error {ex}", flush=True)


def frames(parts, out, count, width, height, elev, bg, turns, start, super_sample=2):
    radius, ymin, ymax = bounds(parts)
    w, h = width * super_sample, height * super_sample
    # 어느 쪽을 봐도 들어오게 — 가로는 지름, 세로는 높이(내려다보면 지름의 sin 만큼 더 차지한다)
    span_y = (ymax - ymin) * np.cos(np.radians(elev)) + 2 * radius * np.sin(np.radians(elev))
    scale = min(w * 0.9 / (2 * radius), h * 0.9 / span_y)
    for k in range(count):
        yaw = start + 360.0 * turns * k / count
        ship = render(parts, yaw, elev, w, h, scale, (ymin + ymax) / 2)
        canvas = Image.new("RGBA", (w, h), bg + (255,))
        canvas.alpha_composite(ship)
        canvas.convert("RGB").resize((width, height), Image.LANCZOS).save(os.path.join(out, f"frame_{k:03d}.png"))
        print(f"frame {k + 1}/{count}", flush=True)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gvo", required=True, help="DHO 저장소의 tools/gvo 폴더")
    ap.add_argument("--model", type=int, required=True, help="배 모형 번호(DHO 배 표의 Model)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--sprites", action="store_true")
    ap.add_argument("--hd", action="store_true")
    ap.add_argument("--sprite-elev", type=float, default=20)
    ap.add_argument("--frames", type=int, default=0)
    ap.add_argument("--width", type=int, default=240)
    ap.add_argument("--height", type=int, default=176)
    ap.add_argument("--elev", type=float, default=15)
    ap.add_argument("--bg", default="40,60,90")
    ap.add_argument("--turns", type=float, default=1.0)
    ap.add_argument("--start", type=float, default=-90, help="첫 장의 각도 — −90 이면 뱃머리가 왼쪽")
    ap.add_argument("--skip-slots", default="", help="뺄 돛 자리 번호(0 ~ 4, 쉼표로)")
    ap.add_argument("--info", action="store_true", help="돛 자리 번호만 'slots 0,1' 로 알린다")
    ap.add_argument("--serve", action="store_true", help="표준 입력으로 view · shot 을 받는다")
    ap.add_argument("--sail-turn", type=float, default=35, help="돛을 돛대 둘레로 트는 각도(도)")
    ap.add_argument("--billow", type=float, default=0.15, help="돛천을 부풀리는 몫(돛 크기의 몫)")
    args = ap.parse_args()

    if args.info:
        print("slots " + ",".join(str(k) for k in sail_slots(model_row(args.gvo, args.model))), flush=True)
        return
    os.makedirs(args.out, exist_ok=True)
    skip = {int(k) for k in args.skip_slots.split(",") if k.strip()}
    parts = load_parts(args.gvo, args.model, skip, args.sail_turn, args.billow)
    print(f"parts {len(parts)}", flush=True)
    # 그림은 돛을 양쪽으로 튼 두 벌에서 각도마다 고른다. 동영상은 돌아가는 동안 돛이 뒤집히지 않게 한 벌만 쓴다.
    variants = [parts]
    if args.sail_turn and (args.sprites or args.serve):
        variants.append(load_parts(args.gvo, args.model, skip, -args.sail_turn, args.billow))
    if args.serve:
        serve(variants, args.out, args.sprite_elev)
        return
    if args.sprites:
        sprites(variants, args.out, args.sprite_elev, args.hd)
    if args.frames > 0:
        bg = tuple(int(c) for c in args.bg.split(","))
        frames(parts, args.out, args.frames, args.width, args.height, args.elev, bg, args.turns, args.start)
    print("done", flush=True)


if __name__ == "__main__":
    main()
