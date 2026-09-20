using System.Windows;
using CdsHelper.Game.Engine.Market;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 시장 — 들어설 때의 인사와, 물건을 사고파는 두 창.
/// </summary>
/// <remarks>
/// 게임의 시장은 시설 객체 하나다. 가상함수표가 <c>0x0051A0F0</c> 이고 인사 자리
/// (<c>+0x28</c>)가 <c>0x004B36C0</c> 인데, 하는 일이라고는 제 얼굴을 들고 알림창을
/// 부르는 것뿐이다.
/// <code>
///   004B36C0  push 0x00544588        ; "어서 오세요."
///             mov  eax, [ecx+0x80]   ; 이 시설의 화자 얼굴
///             push 0 / push eax
///             call 0x004692E0        ; 얼굴 달린 알림창
/// </code>
/// 얼굴은 마을 문화권이 정한다(<see cref="Local.Helpers.SpeakerFaceTable"/>) — 같은 시장이라도
/// 리스본과 이스탄불에 딴 사람이 선다.
///
/// 사고파는 규칙은 <see cref="Engine.Market.Market"/> 이 알고, 여기서는 창을 여는 일만 맡는다.
/// </remarks>
/// <param name="view">이 시장을 낸 도시 창. 창들의 주인이다.</param>
/// <param name="game">이 판 — 주인공과 표들이 여기서 온다.</param>
/// <param name="cityId">이 마을 번호. 시세와 내놓는 물건이 마을마다 다르다.</param>
/// <param name="culture">이 마을 문화권. 화자 얼굴이 여기 따라 갈린다.</param>
/// <param name="rules">사고파는 규칙. 아이템 표를 못 읽었으면 null 이고, 그러면 줄이 흐리다.</param>
internal sealed class MarketMenu(Window view, Engine.Game game, int cityId, int culture,
                                 Market? rules)
{
    /// <summary>시장의 건물 코드. 화자표에서 장사꾼을 찾을 때 쓴다.</summary>
    private const int BuildingCode = 7;

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly int _cityId = cityId;
    private readonly int _culture = culture;
    private readonly Market? _rules = rules;

    /// <summary>들어설 때 장사꾼이 건네는 한마디(<c>0x00544588</c>).</summary>
    public void Greet() =>
        ConfirmDialog.Tell(_view, "어서 오세요.",
                           face: _game.SpeakerFace(BuildingCode, _culture));

    /// <summary>"구입" — 이 마을이 내놓은 물건을 산다.</summary>
    public void Buy()
    {
        if (_rules == null) return;
        MarketBuyDialog.Show(_view, _game.Player, _rules, _cityId,
                             _game.ItemText, _game.ItemPictures, _game.Discoveries,
                             _game.SpeakerFace(BuildingCode, _culture), _game);
    }

    /// <summary>"매각" — 지닌 물건을 판다.</summary>
    public void Sell()
    {
        if (_rules == null || _game.Items is not { } items) return;
        MarketSellDialog.Show(_view, _game.Player, _rules, items, _cityId,
                              _game.SpeakerFace(BuildingCode, _culture));
    }
}
