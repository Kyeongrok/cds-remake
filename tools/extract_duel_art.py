# -*- coding: utf-8 -*-
"""
FIGHTER.CDS 에서 일기토 그림을 뽑아 asset/duel 에 둔다.

게임의 일기토 화면(0x004A7050)은 위에 마당 384x136 을, 아래에 눈금판 384x112 를
깐다. FIGHTER.CDS 는 LS12 이고 파트가 33 이다.

    0~17    646272 + 팔레트 768   아홉 벌   ; 사람 몸짓 33장씩 (144x136)
    18~31    52224 + 팔레트 768   일곱 벌   ; 마당 384x136
    32       43024                          ; 눈금판 — 앞 16바이트를 건너뛰고 384x112

<b>팔레트가 8비트다.</b> 다른 미니 게임 그림들은 6비트(0~63)라 4를 곱해야 했는데
이 파일은 그대로 쓴다. 칸 차례는 여느 것과 같이 (파랑, 빨강, 초록)이다.

<b>바탕 값이 파트마다 다르다.</b> 마당은 74, 눈금판은 11 이다.
눈금판은 제 팔레트가 없어 마당 것을 같이 쓴다 — 게임도 그렇게 해서 눈금판이
마당 빛깔로 물든다.
"""
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ls12                                                    # noqa: E402

GAME = r"C:\Users\ocean\Desktop\대항해시대3"
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), "asset", "duel")

ARENA_W, ARENA_H = 384, 136
PANEL_W, PANEL_H = 384, 112
PANEL_SKIP = 16                    # 눈금판 앞머리 16바이트

#: 눈금판이 쓰는 <b>공용 색표</b> — 색인 0~73 이다.
#:
#: 눈금판 색인이 11~73 이라 <b>죄다 74 밑</b>이다. 게임은 74 부터 그림마다의 팔레트를
#: 얹으므로(GamePalette.OwnPaletteBase) 그 아래는 이 공용 표를 본다 — 곧 눈금판은
#: 마당 팔레트를 안 쓴다. 마당 것을 씌우면 나무빛이 분홍으로 뭉개지고 막대 자리와
#: H·M·L 글자가 바탕에 묻힌다.
#:
#: 값은 CdsHelper.Game/Local/Helpers/GamePalette.cs 의 Low 와 같아야 한다.
COMMON = bytes([
    186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186, 186,
    186, 186, 186, 186, 186, 186, 244, 232, 224, 144, 140, 140, 64, 60, 52, 96, 100, 100, 60, 44, 40, 96, 60, 56,
    72, 48, 40, 52, 28, 20, 92, 56, 44, 116, 104, 92, 88, 68, 52, 128, 120, 108, 180, 172, 156, 140, 120, 92,
    132, 116, 84, 80, 56, 36, 196, 180, 148, 156, 132, 96, 112, 92, 72, 160, 148, 136, 224, 192, 160, 104, 88, 68,
    236, 200, 176, 88, 88, 76, 144, 136, 116, 236, 204, 172, 188, 168, 128, 168, 144, 108, 88, 72, 56, 132, 108, 72,
    244, 216, 176, 248, 224, 196, 224, 212, 192, 212, 200, 176, 44, 52, 72, 32, 72, 100, 48, 64, 100, 52, 76, 100,
    72, 88, 108, 80, 96, 112, 24, 36, 48, 32, 72, 100, 56, 72, 100, 76, 100, 128, 84, 100, 128, 84, 100, 128,
    96, 116, 136, 104, 124, 140, 108, 44, 12, 120, 56, 36, 148, 64, 52, 156, 76, 64, 160, 96, 84, 172, 104, 96,
    172, 124, 100, 184, 128, 116, 48, 72, 32, 0, 100, 4, 56, 96, 56, 80, 136, 76, 106, 132, 104, 84, 130, 80,
    120, 148, 118, 24, 20, 12,
])

ARENAS = [
    (18, "deck", "배 갑판 — 해전 일기토가 쓰는 마당"),
    (20, "field", "초원"),
    (22, "wood", "숲"),
    (24, "sand", "사막"),
    (26, "tavern", "술집"),
    (28, "mosque", "이슬람 광장"),
    (30, "temple", "일본 절"),
]


def png(path, width, height, rows):
    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    raw = b"".join(b"\x00" + row for row in rows)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n"
                + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw))
                + chunk(b"IEND", b""))


def paint(pixels, palette, width, height, base, skip=0, rgb=False):
    """색인 그림에 색표를 얹는다.

    <b>칸 차례가 색표마다 다르다.</b> 파일에 든 팔레트 파트는 (파랑, 빨강, 초록)이고,
    공용 색표(COMMON, game_palette.h 를 옮긴 것)는 (빨강, 초록, 파랑)이다. 이것을 안
    가리고 파일 차례로 읽으면 색이 한 칸씩 돌아가 <b>나무빛이 보랏빛으로</b> 나온다 —
    눈금판이 그렇게 어긋나 있었다.
    """
    rows = []
    for y in range(height):
        row = bytearray()
        for x in range(width):
            slot = (pixels[skip + y * width + x] - base) & 0xFF
            three = palette[slot * 3:slot * 3 + 3]
            if rgb:
                red, green, blue = three
            else:
                blue, red, green = three
            row += bytes((red, green, blue))
        rows.append(bytes(row))
    return rows



#: 싸움꾼 몸짓 — 파트 0·2·4…16 이 벌 아홉이고 팔레트가 그 다음 홀수 파트다.
#: 한 벌이 144x136 짜리 33장(646272 = 144*136*33)이고, 색인은 <b>160</b> 을 뺀다.
#: 서른세 장을 다 뽑는다 — 치고 막고 지고 이기는 몸짓이 골고루 흩어져 있다.
FIGHTER_W, FIGHTER_H, FIGHTER_FRAMES = 144, 136, 33
FIGHTER_BASE = 160
FIGHTER_KEEP = range(FIGHTER_FRAMES)


def clear(path, key=(255, 0, 255)):
    """마젠타 바탕을 비운다 — 싸움꾼은 마당 위에 얹히므로 테가 있으면 안 된다."""
    from PIL import Image
    art = Image.open(path).convert("RGBA")
    art.putdata([(0, 0, 0, 0) if p[:3] == key else p for p in art.getdata()])
    art.save(path)


def fighters(cds):
    for kit in range(9):
        art = cds.decode(kit * 2)
        palette = cds.decode(kit * 2 + 1)
        for frame in FIGHTER_KEEP:
            skip = frame * FIGHTER_W * FIGHTER_H
            rows = paint(art, palette, FIGHTER_W, FIGHTER_H, FIGHTER_BASE, skip)
            where = os.path.join(OUT, "duel-fighter-%d-%02d.png" % (kit, frame))
            png(where, FIGHTER_W, FIGHTER_H, rows)
            clear(where)
        print("duel-fighter-%d-*.png  %dx%d x%d"
              % (kit, FIGHTER_W, FIGHTER_H, len(FIGHTER_KEEP)))


#: 체력 막대 조각이 앉은 자리 — 눈금판 파트의 <b>앞 16바이트</b>다.
#:
#: 게임은 이 조각을 1점 폭 x 8점 높이로 72번까지 찍어 막대를 지우고 칠한다.
#:   0x004A7279  push 0   ; 자리 0  → 빨강 (이번에 깎인 자리)
#:   0x004A71D0  push 8   ; 자리 8  → 빈 칸 (나뭇결)
#:   0x004A709B  push 16  ; 자리 16 → 눈금판 384x112
#: 파랑(가득 찬 막대)만은 조각이 따로 없고 <b>눈금판 그림에 이미 그려져</b> 있어
#: 막대 자리(112, 52)에서 한 칸 오려 낸다.
BAR_H = 8
BAR_HURT_AT, BAR_EMPTY_AT = 0, 8
BAR_FULL_X, BAR_FULL_Y = 112, 52


def bars(panel):
    """체력 막대 조각 셋을 1x8 로 뽑는다 — 빨강 · 빈 칸 · 파랑."""
    def one(name, rows, what):
        png(os.path.join(OUT, "duel-bar-%s.png" % name), 1, BAR_H, rows)
        print("duel-bar-%s.png  %s" % (name, what))

    for name, at, what in (("hurt", BAR_HURT_AT, "맞은 자리(빨강)"),
                           ("empty", BAR_EMPTY_AT, "빈 칸(나뭇결)")):
        one(name, paint(panel[at:at + BAR_H], COMMON, 1, BAR_H, 0, rgb=True), what)

    full = []
    for dy in range(BAR_H):
        slot = panel[PANEL_SKIP + (BAR_FULL_Y + dy) * PANEL_W + BAR_FULL_X]
        red, green, blue = COMMON[slot * 3:slot * 3 + 3]
        full.append(bytes((red, green, blue)))
    one("full", full, "가득 찬 자리(파랑)")


def main():
    os.makedirs(OUT, exist_ok=True)
    cds = ls12.Ls12.open(os.path.join(GAME, "FIGHTER.CDS"))
    panel = cds.decode(32)

    fighters(cds)

    bars(panel)

    # 눈금판은 마당마다가 아니라 <b>한 장</b>이다 — 공용 색표를 쓰므로 마당을 안 탄다.
    png(os.path.join(OUT, "duel-panel.png"), PANEL_W, PANEL_H,
        paint(panel, COMMON, PANEL_W, PANEL_H, 0, PANEL_SKIP, rgb=True))
    print("duel-panel.png  눈금판(공용 색표)")

    for part, name, what in ARENAS:
        art = cds.decode(part)
        palette = cds.decode(part + 1)

        png(os.path.join(OUT, "duel-%s.png" % name),
            ARENA_W, ARENA_H, paint(art, palette, ARENA_W, ARENA_H, 74))
        print("duel-%s.png  %s" % (name, what))

        # <b>눈금판은 제 팔레트가 없어 마당 것을 같이 쓴다.</b> 그래서 마당마다 빛깔이
        # 다르다 — 갑판 것 하나만 뽑아 두면 초원 판에 갑판 빛깔이 얹혀 보랏빛이 된다.
        # 마당마다 한 장씩 뽑는다.



if __name__ == "__main__":
    main()
