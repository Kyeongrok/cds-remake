namespace CdsHelper.Game.Local.Settings;

/// <summary>도시 창이 열릴 때 주는 효과.</summary>
public enum CityOpenEffect
{
    /// <summary>효과 없이 그 자리에 바로 뜬다.</summary>
    None,

    /// <summary>가운데서 제 크기로 펼쳐진다.</summary>
    Expand,

    /// <summary>오른쪽 바깥에서 미끄러져 들어온다.</summary>
    Slide,

    /// <summary>투명한 상태에서 서서히 나타난다.</summary>
    Fade,

    /// <summary>
    /// 확대/축소 — 파워포인트의 "나타내기 &gt; 확대/축소" 처럼 커지면서 흐림이 걷히고,
    /// 끝에서 제 크기를 살짝 넘쳤다 돌아온다.
    /// </summary>
    Zoom,
}
