# -*- coding: utf-8 -*-
"""
MISC.CDS 에서 말(육상 이동) 걷는 그림 서른두 장을 떠서 asset/horse/horse_walk.png 로 만든다.

배 그림과 달리 이것은 게임을 켜지 않아도 된다. 예전에는 실행 중인 CDS_95 의 아틀라스
(0x6092D0)를 떠 왔는데(tools/extract_ship_sprites.py --land), 그 아틀라스를 채우는 원본이
MISC.CDS 의 2번 파트라는 것을 확인했다 — 파일에서 뜬 서른두 장이 메모리에서 뜬 여덟 장과
점 하나까지 같다(그때 걸음 번호가 7 이었다).

    MISC.CDS 1번 파트   73728 = 32장  배   (4벌 x 8방향)
    MISC.CDS 2번 파트   73728 = 32장  말   (4방향 x 8걸음)
    한 장                48x48 팔레트 색인, 색은 OceanPalette.cs 그대로
    비침                 색인 <b>160</b> (252,0,252) — 0 이 아니다

게임이 고르는 규칙(렌더러 0x48A82E~0x48A8A4).

    dd    = (16방향 + 1) & 0xF
    프레임 = (dd >> 2) * 8 + 걸음번호(0x569550)

즉 <b>줄이 방향(4), 칸이 걸음(8)</b>이다. 걸음 번호는 지도 한 틱마다 는다 — 그래서 말이
걸어가는 동안 다리가 움직인다.

만들어 내는 그림은 8칸 x 4줄(384x192)짜리 한 장이다. 앱은 이 한 장을 읽어 잘라 쓴다
(CdsHelper.Game/Local/Helpers/ShipSprites.cs).

    python tools/extract_horse_walk.py
"""
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ls12 import Ls12                                   # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PALETTE_CS = os.path.join(ROOT, "CdsHelper.Support", "Local", "Helpers", "OceanPalette.cs")

PART_LAND = 2
SPR_W = 48
SPR_SZ = SPR_W * SPR_W
KEY = 160          # 비침 색인
PHASES = 8         # 한 방향의 걸음 수
ROWS = 4           # 말은 네 방향뿐이다


def load_palette():
    src = open(PALETTE_CS, encoding="utf-8-sig").read()
    body = src.split("Rgb =", 1)[1]
    body = body[body.index("[") + 1:body.index("];")]
    vals = [int(x) for x in re.findall(r"\d+", body)]
    if len(vals) != 768:
        raise SystemExit(f"팔레트가 768개가 아니다: {len(vals)}")
    return vals


def main(argv):
    game = argv[1] if len(argv) > 1 else r"C:\Users\ocean\Desktop\대항해시대3"
    misc = os.path.join(game, "MISC.CDS")
    if not os.path.exists(misc):
        raise SystemExit(f"{misc} 가 없다 — 게임 폴더를 인자로 줘라")

    raw = Ls12.open(misc).decode(PART_LAND)
    if len(raw) != ROWS * PHASES * SPR_SZ:
        raise SystemExit(f"2번 파트가 {len(raw)}바이트다 — {ROWS * PHASES * SPR_SZ} 이어야 한다")

    palette = load_palette()
    from PIL import Image
    im = Image.new("RGBA", (SPR_W * PHASES, SPR_W * ROWS))
    px = im.load()
    for row in range(ROWS):
        for phase in range(PHASES):
            frame = row * PHASES + phase
            ox, oy = phase * SPR_W, row * SPR_W
            for y in range(SPR_W):
                for x in range(SPR_W):
                    i = raw[frame * SPR_SZ + y * SPR_W + x]
                    px[ox + x, oy + y] = ((0, 0, 0, 0) if i == KEY else
                                          (palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2], 255))

    out = os.path.join(ROOT, "asset", "horse", "horse_walk.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    im.save(out)
    print(f"{ROWS}방향 x {PHASES}걸음 -> {os.path.relpath(out, ROOT)} ({im.width}x{im.height})")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
