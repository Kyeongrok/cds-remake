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
            j = string_end(src, i)
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


def string_end(src, i):
    """i 에서 시작하는 글("…" · $"…" · @"…" · $@"…")이 끝난 다음 자리.

    $"…{식}…" 의 식 자리는 코드라, 그 속의 글(<c>{Josa(x, "은", "는")}</c> · <c>{(on ? "끄기" : "켜기")}</c>)과
    글자(<c>'"'</c>)를 따로 건너뛴다 — 따옴표에서 글이 끝난 것으로 보면 글이 조각나 깨진 틀(「)} 금화 …」)이 된다.
    """
    n = len(src)
    j = i
    while j < n and src[j] in '$@':
        j += 1
    interp = '$' in src[i:j]
    verbatim = '@' in src[i:j]
    j += 1
    while j < n:
        c = src[j]
        if verbatim and c == '"' and src.startswith('""', j):
            j += 2; continue
        if not verbatim and c == '\\':
            j += 2; continue
        if c == '"':
            return j + 1
        if not verbatim and c == '\n':
            return j          # 닫히지 않은 글 — 줄 끝에서 멈춘다
        if interp and c == '{':
            if src.startswith('{{', j):
                j += 2; continue
            j = hole_end(src, j + 1); continue
        j += 1
    return j


def hole_end(src, j):
    """보간 식 자리 속(여는 { 다음)에서 시작해 닫는 } 다음 자리."""
    n, depth = len(src), 1
    while j < n and depth:
        c = src[j]
        if c == '"' or (c in '$@' and j + 1 < n and src[j + 1] in '"$@'):
            j = string_end(src, j); continue
        if c == "'":
            k = j + 1
            while k < n and src[k] != "'":
                k += 2 if src[k] == '\\' else 1
            j = k + 1; continue
        if c == '{': depth += 1
        elif c == '}': depth -= 1
        j += 1
    return j


class _Match:
    """re.Match 꼴 — group(1) 머리, group(2) 몸."""
    def __init__(self, src, start, end):
        k = start
        while src[k] in '$@':
            k += 1
        self._prefix = src[start:k] or None
        self._body = src[k + 1:end - 1] if end > k + 1 and src[end - 1] == '"' else src[k + 1:end]
        self._start, self._end = start, end

    def group(self, i):
        return self._prefix if i == 1 else self._body

    def start(self):
        return self._start

    def end(self):
        return self._end


_HEAD = re.compile(r'(\$@|@\$|\$|@)?"')


class STR:
    """글 찾기 — 보간 자리 속 글까지 바르게 건넌다. 자리 속 글도 따로 하나씩 낸다(조사 · 단추 이름)."""

    @staticmethod
    def match(src, at):
        m = _HEAD.match(src, at)
        if not m:
            return None
        return _Match(src, at, string_end(src, at))

    @staticmethod
    def finditer(src, start=0, end=None):
        end = len(src) if end is None else end
        i = start
        while True:
            m = _HEAD.search(src, i, end)
            if not m:
                return
            at = m.start()
            if at > 0 and src[at - 1] == "'":   # '"' 글자
                i = at + 1; continue
            stop = string_end(src, at)
            found = _Match(src, at, stop)
            yield found
            if found.group(1) and '$' in found.group(1):
                # 자리 속 글 — 몸 안에서 다시 찾는다.
                body_start = at + len(found.group(1)) + 1
                k = body_start
                while k < stop - 1:
                    ch = src[k]
                    if ch == '{' and not src.startswith('{{', k):
                        h = hole_end(src, k + 1)
                        yield from STR.finditer(src, k + 1, h - 1)
                        k = h; continue
                    if ch == '{':
                        k += 2; continue
                    if ch == '\\' and '@' not in found.group(1):
                        k += 2; continue
                    k += 1
            i = stop


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
            j = hole_end(body, i + 1)
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
                   + glob.glob(os.path.join(ROOT, 'CdsHelper.Support', '**', '*.cs'), recursive=True)
                   + glob.glob(os.path.join(ROOT, 'CostaDelSol.Play', '**', '*.cs'), recursive=True)):
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
