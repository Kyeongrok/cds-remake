using System.Windows;
using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 도서관 — 들어설 때 사서가 건네는 인사와 서가 열람.
/// </summary>
/// <remarks>
/// 게임도 서가를 보여 주기 전에 한마디를 낸다. 대답은 받지 않는다 — 화면에도 단추가
/// "확인" 하나뿐이라 물음이 아니라 인사다.
/// </remarks>
/// <param name="view">이 도서관을 낸 도시 창. 창들의 주인이다.</param>
/// <param name="game">이 판 — 책 표와 화자 얼굴이 여기서 온다.</param>
/// <param name="cityId">이 마을 번호.</param>
/// <param name="cityName">이 마을 이름. 서가 제목에 붙는다.</param>
/// <param name="culture">이 마을 문화권. 사서 얼굴이 여기 따라 갈린다.</param>
/// <param name="buildings">건물 표. 책이 가리키는 건물 이름을 푼다.</param>
internal sealed class LibraryMenu(Window view, Engine.Game game, int cityId, string cityName,
                                  int culture, CityBuildingTable buildings)
{
    /// <summary>도서관의 건물 코드. 화자표에서 사서를 찾을 때 쓴다.</summary>
    private const int BuildingCode = 8;

    private readonly Window _view = view;
    private readonly Engine.Game _game = game;
    private readonly int _cityId = cityId;
    private readonly string _cityName = cityName;
    private readonly int _culture = culture;
    private readonly CityBuildingTable _buildings = buildings;

    /// <summary>사서가 건네는 한마디. 얼굴은 이 마을 문화권이 정한다.</summary>
    public void Greet() =>
        ConfirmDialog.Tell(_view, "책을 찾고 계십니까?",
                           face: _game.SpeakerFace(BuildingCode, _culture));

    /// <summary>책 표를 못 읽었으면 열람 줄이 흐리다.</summary>
    public bool CanRead => _game.Books != null;

    /// <summary>
    /// 「검색」(<c>0x004B3540</c>) — 사서가 묻기만 하고 목록은 뜨지 않는다.
    /// </summary>
    /// <remarks>
    /// 게임은 책 257권을 훑어 <b>기억 칸(<c>+0x40</c>)이 0 이 아닌 것</b>만 고르게 한다
    /// (<c>0x004B3440</c> 이 램 표 <c>0x00581120</c> 을 0x48 간격으로 훑는다). 그런데 그 칸을
    /// 0 말고 다른 값으로 두는 코드가 <b>어디에도 없다</b> — 판을 열 때 <c>0x00414151</c> 이
    /// 0 으로 채우고, 세이브(<c>0x00414160</c> 읽기 · <c>0x00414200</c> 쓰기)는 그 값을 넣었다
    /// 뺄 뿐이다. 그래서 목록은 늘 비어 있고 「오래 기다리셨습니다. 이 책입니다.」
    /// (<c>0x00544AF0</c>)와 「안됐습니다만, 그 책은 여기에는 없습니다.」(<c>0x00544B18</c>)는
    /// <b>원본에서도 절대 안 나온다</b>. 물음만 옮기고 그 뒤는 원본대로 비워 둔다.
    /// </remarks>
    public void Search(Window owner) =>
        ConfirmDialog.Tell(owner, "무슨 책을 찾고 계십니까?",
                           face: _game.SpeakerFace(BuildingCode, _culture));

    /// <summary>
    /// 서가를 펼친다.
    /// </summary>
    /// <param name="owner">
    /// 서가 창의 주인. <b>명령 창을 넘겨야 한다</b> — 도시 그림 창을 주인으로 삼으면
    /// 서가를 닫을 때 활성 창이 도시 그림으로 갔다가, 명령 창을 누를 때 도로 넘어와서
    /// 창이 한 번 깜빡인다. 보급·소지품 창도 같은 까닭으로 명령 창을 주인으로 쓴다.
    /// </param>
    /// <param name="say">못 읽는 책의 까닭을 낼 곳 — 게임 화면 맨 아래 띠다.</param>
    public void Read(Window owner, Action<string>? say = null)
    {
        if (_game.Books is not { } books) return;

        LibraryDialog.Show(owner, _game.Directory, _cityName, _cityId,
                           _game.Player, books, _buildings, _game.HintName,
                           _game.Book, id => _game.Hints?.Find(id)?.Text ?? "", say,
                           _game.Sfx, Reported);
    }

    /// <summary>
    /// 그 힌트가 가리키는 발견물을 <b>찾아서 보고까지</b> 했는지 — 펼친 책의 종이 색이
    /// 이것으로 갈린다(힌트 상태 <c>(+4 &amp; 3) == 3</c>, <c>0x00464C50</c>).
    /// </summary>
    /// <remarks>
    /// 힌트와 발견물은 번호로 짝을 맺는다(힌트의 <c>Discovery</c> 와 발견물의 <c>Hint</c>).
    /// 책으로만 얻은 힌트는 흰 종이다.
    /// </remarks>
    private bool Reported(int hint)
    {
        if (_game.Hints?.Find(hint) is not { } row || _game.Discoveries?.Table is not { } table)
            return false;

        foreach (var found in table.Discoveries)
            if (found.Hint == row.Discovery
                && _game.Player.HasFound(found.Id) && _game.Player.HasAnnounced(found.Id))
                return true;
        return false;
    }
}
