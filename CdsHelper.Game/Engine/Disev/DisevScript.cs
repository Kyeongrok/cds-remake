using System.Buffers.Binary;
using System.Text;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 발견 이벤트 덩이(조건·본문)를 사람이 읽을 명령으로 푼다.
/// </summary>
/// <remarks>
/// 뜯어 둔 것은 <c>github.com/dkenldlqfur/cds_disev_editor</c> 의
/// <c>Resources/dump_disev.py</c> 를 옮긴 것이다. <b>모르는 바이트는 지어내지 않는다</b> —
/// 짚이지 않는 자리는 「미확인」으로 두고 날바이트를 그대로 보인다. 그래야 고칠 때
/// 원본을 잃지 않는다.
///
/// 명령 꼴은 셋뿐이다.
/// <code>
///   0xFF                        덩이/갈래 끝
///   [창 플래그] 0A [CP949] 00   대사
///   그 밖                       <see cref="Forms"/> 표에 있는 고정 길이 명령
/// </code>
/// </remarks>
public static class DisevScript
{
    /// <summary>명령 한 꼴 — 앞머리 바이트와 길이, 그리고 상대 이동값이 있으면 그 자리.</summary>
    /// <param name="Signature">앞머리 바이트.</param>
    /// <param name="Length">이 명령이 차지하는 바이트 수.</param>
    /// <param name="Kind">사람이 읽을 이름.</param>
    /// <param name="JumpOffset">상대 이동값(u16)이 든 자리. 없으면 −1.</param>
    public readonly record struct Form(byte[] Signature, int Length, string Kind, int JumpOffset = -1);

    /// <summary>푼 명령 하나.</summary>
    /// <param name="Offset">파트 안의 자리.</param>
    /// <param name="Length">바이트 수.</param>
    /// <param name="Kind">갈래 이름. 못 짚으면 「미확인 명령/데이터」.</param>
    /// <param name="Text">사람이 읽을 풀이.</param>
    /// <param name="Hex">날바이트.</param>
    /// <param name="Known">표에 있는 명령인가.</param>
    public readonly record struct Op(int Offset, int Length, string Kind, string Text, string Hex, bool Known);

    private static byte[] Sig(params byte[] bytes) => bytes;

    /// <summary><c>43 2B~2E 1C</c> 비교 분기의 갈래 이름. 어떤 비교인지는 둘째 바이트에 있다.</summary>
    public const string CompareKind = "상태값 비교 분기";

    /// <summary>
    /// <c>43 2B~2E 1C [u16] [값 식] [u16]</c> 의 길이. 값 식 머리 바이트로 갈린다. 모르는 꼴이면 −1.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   1A [u32]          상수          12
    ///   14 [u32]          상수(옛 표기) 12
    ///   00 [u16]          u16 상수      10
    ///   1C [u16]          다른 상태값   10
    ///   20 [u32 폭][u32]  무작위        16
    /// </code>
    /// 예전에는 늘 12 로 읽어 10·16 꼴 뒤가 통째로 밀렸다(cds_disev_editor v1.0 이 짚었다).
    /// </remarks>
    public static int CompareLength(byte operand) => operand switch
    {
        0x1A or 0x14 => 12,
        0x00 or 0x1C => 10,
        0x20 => 16,
        _ => -1,
    };

    /// <summary>
    /// 아는 명령 꼴. <b>차례가 중요하다</b> — 앞머리가 겹치면 긴 쪽이 먼저 와야 한다.
    /// </summary>
    public static readonly Form[] Forms =
    [
        // 00 은 본문 하위 명령 묶음이다. 00 02 [u16] 를 먼저 잡지 않으면
        // 뒤의 02 0A 00 을 빈 대사로 잘못 읽는다.
        new(Sig(0x00, 0x01), 4, "DSTILL 이미지 표시"),
        new(Sig(0x00, 0x02), 4, "AVI 재생"),
        new(Sig(0x00, 0x1F), 4, "EVSTILL 이미지 표시"),
        // 00 1E [u16 n] — 특수 조우 연출. 0x004085D2 가 n(0~8)을 뜀표 0x0040C160 으로 가려 사건 애니메이션
        // 0x0048E820(장면)을 튼다. 이름은 cds_disev_editor v0.4 것이다(<see cref="Encounters"/>).
        new(Sig(0x00, 0x1E), 4, "특수 조우 연출"),
        // 00 0C [u16 n] — DISCOVER.CDS 파트 n 을 움직이는 그림으로 튼다(0x00408429 → 0x00466C90).
        // 존왕의 술잔(파트 104)의 +0xA3 이 이것이다. 예전에는 00 을 못 짚어 「0C 04 00」으로 읽었다 —
        // 0C 는 00 없이 오면 인물 대화(0C 0D)만 뜻이 있다(0x00408BA4).
        new(Sig(0x00, 0x0C), 4, "CG 애니메이션 재생"),
        new(Sig(0x43, 0x2C, 0x08), 15, "교역품 조건 분기", 13),
        // 43 2B|2C|2D|2E 1C [u16 칸] [값 식] [u16 이동] — 값 식에 따라 길이가 다르다(<see cref="CompareLength"/>).
        // Parse 가 먼저 잡고, 여기 셋은 흐름도가 뛰는 자리를 찾는 데만 쓴다(길이로 가른다).
        new(Sig(0x43, 0x2B, 0x1C), 12, CompareKind, 10),
        new(Sig(0x43, 0x2B, 0x1C), 10, CompareKind, 8),
        new(Sig(0x43, 0x2B, 0x1C), 16, CompareKind, 14),
        new(Sig(0x43, 0x12, 0x05), 7, "아이템 조건 분기", 5),
        // 43 12 0E [u16 힌트] [u16 이동] — 조건 12 0E 는 힌트 상태(+4)의 아래 두 비트가 0 이면 1 을
        // 낸다(0x0040902F). 43 은 조건이 0 일 때 뛰므로 <b>그 힌트를 얻었거나 보고했으면 뛴다</b>.
        // 파르테논 신전(파트 23)이 이것으로 힌트 없는 손님을 「길이 막혀 있습니다」로 돌려보낸다.
        new(Sig(0x43, 0x12, 0x0E), 7, "힌트 조건 분기", 5),
        new(Sig(0x43, 0x3A, 0x0B), 7, "발견물 조건 분기", 5),
        // 조건 0F 0E = 힌트가 서 있음 → 43 은 <b>힌트가 없으면</b> 뛴다(cds_disev_editor v1.0).
        new(Sig(0x43, 0x0F, 0x0E), 7, "힌트 미활성 분기", 5),
        // 조건 0F 05 = 아이템 있음(0x00408EC8) → <b>없으면</b> 뛴다.
        new(Sig(0x43, 0x0F, 0x05), 7, "아이템 미소지 분기", 5),
        // 조건 02 0B = 0x004AAD80(발견물) 그대로(0x004089C2) — 3A 0B 의 반대다. 찾았으면 참이라 <b>못 찾았으면</b> 뛴다.
        new(Sig(0x43, 0x02, 0x0B), 7, "미발견 분기", 5),
        // 아래는 cds_disev_editor v1.0 이 짚은 꼴이다. 1C 16 은 연도 == X(0x00409704), 39 16 은 X ≥ 연도(0x0040AAF0),
        // 17 08 은 지금 도시 == X(0x00409074) 로 EXE 에서 맞춰 보았다.
        new(Sig(0x43, 0x1B, 0x16), 7, "기준 연도 분기", 5),
        new(Sig(0x43, 0x39, 0x16), 7, "연도 상한 분기", 5),
        new(Sig(0x43, 0x36, 0x16), 10, "연도 범위 분기", 8),
        new(Sig(0x43, 0x17, 0x08), 7, "도시 분기", 5),
        new(Sig(0x43, 0x37, 0x0D), 7, "인물 조우 분기", 5),
        new(Sig(0x43, 0x37, 0x12), 7, "후원자 활성 분기", 5),
        new(Sig(0x43, 0x28, 0x00), 10, "도시 국적 분기", 8),
        new(Sig(0x43, 0x00, 0x15), 6, "미확인 0015 분기", 4),
        new(Sig(0x43, 0x11), 6, "선택지 분기", 4),
        // 43 45 는 늘 뛰는 것이 아니다 — 조건 45 가 「마지막 결과」를 그대로 내므로(0x0040B1B4)
        // <b>결과가 거짓일 때만</b> 뛴다. 결과 밑값은 1 이다(0x00408125). 대본은 이것을 예/아니오
        // 물음 뒤의 갈림으로 쓰는데, 그 물음을 러너가 아직 안 풀어 이름은 「이동」으로 둔다.
        new(Sig(0x43, 0x45), 4, "결과 거짓 시 이동", 2),
        // 43 은 조건 머리다(0x0040B19C) — 뒤따르는 조건이 0 이면 u16 만큼 뛴다(0x0040BCF9).
        // 조건 47 은 「마지막 결과가 0 인가」(0x0040B1C8)라 <b>미니게임을 이겼으면 뛴다</b>.
        new(Sig(0x43, 0x47), 4, "결과 참 시 이동", 2),
        // 조건 4B = 바로 앞 조건 값([ebp-0x14], 0x0040BCD2 가 적는다)이 0 인가(0x0040B23F) → 앞 조건이 <b>참이면</b> 뛴다.
        new(Sig(0x43, 0x4B), 4, "이전 조건 참 시 이동", 2),
        // 조건 56 = 부관 자리(0x0047CC50(0))가 −1 인가(0x0040B368) → <b>부관이 있으면</b> 뛴다.
        new(Sig(0x43, 0x56), 4, "부관 고용 시 이동", 2),
        new(Sig(0x43, 0x6D), 4, "STORY0.CDS 외 분기", 2),
        new(Sig(0x43, 0x6E), 4, "STORY1.CDS 외 분기", 2),
        new(Sig(0x17, 0x00), 4, "국가 조건"),
        new(Sig(0x17, 0x08), 4, "도시 조건"),
        new(Sig(0x17, 0x10), 4, "건물 조건"),
        new(Sig(0x41, 0x08), 4, "도시 아님 조건"),
        new(Sig(0x41, 0x10), 4, "건물 아님 조건"),
        new(Sig(0x42, 0x10), 7, "건물 명령 조건"),
        new(Sig(0x65), 1, "후원자 건물 나섬 조건"),
        new(Sig(0x59), 1, "배 있음 조건"),
        new(Sig(0x17, 0x19), 4, "문화권 조건"),
        new(Sig(0x1B, 0x16), 4, "연도 조건"),
        new(Sig(0x1B, 0x17), 6, "연월 조건"),
        // 1C 16 — cds_disev_editor v1.0 은 「기준 연도 일치」(연도 == X)라 적는다. 예전 이름은 「연도 상한」이었다.
        new(Sig(0x1C, 0x16), 4, "기준 연도 일치 조건"),
        new(Sig(0x36, 0x16), 7, "연도 범위 조건"),
        new(Sig(0x1B, 0x0B), 4, "발견 완료 조건"),
        new(Sig(0x5E, 0x0B), 4, "미발견 조건"),
        new(Sig(0x2A, 0x1C), 9, "수치 비교 (초과)"),
        new(Sig(0x2B, 0x1C), 9, "능력치 조건"),
        new(Sig(0x2C, 0x1C), 9, "수치 비교 (미만)"),
        new(Sig(0x2D, 0x1C), 9, "수치 비교 (이하)"),
        new(Sig(0x2E, 0x1A), 11, "무작위 확률 조건"),
        // 48 … 49 는 화면을 떠 두었다가(0x0040B1D5 → 0x004BA340) 도로 까는(0x0040B220 →
        // 0x004BA213) 쌍이고, 그 사이 「29 1A [u32]」는 값 x 20 만큼 기다린다(0x0040A2C6 →
        // 0x00428000). 뜻은 연출뿐이지만 <b>길이는 꼭 알아야 한다</b> — 모르면 29 1A 01 00 00 00 의
        // 「01 00 00」을 DSTILL 0(흰곰)으로 잘못 읽는다(파트 26·27 의 흰곰이 그 까닭이었다).
        new(Sig(0x29, 0x1A), 6, "기다리기(29 1A)"),
        // 33 은 00 1F 로 띄운 EVSTILL 그림을 닫는다 — 0x0040A79E 가 [ebp-0x20](0x00472FA0 이 만든 그림)의
        // 가상 +0x18 로 닫고 지운 뒤 「그림 떠 있음」 비트 0x2000 을 끈다. 인자 없는 한 바이트다.
        // 31 — 제독의 성미 여덟 칸을 말로 풀어 낸다(0x0040A4C0). 칸마다 0·2 면 낱말 하나를
        // 집어 「소심! 우유부단! …」처럼 잇고, 1 이면 건너뛴다. 낱말 짝은 0x00538A28 부터다
        // (소심↔거만 · 우유부단↔독선 · 변덕↔집착 · 겁장이↔무모 · 냉혹↔팔방 미인 ·
        //  편협↔욕심장이 · 무신경↔신경질 · 낭비가↔깍쟁이). 델포이 무당이 이것을 쓴다.
        new(Sig(0x31), 1, "델포이 신탁 출력"),
        // 30 1D [u16 v] — 파트 <b>+4+v</b> 로 가는 절대 이동(0x0040A48D: [해석기+4] + v 를 읽는 자리에 넣는다).
        // 길이를 몰라 「30 1D v」 세 바이트만 묶었더니 v 의 뒤 바이트가 다음 명령 머리로 읽혔다 —
        // 델포이(파트 24)의 「30 1D A5 01」 이 「01 0B …」 발견 처리로 잘못 풀렸다.
        new(Sig(0x30, 0x1D), 4, "절대 이동"),
        new(Sig(0x33), 1, "이미지 표시 종료"),
        new(Sig(0x48), 1, "대화창 숨김"),
        new(Sig(0x49), 1, "대화창 표시"),
        // 46 — 공용 결과를 거짓으로 둔다(0x0040B1BC).
        new(Sig(0x46), 1, "결과 거짓 설정"),
        // 4A 는 놀이를 끝낸다 — 0x0044AF40(0x5A4D18, 0) 이다(0x0040BDBA).
        new(Sig(0x4A), 1, "게임 오버"),
        new(Sig(0x37, 0x0D), 4, "인물 런타임 조건"),
        new(Sig(0x37, 0x12), 4, "후원자 런타임 조건"),
        // 26 1C 1A 00 08 [u16 도시] — 그 도시를 주인공 나라로 바꾼다. 26 1C 보다 먼저 잡아야 9바이트로 안 읽힌다.
        new(Sig(0x26, 0x1C, 0x1A, 0x00, 0x08), 7, "도시 국적 변경"),
        new(Sig(0x19, 0x1C), 9, "능력치 증가"),
        new(Sig(0x1A, 0x1C), 9, "능력치 감소"),
        new(Sig(0x26, 0x1C), 9, "능력치/기한 설정"),
        new(Sig(0x22, 0x1C), 9, "능력치 설정"),
        new(Sig(0x19, 0x14), 6, "금화 증가"),
        new(Sig(0x1A, 0x14), 6, "금화 감소"),
        new(Sig(0x12, 0x05), 4, "아이템 비소지 조건"),   // 0x00409022: 0x0047CE20 가 −1 이면 1 — 없음
        new(Sig(0x0F, 0x05), 4, "아이템 소지 조건"),     // 0x00408EC8: 있으면 1 (cds_disev_editor 이름이 뒤바뀌어 있었다)
        new(Sig(0x0F, 0x0E), 4, "힌트 상태 활성 조건"),
        new(Sig(0x12, 0x0E), 4, "힌트 상태 미활성 조건"),
        new(Sig(0x00, 0x05), 4, "아이템 보이기"),
        new(Sig(0x57, 0x05), 4, "아이템 상실"),
        new(Sig(0x01, 0x0B), 4, "발견물 등록/발견 처리"),
        new(Sig(0x01, 0x15), 4, "교역품 활성화"),
        // 5B — 실은 교역품을 거둬 가고 값을 치른다(0x0040B3C8). 길이를 모르면 뒤따르는 도시 번호
        // (43 00 따위)를 분기로 잘못 쪼갠다.
        new(Sig(0x5B, 0x08), 7, "교역품 인수(산지)"),
        new(Sig(0x5B, 0x15), 4, "교역품 인수"),
        new(Sig(0x05, 0x05), 4, "아이템 획득(발견물 제외)"),
        new(Sig(0x26, 0x05), 4, "아이템 획득(버리기 창)"),
        new(Sig(0x05, 0x0E), 4, "힌트 획득"),
        new(Sig(0x26, 0x08), 4, "신도시 생성"),
        new(Sig(0x26, 0x10), 7, "특수 건물 생성"),
        new(Sig(0x22, 0x00), 4, "국가 멸망 처리"),
        new(Sig(0x38, 0x0D), 4, "인물 조우 처리"),
        new(Sig(0x3D, 0x0D), 4, "통역 고용·교체"),
        new(Sig(0x23, 0x08), 4, "도시 점령지 설정"),
        new(Sig(0x25, 0x08), 4, "도시 점령지 해제"),
        new(Sig(0x22, 0x08), 4, "도시 제거"),
        new(Sig(0x22, 0x10), 7, "도시 시설 제거"),
        new(Sig(0x3C, 0x08), 4, "이벤트 대상 도시 이동"),
        new(Sig(0x34, 0x1C), 9, "투입 인원 절반"),
        // 35 1C [u16 칸] — 그 능력으로 판정해 결과를 세운다(6 무력 · 18 운 · 21 지력 · 23 신앙심).
        new(Sig(0x35, 0x1C), 4, "능력 판정"),
        new(Sig(0x66, 0x03), 4, "음원 정지"),
        // 0C 0D [u16 인물] — 그 인물과 일기토. 26 0F 가 고른 FIGHTER.CDS 벌을 쓴다.
        new(Sig(0x0C, 0x0D), 4, "일기토"),
        new(Sig(0x40, 0x0D), 4, "부관 앉힘"),
        new(Sig(0x38, 0x12), 4, "후원자 소개"),
        new(Sig(0x06, 0x4D), 2, "다음 단계"),
        new(Sig(0x04, 0x4D), 2, "이벤트 완전 종료"),
        new(Sig(0x06, 0xFF), 1, "다음 단계"),
        new(Sig(0x06), 1, "다음 단계"),
        new(Sig(0x04), 1, "이야기 끝"),
        new(Sig(0x0E, 0x03), 4, "음원 재생"),
        // 0E 04 [u16 n] — 미니게임 n 을 한 판 하고 이겼는지를 「마지막 결과」에 둔다(0x00408D16).
        // 0 성배 퍼즐(0x004684D0) · 1 스핑크스 퀴즈 · 2 미궁 64 · 3 낚시 · 6 큐브 퍼즐.
        new(Sig(0x0E, 0x04), 4, "미니게임"),
        // 0E 14|1A [u32 판자] 04 [u16 n] — 코인 게임(4)·발라몬의 탑(5)(0x00408DF7). 둘째 바이트 14 와 1A 는
        // 같은 손으로 간다(0x0040C198 의 17·23 칸 — 둘째−3 이 칸 번호다). 04 가 아니면 아무것도 안 한다.
        // 판자는 탑만 쓴다. cds_disev_editor v0.4 도 0E 14 로 적는다.
        new(Sig(0x0E, 0x14), 9, "퍼즐 미니게임"),
        new(Sig(0x0E, 0x1A), 9, "퍼즐 미니게임"),
        // 2F 0D [u16 인물] — 그 인물이 이끄는 적과 육상전(0x0040A40B → 0x0044AA30(3, 아군, 적, 0, 지형)).
        // 2F 08 [u16 도시] — 그 도시와 육상전(0x0040A3B2 → 0x0044AA30(4, 아군, 0, 도시, 7)).
        // 결과는 「이겼는가」로 남아 43 47 이 이기면 뛴다. 전멸하면 게임이 먼저 게임 오버를 걸고
        // 해석기가 그 자리에서 빠진다(0x0040A471 → 0x0040BDB3).
        new(Sig(0x2F, 0x0D), 4, "육상전(인물)"),
        // 0D 0D [u16 인물] — 그 인물이 이끄는 적과 <b>해전</b>. 바다 괴물 넷이 이것으로 덤빈다
        // (시서펜트 272 · 크라켄 271 · 식인상어 273 · 맨터 274 — 인물 표 뒤쪽의 괴물 자리다).
        // 육상전과 같이 결과가 남아 43 47 이 이기면 뛴다.
        new(Sig(0x0D, 0x0D), 4, "해전(인물)"),
        new(Sig(0x2F, 0x08), 4, "육상전(도시)"),
        // 26 0F [u16 0~6] — 다음 일기토(0C 0D)가 FIGHTER.CDS 에서 읽을 그림 벌을 둔다([ebp-0x6C] → 0x004AA700).
        // cds_disev_editor v1.0 이 짚었다. 예전 이름은 「인물 대화 갈래 설정」이었다.
        new(Sig(0x26, 0x0F), 4, "일기토 연출 세트 설정"),
        new(Sig(0x5A), 1, "후원자 계약 없음 조건"),
        // 50 — 조건 덩이에서 앞 조건과 다음 조건을 OR 로 묶는다(0x00407EB1). 50 이 없으면 AND 다.
        new(Sig(0x50), 1, "OR 연결"),
        new(Sig(0x4C), 1, "이벤트 결과 코드 0"),
        new(Sig(0x4D), 1, "이벤트 결과 코드 1"),
        new(Sig(0x4E), 1, "이벤트 결과 코드 2"),
    ];

    /// <summary>
    /// <c>00 1E [n]</c> 특수 조우 연출 — 이름과 <c>0x0048E820</c> 에 넘기는 사건 애니메이션 장면 번호.
    /// </summary>
    /// <remarks>
    /// 장면 번호는 뜀표 <c>0x0040C160</c> 아홉 칸이 밀어 넣는 값 그대로다(<c>0x004085F1</c>~<c>0x00408679</c>).
    /// 7·8 은 같은 장면 <c>0x0E</c> 다. 이름은 cds_disev_editor v0.4 의 것을 받았다.
    /// </remarks>
    public static readonly (string Name, int Scene)[] Encounters =
    [
        ("백경", 0x13), ("돌고래", 0x14), ("날치", 0x15), ("유령선", 0x09), ("오로라", 0x0B),
        ("플라밍고 떼", 0x16), ("모르포 나비 떼", 0x17), ("유빙·빙산", 0x0E), ("유빙·빙산", 0x0E),
    ];

    /// <summary>특수 조우 연출 번호의 이름. 8 넘으면 게임이 건너뛴다(<c>0x004085E1</c>).</summary>
    public static string EncounterName(int n) =>
        n >= 0 && n < Encounters.Length ? Encounters[n].Name : "(없음 — 게임이 건너뜀)";

    /// <summary>미니게임 번호의 이름(<see cref="DisevMinigame"/>). 뜀표에서 건너뛰는 번호면 그렇다고 적는다.</summary>
    public static string MinigameName(int game) => ((DisevMinigame)game).Title();

    /// <summary>능력치 번호 → 이름. 빈 자리는 아직 못 짚은 것이다.</summary>
    public static readonly IReadOnlyDictionary<int, string> StatNames = new Dictionary<int, string>
    {
        // cds_disev_editor v1.0 의 STAT_TARGETS 를 받았다. 5·19·30 은 제독 성미 칸, 9 는 부관 성미 칸이다.
        [0] = "피로도", [1] = "규율", [2] = "총 선원 수", [3] = "소지금", [4] = "악명",
        [5] = "주인공 성격[5] 로맨틱", [6] = "무력", [7] = "체력", [8] = "생명력",
        [9] = "부관 성격[0] 당당", [10] = "부관 체력", [11] = "부관 생명력", [12] = "부관 사격술",
        [13] = "부관 무력", [14] = "부관 역사학", [15] = "주인공 아프리카토착어", [16] = "육상전 상대 병력 수",
        [17] = "명성", [18] = "운", [19] = "주인공 성격[0] 당당", [20] = "현재 함선 내구도",
        [21] = "지력", [22] = "매력", [23] = "신앙심", [24] = "부관 지력", [25] = "주인공 과학",
        [26] = "주인공 국적 경로(0 포르투갈 · 1 에스파니아)", [27] = "후원자 계약 남은 기한(일)",
        [29] = "STORY 의뢰 남은 기한(일)", [30] = "주인공 성격[7] 견실",
    };

    /// <summary>
    /// 화자 표 — 한국어판인데도 <b>화자 이름만 일본어(CP932) 그대로</b> 남아 있다.
    /// 열쇠는 그 바이트열의 16진 글자다.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SpeakerNames = new Dictionary<string, string>
    {
        ["8380815B906C"] = "무어인", ["834D838B8368"] = "조합", ["8BB389EF"] = "교회",
        ["959B8AAF"] = "부관", ["8CF088D58F8A"] = "교역소", ["837D836B8347838B88EA90A2"] = "마누엘 1세",
        ["8FE996E5"] = "성문", ["8EB78E96"] = "집사", ["8EF08FEA"] = "술집",
        ["838483528376818183748362834B815B"] = "야코프 푸거", ["96BA"] = "딸",
        ["834A838B838D835888EA90A2"] = "카를로스 1세", ["89C396F592E9"] = "가정제",
        ["838C83498F5C90A2"] = "레오 10세", ["835783878341839393F190A2"] = "조안 2세",
        ["837E8350815B838C818183588373836D8389"] = "미켈레 스피놀라",
        ["8345838B834F81818378834E"] = "우르그 벡", ["89A48F97"] = "왕녀",
        ["83578387834183938181836F838D8358"] = "조안 바로스", ["8F6889AE"] = "여관",
        ["836B83578393834B8181836B834E8345"] = "누진가 누쿠우", ["95BA8E6D"] = "병사",
        ["8EE5906C8CF6"] = "주인공", ["91A291448F8A"] = "조선소", ["837D8380838B815B834E"] = "맘루크",
        ["83438346836A83608346838A"] = "예니체리", ["8AC48E408AAF"] = "감찰관",
        ["8343839383668342834982CC8EF197CC"] = "인디오의 족장", ["89A495E682CC94D4906C"] = "왕묘의 파수꾼",
        ["92868D9182CC9856906C"] = "중국의 노인", ["939091AF82CC93AA"] = "도적 두목",
        ["83578383838F82CC9856906C"] = "자와의 노인", ["83438393836882CC9856906C"] = "인도의 노인",
        ["916D97B5"] = "승려", ["92CB8CB483679360"] = "츠카하라 보쿠덴",
        ["837E83508389839383578346838D"] = "미켈란젤로", ["90B39171894082CC94D4906C"] = "쇼소인의 파수꾼",
        ["96EC959A82B982E8"] = "노부세리", ["837A83628365839383678362836791B092B7"] = "호텐토트 족장",
        ["83708368839382CC91B092B7"] = "파돈의 족장",
        ["8341837B838A8357836A82CC91B092B7"] = "아보리지니의 족장",
        ["836A8385815B834D836A834182CC91B092B7"] = "뉴기니아의 족장",
        ["8343836B83438362836782CC91B092B7"] = "이누이트의 족장",
        ["83438393836683428341839382CC8F5592B7"] = "인디언의 족장",
        ["8341837D835D836C835882CC91B092B7"] = "아마조네스의 족장", ["93EC8BC9906C"] = "남극인",
        ["837583898368"] = "블라드", ["8382834E83658358837D93F190A2"] = "목테수마 2세",
        ["8341835E838F838B8370"] = "아타왈파", ["834C8358834C8358"] = "키스키스",
        ["836783708362834E"] = "토팍", ["838C83498369838B8368"] = "레오나르도",
        ["836A8352838983458358"] = "니콜라우스", ["8377838D836A8382"] = "헤로니모",
        ["834183588365834A82CC96F0906C"] = "아스테카 관리", ["8381838A835F82CC90659583"] = "메리다의 아버지",
        ["8368815B836A8383"] = "도냐", ["8367834483898358834A838982CC90C2944E"] = "툴라스칼라의 청년",
        ["8367834483898358834A838982CC90ED8E6D"] = "툴라스칼라의 전사", ["835F839383668342"] = "단디",
        ["8341838B8378815B838B"] = "알베르", ["835783468389838B8368"] = "제라르드",
        ["83798367838D8358"] = "페트로스", ["837D838B834E8358"] = "마르쿠스", ["83578385838A8349"] = "줄리오",
        ["8355834B815B"] = "자가르", ["835A838A836B"] = "세리누",
    };

    private static Encoding? _cp949;
    private static Encoding? _cp932;

    private static Encoding Cp949 => _cp949 ??= GetCodePage(949);
    private static Encoding Cp932 => _cp932 ??= GetCodePage(932);

    private static Encoding GetCodePage(int page)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(page);
    }

    /// <summary>바이트열을 "01 0B 12" 꼴로.</summary>
    public static string Hex(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder(data.Length * 3);
        foreach (byte value in data)
        {
            if (text.Length > 0) text.Append(' ');
            text.Append(value.ToString("X2"));
        }
        return text.ToString();
    }

    /// <summary>"01 0B 12" 나 "010B12" 를 바이트열로. 못 읽으면 null.</summary>
    public static byte[]? ParseHex(string text)
    {
        var digits = new StringBuilder();
        foreach (char ch in text)
        {
            if (char.IsWhiteSpace(ch) || ch is ',' or '-') continue;
            if (!Uri.IsHexDigit(ch)) return null;
            digits.Append(ch);
        }
        if (digits.Length % 2 != 0) return null;

        var output = new byte[digits.Length / 2];
        for (int i = 0; i < output.Length; i++)
            output[i] = Convert.ToByte(digits.ToString(i * 2, 2), 16);
        return output;
    }

    private static int U16(ReadOnlySpan<byte> data, int at) =>
        at + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : 0;

    private static long U32(ReadOnlySpan<byte> data, int at) =>
        at + 4 <= data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : 0;

    /// <summary>덩이 하나를 명령 줄로 푼다.</summary>
    /// <param name="data">파트 알맹이.</param>
    /// <param name="start">덩이 시작.</param>
    /// <param name="end">덩이 끝(다음 덩이 시작).</param>
    /// <param name="stillSlot">
    /// 이 발견물의 DSTILL 그림 번호. <c>01 0B</c> 가 「그림 11 재생」인지
    /// 「발견 처리」인지 가르는 데 쓴다. 모르면 −1.
    /// </param>
    public static List<Op> Parse(byte[] data, int start, int end, int stillSlot = -1)
    {
        var ops = new List<Op>();
        int i = Math.Max(0, start);
        end = Math.Min(end, data.Length);

        while (i < end)
        {
            var span = data.AsSpan();

            if (data[i] == 0xFF)
            {
                ops.Add(new Op(i, 1, "덩이/갈래 끝", "덩이/갈래 끝", "FF", true));
                i++;
                continue;
            }

            // 1F — 발견물 이름(0x0040984A). 앞머리가 1F 0A 라 「창 플래그 1F 대사」로 잘못 읽히므로 먼저 잡는다.
            //   1F 0A [이름] 00 0B [u16 발견물]   곧장 이름을 박는다(0x00409888 → 0x004AAB00)
            //   1F 0B [u16 발견물] 0A [글] 00     이름 입력창을 열어 친 글 뒤에 이 글을 붙인다
            // 꼴은 cds_disev_editor v0.4 가 가른 것과 같다.
            if (data[i] == 0x1F && i + 2 < end)
            {
                if (data[i + 1] == 0x0A)
                {
                    int term = Array.IndexOf(data, (byte)0, i + 2, end - (i + 2));
                    if (term >= 0 && term + 4 <= end && data[term + 1] == 0x0B)
                    {
                        var (_, name) = DecodeDialogue(span[(i + 2)..term]);
                        int id = U16(span, term + 2);
                        ops.Add(new Op(i, term + 4 - i, "발견물 이름 강제 입력",
                            $"발견물 이름 강제 입력: 발견물 {id} ← \"{name}\"", Hex(span[i..(term + 4)]), true));
                        i = term + 4;
                        continue;
                    }
                }
                else if (data[i + 1] == 0x0B && i + 5 <= end && data[i + 4] == 0x0A)
                {
                    int term = Array.IndexOf(data, (byte)0, i + 5, end - (i + 5));
                    if (term >= 0)
                    {
                        var (_, tail) = DecodeDialogue(span[(i + 5)..term]);
                        int id = U16(span, i + 2);
                        ops.Add(new Op(i, term + 1 - i, "발견물 이름 입력 대기",
                            $"발견물 이름 입력 대기: 발견물 {id}, 친 글 뒤에 \"{tail}\"", Hex(span[i..(term + 1)]), true));
                        i = term + 1;
                        continue;
                    }
                }
            }

            // 43 2B~2E 1C — 값 식에 따라 길이가 다른 비교 분기. 표보다 먼저 잡는다.
            if (data[i] == 0x43 && i + 6 <= end && data[i + 1] is >= 0x2B and <= 0x2E && data[i + 2] == 0x1C &&
                CompareLength(data[i + 5]) is > 0 and var compareLength && i + compareLength <= end)
            {
                var raw = span.Slice(i, compareLength);
                ops.Add(new Op(i, compareLength, CompareKind, DescribeCompare(raw, i), Hex(raw), true));
                i += compareLength;
                continue;
            }

            // 19 1C [u16 칸] 1C [u16 피연산자] — 상태값에 계산값을 더한다(7바이트). 9바이트 「능력치 증가」보다 먼저.
            if (data[i] == 0x19 && i + 7 <= end && data[i + 1] == 0x1C && data[i + 4] == 0x1C)
            {
                var raw = span.Slice(i, 7);
                ops.Add(new Op(i, 7, "상태값 참조 증가",
                    $"상태값 참조 증가: {StatName(U16(raw, 2))} += {StatName(U16(raw, 5), "피연산자")}", Hex(raw), true));
                i += 7;
                continue;
            }

            // 20 0A [글] 00 08 [u16 도시] — 도시 소문 등록. 대사처럼 보이지만 창은 안 뜬다.
            // 꼬리가 19 면 <b>그 문화권 도시 전부</b>다(0x004099E8 → 0x00409A18).
            if (data[i] == 0x20 && i + 2 < end && data[i + 1] == 0x0A)
            {
                int term = Array.IndexOf(data, (byte)0, i + 2, end - (i + 2));
                if (term >= 0 && term + 4 <= end && data[term + 1] is 0x08 or 0x19)
                {
                    bool byCulture = data[term + 1] == 0x19;
                    var (_, rumor) = DecodeDialogue(span[(i + 2)..term]);
                    string where = byCulture ? $"문화권 {U16(span, term + 2)}" : $"도시 {U16(span, term + 2)}";
                    ops.Add(new Op(i, term + 4 - i, "도시 소문 등록",
                        $"도시 소문 등록: {where} ← \"{rumor}\"", Hex(span[i..(term + 4)]), true));
                    i = term + 4;
                    continue;
                }
            }

            // 10|18 0A [글, 선택지는 81 5E 로 가름] 00 [u8 밑값] — 다중 선택지(0x00408EF4 · 0x0040914F).
            // 고른 자리 + 밑값이 선택값([ebp-0x44])이 되고 43 11 0A [u8] 이 그것과 견준다.
            if (data[i] is 0x10 or 0x18 && i + 2 < end && data[i + 1] == 0x0A)
            {
                int term = Array.IndexOf(data, (byte)0, i + 2, end - (i + 2));
                if (term >= 0 && term + 2 <= end)
                {
                    var (speaker, choices) = DecodeDialogue(span[(i + 2)..term]);
                    string who = speaker == null ? "" : $", 화자 {speaker}";
                    ops.Add(new Op(i, term + 2 - i, "다중 선택지 대사",
                        $"다중 선택지 대사{who}: \"{choices}\" (선택값 밑 {data[term + 1]})", Hex(span[i..(term + 2)]), true));
                    i = term + 2;
                    continue;
                }
            }

            // 대사 — 0A 로 바로 열거나, 창 플래그(00 보통 · 0B 예/아니오) 한 바이트 뒤에 0A 가 온다.
            // 플래그를 이 둘로만 받는다 — 예전에는 모르는 명령 뒤에 0A 만 오면(43 56 0A 00 따위)
            // 「창 플래그 43 대사」로 NUL 까지 먹었다.
            if (data[i] == 0x0A || (data[i] is 0x00 or 0x0B && i + 1 < end && data[i + 1] == 0x0A))
            {
                int? flag = data[i] == 0x0A ? null : data[i];
                int textStart = flag == null ? i + 1 : i + 2;
                int terminator = Array.IndexOf(data, (byte)0, textStart, end - textStart);
                int next = terminator < 0 ? end : terminator + 1;
                if (terminator < 0) terminator = end;

                var (speaker, body) = DecodeDialogue(span[textStart..terminator]);
                string who = speaker == null ? "" : $", 화자 {speaker}";
                string label = flag switch
                {
                    null or 0 => $"대사{who}: \"{body}\"",
                    0x0B => $"예/아니오 대사{who}: \"{body}\"",
                    _ => $"대사(창 플래그 {flag}{who}): \"{body}\"",
                };
                ops.Add(new Op(i, next - i, "대사", label, Hex(span[i..next]), true));
                i = next;
                continue;
            }

            // 미디어는 늘 00 01|02|0C [u16] 네 바이트다(표에 있다). 예전에는 00 없이 온 01·02·0C 를
            // 세 바이트 그림·동영상으로 먼저 잡아, 01 15(교역품)·0C 0D(일기토)·43 02 0B 속 02 0B 를 오독했다.
            // 32 — 날짜 경과(cds_disev_editor v1.0). 값 식 하나를 받는다. 크노소스(파트 25)가 본문 첫 줄에 쓴다.
            //   32 1A [u32]            상수 (여섯 바이트)
            //   32 20 [u32] [u32]      무작위 (열 바이트)
            // <b>길이를 모르면 뒤가 통째로 밀린다.</b> 예전에는 이 열 바이트를 「미확인」으로
            // 묶다가 그 속의 <c>00 05 00 00</c> 을 「아이템 0 획득」으로 읽어, 크노소스에서
            // <b>잠수폭탄</b>이 나왔다(제대로는 파트 끝의 <c>00 05 32 00</c> 으로 미노타우로스의
            // 도끼 하나뿐이다).
            if (data[i] == 0x32 && i + 1 < end && data[i + 1] is 0x1A or 0x20)
            {
                int length = Math.Min(data[i + 1] == 0x1A ? 6 : 10, end - i);
                var value = span.Slice(i, length);
                string days = data[i + 1] == 0x1A
                    ? $"{U32(value, 2)}일"
                    : $"무작위 {U32(value, 6)}~{U32(value, 6) + Math.Max(U32(value, 2) - 1, 0)}일";
                ops.Add(new Op(i, length, "날짜 경과", $"날짜 경과: {days}", Hex(value), true));
                i += length;
                continue;
            }

            var form = FormAt(data, i, end);
            if (form is { } known)
            {
                int length = known.Length;
                // 능력치 수식은 상수(1A, 9바이트) 아니면 무작위(20, 13바이트)로 끝난다.
                if (known.Kind is "능력치 증가" or "능력치 감소" or "능력치/기한 설정" or "능력치 설정"
                    && i + 13 <= end && data[i + 4] == 0x20)
                {
                    length = 13;
                }
                var raw = span.Slice(i, Math.Min(length, end - i));
                ops.Add(new Op(i, length, known.Kind, Describe(known, raw, i), Hex(raw), true));
                i += length;
                continue;
            }

            // 못 짚은 바이트는 아는 명령이 다시 나올 때까지 묶는다(최대 16).
            int j = i + 1;
            while (j < end && j - i < 16 && !LikelyStart(data, j, end)) j++;
            var unknown = span[i..j];
            ops.Add(new Op(i, j - i, "미확인 명령/데이터", "미확인 명령/데이터", Hex(unknown), false));
            i = j;
        }

        return ops;
    }

    private static Form? FormAt(byte[] data, int offset, int end)
    {
        foreach (var form in Forms)
        {
            if (offset + form.Length > end) continue;
            if (data.AsSpan(offset).StartsWith(form.Signature)) return form;
        }
        return null;
    }

    private static bool LikelyStart(byte[] data, int offset, int end)
    {
        if (offset >= end) return false;
        if (data[offset] is 0xFF or 0x0A) return true;
        if (offset + 1 < end && data[offset + 1] == 0x0A && data[offset] is 0x00 or 0x0B or 0x10 or 0x18 or 0x1F or 0x20)
            return true;
        return FormAt(data, offset, end) != null;
    }

    private static string Describe(Form form, ReadOnlySpan<byte> raw, int offset)
    {
        string kind = form.Kind;
        switch (kind)
        {
            case "AVI 재생":
            case "EVSTILL 이미지 표시":
            case "음원 재생":
                return $"{kind}: 슬롯 {U16(raw, 2)}";
            case "CG 애니메이션 재생":
                return $"{kind}: DISCOVER.CDS 파트 {U16(raw, 2)}";
            case "특수 조우 연출":
            {
                int n = U16(raw, 2);
                return $"특수 조우 연출: {n} {EncounterName(n)}";
            }
            case "미니게임":
            {
                int game = U16(raw, 2);
                return $"미니게임: {game} {MinigameName(game)}";
            }
            case "퍼즐 미니게임":
            {
                // 가운데 바이트가 04 가 아니면 게임은 아무것도 안 한다(0x00408E13).
                if (raw.Length < 9 || raw[6] != 0x04) return $"{kind}: (가운데 바이트가 04 가 아님 — 게임이 건너뜀)";
                int game = U16(raw, 7);
                string name = ((DisevMinigame)game).ByPuzzleCommand() ? MinigameName(game) : "(없음 — 게임이 건너뜀)";
                return game == (int)DisevMinigame.Tower
                    ? $"미니게임: {game} {name}, 판자 {U32(raw, 2)}장"
                    : $"미니게임: {game} {name}";
            }
            case "연도 조건":
                return $"연도 >= {U16(raw, 2)}";
            case "연월 조건":
                return $"연월 조건: {U16(raw, 4)}년 {raw[2]}월";
            case "기준 연도 일치 조건":
                return $"연도 == {U16(raw, 2)}";
            case "연도 범위 조건":
                return $"연도 범위: {U16(raw, 2)}~{U16(raw, 5)}";
            case "무작위 확률 조건":
                if (raw.Length < 11 || raw[6] != 0x1A) return "무작위 확률 조건: 피연산자 형식 미확인";
                return $"무작위 확률 조건: {U32(raw, 7)} / {U32(raw, 2)}";
            case "인물 런타임 조건":
            case "후원자 런타임 조건":
                return $"{kind}: 번호 {U16(raw, 2)}";
            case "발견 완료 조건":
            case "미발견 조건":
                return $"{kind}: 발견물 ID {U16(raw, 2)}";
            case "능력치 조건":
            case "수치 비교 (초과)":
            case "수치 비교 (이하)":
            case "수치 비교 (미만)":
            {
                string op = kind switch
                {
                    "능력치 조건" => ">=",      // 2B: A ≥ B (0x0040A343)
                    "수치 비교 (초과)" => ">",   // 2A: A > B (0x0040A32D)
                    "수치 비교 (이하)" => "<=",
                    _ => "<",
                };
                return $"조건: {StatName(U16(raw, 2))} {op} {U32(raw, 5)}";
            }
            case "능력치 증가":
            case "능력치 감소":
            case "능력치/기한 설정":
            case "능력치 설정":
            {
                int stat = U16(raw, 2);
                string value;
                if (raw.Length >= 13 && raw[4] == 0x20)
                {
                    long width = U32(raw, 5), from = U32(raw, 9);
                    value = $"무작위 {from}~{from + Math.Max(width - 1, 0)}";
                }
                else
                {
                    value = U32(raw, 5).ToString();
                }
                string symbol = kind == "능력치 증가" ? "+" : kind == "능력치 감소" ? "-" : "=";
                return $"{StatName(stat, "능력치")} {symbol} {value}";
            }
            case "금화 증가":
                return $"금화 +{U32(raw, 2)}";
            case "금화 감소":
                return $"금화 -{U32(raw, 2)}";
            case "아이템 소지 조건":
            case "아이템 비소지 조건":
            case "아이템 보이기":
            case "아이템 상실":
                return $"{kind}: 아이템 ID {U16(raw, 2)}";
            case "힌트 상태 활성 조건":
            case "힌트 상태 미활성 조건":
                return $"{kind}: 힌트 상태 ID {U16(raw, 2)}";
            case "신도시 생성":
            case "도시 점령지 설정":
            case "도시 점령지 해제":
            case "도시 제거":
            case "이벤트 대상 도시 이동":
                return $"{kind}: 도시 ID {U16(raw, 2)}";
            case "도시 국적 변경":
                return $"도시 국적 변경: 도시 ID {U16(raw, 5)} → 주인공 나라";
            case "도시 시설 제거":
                return $"도시 시설 제거: 시설 비트 {U16(raw, 2)}, 도시 {U16(raw, 5)}";
            case "국가 멸망 처리":
                return $"국가 멸망 처리: 나라 {U16(raw, 2)}";
            case "인물 조우 처리":
            case "통역 고용·교체":
            case "일기토":
                return $"{kind}: 인물 {U16(raw, 2)}";
            case "일기토 연출 세트 설정":
                return $"일기토 연출 세트: {U16(raw, 2)}";
            case "힌트 획득":
                return $"힌트 획득: 힌트 {U16(raw, 2)}";
            case "교역품 활성화":
                return $"교역품 활성화: 교역품 {U16(raw, 2)}";
            case "아이템 획득(발견물 제외)":
            case "아이템 획득(버리기 창)":
                return $"{kind}: 아이템 ID {U16(raw, 2)}";
            case "음원 정지":
                return $"음원 정지: 슬롯 {U16(raw, 2)}";
            case "DSTILL 이미지 표시":
                return $"DSTILL 이미지 표시: 슬롯 {U16(raw, 2)}";
            case "발견물 등록/발견 처리":
                return $"발견물 등록/발견 처리: ID {U16(raw, 2)}";
            case "절대 이동":
                return $"절대 이동 → 파트 +0x{4 + U16(raw, 2):X}";
            case "능력 판정":
                return $"능력 판정: {StatName(U16(raw, 2))}";
            case "투입 인원 절반":
                return "투입 인원 절반(올림)";
            case "특수 건물 생성":
                return $"특수 건물 생성: 건물 {U16(raw, 2)}, 도시 {U16(raw, 5)}";
        }

        if (kind.EndsWith("조건") && raw.Length == 4 && raw[0] == 0x17)
            return $"{kind}: 값 {U16(raw, 2)}";

        if (form.JumpOffset >= 0 && form.JumpOffset + 2 <= raw.Length)
        {
            int relative = U16(raw, form.JumpOffset);
            int target = offset + form.Length + relative;
            string extra = kind switch
            {
                "아이템 조건 분기" or "아이템 미소지 분기" => $", 아이템 ID {U16(raw, 3)}",
                "힌트 미활성 분기" => $", 힌트 {U16(raw, 3)}",
                "미발견 분기" => $", 발견물 {U16(raw, 3)}",
                "기준 연도 분기" or "연도 상한 분기" => $", 연도 {U16(raw, 3)}",
                "연도 범위 분기" => $", 연도 {U16(raw, 3)}~{U16(raw, 6)}",
                "도시 분기" => $", 도시 {U16(raw, 3)}",
                "인물 조우 분기" => $", 인물 {U16(raw, 3)}",
                "후원자 활성 분기" => $", 후원자 {U16(raw, 3)}",
                "도시 국적 분기" => $", 나라 {U16(raw, 3)}, 도시 {U16(raw, 6)}",
                "선택지 분기" => $", 선택값 {raw[3]}",
                "교역품 조건 분기" =>
                    $", 원산 도시 {U16(raw, 3)}, 교역품 {U16(raw, 6)}, 수량 {U32(raw, 9)}",
                _ => "",
            };
            return $"{kind}{extra}, 상대 +0x{relative:X} → 파트 +0x{target:X}";
        }
        return kind;
    }

    /// <summary>
    /// 성격 칸 상태값 — 값 0·2 가 무슨 낱말인지(<c>0x00538A28</c> 짝, 1 은 보통). 성격 칸이 아니면 null.
    /// </summary>
    /// <remarks>
    /// 성격 여덟 칸(당당·강인·의지·용감·친절·로맨틱·섬세·견실)은 한 칸이 0·1·2 값 하나다. 칸 번호와 상태값 번호의
    /// 짝은 cds_disev_editor v1.0 이 신탁 출력과 맞춰 짚은 것이고, 5 번만 EXE 게터(<c>0x00406F0E</c>)로 확인했다.
    /// </remarks>
    public static (string Low, string High)? TraitWordsOf(int stat) => stat switch
    {
        5 => ("편협", "욕심장이"),
        9 or 19 => ("소심", "거만"),
        30 => ("낭비가", "깍쟁이"),
        _ => null,
    };

    /// <summary>상수 값에 성격 낱말을 붙인다 — 「2(욕심장이)」.</summary>
    private static string TraitValue(int stat, long value) => TraitWordsOf(stat) is { } words
        ? value switch { 0 => $"0({words.Low})", 1 => "1(보통)", 2 => $"2({words.High})", _ => value.ToString() }
        : value.ToString();

    /// <summary>비교 분기 풀이 — 무엇을 무엇과 견주고, <b>언제 뛰는지</b>.</summary>
    private static string DescribeCompare(ReadOnlySpan<byte> raw, int offset)
    {
        int stat = U16(raw, 3);
        string left = StatName(stat);
        string right = raw[5] switch
        {
            0x1A or 0x14 => TraitValue(stat, U32(raw, 6)),
            0x00 => TraitValue(stat, U16(raw, 6)),
            0x1C => StatName(U16(raw, 6)),
            _ => $"무작위 {U32(raw, 10)}~{U32(raw, 10) + Math.Max(U32(raw, 6) - 1, 0)}",
        };
        // 43 은 조건이 거짓일 때 뛴다 — 뛰는 때를 적는다(2B A≥B · 2C A<B · 2D A≤B · 2E A==B 의 반대).
        string jumpsWhen = raw[1] switch
        {
            0x2B => "<",
            0x2C => ">=",
            0x2D => ">",
            _ => "!=",
        };
        int relative = U16(raw, raw.Length - 2);
        return $"{left} {jumpsWhen} {right} 이면 이동, 상대 +0x{relative:X} → 파트 +0x{offset + raw.Length + relative:X}";
    }

    /// <summary>능력치 이름. 모르는 번호면 <paramref name="fallback"/> 뒤에 번호를 붙인다.</summary>
    private static string StatName(int stat, string fallback = "필드") =>
        StatNames.TryGetValue(stat, out var name) ? name : $"{fallback} {stat}";

    /// <summary>대사 한 줄을 화자와 본문으로 가른다.</summary>
    /// <remarks>
    /// 화자표는 CP932 로 적히고 전각 콜론(<c>81 46</c>)으로 끝난다. 본문은 CP949 다.
    /// 자리표 <c>81 93 82 xx</c> 는 놀이가 그때그때 채우는 것이라 뜻을 적어 둔다.
    /// </remarks>
    public static (string? Speaker, string Body) DecodeDialogue(ReadOnlySpan<byte> data) =>
        DecodeDialogue(data, normalize: true);

    /// <summary>
    /// 놀이가 낼 글 — 자리표에 <b>제독 이름과 조사</b>를 채워 넣는다.
    /// </summary>
    /// <param name="player">제독 이름. 비었으면 「제독」으로 물러선다.</param>
    public static (string? Speaker, string Body) DecodeDialogue(ReadOnlySpan<byte> data, string player) =>
        DecodeDialogue(data, normalize: true, player.Length > 0 ? player : "제독");

    /// <summary>
    /// 같은 것을 <b>손실 없이</b> 푼다 — 고치는 칸에 넣을 글이다.
    /// </summary>
    /// <param name="normalize">
    /// 참이면 전각을 반각으로 고르고 「」 따위를 <c>"</c> 로 바꾼다(읽기 좋으라고).
    /// <b>거짓이면 아무것도 안 고른다</b> — 그래야 다시 구웠을 때 바이트가 같다.
    /// 자리표도 <c>&lt;제독&gt;</c> 처럼 되돌릴 수 있는 꼴로 적는다.
    /// </param>
    public static (string? Speaker, string Body) DecodeDialogue(ReadOnlySpan<byte> data, bool normalize,
                                                               string? player = null)
    {
        string? speaker = null;
        int bodyStart = 0;

        int look = Math.Min(data.Length, 40);
        for (int i = 0; i + 1 < look; i++)
        {
            if (data[i] != 0x81 || data[i + 1] != 0x46) continue;
            var tag = data[..i];
            string key = Hex(tag).Replace(" ", "");
            speaker = SpeakerNames.TryGetValue(key, out var name) ? name : Cp932.GetString(tag);
            bodyStart = i + 2;
            break;
        }

        var source = data[bodyStart..];
        var cooked = new List<byte>(source.Length);
        for (int i = 0; i < source.Length;)
        {
            if (i + 1 < source.Length && source[i] == 0x81 && source[i + 1] == 0x5E)
            {
                cooked.Add((byte)'/');
                i += 2;
                continue;
            }
            if (i + 4 <= source.Length && source[i] == 0x81 && source[i + 1] == 0x93 && source[i + 2] == 0x82)
            {
                string token = TokenText(source[i + 3], normalize, player);
                cooked.AddRange(Cp949.GetBytes(token));
                i += 4;
                continue;
            }
            cooked.Add(source[i]);
            i++;
        }

        string body = Safe(Cp949.GetString(cooked.ToArray()));
        return (speaker, normalize ? Normalize(body).TrimEnd(' ') : body);
    }

    /// <summary>
    /// 자리표 한 개를 글로 편다(<see cref="NameToken"/>).
    /// </summary>
    /// <param name="player">
    /// 놀이가 낼 글이면 제독 이름. <b>null 이면 고치는 창</b>이라 이름 대신 자리 이름을 적는다 —
    /// 그래야 무손실 글이 다시 같은 바이트로 구워진다(<see cref="DisevForm"/>).
    /// </param>
    private static string TokenText(byte code, bool normalize, string? player)
    {
        // 글자 그대로 박는 자리표는 어느 쪽에서든 같은 글이다.
        if (NameToken.WordOf(code) is { } word) return normalize ? word : $"<{word}>";

        if (NameToken.IsName(code))
            return player ?? (normalize ? "제독" : "<제독>");

        int kind = NameToken.KindOf(code);
        if (kind < 0) return $"<자리표 0x{code:X2}>";
        if (player != null) return NameToken.Of(player, kind);

        string label = kind switch
        {
            0 => "<이/가>",
            1 => "<은/는>",
            3 => "<와/과>",
            8 => "<(이)라는>",
            16 => "<(이)>",
            _ => $"<자리표 0x{code:X2}>",
        };
        return label;
    }

    /// <summary>
    /// 눈에 안 보이는 글자를 <c>&lt;01&gt;</c> 꼴로 드러낸다 — 고칠 때 있는 줄 알아야 한다.
    /// </summary>
    private static string Safe(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            output.Append(ch switch
            {
                '\r' => "<CR>",
                '\n' => "<LF>",
                '\t' => "<TAB>",
                _ => ch < 0x20 || ch == 0x7F ? $"<{(int)ch:X2}>" : ch.ToString(),
            });
        }
        return output.ToString();
    }

    /// <summary>전각 문장부호와 전각 영숫자를 보통 글자로 고른다 — 창에서 읽기 좋으라고.</summary>
    private static string Normalize(string text)
    {
        var output = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            char mapped = ch switch
            {
                '　' => ' ', '。' => '.', '、' => ',', '・' => '·',
                '「' or '」' or '『' or '』' => '"',
                '【' => '[', '】' => ']', '〔' => '(', '〕' => ')',
                _ => ch,
            };
            output.Append(mapped is >= '！' and <= '～' ? (char)(mapped - 0xFEE0) : mapped);
        }
        return output.ToString();
    }
}
