"""DHO(대항해시대 온라인) 배 모형을 그림으로 찍는다 — 편집기 「DHO 배 찍기」 창(DhoShipShotDialog)이 부른다.

    python -I dho_ship_render.py --gvo <DHO>/tools/gvo --model 1 --out <폴더> --sprites [--hd]
    python -I dho_ship_render.py --gvo <DHO>/tools/gvo --model 1 --out <폴더> --frames 60 [--width 240 --height 176]

모형 읽기는 DHO 저장소의 도구(tools/gvo — pack · model · texture · ships)를 그대로 빌려 쓴다. 그 도구는 설치된
GV Online 클라이언트(환경 변수 GVO_DIR, 없으면 기본 설치 자리)를 읽는다.

모형 번호 하나는 배 모형 표(0001\\0002.bin)의 한 줄이고, 그 줄의 선체 자원 하나와 돛대 · 돛 자원 다섯 벌씩을
색인(갈래 0x300)으로 묶음 항목에 잇는다 — DHO 의 ShipModel(Gfx, model) 과 같은 차례다.

  --sprites  게임 항해 지도의 8방향 그림 ship_0.png ~ ship_7.png(48x48). 번호는 0 북에서 반시계(0 북 · 2 서 · 4 남 · 6 동).
             --hd 면 같은 그림을 192x192 로도 ship_{n}_hd.png 에 남긴다. 여덟 장은 같은 배율이다.
  --frames   한 바퀴 도는 장면 frame_000.png … 을 frames 장. 배경색을 깔고 배율은 장마다 같다.
  --skip-slots  뺄 돛 자리(0 ~ 4). 한 돛대에 가로돛 · 삼각돛이 겹쳐 서는 모형이 있어 고르게 한다.
  --info     그 모형의 돛 자리 번호만 알린다.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image


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


def load_parts(gvo_dir, model_no, skip=()):
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
            if hull:
                parts.append((vb["pos"], vb.get("uv0"), tris, base, (150, 120, 80)))
            elif vb["fvf"] == 0x316:
                parts.append((vb["pos"], vb.get("uv0"), tris, sail, (230, 225, 210)))
            else:
                parts.append((vb["pos"], None, tris, None, (120, 95, 60)))

    part(row["hull_res"], True)
    for mast in range(5):
        # 돛 자리를 뺄 수 있다 — 한 돛대에 가로돛 · 삼각돛이 겹쳐 서는 모형(바사 따위)이 있다.
        if mast in skip:
            continue
        part(row["bone_res"][mast], False)
        part(row["sail_res"][mast], False)
    if not parts:
        raise SystemExit(f"모형 {model_no} 의 조각을 못 읽었습니다")
    return parts


LIGHT = np.array([0.4, 0.8, 0.45]) / np.linalg.norm([0.4, 0.8, 0.45])


def render(parts, yaw_deg, elev_deg, width, height, scale, center_y):
    """정사영으로 그린다. 좌표는 y 가 위, 뱃머리가 +z. yaw 0 이면 뱃머리가 화면 안쪽(북)이다. RGBA 를 돌려준다."""
    a, e = np.radians(yaw_deg), np.radians(elev_deg)

    def proj(p):
        x = p[:, 0] * np.cos(a) + p[:, 2] * np.sin(a)
        z = -p[:, 0] * np.sin(a) + p[:, 2] * np.cos(a)
        y = p[:, 1] * np.cos(e) - z * np.sin(e)
        d = -(p[:, 1] * np.sin(e) + z * np.cos(e))
        return np.stack([x, y, d], 1)

    img = np.zeros((height, width, 4), np.uint8)
    zb = np.full((height, width), -1e30)
    for pos, uv, tris, tex, col in parts:
        q = proj(pos)
        sx = q[:, 0] * scale + width / 2
        sy = height / 2 - (q[:, 1] - center_y * np.cos(e)) * scale
        for t in tris:
            p3 = pos[t]
            nrm = np.cross(p3[1] - p3[0], p3[2] - p3[0])
            ln = np.linalg.norm(nrm)
            if ln == 0:
                continue
            shade = 0.45 + 0.55 * abs(float(nrm @ LIGHT) / ln)
            xs, ys, zs = sx[t], sy[t], q[t, 2]
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
            if tex is not None and uv is not None:
                u = w0 * uv[t[0], 0] + w1 * uv[t[1], 0] + w2 * uv[t[2], 0]
                v = w0 * uv[t[0], 1] + w1 * uv[t[1], 1] + w2 * uv[t[2], 1]
                th, tw = tex.shape[:2]
                c = tex[np.floor(v * th).astype(int) % th, np.floor(u * tw).astype(int) % tw]
                m &= c[..., 3] > 40
                rgb = np.clip(c[..., :3].astype(float) * shade, 0, 255)
            else:
                rgb = np.broadcast_to(np.array(col, float) * shade, gx.shape + (3,))
            sub[m] = z[m]
            blk = img[y0:y1 + 1, x0:x1 + 1]
            blk[m, :3] = rgb[m].astype(np.uint8)
            blk[m, 3] = 255
    return Image.fromarray(img, "RGBA")


def bounds(parts):
    allp = np.concatenate([p[0] for p in parts])
    radius = float(np.linalg.norm(allp[:, [0, 2]], axis=1).max())
    return radius, float(allp[:, 1].min()), float(allp[:, 1].max())


def sprites(parts, out, elev, hd, work=384):
    radius, ymin, ymax = bounds(parts)
    half = max(radius, ymax - ymin)
    scale = (work / 2 - 4) / half
    # 게임 방향 i(0 북부터 반시계)는 렌더 각도 −45°·i 다 — yaw 90 이면 뱃머리가 오른쪽(동)이다.
    raw = []
    for i in range(8):
        raw.append(render(parts, -45 * i, elev, work, work, scale, (ymin + ymax) / 2))
        print(f"sprite {i + 1}/8", flush=True)
    box = None
    for im in raw:
        b = im.getbbox()
        if b is None:
            continue
        box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
    if box is None:
        raise SystemExit("그린 것이 없습니다")
    # 여덟 장을 같은 네모로 자른다 — 둘레를 조금 남겨 붙박이 배 그림(48 안에 30 남짓)과 크기를 맞춘다.
    cx, cy = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
    side = max(box[2] - box[0], box[3] - box[1]) * 1.3
    crop = (int(cx - side / 2), int(cy - side / 2 + side * 0.04), int(cx + side / 2), int(cy + side / 2 + side * 0.04))
    for i, im in enumerate(raw):
        cut = im.crop(crop)
        for size, name in [(48, f"ship_{i}.png")] + ([(192, f"ship_{i}_hd.png")] if hd else []):
            small = cut.resize((size, size), Image.LANCZOS)
            # 알파는 끊어 둔다 — 반투명 테두리가 바다 위에서 뿌옇게 번진다(붙박이 그림도 비침이 0 · 255 뿐이다).
            small.putalpha(small.getchannel("A").point(lambda v: 255 if v > 110 else 0))
            small.save(os.path.join(out, name))


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
    args = ap.parse_args()

    if args.info:
        print("slots " + ",".join(str(k) for k in sail_slots(model_row(args.gvo, args.model))), flush=True)
        return
    os.makedirs(args.out, exist_ok=True)
    skip = {int(k) for k in args.skip_slots.split(",") if k.strip()}
    parts = load_parts(args.gvo, args.model, skip)
    print(f"parts {len(parts)}", flush=True)
    if args.sprites:
        sprites(parts, args.out, args.sprite_elev, args.hd)
    if args.frames > 0:
        bg = tuple(int(c) for c in args.bg.split(","))
        frames(parts, args.out, args.frames, args.width, args.height, args.elev, bg, args.turns, args.start)
    print("done", flush=True)


if __name__ == "__main__":
    main()
