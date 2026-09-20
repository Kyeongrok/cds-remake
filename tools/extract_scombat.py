# -*- coding: utf-8 -*-
"""
SCOMBAT.CDS — 해전 화면 그림을 떠서 asset/scombat/*.png 로 저장한다.

크기를 짐작하지 않는다. EXE 가 조각을 어디에 얹고 어떤 크기로 찍는지 그대로 옮겼다.
게임은 파트를 하나로 이어 붙인 버퍼에 풀어 놓고, 그 안의 자리를 상수로 들고 있다.

    0x00549A00  480000   파트 1 시작
    0x00549A04  521472   파트 1 + 41472
    0x00549A08  524544   파트 1 + 44544
    0x00549A0C  534784   파트 1 + 54784
    0x00549A10  534976   파트 2 시작
    0x00549A14  539072   파트 3 시작

찍는 자리(그리기는 0x004B5CB9(x, y, 폭, 높이, 조각))에서 크기가 나온다.

    0x004378BC  0x549A00 → 48 x 48   틀 2304, +0x6C00 부터
    0x0044006C  0x549A04 → 48 x 32   틀 1536
    0x00440326  0x549A08 → 32 x 32   틀 1024
    0x0043878B  0x549A0C →  8 x  8   틀 64
    0x00440B32  0x549A10 → 64 x 32
    0x00440BBE  0x549A14 → 640 x 32  (화면이 넓으면 800, 0x00440BA2 가 가른다)

그래서 조각이 이렇다.

    파트 0        800x600            바다 바탕 — 넓은 화면일 때 통째로 쓴다
    파트 1  +0        48x48 x 18     폭발·불길·잔해
            +41472    48x32 x  2     칸(마름모) — 빈 칸과 짚은 칸
            +44544    32x32 x 10     방향 화살표·작은 배·문장
            +54784     8x 8 x  3     작은 표시
    파트 2        64x32 x 2          기둥 머리(문장) — 왼쪽 (0,32) · 오른쪽 (0x2E0,32), v≠0 일 때만
    파트 3        640 화면 테두리    위 띠 640x32 @0 · 아래 띠 @0x5000 · 왼 기둥 64x384 @0xA000 · 오른 기둥 @0x10000
    파트 4        800 화면 테두리    위 띠 800x32 @0 · 아래 띠 @0x6400 · 왼 기둥 64x504 @0xC800 · 오른 기둥 @0x14600
                                     (아래 띠 오른쪽 끝에 Set · Cancel 이 그려져 있다)
    파트 5~12     48x48 x 12         배 여덟 벌 — 열두 방향
    파트 13~16    276,480 씩 넷      괴물 그림 — 13+괴물종류(0x004430E4)
    파트 17       261바이트  87색 제 팔레트
    파트 18       768바이트  256색 (앞쪽만 값이 있다)
    파트 19       112x112 x 7        나침반 — 0 풍배도, 1~6 풍향마다 얹는 백합(0x004337C0), 비침 0xA0
    파트 20       24x24 x 10         (피해 숫자로 보인다, 0x0043742A 벌)

800 화면 테두리 자리(0x00440C80~, v = [해전+0x8EC] = 1):

    위 띠      (0, 0)        800x32
    기둥 머리  (0, 32) · (736, 32)   64x32
    왼 기둥    (0, 64)       64x504
    오른 기둥  (736, 64)     64x504
    아래 띠    (0, 560)      800x32
    바다       (0x40, 0x20) − 스크롤   800x600 — 테두리가 위에 덮인다
    칸·배      x = X*32 − 스크롤 + 0x38 , y = (Y+1)*32 − 스크롤 + (X 짝수 ? 16 : 0)
    퇴각 E     mark-09 (32x32) — 바람 4·5 는 x 64 · y 208~368 세로, 1·2 는 x 768, 0·3 은 x 352~480 가로
    작은 글자  dot-01 = A(파랑) · dot-02 = E — 8x8

★ 팔레트가 **두 벌**이다. 게임이 얹는 자리를 그대로 옮겼다.

    0x004432F0  0x0046B540(17, 버퍼A)     파트 17 → 261바이트
    0x0044332F  0x004BA161(74, 86, A)     색인  74~159 자리에 얹는다
    0x004432FE  0x0046B540(18, 버퍼B)     파트 18 → 768바이트
    0x00443350  0x004BA161(160, 86, B)    색인 160~245 자리에 얹는다

  색인 0~73 은 공용 색표다. 한 색이 (파랑, 빨강, 초록) 순인 것은 도시 그림과 같다.
  스프라이트 쪽에서는 **색인 160 이 비침**이다(BOOKSHEL.CDS 와 같은 관례) —
  통짜 배경(파트 0·3·4·19)에서는 둘째 벌의 첫 색으로 쓰인다.

  한동안 파트 0·3·4·19 를 못 풀었다. 파트 18 을 256색 팔레트로 보고 색인을 그대로
  넣었더니 200번대가 채움값(255,255,0)이라 몽땅 마젠타가 나왔다. 둘째 벌이라
  **160 을 빼고** 찾아야 했다.
"""
import argparse
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ls12 import Ls12                                       # noqa: E402

try:
    from PIL import Image
except ImportError:
    sys.exit("pillow 가 필요하다:  pip install pillow")

BANK_A_PART, BANK_A_BASE = 17, 74      # 색인  74~159
BANK_B_PART, BANK_B_BASE = 18, 160     # 색인 160~245
BANK_SIZE = 86
TRANSPARENT = 160
DOT_BACK = 74          # 작은 글자 A·E 의 바탕 색인(흰색) — 이것도 비침이다

# (파트, 시작, 폭, 높이, 이름) — 시작은 그 파트 안에서의 자리다.
PIECES = [
    (1, 0, 48, 48, "blast"),      # 폭발·불길·잔해 18장
    (1, 41472, 48, 32, "cell"),   # 칸(마름모) 2장
    (1, 44544, 32, 32, "mark"),   # 방향·작은 배·문장 10장
    (1, 54784, 8, 8, "dot"),      # 작은 표시 3장
    (2, 0, 64, 32, "pair"),
    (20, 0, 24, 24, "digit"),     # 피해 숫자 0~9 (0x0043742A 벌 — 일·십·백 자리를 x+0x50·+0x38·+0x20)
]

# 배 여덟 벌 — 한 벌이 열두 방향이다.
SHIP_PARTS = range(5, 13)
SHIP_W = SHIP_H = 48

# 통짜 그림 — 비침이 없다(색인 160 도 색으로 친다).
FLATS = [
    (0, 800, 600, "sea"),      # 바다 바탕
]

# 판 테두리 — 파트 3(640 화면)·4(800 화면)가 한 파트에 띠 둘과 기둥 둘을 잇대어 담는다.
# 게임이 파트를 0x549A14 에 풀고 이 자리에서 잘라 찍는다(0x00440B95~0x00440D4B).
#   (파트, 시작, 폭, 높이, 이름)
FRAMES = [
    (3, 0x0000, 640, 32, "bar-a-00"),          # 640 화면 위 띠      (0, 0)
    (3, 0x5000, 640, 32, "bar-a-01"),          # 640 화면 아래 띠    (0, v*32+0x1B0?)
    (3, 0xA000, 64, 384, "frame-narrow-left"), # 640 화면 왼 기둥    (0, (v+1)*32)
    (3, 0x10000, 64, 384, "frame-narrow-right"),  # 640 화면 오른 기둥 (화면폭-64, (v+1)*32)
    (4, 0x0000, 800, 32, "bar-b-00"),          # 800 화면 위 띠      (0, 0)
    (4, 0x6400, 800, 32, "bar-b-01"),          # 800 화면 아래 띠    (0, v*32+0x210)
    (4, 0xC800, 64, 504, "frame-left"),        # 800 화면 왼 기둥    (0, (v+1)*32)
    (4, 0x14600, 64, 504, "frame-right"),      # 800 화면 오른 기둥  (0x2E0, (v+1)*32)
]

# 나침반 — 파트 19, 112x112 일곱 장. 0 이 풍배도, 1~6 은 풍향(+1)마다 얹는 장이다(0x004337C0).
# 읽을 때 색인 0xA0(160)을 0 으로 바꿔 비침으로 쓴다(0x0043396D).
COMPASS = (19, 112, 112, "compass")

# 옛 판이 잘못 잘라 남긴 파일 — 지운다.
STALE = ["bar-a-02", "bar-a-03", "bar-b-02", "bar-b-03",
         "bar-c-00", "bar-c-01", "bar-c-02", "bar-c-03"]


def game_palette(repo):
    """앱의 GamePalette.cs 에서 공용 색표를 읽는다 — 값을 두 군데 두지 않으려고."""
    path = os.path.join(repo, "CdsHelper.Game", "Local", "Helpers", "GamePalette.cs")
    src = open(path, encoding="utf-8-sig").read()
    body = src.split("private static readonly byte[] Low =")[1].split("];")[0]
    nums = [int(x) for x in re.findall(r"\d+", body)]
    pal = [(nums[i * 3], nums[i * 3 + 1], nums[i * 3 + 2]) for i in range(len(nums) // 3)]
    return pal + [(255, 0, 255)] * (256 - len(pal))


def color(value, banks, shared, clear=True, keys=(TRANSPARENT,)):
    """색인 하나를 색으로. 비침이면 None(통짜 그림은 clear=False)."""
    if clear and value in keys:
        return None
    for base, table in banks:
        if base <= value < base + BANK_SIZE:
            k = (value - base) * 3
            if k + 2 < len(table):
                return (table[k + 1], table[k + 2], table[k])   # (파랑, 빨강, 초록)
    return shared[value]


def frames(data, w, h, banks, shared, clear=True, keys=(TRANSPARENT,)):
    """한 덩이를 틀 크기로 잘라 낸다. clear 면 RGBA(비침 있음), 아니면 RGB."""
    out = []
    for f in range(len(data) // (w * h)):
        px = []
        for v in data[f * w * h:(f + 1) * w * h]:
            c = color(v, banks, shared, clear, keys)
            px.append((0, 0, 0, 0) if c is None else
                      ((c[0], c[1], c[2], 255) if clear else c))
        im = Image.new("RGBA" if clear else "RGB", (w, h))
        im.putdata(px)
        out.append(im)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="게임 폴더 (SCOMBAT.CDS 가 있는 곳)")
    ap.add_argument("--out", default=None, help="저장할 폴더 (기본 asset/scombat)")
    args = ap.parse_args()

    path = os.path.join(args.game, "SCOMBAT.CDS")
    if not os.path.exists(path):
        sys.exit("SCOMBAT.CDS 가 없다: " + path)

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = args.out or os.path.join(repo, "asset", "scombat")
    os.makedirs(out_dir, exist_ok=True)

    archive = Ls12.open(path)
    banks = [(BANK_A_BASE, archive.decode(BANK_A_PART)),
             (BANK_B_BASE, archive.decode(BANK_B_PART))]
    shared = game_palette(repo)

    made = 0
    for part, start, w, h, name in PIECES:
        data = archive.decode(part)
        span = len(data) - start
        # 다음 조각이 있으면 거기까지만 자른다.
        for p2, s2, _, _, _ in PIECES:
            if p2 == part and s2 > start:
                span = min(span, s2 - start)
        # 작은 글자(A·E)와 피해 숫자는 바탕을 색인 74(공용 색표의 흰색)로 칠해 두었다 — 160 과 함께
        # 비침으로 친다. 안 그러면 판 위에 흰 네모가 깔린다(포탄 dot-00 은 160 바탕이라 그대로다).
        keys = (TRANSPARENT, DOT_BACK) if name in ("dot", "digit") else (TRANSPARENT,)
        for i, im in enumerate(frames(data[start:start + span], w, h, banks, shared, keys=keys)):
            im.save(os.path.join(out_dir, "%s-%02d.png" % (name, i)))
            made += 1

    for k, part in enumerate(SHIP_PARTS):
        data = archive.decode(part)
        for i, im in enumerate(frames(data, SHIP_W, SHIP_H, banks, shared)):
            im.save(os.path.join(out_dir, "ship%d-%02d.png" % (k, i)))
            made += 1

    # 통짜 그림 — 비침 없이 그대로 뜬다.
    for part, w, h, name in FLATS:
        data = archive.decode(part)
        for i, im in enumerate(frames(data, w, h, banks, shared, clear=False)):
            im.save(os.path.join(out_dir, "%s-%02d.png" % (name, i)))
            made += 1

    # 판 테두리 — 띠·기둥을 제 자리에서 한 장씩 잘라 낸다(비침 없음).
    for part, start, w, h, name in FRAMES:
        data = archive.decode(part)
        piece = data[start:start + w * h]
        if len(piece) < w * h:
            print("파트 %d 가 짧다: %s (%d < %d)" % (part, name, len(piece), w * h))
            continue
        frames(piece, w, h, banks, shared, clear=False)[0].save(
            os.path.join(out_dir, name + ".png"))
        made += 1

    # 나침반 — 비침 있음.
    part, w, h, name = COMPASS
    for i, im in enumerate(frames(archive.decode(part), w, h, banks, shared)):
        im.save(os.path.join(out_dir, "%s-%02d.png" % (name, i)))
        made += 1

    for stale in STALE:
        path_ = os.path.join(out_dir, stale + ".png")
        if os.path.exists(path_):
            os.remove(path_)

    with open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8") as f:
        f.write(__doc__.strip() + "\n")
    print("%d장을 %s 에 저장했다" % (made, out_dir))


if __name__ == "__main__":
    main()
