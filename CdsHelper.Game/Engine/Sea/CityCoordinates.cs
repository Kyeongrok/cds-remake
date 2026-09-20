using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Sea;

/// <summary>
/// 바다 커맨드의 「도시좌표」(<c>0x004269F0</c>) — 가 본 도시의 위도·경도를 일러 준다.
/// </summary>
/// <remarks>
/// 줄은 <b>측량</b>을 아는 사람이 있어야 뜬다(<c>0x0048B469</c> → <c>0x0047CCA0(7, 2, …)</c>) —
/// 제독이나 <b>측량사 자리</b>(부하 자리 2) 가운데 측량이 1 이상이면 된다.
///
/// 좌표는 도시 칸(도시 표 <c>+0x04</c>·<c>+0x08</c>)을 열여섯 곱한 값으로 센다 —
/// 배 자리(<c>0x005B63B0</c>·<c>0x005B63B4</c>)와 같은 눈금이다.
/// <code>
///   경도 = 18 x |칸X x 16 − 20000| / 2000     20000 이상이면 동, 아니면 서
///   위도 =  9 x |칸Y x 16 − 10000| / 1000     10000 이상이면 남, 아니면 북
/// </code>
/// 가 본 도시가 열여섯 곳이 안 되면 문화권을 안 묻고 바로 도시 목록을 낸다(<c>0x00426A6B</c>).
/// </remarks>
public static class CityCoordinates
{
    /// <summary>측량 기능 칸(<c>0x00560A10</c> 표의 일곱째).</summary>
    public const int SurveySkill = 7;

    /// <summary>측량사가 앉는 부하 자리(<c>0x00560AD0</c> 의 셋째 — 0 부관 · 1 항해사 · 2 측량사 · 3 통역).</summary>
    public const int SurveyorSlot = 2;

    /// <summary>문화권을 먼저 묻기 시작하는 도시 수(<c>0x00426A6B</c> 의 <c>cmp 0x10</c>).</summary>
    public const int AskRegionFrom = 16;

    /// <summary>안내 글(<c>0x005333E0</c>) — 문화권을 물을 때 한 번 낸다.</summary>
    public const string Guide = "알고 싶은 도시의 문화권 좌표를 선택해 주십시오. "
                              + "전도시 일람으로, 전도시를 표시해 선택할 수도 있습니다";

    /// <summary>문화권 목록 끝에 붙는 줄(<c>0x005333D0</c>)과 도시 목록의 제목(<c>0x005333C0</c>).</summary>
    public const string AllCities = "전도시 일람", CityListTitle = "도시일람";

    /// <summary>문화권 이름 열하나(<c>0x00560BE8</c>).</summary>
    public static readonly string[] Regions =
    [
        "이베리아", "북유럽", "지중해", "아프리카", "중근동", "인도",
        "중국", "중앙아시아", "동남아시아", "일본", "아메리카",
    ];

    /// <summary>한 도시의 좌표 — 도(度)와 방위.</summary>
    /// <param name="North">북쪽인지(<c>칸Y x 16 &lt; 10000</c>).</param>
    /// <param name="East">동쪽인지(<c>칸X x 16 ≥ 20000</c>).</param>
    public readonly record struct Spot(int Latitude, bool North, int Longitude, bool East)
    {
        /// <summary>「남위 12도 서경 7도」 꼴.</summary>
        public string Words =>
            $"{(North ? "북" : "남")}위{Latitude}도 {(East ? "동" : "서")}경{Longitude}도";
    }

    /// <summary>그 도시의 좌표. 표에 없으면 null.</summary>
    public static Spot? Of(CityExeTable? cities, int city)
    {
        if (cities?.TryCell(city, out int x, out int y, out _) != true) return null;

        int lon = x << 4, lat = y << 4;
        return new Spot(9 * Math.Abs(lat - 10000) / 1000, lat < 10000,
                        18 * Math.Abs(lon - 20000) / 2000, lon >= 20000);
    }
}
