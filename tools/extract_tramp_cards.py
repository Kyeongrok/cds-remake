# -*- coding: utf-8 -*-
"""
TRAMP.CDS 에서 술집 포카(카드 도박) 그림을 뽑는다.

TRAMP.CDS 는 LS12 아카이브고 파트가 다섯이다. 다른 미니 게임과 달리 <b>팔레트가
아카이브 안에 없다</b> — 옆에 놓인 날것 파일 <b>TRAMP.P</b>(423바이트 = 141색)가
팔레트다.

    파트 0   239616바이트   576x416     바탕 (창 크기 0x240 x 0x1A0 과 같다)
    파트 1    23104바이트   80x96 x3    카드 뒷면 (3 x 7680 = 23040, 뒤 64바이트는 덤)
    파트 2   133120바이트   40x64 x52   <b>선 카드 쉰두 장</b>
    파트 3   332800바이트   80x80 x52   <b>기울여 놓은 카드 쉰두 장</b>
    파트 4     1024바이트   16x16 x4    <b>무늬 표시</b> 클럽·다이아·하트·스페이드

카드 쉰두 장은 <b>무늬 x 13 + 끗수</b> 차례로 늘어선다 — 클럽·다이아·하트·스페이드
순이고 각 줄이 2 부터 A 까지다. 게임 안에서 카드 한 장은 1바이트
`(무늬 << 4) | 끗수` 이므로 그림 번호가 바로 나온다.

    그림번호 = (카드 >> 4) * 13 + (카드 & 0x0F)

색인은 다른 그림과 마찬가지로 <b>74 를 빼서</b> 제 팔레트 자리로 삼는다.
쓰인 색인이 73~214 이고 141색이 74~214 를 덮는다 — 74 라야 색인 74 가 흰색(카드
바닥), 213 이 검정(테두리), 214 가 노랑(비어 있음)으로 맞아떨어진다.

    비어 있음 = 색인 214 (팔레트 마지막 색, 노랑)

<b>색인 73 은 TRAMP.P 밖이지만 검정이다.</b> 게임이 이 팔레트를 더 큰 공용
팔레트에 얹어 쓰기 때문이다 — 파트 4 의 클럽·스페이드가 이 색으로 그려져 있어,
비운 자리로 치면 무늬 둘이 통째로 사라진다.

바이트 차례는 다른 팔레트와 같은 (파랑, 빨강, 초록)이다.

자세한 것은 볼트 `분석/미니게임/67.분석-미니 게임(포카·카드 도박).md` 를 보라.

쓰는 법:
    python tools/extract_tramp_cards.py --game "C:\\...\\대항해시대3"
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ls12  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "asset", "poker")

#: 제 팔레트가 덮기 시작하는 색인.
BASE = 74

#: 비어 있음을 뜻하는 색인 — 팔레트 마지막 자리(노랑)다.
CLEAR_INDEX = 214

#: 팔레트 밖이지만 검정인 색인 (공용 팔레트에서 온다).
BLACK_INDEX = 73

#: 무늬 이름 — 그림이 늘어선 차례다.
SUITS = ["clubs", "diamonds", "hearts", "spades"]

#: 끗수 이름 — EXE 의 이름표 0x00569430 과 같은 차례다.
RANKS = ["02", "03", "04", "05", "06", "07", "08", "09", "10", "J", "Q", "K", "A"]

#: 통짜 그림 — (파트, 너비, 높이, 이름).
SOLID = [
    (0, 576, 416, "poker-bg.png"),
]

#: 조각 벌 — (파트, 개수, 너비, 높이, 이름 꼴). 비어 있는 자리는 투명으로 뺀다.
SPRITES = [
    (1, 3, 80, 96, "poker-back-{0}.png"),
]

#: 무늬 표시 넷 — 파트 4 에 16x16 으로 나란히 들어 있다.
MARKS = (4, 16, 16, "poker-mark-{0}.png")

#: 카드 벌 — (파트, 너비, 높이, 이름 앞머리).
CARDS = [
    (2, 40, 64, "poker-card"),
    (3, 80, 80, "poker-card-tilt"),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="대항해시대3 폴더")
    ap.add_argument("--sheet", action="store_true",
                    help="낱장 대신 13x4 한 장으로 붙여 낸다")
    args = ap.parse_args()

    cds = os.path.join(args.game, "TRAMP.CDS")
    pal_path = os.path.join(args.game, "TRAMP.P")
    for path in (cds, pal_path):
        if not os.path.exists(path):
            raise SystemExit(f"{path} 가 없다")

    from PIL import Image

    archive = ls12.Ls12(open(cds, "rb").read())
    own = ls12.palette(open(pal_path, "rb").read())
    os.makedirs(OUT_DIR, exist_ok=True)

    def color(v):
        if v == BLACK_INDEX:
            return (0, 0, 0)
        k = v - BASE
        return own[k] if 0 <= k < len(own) else own[CLEAR_INDEX - BASE]

    def cut(px, at, width, height):
        return [color(v) for v in px[at:at + width * height]]

    def save(rgb, width, height, name, solid=False):
        clear = own[CLEAR_INDEX - BASE]
        if solid:
            image = Image.new("RGB", (width, height))
            image.putdata(rgb)
        else:
            image = Image.new("RGBA", (width, height))
            image.putdata([(0, 0, 0, 0) if c == clear else (*c, 255) for c in rgb])
        image.save(os.path.join(OUT_DIR, name))

    for part, width, height, name in SOLID:
        px = archive.decode(part)
        check(part, len(px), width * height)
        save(cut(px, 0, width, height), width, height, name, solid=True)
        print(f"{name}  {width}x{height}  (파트 {part})")

    for part, count, width, height, shape in SPRITES:
        px = archive.decode(part)
        for i in range(count):
            save(cut(px, i * width * height, width, height), width, height,
                 shape.format(i))
        print(f"{shape.format('*')}  {width}x{height} x{count}  (파트 {part})")

    part, width, height, shape = MARKS
    px = archive.decode(part)
    check(part, len(px), width * height * 4)
    for i in range(4):
        save(cut(px, i * width * height, width, height), width, height,
             shape.format(SUITS[i]))
    print(f"{shape.format('*')}  {width}x{height} x4  (파트 {part})")

    for part, width, height, prefix in CARDS:
        px = archive.decode(part)
        check(part, len(px), width * height * 52)
        if args.sheet:
            sheet = Image.new("RGBA", (13 * width, 4 * height))
            for n in range(52):
                tile = Image.new("RGBA", (width, height))
                rgb = cut(px, n * width * height, width, height)
                clear = own[CLEAR_INDEX - BASE]
                tile.putdata([(0, 0, 0, 0) if c == clear else (*c, 255) for c in rgb])
                sheet.paste(tile, ((n % 13) * width, (n // 13) * height))
            sheet.save(os.path.join(OUT_DIR, prefix + "s.png"))
            print(f"{prefix}s.png  {13 * width}x{4 * height}  (파트 {part}, 13x4)")
            continue
        for n in range(52):
            name = f"{prefix}-{SUITS[n // 13]}-{RANKS[n % 13]}.png"
            save(cut(px, n * width * height, width, height), width, height, name)
        print(f"{prefix}-*.png  {width}x{height} x52  (파트 {part})")


def check(part, got, want):
    if got != want:
        raise SystemExit(f"파트 {part} 크기가 {got} 라 {want} 와 안 맞는다")


if __name__ == "__main__":
    main()
