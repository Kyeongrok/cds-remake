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


def template(prefix, body, first=0):
    """$"…{식}…" 를 {0} … 틀로. 이스케이프는 실제 글자로 푼다. 자리 번호는 first 부터."""
    interp = prefix and '$' in prefix
    verbatim = prefix and '@' in prefix
    text, args, i = [], first, 0
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
    return ''.join(text), args - first


# 「"…" + "…"」 사슬 — 화면에는 이어진 한 글로 간다. 사이에 낀 짧은 값은 {n} 자리, Environment.NewLine 은 줄바꿈.
EXPR = re.compile(r'[A-Za-z_][\w.]*(?:\([^()+;\n]*\))?(?:\[[^\[\]+;\n]*\])?')
NEWLINE = re.compile(r'Environment\.NewLine')


def chain_at(clean, m):
    """m(글) 에서 시작하는 + 사슬을 한 틀로. 사슬이 아니면 None."""
    parts = []          # (kind, value) — 'lit' (prefix, body) | 'hole' | 'nl'
    before = re.search(r'([A-Za-z_][\w.]*(?:\([^()+;\n]*\))?)\s*\+\s*$', clean[max(0, m.start() - 120):m.start()])
    if before and not before.group(1).endswith('return'):
        parts.append(('hole', None))
    parts.append(('lit', (m.group(1) or '', m.group(2))))
    pos = m.end()
    while True:
        rest = re.match(r'\s*\+(?![+=])\s*', clean[pos:])
        if not rest:
            break
        at = pos + rest.end()
        lit = STR.match(clean, at)
        if lit:
            parts.append(('lit', (lit.group(1) or '', lit.group(2)))); pos = lit.end(); continue
        nl = NEWLINE.match(clean, at)
        if nl:
            parts.append(('nl', None)); pos = nl.end(); continue
        ex = EXPR.match(clean, at)
        if ex and ex.end() > at:
            parts.append(('hole', None)); pos = ex.end(); continue
        break
    if len(parts) < 2 or sum(1 for k, _ in parts if k == 'lit') < 1:
        return None, pos
    out, n = [], 0
    for kind, value in parts:
        if kind == 'hole':
            out.append('{%d}' % n); n += 1
        elif kind == 'nl':
            out.append('\n')
        else:
            text, used = template(value[0], value[1], n)
            out.append(text); n += used
    return ''.join(out), pos


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
        lineno = clean.count('\n', 0, m.start()) + 1
        if text not in found:
            found[text] = {'at': f'{rel}:{lineno}', 'args': args}
        # 이 글이 + 사슬의 머리면 이어진 글도 적는다(조각은 조각대로 둔다).
        if re.search(r'\+\s*$', clean[max(0, m.start() - 40):m.start()]) is None:
            joined, _ = chain_at(clean, m)
            if joined and joined != text and HANGUL.search(joined) and joined not in found:
                found[joined] = {'at': f'{rel}:{lineno}', 'args': joined.count('{'), 'chain': True}

out = os.path.join(ROOT, 'lang', 'ko-strings.json')
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(found, f, ensure_ascii=False, indent=1)
    f.write('\n')
print(len(found), 'strings,', sum(1 for v in found.values() if v['args']), 'templates,',
      sum(len(k) for k in found), 'chars')
