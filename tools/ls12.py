# -*- coding: utf-8 -*-
"""
KOEI LS11/Ls12 압축 아카이브 리더. 대항해시대3의 .CDS 파일 대부분이 이 형식이다
(WORLD.CDS / SAVEDATA.CDS / ACCDATA.CDS 만 예외로 날것).

CdsHelper.Support/Local/Helpers/Ls12Reader.cs 를 그대로 옮긴 것이다. 앱은 C# 쪽을 쓰고,
이 파일은 asset 을 뽑는 tools/*.py 가 쓴다. 디코드만 있다 — CDS 를 읽기만 한다.

파일 구조
    0x000  매직 "Ls12"(또는 "LS11") + 공백 채움      16바이트
    0x010  사전 dictionary[256]                     256바이트
    0x110  파트 표 — 12바이트씩 N개 (전부 빅엔디안)
             +0 압축크기  +4 원본크기  +8 시작주소
           4바이트 0 = 표 끝
           데이터 블록들

압축은 가변길이 비트코드다. code < 256 이면 사전에서 바이트 하나를 내고,
code >= 256 이면 거리(code-256)를 받아 둔 뒤 다음 code 로 길이(3+code)만큼 뒤에서 복사한다.
"""
import struct

DICT_OFFSET = 0x10
TABLE_OFFSET = 0x110
MAX_PARTS = 512


class Ls12:
    def __init__(self, data):
        if len(data) < TABLE_OFFSET + 4:
            raise ValueError("파일이 너무 짧다")
        if data[:4] not in (b"LS11", b"Ls12"):
            raise ValueError("LS11/Ls12 가 아니다: %r" % data[:4])

        self._data = data
        self._dict = data[DICT_OFFSET:DICT_OFFSET + 256]
        self._parts = []

        pos = TABLE_OFFSET
        while pos + 12 <= len(data) and len(self._parts) < MAX_PARTS:
            comp, uncomp, off = struct.unpack_from(">III", data, pos)
            if comp == 0:                       # 4바이트 0 = 표 끝
                break
            self._parts.append((comp, uncomp, off))
            pos += 12
        if not self._parts:
            raise ValueError("파트가 없다")

    @classmethod
    def open(cls, path):
        with open(path, "rb") as f:
            return cls(f.read())

    def __len__(self):
        return len(self._parts)

    def part_size(self, index):
        """파트의 원본(압축 해제) 크기."""
        return self._parts[index][1] if 0 <= index < len(self._parts) else 0

    def decode(self, index):
        """파트 하나를 원본 크기만큼 풀어 돌려준다. 못 풀면 None."""
        if not (0 <= index < len(self._parts)):
            return None
        comp, uncomp, off = self._parts[index]

        # 파트 표의 값은 파일에서 그대로 읽은 것이라 파일 밖을 가리킬 수 있다.
        data = self._data
        if off >= len(data) or comp > len(data) - off or uncomp == 0:
            return None

        if comp == uncomp:                      # 무압축 저장
            return bytearray(data[off:off + uncomp])

        src = data[off:off + comp]
        total_bits = comp * 8
        bit_pos = 0
        out = bytearray(uncomp)
        out_pos = 0
        delta = 0

        while out_pos < uncomp and bit_pos < total_bits:
            # unary: 1 이 이어지는 동안 읽다가 0 을 만나면 멈춘다. 읽은 비트 수가 mask_len.
            mask_len = 0
            while True:
                bit = (src[bit_pos >> 3] >> (7 - (bit_pos & 7))) & 1
                bit_pos += 1
                mask_len += 1
                if bit == 0 or bit_pos >= total_bits or mask_len >= 31:
                    break

            # 31비트를 넘기면 code 계산이 넘쳐 버린다 — 정상 스트림에는 없는 일이라 여기서 끊는다.
            if mask_len >= 31:
                break

            factor = 0
            for _ in range(mask_len):
                if bit_pos >= total_bits:
                    break
                factor = (factor << 1) | ((src[bit_pos >> 3] >> (7 - (bit_pos & 7))) & 1)
                bit_pos += 1
            code = ((1 << mask_len) - 2) + factor

            if delta > 0:                       # 앞서 거리를 받아 뒀으면 이번엔 길이다
                for _ in range(3 + code):
                    if out_pos >= uncomp:
                        break
                    out[out_pos] = out[out_pos - delta] if out_pos >= delta else 0
                    out_pos += 1
                delta = 0
            elif code < 256:
                out[out_pos] = self._dict[code]
                out_pos += 1
            else:
                delta = code - 256

        return out if out_pos == uncomp else None


def palette(part, order="BRG"):
    """
    768바이트(256색) / 258바이트(86색) 팔레트를 (r,g,b) 리스트로 푼다.

    ★ 앞 2바이트는 머리말이 아니다 — 768 = 256x3, 258 = 86x3 으로 딱 떨어진다.
      2 부터 읽으면 색이 한 칸씩 밀려 형광색이 된다.
    ★ 바이트 차례는 (파랑, 빨강, 초록). 투명 자리(색인 64)는 어느 차례로 읽어도
      마젠타가 나오므로 그것으로는 차례를 가릴 수 없다 — 살색·흙색이 살아나는지를 봐야 한다.
    """
    idx = {"BRG": (1, 2, 0), "RGB": (0, 1, 2)}[order]
    n = len(part) // 3
    return [(part[i * 3 + idx[0]], part[i * 3 + idx[1]], part[i * 3 + idx[2]])
            for i in range(n)]
