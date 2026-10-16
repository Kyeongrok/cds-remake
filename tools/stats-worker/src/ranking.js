// 하드코어 랭킹 — /ranking 이 이 HTML 을 그대로 내고, 페이지가 /v1/ranking 에서 순위를 받아 그린다.

export const RANKING_HTML = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>대항해시대3 리메이크 — 하드코어 랭킹</title>
<style>
  body { margin: 0; padding: 24px; background: #1b1410; color: #f1e6d0; font-family: "Malgun Gothic", sans-serif; }
  h1 { margin: 0 0 4px; font-size: 22px; color: #f6d77a; }
  p { margin: 0 0 16px; color: #b9a98c; font-size: 13px; }
  table { border-collapse: collapse; width: 100%; max-width: 860px; }
  th, td { padding: 6px 10px; border-bottom: 1px solid #3a2c22; text-align: left; font-size: 14px; }
  th { color: #f6d77a; font-weight: normal; background: #2a1f18; }
  td.n { text-align: right; font-variant-numeric: tabular-nums; }
  td.marks { font-size: 12px; color: #e0a070; } td.pure { font-size: 12px; color: #8fcf8f; }
  tr:nth-child(1) td.rank { color: #ffd700; } tr:nth-child(2) td.rank { color: #c0c0c0; } tr:nth-child(3) td.rank { color: #cd7f32; }
  .empty { color: #b9a98c; padding: 16px 0; }
</style>
</head>
<body>
<h1>하드코어 랭킹</h1>
<p>하드코어 모드로 시작해 게임 오버가 될 때까지 발견한 발견물 수입니다. 같으면 더 이른 날짜에 해낸 쪽이 위입니다.<br>
「모드」 칸은 그 판에서 한 번이라도 켜거나 쉽게 바꾼 모드와 쓴 개발 기능입니다. 비어 있으면 원본 규칙 그대로 한 판입니다.</p>
<table>
  <thead><tr><th>순위</th><th>제독</th><th>나라</th><th class="n">발견물</th><th>게임 오버</th><th>까닭</th><th>모드</th><th>판</th></tr></thead>
  <tbody id="rows"><tr><td colspan="8" class="empty">불러오는 중…</td></tr></tbody>
</table>
<script>
  const CAUSES = { 11: '반란 · 일기토 · 감옥 등', 12: '함대 · 해전', 13: '육상전' };
  // 모드 열쇠 → 이름(CdsHelper.Game/Engine/HardcoreRules.cs). 「이름=값」은 그 값으로 바꾼 것이다.
  const MARKS = {
    SuezCanal: '수에즈 운하', NoWeight: '중량 없음', RichCap: '소지금 상한 10배', LongRest: '장기 휴양 120개월',
    SkipMinigames: '미니게임 건너뛰기', SphinxHelper: '스핑크스 도우미', DuelImmortal: '일기토 불사', AutoFundRaise: '자금 증가 자동',
    Nobility: '작위 제도', LandAutoWalk: '육지 자동이동', SpecialOrder: '특별주문', MiniMapYet: '미니맵 미발견 표시',
    ShowDiscoveryMapMenu: '발견물 지도', RerollOnJob: '직업 누르면 다시 굴림', MonsterRetry: '괴물 다시 만나기',
    SeaRaidScale: '해적 출현 배수', MutinyRate: '반란 확률', PortDays: '출입 일수', PersonRollDays: '인물 이동 간격',
    PersonMoveOdds: '인물 이동 확률', PersonRestDays: '인물 휴식 일수', InfoLevel: '정보 제공 등급',
    'dev:ctrl-click': 'Ctrl+클릭 배 놓기', 'dev:item': '개발: 아이템 넣기', 'dev:quest': '개발: 이야기 상태',
    'dev:ruse': '개발: 묘책 확률', 'dev:scenario': '개발: 시나리오', tampered: '세이브 변조',
  };
  const markName = (m) => {
    if (m.startsWith('edited:')) return '표 수정: ' + m.slice(7);
    const [k, v] = m.split('=');
    return (MARKS[k] || k) + (v !== undefined ? '(' + v + ')' : '');
  };
  const esc = (s) => String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  fetch('/v1/ranking').then((r) => r.json()).then(({ ranking }) => {
    const body = document.getElementById('rows');
    if (!ranking.length) { body.innerHTML = '<tr><td colspan="8" class="empty">아직 기록이 없습니다.</td></tr>'; return; }
    body.innerHTML = ranking.map((r, i) =>
      '<tr><td class="rank">' + (i + 1) + '</td><td>' + esc(r.name) + '</td><td>' + esc(r.nation) + '</td>'
      + '<td class="n">' + r.discoveries + '</td><td>' + esc(r.died) + '</td><td>' + esc(CAUSES[r.cause] || '') + '</td>'
      + (r.marks.length ? '<td class="marks">' + r.marks.map((m) => esc(markName(m))).join(', ') + '</td>' : '<td class="pure">없음(순수)</td>')
      + '<td>' + esc(r.version) + '</td></tr>').join('');
  }).catch(() => {
    document.getElementById('rows').innerHTML = '<tr><td colspan="8" class="empty">불러오지 못했습니다.</td></tr>';
  });
</script>
</body>
</html>
`;
