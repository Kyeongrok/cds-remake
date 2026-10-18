"""소문표(exe-tables/소문표.json)의 줄마다 일본어 본문(Ja)과 줄 번호(Id)를 굽는다.

소문은 줄 번호로 번역한다 — 한국어판과 일본어판 CDS95.EXE 의 소문표(24바이트 x 191)는 줄 차례가 같아서
같은 번호가 같은 소문이다. 예전에는 lang/ja.json 에 「한국어 글 → 일본어 글」로 넣어 화면에서 바꿨는데,
한국어 글을 고치면 열쇠가 바뀌어 번역이 끊겼다. 이제 게임(RumorTable)이 언어에 맞는 칸을 고른다.

쓰는 법:  python tools/bake_rumor_ja.py <일본어판 CDS95.EXE>
"""
import json, os, struct, sys

import pefile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, 'exe-tables', '소문표.json')

# 일본어판 소문표 — 한국어판 0x00525078 자리의 같은 표다(bake_lang_ja.py 와 같은 주소).
JP_TABLE, ROW, TEXT = 0x004D7F48, 0x18, 0x14

# 일본어판 표는 190줄이다 — 한국어판 마지막 줄(191번째, 불국사)은 한국어판에만 있는 소문이라 Ja 를 손으로 적어 둔다.
JP_COUNT = 190

exe = sys.argv[1]
pe = pefile.PE(exe, fast_load=True)
data = open(exe, 'rb').read()
base = pe.OPTIONAL_HEADER.ImageBase


def off(va):
    rva = va - base
    for s in pe.sections:
        if s.VirtualAddress <= rva < s.VirtualAddress + s.SizeOfRawData:
            return s.PointerToRawData + rva - s.VirtualAddress
    return None


def text(va):
    o = off(va) if va else None
    if o is None:
        return ''
    end = data.find(b'\0', o)
    if end < 0 or end - o > 4000:
        return ''
    return data[o:end].decode('cp932', 'replace').strip()


raw = open(PATH, 'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
doc = json.loads(raw.decode('utf-8-sig'))
rumors = doc['Data']['Rumors']

filled = 0
for i, r in enumerate(rumors):
    r['Id'] = i
    if i >= JP_COUNT:
        continue   # 한국어판에만 있는 줄 — 손으로 적어 둔 Ja 는 그대로 둔다
    at = off(JP_TABLE + i * ROW)
    jp_discovery = struct.unpack_from('<i', data, at)[0]
    if jp_discovery != r['Discovery']:
        sys.exit(f'{i}번 줄의 발견물이 다르다(한국어 {r["Discovery"]} · 일본어 {jp_discovery}) — 표 자리를 다시 본다')
    ja = text(struct.unpack_from('<I', data, at + TEXT)[0])
    if ja:
        r['Ja'] = ja
        filled += 1
    else:
        r.pop('Ja', None)

out = json.dumps(doc, ensure_ascii=False, indent=2) + '\n'
open(PATH, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out.encode('utf-8'))
print(f'{len(rumors)}줄 가운데 일본어 {filled}줄')
