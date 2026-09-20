using System.Windows;

namespace CdsHelper.Game.Engine.Menu;

/// <summary>
/// 도시 화면이 띄우는 <b>창 둘</b>을 한자리에서 든다 — 시설 명령 창과 도시 커맨드 창.
/// </summary>
/// <remarks>
/// 도시 화면(<c>CityPicView</c>)이 그림 · 이름표 · 건물 눌림에 더해 창 띄우기까지
/// 쥐고 있었다. 창을 언제 어디에 내고 닫을 때 무엇을 되돌리는지는 <b>그림 일이 아니라
/// 차림표 일</b>이라 이리로 옮긴다.
///
/// <b>둘은 자리가 다르다.</b>
/// <list type="bullet">
///   <item>
///     <b>시설 명령 창</b>은 <b>그림 한가운데</b>다 — 게임이 누른 건물과 상관없이 늘
///     <c>그리는 영역의 원점 + 크기/2</c> 로 낸다(<c>0x00469E80</c>, 볼트
///     <c>15.분석-시설 화면 엔진</c>).
///   </item>
///   <item>
///     <b>도시 커맨드 창</b>은 <b>누른 자리</b>다 — 게임에 없는 창이고 그림 아무 데나
///     눌러서 내는 것이라 손이 간 자리에 뜨는 편이 맞다.
///   </item>
/// </list>
///
/// 창은 그림 안에 그리지 않고 <b>제 창</b>으로 띄운다. 자택처럼 줄이 열한 개나 되는
/// 시설은 그림을 통째로 덮어 버려 도시가 안 보이기 때문이다.
///
/// 시설 창이 닫힐 때 되돌릴 것(건물 사진 · 곡 · 이름표)은 도시 화면만 아는 일이라
/// <paramref name="onFacilityClosed"/> · <paramref name="onFacilityOpening"/> 으로 받는다.
/// </remarks>
/// <param name="owner">창을 얹을 도시 화면.</param>
/// <param name="onFacilityOpening">시설 창을 열기 직전에 할 일 — 이름표를 걷는다.</param>
/// <param name="onFacilityClosed">시설 창이 닫힌 뒤에 할 일 — 사진을 걷고 곡을 되돌린다.</param>
internal sealed class CityMenus(Window owner, Action onFacilityOpening, Action onFacilityClosed)
{
    private GameMenuHost? _facility;
    private GameMenuHost? _city;

    /// <summary>
    /// 시설 명령 창. 띄우고 겹치고 되돌아가는 것은 이쪽이 맡는다.
    /// </summary>
    public GameMenuHost Facility
    {
        get
        {
            if (_facility != null) return _facility;

            _facility = new GameMenuHost(owner);
            // 창을 그냥 닫아도(줄·ESC·오른쪽 단추) 도시 곡으로 돌아가고 사진도 걷힌다.
            _facility.Closed += onFacilityClosed;
            return _facility;
        }
    }

    /// <summary>도시 커맨드 창. 오른쪽 단추로 부른다.</summary>
    public GameMenuHost City => _city ??= new GameMenuHost(owner);

    /// <summary>
    /// 시설 창의 실제 창 — 안 떠 있으면 null.
    /// </summary>
    /// <remarks>
    /// <b>창을 새로 짓지 않는다.</b> 그림 짓시늉이 잠깐 감출 때 쓰는 자리라, 여기서
    /// <see cref="Facility"/> 를 건드리면 안 열려 있던 창이 생겨 버린다.
    /// </remarks>
    public Window? FacilityWindow => _facility?.Window;

    /// <summary>
    /// 떠 있는 창을 닫는다. 시설 창이 먼저다. 하나라도 닫았으면 true.
    /// </summary>
    /// <remarks>
    /// 그림이 초점을 쥔 채 ESC 가 왔을 때 쓴다 — 창이 초점을 쥐고 있으면 그쪽이 제 ESC 로
    /// 닫히므로 여기까지 오지 않는다.
    /// </remarks>
    public bool CloseOpen()
    {
        if (_facility is { IsOpen: true }) { _facility.Close(); return true; }
        if (_city is { IsOpen: true }) { _city.Close(); return true; }
        return false;
    }

    /// <summary>시설 명령 창을 연다. 열기 전에 이름표를 걷는다.</summary>
    public void ShowFacility(Func<GameMenu> build)
    {
        onFacilityOpening();
        Facility.Open(build);
    }

    /// <summary>시설 명령 창을 닫는다. 곡과 사진은 <c>Closed</c> 가 되돌린다.</summary>
    public void CloseFacility() => _facility?.Close();

    /// <summary>도시 커맨드 창을 누른 자리에 띄운다. 이미 떠 있으면 앞으로 가져온다.</summary>
    public void ShowCity(Func<GameMenu> build, Point at)
    {
        if (City.IsOpen) { City.Focus(); return; }
        City.Open(build, at);
    }

    /// <summary>도시 커맨드 창을 닫는다.</summary>
    public void CloseCity() => City.Close();
}
