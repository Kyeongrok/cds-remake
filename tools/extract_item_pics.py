# -*- coding: utf-8 -*-
"""
ITEM.CDS 에서 아이템·교역품 그림을 떠서 asset/item/*.png 로 저장한다.

ITEM.CDS 는 LS12 아카이브다. 파트가 둘씩 짝을 이룬다.

    파트 2p       14400바이트 = 120x120, 8bpp 색인, 위에서 아래로   (p = 0~205)
    파트 2p+1       258바이트 = 86색 팔레트, 한 색이 3바이트

색인을 색으로 바꾸는 규칙은 그림마다 팔레트가 따로 붙는다는 것 하나로 갈린다.

    색인 >= 160   이 그림 제 팔레트. k = 색인-160 자리의 3바이트가 (파랑, 빨강, 초록).
                  86색이 색인 160~245 를 딱 채운다.
    색인 <  160   게임 공용 색표. 그림에는 10~73 만 나온다.

팔레트 성분은 6비트 DAC 값을 2비트 올린 것이라 최대가 252다. 255 로 늘리지 않는다 —
초상화·손님 그림과 톤을 맞추려는 것이다.

그림 번호는 **아이템 번호와 1:1 이 아니다**. 어느 아이템이 몇 번 그림을 쓰는지는
CDS_95.EXE 의 아이템 레코드표(파일오프셋 0x0FBB58, 28바이트 x 286)의 `+0x04` 에 있다.
99개는 그림이 없고(-1), 한 그림을 여럿이 나눠 쓰기도 한다(58번은 아이템 12개).
그래서 파일 이름은 아이템 번호가 아니라 **그림 번호**로 붙인다.

교역품 70종은 그림 134~203 에 이름 차례 그대로 놓여 있다(0 밀=134 … 69 노예=203).

이 규칙은 cds95-mod 의 CharacterUtilKR/src/itempic.c 가 그림 206장을 다 풀어 확인한
것을 그대로 따랐다.

쓰는 법:
    python tools/extract_item_pics.py --game "C:\\...\\대항해시대3"

PIL(pillow) 이 있어야 한다.
"""
import argparse
import os
import re
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ls12  # noqa: E402

W = H = 120
SIZE = W * H
PAL_BASE = 160          # 그림 제 팔레트가 얹히는 첫 색인
PAL_BYTES = 258         # 86색 x 3바이트

# 아이템 레코드표 — 그림 번호를 얻어 README 에 "어느 아이템이 쓰는지" 를 적는다.
REC_OFF = 0x0FBB58
REC_N = 286
REC_SZ = 28

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def game_palette():
    """앱의 GamePalette.cs 에서 공용 색표를 읽는다 — 값을 두 군데 두지 않으려고."""
    path = os.path.join(ROOT, "CdsHelper.Game", "Local", "Helpers", "GamePalette.cs")
    src = open(path, encoding="utf-8").read()
    body = src.split("private static readonly byte[] Low =")[1].split("];")[0]
    nums = [int(x) for x in re.findall(r"\d+", body)]
    pal = [(nums[i * 3], nums[i * 3 + 1], nums[i * 3 + 2]) for i in range(len(nums) // 3)]
    return pal + [(255, 0, 255)] * (256 - len(pal))


def item_records(game_dir):
    """CDS_95.EXE 의 아이템 레코드에서 (이름, 그림번호) 를 읽는다. 못 읽으면 빈 목록."""
    path = os.path.join(game_dir, "CDS_95.EXE")
    if not os.path.exists(path):
        return []
    d = open(path, "rb").read()

    pe = struct.unpack_from("<I", d, 0x3C)[0]
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    optsize = struct.unpack_from("<H", d, pe + 20)[0]
    base = struct.unpack_from("<I", d, pe + 24 + 28)[0]
    so = pe + 24 + optsize
    secs = []
    for i in range(nsec):
        o = so + i * 40
        vs, va, rs, raw = struct.unpack_from("<IIII", d, o + 8)
        secs.append((base + va, vs, raw, rs))

    def text(va, limit=80):
        for sva, vs, raw, rs in secs:
            if sva <= va < sva + vs:
                o = raw + (va - sva)
                if o >= raw + rs:
                    return None
                end = d.find(b"\0", o, o + limit)
                try:
                    return d[o:end].decode("cp949") if end > 0 else None
                except UnicodeDecodeError:
                    return None
        return None

    out = []
    for i in range(REC_N):
        namep, pic = struct.unpack_from("<Ii", d, REC_OFF + i * REC_SZ)
        out.append((i, text(namep) or "", pic))
    return out


def decode(archive, pic, shared):
    """그림 한 장을 (r,g,b) 바이트열로 푼다. 못 풀면 None."""
    idx = archive.decode(2 * pic)
    if idx is None or len(idx) != SIZE:
        return None
    raw = archive.decode(2 * pic + 1)
    if raw is None or len(raw) < 3:
        return None

    own = ls12.palette(raw[:PAL_BYTES], order="BRG")
    out = bytearray(SIZE * 3)
    for i, v in enumerate(idx):
        k = v - PAL_BASE
        r, g, b = own[k] if 0 <= k < len(own) else shared[v]
        out[i * 3] = r
        out[i * 3 + 1] = g
        out[i * 3 + 2] = b
    return bytes(out)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", required=True, help="게임 폴더 (ITEM.CDS 가 있는 곳)")
    ap.add_argument("--out", default=None, help="저장할 폴더 (기본 asset/item)")
    args = ap.parse_args()

    path = os.path.join(args.game, "ITEM.CDS")
    if not os.path.exists(path):
        sys.exit("ITEM.CDS 가 없다: " + path)

    try:
        from PIL import Image
    except ImportError:
        sys.exit("pillow 가 있어야 한다: pip install pillow")

    archive = ls12.Ls12.open(path)
    count = len(archive) // 2
    shared = game_palette()
    out_dir = args.out or os.path.join(ROOT, "asset", "item")
    os.makedirs(out_dir, exist_ok=True)

    print("파트 %d개 → 그림 %d장" % (len(archive), count))

    ok, bad = 0, []
    for pic in range(count):
        rgb = decode(archive, pic, shared)
        if rgb is None:
            bad.append(pic)
            continue
        Image.frombytes("RGB", (W, H), rgb).save(
            os.path.join(out_dir, "item-%03d.png" % pic))
        ok += 1

    print("떴다 %d장, 못 떴다 %d장 %s" % (ok, len(bad), bad[:10] if bad else ""))

    # 어느 아이템이 어느 그림을 쓰는지 README 에 적어 둔다.
    recs = item_records(args.game)
    users = {}
    for i, name, pic in recs:
        if pic >= 0:
            users.setdefault(pic, []).append("%d %s" % (i, name))

    lines = [
        "# 아이템·교역품 그림",
        "",
        "`ITEM.CDS` 를 뽑은 것이다. `tools/extract_item_pics.py` 가 만든다.",
        "",
        "한 장이 120x120 이고 모두 %d장이다. 파일 이름은 **그림 번호**다 —" % ok,
        "아이템 번호가 아니다. 어느 아이템이 몇 번을 쓰는지는 CDS_95.EXE 의",
        "아이템 레코드표(`0x0FBB58`, 28바이트 x 286)의 `+0x04` 가 낸다.",
        "",
        "교역품 70종은 134~203 에 이름 차례 그대로 놓여 있다(0 밀=134 … 69 노예=203).",
        "",
    ]
    if recs:
        noPic = sum(1 for _, _, p in recs if p < 0)
        shared_pics = {p: u for p, u in users.items() if len(u) > 1}
        lines += [
            "## 아이템 -> 그림",
            "",
            "아이템 %d개 중 그림이 있는 것 %d개, 없는 것 %d개." % (
                len(recs), len(recs) - noPic, noPic),
            "그림 %d장을 아이템이 나눠 쓴다(한 장을 여럿이 쓰기도 한다)." % len(users),
            "",
            "### 여럿이 나눠 쓰는 그림",
            "",
            "| 그림 | 쓰는 아이템 |",
            "|---|---|",
        ]
        for p in sorted(shared_pics, key=lambda k: -len(shared_pics[k]))[:12]:
            lines.append("| %03d | %s |" % (p, ", ".join(shared_pics[p])))
    open(os.path.join(out_dir, "README.md"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("README.md 적었다")


if __name__ == "__main__":
    main()
