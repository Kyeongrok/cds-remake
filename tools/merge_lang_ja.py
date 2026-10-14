"""번역 결과(JSON {한국어: 일본어})를 lang/ja.json 에 합친다.

자리({0} …)가 한국어에 없는 번호를 쓰는 번역은 버린다 — 런타임 틀 맞추기가 빈 칸을 넣게 된다.
이미 있는 열쇠는 덮어쓴다(새 번역이 이긴다). 빈 번역은 넣지 않는다.

쓰는 법: python tools/merge_lang_ja.py out-0.json out-1.json …
"""
import json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
path = os.path.join(ROOT, 'lang', 'ja.json')
table = json.load(open(path, encoding='utf-8-sig'))
added = replaced = dropped = 0
HOLE = re.compile(r'\{(\d+)\}')
for f in sys.argv[1:]:
    for ko, ja in json.load(open(f, encoding='utf-8-sig')).items():
        if not isinstance(ja, str) or not ja.strip() or ja == ko:
            continue
        if not set(HOLE.findall(ja)) <= set(HOLE.findall(ko)):
            dropped += 1
            print('  drop (자리 어긋남):', ko[:40], '→', ja[:40])
            continue
        if ko in table:
            replaced += table[ko] != ja
        else:
            added += 1
        table[ko] = ja
with open(path, 'w', encoding='utf-8', newline='\n') as out:
    json.dump(dict(sorted(table.items())), out, ensure_ascii=False, indent=1)
    out.write('\n')
print(f'added {added}, replaced {replaced}, dropped {dropped}, total {len(table)}')
