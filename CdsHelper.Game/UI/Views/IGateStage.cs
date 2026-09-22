namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 성문 앞 연출을 돌릴 무대 — 하트(교섭) · 동전(잠입) · 달아나기.
/// </summary>
/// <remarks>
/// 뭍으로 성문에 닿으면 <see cref="GateScene"/> 이 도시 그림을 새로 펴서 그 위에서 돌리고,
/// 배로 닿아 항구에서 마을로 들 때는 이미 떠 있는 <see cref="CityPicView"/> 가 제 그림 위에서
/// 돌린다. 차림표(<see cref="HostileCityMenu"/>)는 어느 쪽인지 모른 채 이것만 부른다.
/// </remarks>
internal interface IGateStage
{
    /// <summary>교섭하는 하트 — 되면 커지고, 어그러지면 깨진다.</summary>
    void PlayHeart(bool won);

    /// <summary>잠입하는 동전 — 돌다가 되면 첫째 장, 어그러지면 넷째 장에서 멎는다.</summary>
    void PlayCoin(bool won);

    /// <summary>들킨 뒤 달아나는 벌.</summary>
    void PlayEscape(bool won);
}
