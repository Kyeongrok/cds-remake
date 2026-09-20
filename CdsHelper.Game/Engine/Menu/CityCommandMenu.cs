namespace CdsHelper.Game.Engine.Menu;

/// <summary>
/// 도시 커맨드 창의 <b>줄 차례와 이름</b>. 도시 화면에서 오른쪽 단추를 누르면 뜬다.
/// </summary>
/// <remarks>
/// 무슨 줄이 어떤 차례로 서는지는 <b>차림표 일</b>이라 그림 쪽에서 떼어 왔다. 누르면
/// 무슨 일이 일어나는지는 도시 화면만 아는 것이라 <see cref="Actions"/> 로 받는다.
///
/// 제목은 도시 이름이고 제목 줄에 <b>닫기(X)</b> 가 있다 — 게임에 없는 창이라 우리가
/// 지은 모양이다. 마지막 「취소」는 <see cref="GameMenu"/> 가 알아서 회녹색 띠로 낸다.
///
/// 「지도를 본다」만은 한 겹 더 들어간다(<see cref="Map"/>).
/// </remarks>
internal static class CityCommandMenu
{
    /// <summary>
    /// 줄마다 할 일. 도시 화면이 채워 준다.
    /// </summary>
    /// <param name="EnterMapPoint">맵 포인트에 들어간다 — 건물을 골라 그 창을 연다.</param>
    /// <param name="ShowPerson">인물 정보 — 부하가 있으면 누구를 볼지 먼저 묻는다.</param>
    /// <param name="ShowFleet">함대 정보 — 배가 한 척도 없으면 null 이라 줄이 흐리다(<c>0x0049300C</c> 가
    /// <c>0x00473CD0() == -1</c> 을 켜짐 칸에 넣는다).</param>
    /// <param name="ShowBelongings">소지품 정보.</param>
    /// <param name="ShowCityInfo">도시 정보.</param>
    /// <param name="ShowHints">힌트 정보.</param>
    /// <param name="ShowContract">계약 정보.</param>
    /// <param name="ShowPatrons">후원자 정보(스폰서 일람).</param>
    /// <param name="ShowMap">지도를 본다 — 한 겹 더 들어간다.</param>
    /// <param name="Quit">게임 종료.</param>
    /// <param name="Cancel">취소 — 창을 닫는다. 제목 줄의 닫기도 이것이다.</param>
    internal readonly record struct Actions(
        Action EnterMapPoint, Action ShowPerson, Action? ShowFleet, Action ShowBelongings,
        Action ShowCityInfo, Action ShowHints, Action ShowContract, Action ShowPatrons,
        Action ShowMap, Action Quit, Action Cancel);

    /// <summary>도시 커맨드 창 한 벌.</summary>
    public static GameMenu Build(string cityName, in Actions on) =>
        new(cityName, on.Cancel,
            ("맵 포인트에 들어간다", on.EnterMapPoint),
            ("인물 정보", on.ShowPerson),
            ("함대 정보", on.ShowFleet),
            ("소지품 정보", on.ShowBelongings),
            ("도시 정보", on.ShowCityInfo),
            ("힌트 정보", on.ShowHints),
            ("계약 정보", on.ShowContract),
            ("후원자 정보", on.ShowPatrons),
            ("지도를 본다", on.ShowMap),
            ("게임 종료", on.Quit),
            ("취소", on.Cancel));

    /// <summary>「지도를 본다」 한 겹 — 항해지도 · 취소.</summary>
    /// <remarks>
    /// 게임의 <c>0x0049328E</c> 다(<c>0x0053BF70</c> "항해지도" · <c>0x0053BF80</c> "취소",
    /// 제목 <c>0x0053BF88</c>). <b>도시에는 주변지도가 없다.</b> 항해지도는 바다와 같은
    /// 모달 창(<c>0x00416A00</c>)이고, 닫으면 이 한 겹으로 되돌아온다.
    /// </remarks>
    public static GameMenu Map(Action wide, Action back) =>
        new("지도를 본다", null,
            ("항해지도", wide),
            ("취소", back));
}
