namespace CdsHelper.Support.Local.Helpers;

/// <summary>좌표 인식 결과.</summary>
public record CoordinatePrediction(
    bool IsNorth,   // true=북위, false=남위
    int LatValue,   // 0-90
    bool IsEast,    // true=동경, false=서경
    int LonValue    // 0-180
)
{
    public double ToLat() => IsNorth ? LatValue : -LatValue;
    public double ToLon() => IsEast ? LonValue : -LonValue;

    public override string ToString()
        => $"{(IsNorth ? "북위" : "남위")} {LatValue}  {(IsEast ? "동경" : "서경")} {LonValue}";
}

/// <summary>학습 데이터 라벨.</summary>
public record CoordinateLabel(
    string ImagePath,
    int LatDir,     // 0=북위, 1=남위
    int LatValue,   // 0-90
    int LonDir,     // 0=동경, 1=서경
    int LonValue    // 0-180
);