namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// <b>운명 코드</b> 한 공간 — 주인공의 운명 자리와 여급의 궁합 코드가 여기서 만난다.
/// </summary>
/// <remarks>
/// <b>0~15 가 아니다.</b> 코드는 <b>0~31 한 덩어리</b>고, 아래 절반이 젊은 제독 몫,
/// 위 절반이 그 <b>중년</b> 몫이다 — 초상화가 <c>얼굴 + 16</c> 인 것과 똑같은 짜임이다.
///
/// <code>
///   주인공 코드 = 운명 자리 + (서른여섯 살 이상이면 16)     BarmaidTable.FortuneOf
///   여급   코드 = 표에 적힌 그대로 (0~30)                   표 0x00517AF8
///   궁합        = 두 코드가 같거나 하나 차이                 BarmaidTable.DestinedGap
/// </code>
///
/// 여급 127명이 실제로 <b>0~30 을 다 쓴다</b>(아래 83명 · 위 44명). 리스본의 알다가
/// 1480년부터 코드 19 로 서 있는데, 그것은 「운명 자리 3 인 <b>중년</b> 제독」의 짝이라는
/// 뜻이다.
///
/// <b>그래서 자리를 0~16 으로 늘릴 수는 없다</b> — 16 은 이미 「자리 0 의 중년」이다.
/// 늘릴 까닭도 없다. 운명 자리는 얼굴에 매인 값이 아니라서, 주인공을 지을 때
/// <b>열여섯 가운데 하나를 굴려 주면</b> 된다(<c>CharacterMakeDialog</c>).
/// </remarks>
public static class FortuneCodes
{
    /// <summary>운명 자리 수. 젊은 얼굴 열여섯과 같다.</summary>
    public const int Slots = 16;

    /// <summary>그 자리와 나이가 내는 코드.</summary>
    public static int CodeOf(int slot, bool aged) =>
        Math.Clamp(slot, 0, Slots - 1) + (aged ? Slots : 0);

    /// <summary>그 자리를 맡은 여급이 하나도 없으면 참.</summary>
    public static bool Empty(int slot, BarmaidTable? table)
    {
        if (table == null) return false;

        foreach (var her in table.Barmaids)
            if (her.Fortune % Slots == slot) return false;
        return true;
    }
}
