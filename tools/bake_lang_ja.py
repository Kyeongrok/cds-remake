"""일본어 번역표(lang/ja.json)를 굽는다 — 한국어 글 → 일본어 글.

리메이크가 화면에 내는 한국어 글(exe-tables/*.json · CdsHelper/*.json)과 일본어판 CDS95.EXE 의 같은 표를
<b>같은 번호끼리</b> 짝짓는다. 두 판의 표는 줄 모양과 숫자가 같고 글 자리만 다르다(주소는 아래 JP 표).
한국어판에만 있는 줄(불국사 · 만파식적 따위)은 짝이 없어 한국어로 남는다.

쓰는 법:  python tools/bake_lang_ja.py <일본어판 CDS95.EXE>
이미 lang/ja.json 에 있는 손 번역은 지우지 않는다 — 구운 것은 빈 칸만 채운다.
"""
import json, os, struct, sys

import pefile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
JP_EXE = sys.argv[1]

pe = pefile.PE(JP_EXE, fast_load=True)
data = open(JP_EXE, 'rb').read()
base = pe.OPTIONAL_HEADER.ImageBase


def off(va):
    rva = va - base
    for s in pe.sections:
        if s.VirtualAddress <= rva < s.VirtualAddress + s.SizeOfRawData:
            return s.PointerToRawData + rva - s.VirtualAddress
    return None


def u32(va):
    o = off(va)
    return struct.unpack_from('<I', data, o)[0] if o is not None else 0


def text(va):
    """VA 에 놓인 0 끝 CP932 글."""
    o = off(va) if va else None
    if o is None:
        return ''
    end = data.find(b'\0', o)
    if end < 0 or end - o > 4000:
        return ''   # 글 자리가 아니다(빈 칸 · 숫자)
    return data[o:end].decode('cp932', 'replace').strip()


def row_text(table, stride, i, field):
    return text(u32(table + i * stride + field))


def ptr_text(array, i):
    return text(u32(array + i * 4))


def load(rel):
    with open(os.path.join(ROOT, rel), encoding='utf-8-sig') as f:
        d = json.load(f)
    return d.get('Data', d) if isinstance(d, dict) else d


pairs = {}
conflicts = []


def pair(ko, ja):
    if not isinstance(ko, str) or not ko.strip() or not ja:
        return
    ko = ko.strip()
    if ko == ja:
        return
    if ko in pairs and pairs[ko] != ja:
        conflicts.append((ko, pairs[ko], ja))
        return
    pairs[ko] = ja


# ── 이름 표 ──────────────────────────────────────────────────────────────
for i, n in enumerate(load('exe-tables/나라표.json')['Nations'][:78]):
    pair(n['Name'], row_text(0x004F8268, 24, i, 0))

goods = load('exe-tables/교역품표.json')
for i, g in enumerate(goods['Items'][:70]):
    pair(g['Name'], row_text(0x0050B560, 0x88, i, 0))
for i, c in enumerate(goods['CategoryNames'][:13]):
    pair(c, ptr_text(0x0055A99C, i))

hints = load('exe-tables/힌트표.json')
for i, h in enumerate(hints['Hints'][:185]):
    pair(h['Name'], row_text(0x004D3F38, 80, i, 0))
    k = u32(0x004D3F38 + i * 80 + 0x1C)
    pair(h.get('Text'), ptr_text(0x00524D08, k))
for i, c in enumerate(hints['CategoryNames'][:8]):
    pair(c, ptr_text(0x0051A230, i))

for i, it in enumerate(load('exe-tables/아이템표.json')['Items'][:285]):
    pair(it['Name'], row_text(0x004D9118, 28, i, 0))
for i, d in enumerate(load('exe-tables/아이템설명.json')['Descriptions'][:285]):
    pair(d, ptr_text(0x005309A8, i))

for i, d in enumerate(load('exe-tables/발견물표.json')['Discoveries'][:273]):
    pair(d['Name'], row_text(0x004CC0E8, 92, i, 0))
for i, d in enumerate(load('exe-tables/발견물설명.json')['Descriptions'][:273]):
    pair(d, ptr_text(0x0051AB68, i))

for i, d in enumerate(load('exe-tables/술표.json')['Drinks'][:55]):
    pair(d['Name'], row_text(0x004CBBC8, 0x14, i, 0))
    pair(d.get('Alias'), row_text(0x004CBBC8, 0x14, i, 0x10))

for i, b in enumerate(load('exe-tables/여급표.json')['Barmaids'][:127]):
    pair(b['Name'], row_text(0x004DC228, 40, i, 0))

# 소문은 여기서 짝짓지 않는다 — 줄 번호로 번역한다(tools/bake_rumor_ja.py 가 소문표.json 의 줄마다 Ja 칸을 굽는다).

for i, s in enumerate(load('exe-tables/후원자표.json')['Sponsors'][:81]):
    pair(s['Name'], row_text(0x0050EE88, 60, i, 0))

for i, f in enumerate(load('exe-tables/마을사람표.json')['Folk'][:275]):
    pair(f.get('Words'), row_text(0x004F2740, 0x20, i, 0x18))
    pair(f.get('Later'), row_text(0x004F2740, 0x20, i, 0x1C))

for i, b in enumerate(load('exe-tables/책표.json')['Books'][:257]):
    pair(b['Title'], row_text(0x004BC548, 0x58, i, 0))
    pair(b.get('Author'), row_text(0x004BC548, 0x58, i, 4))

blds = load('exe-tables/건물표.json')
for i, b in enumerate(blds['Buildings'][:1508]):
    pair(b['Name'], row_text(0x004DDD60, 0x38, i, 0))
    pair(b['Kind'], row_text(0x004DDD60, 0x38, i, 4))
for i, s in enumerate(blds['SkillNames'][:13]):
    pair(s, ptr_text(0x00519FF8, i))
for i, s in enumerate(blds['LanguageNames'][:14]):
    pair(s, ptr_text(0x0051A030, i))

names = load('exe-tables/이름표.json')
for i, s in enumerate(names['Family'][:48]):
    pair(s, ptr_text(0x0054A0DC, i))
for i, s in enumerate(names['Given'][:74]):
    pair(s, ptr_text(0x0054A19C, i))

# 직업 여덟(탐험가 …) — 인물밑표의 JobName 과 같은 말이다.
for i, j in enumerate(['탐험가', '발굴자', '사냥꾼', '정복자', '해적', '전도사', '상인', '군인']):
    pair(j, ptr_text(0x0051A088, i))

# 인물 이름(인물표 First/Last) — 일본어판 인물 줄 +0 이름 · +4 성.
for p in load('CdsHelper/인물표.json')['People'][:281]:
    i = p['Id']
    pair(p['First'], row_text(0x004F89B8, 0xCC, i, 0))
    pair(p['Last'], row_text(0x004F89B8, 0xCC, i, 4))

# 도시 이름 · 문화권 — 한국어는 CdsHelper/cities.json, 일본어는 도시 줄 +0.
cities = load('CdsHelper/cities.json')
for c in cities[:226]:
    pair(c['name'], row_text(0x004C1EC0, 136, c['id'], 0))

# ── 굽기 ─────────────────────────────────────────────────────────────────
out_path = os.path.join(ROOT, 'lang', 'ja.json')
try:
    with open(out_path, encoding='utf-8-sig') as f:
        old = json.load(f)
except (OSError, ValueError):
    old = {}
merged = dict(pairs)
merged.update({k: v for k, v in old.items() if v})   # 손 번역이 이긴다
with open(out_path, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(dict(sorted(merged.items())), f, ensure_ascii=False, indent=1)
    f.write('\n')

print(f'pairs {len(pairs)}, kept hand {len(old)}, total {len(merged)}, conflicts {len(conflicts)}')
for ko, a, b in conflicts[:15]:
    print('  conflict', ko, '|', a, '|', b)
