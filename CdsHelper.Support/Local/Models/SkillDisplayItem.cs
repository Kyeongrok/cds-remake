namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 스킬 통합 표시용 모델
/// </summary>
public class SkillDisplayItem
{
    public string Name { get; set; } = "";
    public byte PlayerLevel { get; set; }
    public byte AdjutantLevel { get; set; }
    public byte NavigatorLevel { get; set; }
    public byte SurveyorLevel { get; set; }
    public byte InterpreterLevel { get; set; }

    // 최고 레벨
    public byte BestLevel => Math.Max(PlayerLevel,
        Math.Max(AdjutantLevel, Math.Max(NavigatorLevel, Math.Max(SurveyorLevel, InterpreterLevel))));

    // 최고 레벨 보유자 (우선순위: 플레이어 > 부관 > 항해사 > 측량사 > 통역)
    public string BestOwner
    {
        get
        {
            var best = BestLevel;
            if (best == 0) return "None";
            if (PlayerLevel == best) return "Player";
            if (AdjutantLevel == best) return "Adjutant";
            if (NavigatorLevel == best) return "Navigator";
            if (SurveyorLevel == best) return "Surveyor";
            if (InterpreterLevel == best) return "Interpreter";
            return "None";
        }
    }

    // 최고 레벨 보유자 색상
    public string BestColor => BestOwner switch
    {
        "Player" => "#2196F3",      // 파란색
        "Adjutant" => "#4CAF50",    // 초록색
        "Navigator" => "#FF9800",   // 주황색
        "Surveyor" => "#9C27B0",    // 보라색
        "Interpreter" => "#F44336", // 빨간색
        _ => "#CCCCCC"              // 회색 (없음)
    };

    // 툴팁 텍스트
    public string ToolTipText
    {
        get
        {
            var best = BestLevel;
            if (best == 0) return "없음";
            return BestOwner switch
            {
                "Player" => $"플레이어: {best}",
                "Adjutant" => $"부관: {best}",
                "Navigator" => $"항해사: {best}",
                "Surveyor" => $"측량사: {best}",
                "Interpreter" => $"통역: {best}",
                _ => "없음"
            };
        }
    }
}
