# -*- coding: utf-8 -*-
"""백동수 모드(「대항해시대3 백동수편」)의 이야기 대본 story7.CDS 를 시나리오 책 JSON 으로 뽑는다.

모드는 story7.CDS 를 초심자 이야기 자리(STORY1)에 쓴다. 앞 일곱 파트(0~6)는 에밀리오 이야기가
남은 것이다 — 모드의 시작 세이브가 이야기 카운터를 7 로 두고 시작하므로 닿지 않는다. 그래서
파트 7 부터 떼어 0 번으로 다시 매긴다(카운터는 「+1」만 하므로 번호를 당겨도 흐름이 같다).

대사는 한국어판 꼴 그대로다 — 화자 이름은 CP932, 말은 CP949. 판 1 꼴(파트 통째 16진)로 적고,
게임 · 편집기가 열면서 지금 판으로 옮겨 적는다.

    python tools/extract_baekdongsu.py "C:\\Users\\...\\백동수" [책 번호=2]
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ls12  # noqa: E402

FIRST_PART = 7          # 백동수 이야기가 시작하는 파트(모드 세이브의 이야기 카운터)
OUT_DIR = "tools/baekdongsu"   # 공개 배포하지 않는다 — 이 PC 설치 앱의 exe-tables 에만 복사한다(README)


def fix_empty_bodies(raw):
    """본문 자리가 0 인 슬롯을 파트 끝에 붙인 끝(FF) 한 바이트로 돌린다.

    모드 편집기는 「이 도시에서는 이벤트 없음」을 본문 0 으로 적는다(원본 203 · 바르셀로나) — 조건이 맞으면
    아무것도 안 하고 끝나 다음 슬롯을 막는다. 본문 0 은 슬롯 표 안을 가리켜 우리 파서가 못 읽으므로
    파트 끝에 FF 를 붙여 그리로 돌린다. 끝에 붙이므로 다른 자리는 안 움직인다.
    """
    data = bytearray(raw)
    count = int.from_bytes(data[2:4], "little")
    end = None
    for s in range(count):
        at = 6 + s * 4
        if int.from_bytes(data[at:at + 2], "little") == 0:
            if end is None:
                end = len(data) - 4
                data.append(0xFF)
            data[at:at + 2] = end.to_bytes(2, "little")
    return bytes(data)


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    src = os.path.join(sys.argv[1], "story7.CDS")
    book = int(sys.argv[2]) if len(sys.argv) > 2 else 2
    archive = ls12.Ls12(open(src, "rb").read())
    count = len(archive._parts)

    rows = []
    for i in range(FIRST_PART, count):
        raw = fix_empty_bodies(archive.decode(i))
        rows.append({"Index": i - FIRST_PART, "Hex": raw.hex(" ").upper()})

    out = os.path.join(OUT_DIR, "scenario-book%d.json" % book)
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"Stamp": "", "Data": {"Parts": rows}, "Source": "story7.CDS (백동수편)", "Version": 1},
                  f, ensure_ascii=False, indent=2)
    print("story7.CDS — 파트 %d 중 %d~ → %s (%d 파트)" % (count, FIRST_PART, out, len(rows)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
