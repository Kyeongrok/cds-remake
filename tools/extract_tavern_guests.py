# -*- coding: utf-8 -*-
"""
MPCG.CDS 에서 술집·여관 손님 그림을 떠서 asset/guest/*.png 로 저장한다.

MPCG.CDS 는 두 가지를 함께 담고 있다.

    파트 0        320x240   타원 테두리 선
    파트 1        320x240   타원 채움 마스크 (63 안 / 47 밖)
    파트 2k+2     320x240   타원 건물 사진 84장 (k = 0~83)
    파트 2k+3     768바이트  그 사진의 팔레트
    파트 170~315  손님 146명  ← 이 스크립트가 뜨는 것

팔레트를 색인대로 갈라 쓴다. **0~63 이 손님 몫, 64~149 가 건물 사진 몫**이다.
그리고 0~63 구간은 84개 팔레트 전부에서 한 바이트도 다르지 않다 — 손님 색은 어느
사진과 함께 읽든 같다. 그래서 손님 파트에는 제 팔레트가 안 붙어 있고, 아무 사진
팔레트(여기서는 파트 3)의 앞 64색을 쓰면 된다.

크기와 성별은 짐작하지 않는다 — **CDS_95.EXE 안 표 `0x0056E3A0`** 에 적혀 있다.
146행 x 12바이트로 `(성별, 폭, 높이)` 다. 성별은 0 이 남자, 1 이 여자.
폭은 48·56·64·66, 높이는 72·80·88·96·104 가 나온다. 크기가 다 64 의 배수라
`길이 / 64` 로 재면 폭이 64 가 아닌 넷(004·011·037·107)이 어긋난 줄무늬가 된다.
EXE 를 못 읽을 때만 행 간 상관으로 폭을 짐작한다.

문화권마다 쓰는 구간이 정해져 있다. 시작은 `0x0049D580(문화권)`, 개수는
`0x0049D500(문화권)` 이 낸다(둘 다 점프표를 쓰는 switch 다).

    1 북유럽    000~016 (17)     0 이베리아 · 2 지중해  017~037 (21)
    5 인도      038~054 (17)     6 중국                055~067 (13)
    3 아프리카  068~083 (16)     4 이슬람              084~096 (13)
    9 일본      097~107 (11)     8 동남아시아          108~118 (11)
    7 중앙아시아 119~129 (11)    10 아메리카           130~145 (16)

이베리아와 지중해는 같은 구간을 함께 쓴다. 합이 딱 146 이다.

문화권 이름은 도시 표 **`0x004D14B0`**(226행 x 136바이트, `+0x00` 이름 포인터 ·
`+0x20` 문화권)에서 맞춘 것이다. 게임은 그 값을 도시 레코드 `+0x58` 에 옮겨 놓고 쓴다
(도시 레코드 배열은 `0x005863A8`, 92바이트씩. BSS 라 파일에는 없다).

투명은 **색인 55** 다(건물 사진 쪽 64 와 다르다).

쓰는 법:
    python tools/extract_tavern_guests.py --game "C:\\...\\대항해시대3"

PIL(pillow) 이 있어야 한다.
"""
import argparse
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ls12 import Ls12, palette                              # noqa: E402

try:
    from PIL import Image
except ImportError:
    sys.exit("pillow 가 필요하다:  pip install pillow")

FIRST_GUEST = 170       # 손님이 시작되는 파트
PALETTE_PART = 3        # 손님 색(0~63)은 어느 사진 팔레트를 써도 같다
TRANSPARENT = 55        # 손님 그림의 비침 색인
WIDTHS = (40, 48, 56, 64, 66, 72, 80)

GUEST_TABLE = 0x0056E3A0    # 146행 x 12바이트 — (성별, 폭, 높이)
GUEST_COUNT = 146
IMAGE_BASE = 0x400000

# 문화권 → (시작, 개수). 0x0049D580 / 0x0049D500 의 switch 에서 읽은 것이다.
# 이름은 도시 표 0x004D14B0 의 +0x20(문화권)을 226개 도시에 대해 읽어 맞춘 것이다.
# 그림만 보고 붙였을 때는 인도/중국/이슬람/중앙아시아 넷이 틀렸다.
SPHERES = [
    (1, 0, 17, "북유럽"), (0, 17, 21, "이베리아"), (2, 17, 21, "지중해"),
    (5, 38, 17, "인도"), (6, 55, 13, "중국"), (3, 68, 16, "아프리카"),
    (4, 84, 13, "이슬람"), (9, 97, 11, "일본"), (8, 108, 11, "동남아시아"),
    (7, 119, 11, "중앙아시아"), (10, 130, 16, "아메리카"),
]

CITY_TABLE = 0x004D14B0     # 226행 x 136바이트 — +0x00 이름 포인터, +0x20 문화권


def read_table(game_dir):
    """
    CDS_95.EXE 의 표 0x0056E3A0 에서 (성별, 폭, 높이) 146행을 읽는다.
    EXE 가 없으면 None — 그때는 폭을 행 간 상관으로 짐작한다.
    """
    exe = os.path.join(game_dir, "CDS_95.EXE")
    if not os.path.exists(exe):
        return None
    with open(exe, "rb") as f:
        data = f.read()

    # PE 구역표를 훑어 VA 를 파일 오프셋으로 옮긴다
    lfanew = struct.unpack_from("<I", data, 0x3C)[0]
    nsec = struct.unpack_from("<H", data, lfanew + 6)[0]
    opt = struct.unpack_from("<H", data, lfanew + 20)[0]
    base = lfanew + 24 + opt
    rva = GUEST_TABLE - IMAGE_BASE
    for i in range(nsec):
        o = base + i * 40
        vsize, vaddr, rsize, raddr = struct.unpack_from("<IIII", data, o + 8)
        if vaddr <= rva < vaddr + max(vsize, rsize):
            off = raddr + (rva - vaddr)
            return [struct.unpack_from("<3I", data, off + k * 12) for k in range(GUEST_COUNT)]
    return None


def best_width(data):
    """
    표를 못 읽었을 때만 쓴다. 행 간 상관으로 폭을 잰다 — 진짜 폭에서는 윗줄과 아랫줄이
    가장 닮으므로 값이 가장 낮다. 크기가 64 의 배수라고 폭이 64 인 것은 아니다.
    """
    best, best_score = 64, float("inf")
    for w in WIDTHS:
        if len(data) % w:
            continue
        score = sum(abs(data[i] - data[i + w]) for i in range(len(data) - w))
        score /= len(data) - w
        if score < best_score:
            best, best_score = w, score
    return best


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="게임 폴더 (MPCG.CDS 가 있는 곳)")
    ap.add_argument("--out", default=None, help="저장할 폴더 (기본 asset/guest)")
    ap.add_argument("--sheet", action="store_true", help="한눈에 보는 모음 장도 함께 저장")
    args = ap.parse_args()

    path = os.path.join(args.game, "MPCG.CDS")
    if not os.path.exists(path):
        sys.exit("MPCG.CDS 가 없다: " + path)

    repo = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out_dir = args.out or os.path.join(repo, "asset", "guest")
    os.makedirs(out_dir, exist_ok=True)

    arc = Ls12.open(path)
    pal = palette(arc.decode(PALETTE_PART))

    table = read_table(args.game)
    print("크기는 %s 에서 읽는다" % ("EXE 표 0x%08X" % GUEST_TABLE if table else "행 간 상관(짐작)"))

    saved, odd, images = 0, [], []
    for part in range(FIRST_GUEST, len(arc)):
        data = arc.decode(part)
        if data is None:
            print("  파트 %d 를 못 풀었다" % part)
            continue

        n = part - FIRST_GUEST
        if table and n < len(table):
            _, w, h = table[n]
            if w * h != len(data):          # 표와 실제가 어긋나면 표를 믿지 않는다
                w = best_width(data)
                h = len(data) // w
        else:
            w = best_width(data)
            h = len(data) // w
        if w != 64:
            odd.append("%d(%dx%d)" % (part, w, h))

        img = Image.new("RGBA", (w, h))
        px = img.load()
        for y in range(h):
            row = y * w
            for x in range(w):
                i = data[row + x]
                px[x, y] = (0, 0, 0, 0) if i == TRANSPARENT else pal[i] + (255,)

        img.save(os.path.join(out_dir, "guest-%03d.png" % n))
        images.append(img)
        saved += 1

    print("손님 %d명을 %s 에 저장했다" % (saved, out_dir))
    if odd:
        print("폭이 64 가 아닌 것: " + " ".join(odd))

    if args.sheet:
        cols, cw, ch = 15, 74, 112
        rows = (len(images) + cols - 1) // cols
        sheet = Image.new("RGBA", (cols * cw, rows * ch), (30, 30, 34, 255))
        for k, im in enumerate(images):
            x = (k % cols) * cw + (cw - im.width) // 2
            y = (k // cols) * ch + (ch - im.height) // 2
            sheet.alpha_composite(im, (x, y))
        sheet_path = os.path.join(out_dir, "_sheet.png")
        sheet.save(sheet_path)
        print("모음 장: " + sheet_path)

    # 어느 번호가 어느 문화권인지 곁에 남긴다 — 번호만으로는 알 수 없다.
    with open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8") as f:
        f.write("# 술집·여관 손님\n\n")
        f.write("`MPCG.CDS` 파트 %d 부터를 뽑은 것이다. `tools/extract_tavern_guests.py` 가 만든다.\n\n"
                % FIRST_GUEST)
        f.write("손님 번호 = 파트 번호 - %d.\n\n" % FIRST_GUEST)

        f.write("## 문화권마다 쓰는 구간\n\n")
        f.write("시작은 `0x0049D580(문화권)`, 개수는 `0x0049D500(문화권)` 이 낸다.\n")
        f.write("이베리아와 지중해는 같은 구간을 함께 쓴다. 합이 딱 %d 이다.\n\n" % GUEST_COUNT)
        f.write("| 문화권 | 이름 | 손님 번호 | 개수 |\n|---|---|---|---|\n")
        for cul, start, n, name in sorted(SPHERES, key=lambda x: (x[1], x[0])):
            f.write("| %d | %s | %03d ~ %03d | %d |\n" % (cul, name, start, start + n - 1, n))
        f.write("\n이름은 도시 표 `0x%08X`(226행 x 136바이트, `+0x20` 이 문화권)에서 맞춘 것이다.\n"
                % CITY_TABLE)
        f.write("게임은 그 값을 도시 레코드 `+0x58` 에 옮겨 놓고 쓴다.\n")

        if table:
            f.write("\n## 크기와 성별\n\n")
            f.write("`CDS_95.EXE` 표 **`0x%08X`** 에 있다. %d행 x 12바이트로 `(성별, 폭, 높이)` 다.\n"
                    % (GUEST_TABLE, GUEST_COUNT))
            f.write("성별은 0 이 남자, 1 이 여자.\n\n")
            women = [i for i, r in enumerate(table) if r[0] == 1]
            f.write("여자 %d명: %s\n\n" % (len(women), ", ".join("%03d" % i for i in women)))
            sizes = {}
            for i, (_, w, h) in enumerate(table):
                sizes.setdefault((w, h), []).append(i)
            f.write("| 크기 | 몇 명 |\n|---|---|\n")
            for (w, h), who in sorted(sizes.items(), key=lambda kv: -len(kv[1])):
                f.write("| %dx%d | %d |\n" % (w, h, len(who)))

        f.write("\n비침은 색인 55, 팔레트는 사진 팔레트의 앞 64색(바이트 차례 파랑·빨강·초록)이다.\n")


if __name__ == "__main__":
    main()
