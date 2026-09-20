namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 세이브 파일의 발견물 슬롯 (한국어판 파일 0x1AA81, ROW = 164바이트 x 274,
/// 발견물 ID로 인덱싱). 자리는 판 문자열 길이에 딸려 움직이므로
/// SaveDataService 가 알맹이 시작을 재서 더한다.
/// 슬롯 안 +0x15 의 state 바이트:
///   bit 6 (0x40) = 발견됨
///   bit 7 (0x80) = 보고됨
///   하위 6비트 = 발견물 종류별 base 값 (0x0C: 건축물, 0x04: 일부 교회, 0x08 등)
/// </summary>
public class DiscoveryData
{
    public int Id { get; set; }
    public byte State { get; set; }

    public bool IsDiscovered => (State & 0x40) != 0;
    public bool IsAnnounced => (State & 0x80) != 0;
}
