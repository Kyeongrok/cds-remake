# -*- coding: utf-8 -*-
"""
MISC.CDS 파트 4 에서 메뉴 띠 조각을 떠서 asset/ui/band/*.png 로 저장한다.

게임의 메뉴 타이틀·버튼 띠는 조각 셋을 옆으로 이어 붙인 것이다.
**한 장을 늘리는 것이 아니다** — 가운데만 되풀이한다.

    +0     16폭 x 24행 (384바이트)   왼끝
    +384    8폭 x 24행 (192바이트)   가운데   ← 폭만큼 옆으로 되풀이한다
    +576   16폭 x 24행 (384바이트)   오른끝

이 한 벌이 960바이트고 세 벌이 들어 있다(파트 4 는 2,880바이트).

    벌 0  진홍 장식 — 메뉴 타이틀
    벌 1  베이지   — 보통 버튼
    벌 2  회녹색   — 다른 상태 버튼

띠 높이는 늘 24픽셀이고 폭은 `16 + 8*n + 16` 이다.

★ 조각마다 제 폭으로 행 우선이라, 파트 전체를 한 폭으로 보고 가로로 자르면 안 된다.
  16x180 으로 보고 y36·60·96 을 경계로 삼았던 적이 있는데 전부 헛것이었다.

게임도 똑같이 짓는다.

    0x00463590  게임 시작 때 파트 4 를 객체 0x005AA3B8+0x14 로 읽어 둔다
    0x00463710  조각 꺼내기(벌, 조각)
    0x00552898  조각 시작 열 표 {0, 16, 24} (열 하나가 24바이트)
    0x0041F606  띠 짓기 — 왼끝 0x41F61B, 가운데 되풀이 0x41F677, 오른끝 0x41F6EB

색은 게임 공용 색표를 그대로 쓴다(색인 0~73 만 나온다).
글자는 CDS 가 아니라 `ALL_FONT.16P`(16x14 한글)·`ANKFONT.DAT`(8x16 ASCII) 에서 온다.

쓰는 법:
    python tools/extract_menu_band.py --game "C:\\...\\대항해시대3"

PIL(pillow) 이 있어야 한다.
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

BAND_PART = 4
STYLE_BYTES = 960
BAND_H = 24
PIECES = [(0, 16, "left"), (384, 8, "mid"), (576, 16, "right")]
STYLES = [(0, "title", "진홍 장식 — 메뉴 타이틀"),
          (1, "button", "베이지 — 보통 버튼"),
          (2, "alt", "회녹색 — 다른 상태 버튼")]


def game_palette(repo):
    """앱의 GamePalette.cs 에서 공용 색표를 읽는다 — 값을 두 군데 두지 않으려고."""
    path = os.path.join(repo, "CdsHelper.Game", "Local", "Helpers", "GamePalette.cs")
    src = open(path, encoding="utf-8").read()
    body = src.split("private static readonly byte[] Low =")[1].split("];")[0]
    nums = [int(x) for x in re.findall(r"\d+", body)]
    pal = [(nums[i * 3], nums[i * 3 + 1], nums[i * 3 + 2]) for i in range(len(nums) // 3)]
    return pal + [(255, 0, 255)] * (256 - len(pal))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="게임 폴더 (MISC.CDS 가 있는 곳)")
    ap.add_argument("--out", default=None, help="저장할 폴더 (기본 asset/ui/band)")
    ap.add_argument("--cells", type=int, default=6, help="맛보기 띠의 가운데 칸 수")
    args = ap.parse_args()

    path = os.path.join(args.game, "MISC.CDS")
    if not os.path.exists(path):
        sys.exit("MISC.CDS 가 없다: " + path)

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = args.out or os.path.join(repo, "asset", "ui", "band")
    os.makedirs(out_dir, exist_ok=True)

    pal = game_palette(repo)
    data = Ls12.open(path).decode(BAND_PART)
    if data is None or len(data) < STYLE_BYTES * len(STYLES):
        sys.exit("파트 %d 가 기대한 크기가 아니다: %s" % (BAND_PART, len(data) if data else None))
    print("파트 %d = %d바이트" % (BAND_PART, len(data)))

    def piece(style, off, w):
        im = Image.new("RGB", (w, BAND_H))
        px = im.load()
        base = style * STYLE_BYTES + off
        for r in range(BAND_H):
            for c in range(w):
                px[c, r] = pal[data[base + r * w + c]]
        return im

    for style, sname, note in STYLES:
        for off, w, pname in PIECES:
            piece(style, off, w).save(os.path.join(out_dir, "%s-%s.png" % (sname, pname)))

        # 이어 붙인 맛보기 — 조각이 제대로 맞물리는지 눈으로 보려고 함께 남긴다
        cells = max(1, args.cells)
        bw = 16 * 2 + 8 * cells
        band = Image.new("RGB", (bw, BAND_H))
        band.paste(piece(style, 0, 16), (0, 0))
        mid = piece(style, 384, 8)
        for i in range(cells):
            band.paste(mid, (16 + i * 8, 0))
        band.paste(piece(style, 576, 16), (bw - 16, 0))
        band.save(os.path.join(out_dir, "%s-band.png" % sname))
        print("  벌 %d %-7s %s" % (style, sname, note))

    with open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8") as f:
        f.write("# 메뉴 띠 조각\n\n")
        f.write("`MISC.CDS` 파트 %d 에서 뽑은 것이다. `tools/extract_menu_band.py` 가 만든다.\n\n"
                % BAND_PART)
        f.write("띠는 **왼끝 · 가운데(되풀이) · 오른끝** 셋을 이어 붙여 짓는다.\n")
        f.write("한 장을 늘리는 것이 아니다. 높이는 늘 %d픽셀, 폭은 `16 + 8*n + 16`.\n\n" % BAND_H)
        f.write("| 파일 | 크기 | 쓰임 |\n|---|---|---|\n")
        for style, sname, note in STYLES:
            for off, w, pname in PIECES:
                f.write("| `%s-%s.png` | %dx%d | %s %s |\n"
                        % (sname, pname, w, BAND_H, note.split(" — ")[0],
                           {"left": "왼끝", "mid": "가운데(되풀이)", "right": "오른끝"}[pname]))
        f.write("\n`*-band.png` 는 가운데를 %d번 되풀이해 이어 붙인 맛보기다.\n" % args.cells)
        f.write("\n앱은 이 PNG 를 쓰지 않고 `UiSprites` 가 게임 폴더의 MISC.CDS 에서 바로 읽는다 —\n")
        f.write("여기 있는 것은 눈으로 보고 손보기 위한 것이다.\n")

    print("저장: " + out_dir)


if __name__ == "__main__":
    main()
