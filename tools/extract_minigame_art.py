# -*- coding: utf-8 -*-
"""
MGGRAPH.CDS 에서 미니 게임 그림을 뽑는다.

MGGRAPH.CDS 는 LS12 아카이브고 파트가 쉰여덟이다. 큰 것 둘이 화면 배경이고,
나머지가 조각과 팔레트다.

    파트 0     48바이트   — 16색 팔레트
    파트 1    471바이트   — 157색 팔레트 (파트 14 것)
    파트 2    471바이트   — 157색 팔레트 (파트 51 것)

<b>조각 크기는 CDS_95.EXE 의 표 0x00549E98 에 있다</b> — 여덟 바이트씩 쉰다섯 칸이고
앞이 너비, 뒤가 높이다(꺼내는 곳은 0x00455DE0). 그리고 <b>조각 n 이 곧 파트 n+3</b>
이다 — 쉰다섯 칸이 파트 3~57 과 하나씩 맞아떨어진다.

    조각 14~16  48x64  파트 17~19  바가지 소·중·대 (빈 것)
    조각 20~22  48x64  파트 23~25  바가지 소·중·대 (물 든 것)
    조각 17~19  48x64  파트 20~22  자루가 반대쪽인 벌
    조각 26~35  24x72  파트 29~38  성배 열 (빈 것)
    조각 36~45  24x72  파트 39~48  성배 열 (찬 것)
    조각 48    368x432 파트 51     성배 퍼즐 배경

성배 조각은 뒤에 돌담이 같이 들어 있어 그 칸을 통째로 덮는다. 바가지 조각은
둘레가 <b>색인 230</b>(마젠타)이라 그것만 비운다.

색인은 <b>74 를 빼서</b> 제 팔레트 자리로 삼는다. 팔레트 한 색은 파일에
(파랑, 빨강, 초록) 차례로 적혀 있다 — 도시 그림·아이템 그림과 같은 규칙이다.

    색인 최소 75, 최대 229 이고 157색이 74~230 을 덮는다.
    base 를 73 으로 잡으면 마지막 자리(마젠타)가 걸려 돌 이음매가 분홍으로 튄다.
    74 라야 그 자리가 검정이 되어 이음매 선이 제대로 나온다.

쓰는 법:
    python tools/extract_minigame_art.py --game "C:\\...\\대항해시대3"
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ls12  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "asset", "minigame")

#: MGGRAPH 그림마다 (파트, 팔레트 파트, 너비, 높이, 파일 이름).
PICTURES = [
    (51, 2, 368, 432, "grail-bg.png"),
    (14, 1, 512, 352, "cube-bg.png"),
]

#: 화살표 입방체 퍼즐은 제 그림 파일이 없다 — 0x0049B422 가 0x00455DE0 을 부르니
#: MGGRAPH.CDS 를 함께 쓴다. 조각 11(파트 14)이 512x352 배경이고, 조각 6~10
#: (파트 9~13)이 좌대와 모험자와 금괴다. 크기는 조각 표(0x00549E98)가 64x48 이라 한다.
CUBE = [
    # 조각 0~5(파트 3~8) — 입방체를 돌리는 화살표 여섯. 왼쪽 검은 칸에 놓인다.
    (3, 64, 48, "cube-turn-0.png"),
    (4, 64, 48, "cube-turn-1.png"),
    (5, 64, 48, "cube-turn-2.png"),
    (6, 64, 48, "cube-turn-3.png"),
    (7, 64, 48, "cube-turn-4.png"),
    (8, 64, 48, "cube-turn-5.png"),
    (9, 64, 48, "cube-hero.png"),
    (10, 64, 80, "cube-hero2.png"),
    (11, 64, 88, "cube-stand.png"),
    (12, 64, 88, "cube-mark.png"),
    (13, 64, 48, "cube-gold.png"),
]

#: MAZE.CDS 는 파트 0 하나에 다 들어 있다 — (자리, 개수, 너비, 높이, 이름 꼴).
MAZE = [
    (30144, 1, 352, 432, "maze-bg.png"),
    (0, 1, 80, 24, "maze-floor.png"),
    (1920, 12, 32, 24, "maze-arrow-{0}.png"),
    (11136, 4, 32, 32, "maze-chest-{0}.png"),
    (19328, 4, 32, 32, "maze-chest-open-{0}.png"),
    (27520, 1, 32, 32, "maze-door.png"),
    (28544, 1, 40, 40, "maze-hero.png"),
]

#: 조각마다 (첫 파트, 개수, 너비, 높이, 이름 꼴). 마젠타는 비운다.
SPRITES = [
    (17, 3, 48, 64, "grail-dipper-{0}.png"),
    (23, 3, 48, 64, "grail-dipper-full-{0}.png"),
    (29, 10, 24, 72, "grail-cup-{0}.png"),
    (39, 10, 24, 72, "grail-cup-full-{0}.png"),
]

#: 비어 있음을 뜻하는 색 — 팔레트 마지막 자리(색인 230)다.
CLEAR = (255, 0, 255)

#: FISHING.CDS 도 파트 0 하나에 다 들어 있다. 자리 표는 EXE 의 0x00569194 다.
#:
#:     0      256바이트    16x16   낚싯바늘
#:     256    3072바이트   32x32x3 배 · 오징어 · 낙지
#:     3328   2048바이트   32x16x4 대어 두 벌 · 잡어 두 벌
#:     5376   1024바이트   32x16x2 왼쪽 화살표 · 오른쪽 화살표
#:     8448   131712바이트 336x392 <b>배경</b>  — 0x0047ADF2 가 (0, 0) 에 찍는다
#: BALANCE.CDS(코인 게임 = 천칭 퍼즐)는 파트가 넷이다 — 0·1·2 가 점, 3 이 157색
#: 팔레트다. 자리 표가 EXE 에 셋으로 나뉘어 있다.
#:
#:     파트 0  0x00549E10  금화 32x32 — <b>스물여덟 장</b>이다
#:                             0~12  번호 새긴 금화 1~13 (밝은 벌)
#:                            13~25  같은 열셋 (어두운 벌 — 접시에 올린 것)
#:                            26~27  납작하게 누운 금화 둘 (접시 위에서 옆으로 보이는 것)
#:                          둘레는 색인 230(마젠타)이라 그것만 비운다.
#:     파트 1  0x00549E20  천칭 — 기둥 64x160, 대 176x16, 나무 192x144 둘,
#:                                    금 208x168 셋
#:                             기둥은 자리 0 이다. 표에는 안 적혀 있지만 파트 1 앞머리
#:                             10,240바이트가 딱 64x160 이고, 실제로 갈색 기둥과 받침이다.
#:     파트 2  0x00549E3C  단추 64x32 셋 · 접시 80x144 둘 · 받침 96x48 · 배경 448x384
#:
#: 배경은 0x00451F91 이 (8, 8) 에 찍는다 — 창이 464x400 이니 8점 테를 두른 꼴이다.
BALANCE_COIN = [
    (0, 0, 13, 32, 32, "coin-face-{0}.png"),
    (0, 13312, 13, 32, 32, "coin-face-dim-{0}.png"),
    (0, 26624, 2, 32, 32, "coin-gold-{0}.png"),
    (2, 33792, 1, 448, 384, "coin-bg.png"),
    (2, 6144, 2, 80, 144, "coin-pan-{0}.png"),
    (2, 29184, 1, 96, 48, "coin-stand.png"),
    (2, 0, 3, 64, 32, "coin-button-{0}.png"),
    (1, 0, 1, 64, 160, "coin-post.png"),
    (1, 10240, 1, 176, 16, "coin-beam.png"),
    (1, 13056, 2, 192, 144, "coin-wood-{0}.png"),
]

#: 금 천칭 셋은 자리가 안 이어져 따로 적는다 — (파트, 자리, 너비, 높이, 이름).
BALANCE_GOLD = [
    (1, 68352, 208, 168, "coin-scale-0.png"),
    (1, 103296, 208, 168, "coin-scale-1.png"),
    (1, 138240, 208, 168, "coin-scale-2.png"),
]

#: TOWER.CDS(발라몬의 탑)는 파트가 둘 — 0 이 점, 1 이 157색 팔레트다. 자리 표는
#: EXE 의 0x00547400 이고 [200704, 303104, 405504] 다.
#:
#:     0       448x448   배경 — 돌 받침 셋
#:     200704  160x80 x8 돌 판자 (0x00431077 이 크기를 준다)
#:     303104  160x80 x8 다른 벌
TOWER = [
    (0, 1, 448, 448, "tower-bg.png"),
    (200704, 8, 160, 80, "tower-plank-{0}.png"),
]

FISHING = [
    (8448, 1, 336, 392, "fish-bg.png"),
    (0, 1, 16, 16, "fish-hook.png"),
    (256, 3, 32, 32, "fish-big-{0}.png"),
    (3328, 4, 32, 16, "fish-small-{0}.png"),
    (5376, 2, 32, 16, "fish-arrow-{0}.png"),
    # 바닥의 대어(실러캔스). 0x0047B698 이 [0x5691A4] 를 64x32 로 (칸*40+0x29, 0x168) 에 찍는다.
    (6400, 1, 64, 32, "fish-bigone.png"),
]

#: 제 팔레트가 덮기 시작하는 색인.
BASE = 74


def decode(archive, part, pal_part, width, height):
    px = archive.decode(part)
    if len(px) != width * height:
        raise SystemExit(f"파트 {part} 크기가 {len(px)} 라 {width}x{height} 와 안 맞는다")
    own = ls12.palette(archive.decode(pal_part))
    out = []
    for v in px:
        k = v - BASE
        out.append(own[k] if 0 <= k < len(own) else (255, 0, 255))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="대항해시대3 폴더")
    args = ap.parse_args()

    path = os.path.join(args.game, "MGGRAPH.CDS")
    if not os.path.exists(path):
        raise SystemExit(f"{path} 가 없다")

    from PIL import Image

    archive = ls12.Ls12(open(path, "rb").read())
    os.makedirs(OUT_DIR, exist_ok=True)

    for part, pal_part, width, height, name in PICTURES:
        rgb = decode(archive, part, pal_part, width, height)
        image = Image.new("RGB", (width, height))
        image.putdata(rgb)
        image.save(os.path.join(OUT_DIR, name))
        print(f"{name}  {width}x{height}  (파트 {part}, 팔레트 {pal_part})")

    for part, width, height, name in CUBE:
        rgb = decode(archive, part, 1, width, height)
        image = Image.new("RGBA", (width, height))
        image.putdata([(0, 0, 0, 0) if c == CLEAR else (*c, 255) for c in rgb])
        image.save(os.path.join(OUT_DIR, name))
        print(f"{name}  {width}x{height}  (MGGRAPH 파트 {part})")

    for first, count, width, height, shape in SPRITES:
        for i in range(count):
            rgb = decode(archive, first + i, 2, width, height)
            image = Image.new("RGBA", (width, height))
            image.putdata([(0, 0, 0, 0) if c == CLEAR else (*c, 255) for c in rgb])
            name = shape.format(i)
            image.save(os.path.join(OUT_DIR, name))
        print(f"{shape.format('*')}  {width}x{height} x{count}  (파트 {first}~{first + count - 1})")

    strip(args.game, "MAZE.CDS", MAZE, "maze-bg.png")
    strip(args.game, "FISHING.CDS", FISHING, "fish-bg.png")
    balance(args.game)
    strip(args.game, "TOWER.CDS", TOWER, "tower-bg.png")


def strip(game, name, table, out_solid):
    """파트 0 하나에 다 든 CDS 를 자리 표대로 자른다."""
    from PIL import Image

    path = os.path.join(game, name)
    if not os.path.exists(path):
        print(f"{path} 가 없다 — 건너뛴다")
        return

    archive = ls12.Ls12(open(path, "rb").read())
    px = archive.decode(0)
    own = ls12.palette(archive.decode(1))

    def color(v):
        k = v - BASE
        return own[k] if 0 <= k < len(own) else CLEAR

    for first, count, width, height, shape in table:
        for i in range(count):
            at = first + i * width * height
            rgb = [color(v) for v in px[at:at + width * height]]
            solid = shape == out_solid
            image = Image.new("RGB" if solid else "RGBA", (width, height))
            image.putdata(rgb if solid
                          else [(0, 0, 0, 0) if c == CLEAR else (*c, 255) for c in rgb])
            image.save(os.path.join(OUT_DIR, shape.format(i)))
        print(f"{shape.format('*')}  {width}x{height} x{count}  (자리 {first})")


#: BALANCE.CDS 조각의 비침 색인. 팔레트 156번이 (255, 0, 255) 다.
BALANCE_CLEAR_INDEX = 230


def balance(game):
    """BALANCE.CDS 는 점이 파트 셋에 나뉘어 있어 따로 자른다."""
    from PIL import Image

    path = os.path.join(game, "BALANCE.CDS")
    if not os.path.exists(path):
        print(f"{path} 가 없다 — 건너뛴다")
        return

    archive = ls12.Ls12(open(path, "rb").read())
    px = [archive.decode(0), archive.decode(1), archive.decode(2)]
    own = ls12.palette(archive.decode(3))

    def color(v):
        # 둘레가 마젠타(색인 230)다 — 팔레트 밖 색인과 똑같이 비운다.
        if v == BALANCE_CLEAR_INDEX:
            return CLEAR
        k = v - BASE
        return own[k] if 0 <= k < len(own) else CLEAR

    def save(part, at, width, height, name):
        rgb = [color(v) for v in px[part][at:at + width * height]]
        solid = name == "coin-bg.png"
        image = Image.new("RGB" if solid else "RGBA", (width, height))
        image.putdata(rgb if solid
                      else [(0, 0, 0, 0) if c == CLEAR else (*c, 255) for c in rgb])
        image.save(os.path.join(OUT_DIR, name))

    for part, first, count, width, height, shape in BALANCE_COIN:
        for i in range(count):
            save(part, first + i * width * height, width, height, shape.format(i))
        print(f"{shape.format('*')}  {width}x{height} x{count}  (파트 {part}, 자리 {first})")

    for part, at, width, height, name in BALANCE_GOLD:
        save(part, at, width, height, name)
    print("coin-scale-*.png  208x168 x3  (파트 1)")


if __name__ == "__main__":
    main()
