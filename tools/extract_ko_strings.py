"""코드 속 한국어 글을 뽑는다 — 번역할 목록(lang/ko-strings.json).

C# 의 "…" · $"…" · @"…" 글 가운데 한글이 든 것을 모은다. 주석과 Debug 출력은 뺀다.
$"…{식}…" 은 식 자리를 {0} {1} … 로 바꾼 <b>틀</b>로 적는다 — 런타임(Lang.T)이 이 틀로 맞춰 본다.
같은 글은 한 번만 적고, 처음 나온 자리(파일:줄)를 붙인다.

쓰는 법: python tools/extract_ko_strings.py
"""
import glob, json, os, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
HANGUL = re.compile(r'[가-힣]')
SKIP_FILES = ('DevDialog', 'DisevEditorDialog', 'ScenarioEditDialog', 'CustomBgmDialog')  # 개발 쪽 창은 뒤로 미룬다


def strip_comments(src):
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if src.startswith('//', i):
            j = src.find('\n', i)
            i = n if j < 0 else j
            continue
        if src.startswith('/*', i):
            j = src.find('*/', i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == '"' or (c in '$@' and i + 1 < n and src[i + 1] in '"$@'):
            # 글 하나를 통째로 건넌다(아래 scan 이 다시 읽는다).
            j = i
            while j < n and src[j] in '$@':
                j += 1
            verbatim = '@' in src[i:j]
            j += 1
            while j < n:
                if verbatim and src[j] == '"' and j + 1 < n and src[j + 1] == '"':
                    j += 2
                    continue
                if not verbatim and src[j] == '\\':
                    j += 2
                    continue
                if src[j] == '"':
                    j += 1
                    break
                j += 1
            out.append(src[i:j])
            i = j
            continue
        if c == "'":
            j = i + 1
            while j < n and src[j] != "'":
                j += 2 if src[j] == '\\' else 1
            out.append(src[i:j + 1])
            i = j + 1
            continue
        out.append(c)
        i += 1
    return ''.join(out)


STR = re.compile(r'(\$@|@\$|\$|@)?"((?:[^"\\\n]|\\.|"")*)"')


def template(prefix, body):
    """$"…{식}…" 를 {0} … 틀로. 이스케이프는 실제 글자로 푼다."""
    interp = prefix and '$' in prefix
    verbatim = prefix and '@' in prefix
    text, args, i = [], 0, 0
    while i < len(body):
        c = body[i]
        if interp and c == '{':
            if body.startswith('{{', i):
                text.append('{'); i += 2; continue
            depth, j = 1, i + 1
            while j < len(body) and depth:
                if body[j] == '{': depth += 1
                elif body[j] == '}': depth -= 1
                j += 1
            text.append('{%d}' % args); args += 1; i = j; continue
        if interp and body.startswith('}}', i):
            text.append('}'); i += 2; continue
        if not verbatim and c == '\\' and i + 1 < len(body):
            e = body[i + 1]
            text.append({'n': '\n', 't': '\t', '"': '"', '\\': '\\', 'r': '\r', '0': '\0'}.get(e, '\\' + e))
            i += 2; continue
        if verbatim and body.startswith('""', i):
            text.append('"'); i += 2; continue
        text.append(c); i += 1
    return ''.join(text), args


found = {}
for path in sorted(glob.glob(os.path.join(ROOT, 'CdsHelper.Game', '**', '*.cs'), recursive=True)
                   + glob.glob(os.path.join(ROOT, 'CdsHelper.Support', '**', '*.cs'), recursive=True)):
    if any(s in path for s in SKIP_FILES):
        continue
    src = open(path, encoding='utf-8-sig').read()
    clean = strip_comments(src)
    rel = os.path.relpath(path, ROOT).replace('\\', '/')
    for m in STR.finditer(clean):
        prefix, body = m.group(1) or '', m.group(2)
        if not HANGUL.search(body):
            continue
        line_start = clean.rfind('\n', 0, m.start()) + 1
        line_end = clean.find('\n', m.end())
        line = clean[line_start:line_end if line_end > 0 else None]
        if 'Debug.WriteLine' in line or 'Trace.' in line:
            continue
        text, args = template(prefix, body)
        if text not in found:
            lineno = clean.count('\n', 0, m.start()) + 1
            found[text] = {'at': f'{rel}:{lineno}', 'args': args}

out = os.path.join(ROOT, 'lang', 'ko-strings.json')
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(found, f, ensure_ascii=False, indent=1)
    f.write('\n')
print(len(found), 'strings,', sum(1 for v in found.values() if v['args']), 'templates,',
      sum(len(k) for k in found), 'chars')
