# -*- coding: utf-8 -*-
"""WAVES.CDS 의 효과음 쉰 개를 asset/sfx 로 뽑는다.

게임 폴더가 없어도 소리가 나게 미리 풀어 두는 것이다(배 그림 asset/ship,
아이템 그림 asset/item 과 같은 길이다). 파일 이름은 <b>파트 번호</b>고,
사운드 ID 는 여기에 28 을 더한 값이다(WaveBank.FirstSoundId).

    python tools/extract_sfx.py "C:\\...\\대항해시대3"
"""
import os
import struct
import sys

PART_COUNT = 50
OUT_DIR = os.path.join("asset", "sfx")


def decode(data, dic, idx):
    """LS12 파트 하나를 푼다. Ls12Reader.Decode 를 그대로 옮긴 것이다."""
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


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    path = os.path.join(sys.argv[1], "WAVES.CDS")
    data = open(path, "rb").read()
    if data[:4] != b"Ls12":
        print("%s 가 Ls12 가 아닙니다" % path)
        return 1

    dic = data[0x10:0x110]
    os.makedirs(OUT_DIR, exist_ok=True)

    total = 0
    for part in range(PART_COUNT):
        wav = decode(data, dic, part)
        if len(wav) < 12 or wav[:4] != b"RIFF":
            print("파트 %2d — RIFF 가 아닙니다(%d바이트)" % (part, len(wav)))
            continue
        out = os.path.join(OUT_DIR, "sfx-%02d.wav" % part)
        open(out, "wb").write(wav)
        total += len(wav)

    print("효과음 %d개 · %.1fMB → %s" % (PART_COUNT, total / 1024 / 1024, OUT_DIR))
    return 0


if __name__ == "__main__":
    sys.exit(main())
