# -*- coding: utf-8 -*-
"""STORY0.CDS · STORY1.CDS 를 대본 편집기가 읽는 JSON 으로 뽑는다.

미리 만든 주인공 둘(EASY)의 이야기다. 그릇도 말도 DISEV.CDS 와 같아서
(<c>Ls12</c> 아카이브 · 같은 명령) 발견 이벤트 편집기가 그대로 읽는다.

판 1 꼴(파트 통째 16진)로 적는다 — 편집기가 열면서 지금 판으로 옮겨 적는다.

    python tools/extract_story.py "C:\\...\\대항해시대3"
"""
import json
import os
import struct
import sys

BOOKS = [("STORY0.CDS", "이야기0"), ("STORY1.CDS", "이야기1")]
OUT_DIR = "CdsHelper"


def decode(data, dic, idx):
    """LS12 파트 하나를 푼다(Ls12Reader.Decode 를 옮긴 것)."""
    comp, uncomp, start = struct.unpack_from(">III", data, 0x110 + idx * 12)
    if uncomp == 0:
        return b""
    if comp == uncomp:
        return data[start:start + uncomp]

    src = data[start:start + comp]
    total = comp * 8
    out = bytearray()
    bit = 0
    delta = 0

    def read_bit():
        nonlocal bit
        b = (src[bit >> 3] >> (7 - (bit & 7))) & 1
        bit += 1
        return b

    while len(out) < uncomp and bit < total:
        n = 0
        while True:
            b = read_bit()
            n += 1
            if b == 0 or bit >= total or n >= 31:
                break
        if n >= 31:
            break

        factor = 0
        for _ in range(n):
            if bit >= total:
                break
            factor = (factor << 1) | read_bit()
        code = ((1 << n) - 2) + factor

        if delta > 0:
            for _ in range(3 + code):
                if len(out) >= uncomp:
                    break
                out.append(out[len(out) - delta] if len(out) >= delta else 0)
            delta = 0
        elif code < 256:
            out.append(dic[code])
        else:
            delta = code - 256

    return bytes(out)


def parts(path):
    data = open(path, "rb").read()
    if data[:4] != b"Ls12":
        raise SystemExit("%s 가 Ls12 가 아닙니다" % path)

    dic = data[0x10:0x110]
    count = 0
    while struct.unpack_from(">I", data, 0x110 + count * 12)[0]:
        count += 1

    return [decode(data, dic, i) for i in range(count)]


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    os.makedirs(OUT_DIR, exist_ok=True)
    for name, cache in BOOKS:
        path = os.path.join(sys.argv[1], name)
        if not os.path.exists(path):
            print("%s 가 없습니다" % path)
            continue

        rows = [{"Index": i, "Hex": raw.hex(" ").upper()} for i, raw in enumerate(parts(path))]
        out = os.path.join(OUT_DIR, cache + ".json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump({"Stamp": "", "Data": {"Parts": rows}, "Source": name, "Version": 1},
                      f, ensure_ascii=False, indent=2)
        print("%s — 파트 %d → %s" % (name, len(rows), out))

    return 0


if __name__ == "__main__":
    sys.exit(main())
