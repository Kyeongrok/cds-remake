namespace CdsHelper.Support.Local.Models;

public class HintData
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public byte Value { get; set; }

    /// <summary>
    /// 발견 여부 (Bit 1)
    /// </summary>
    public bool IsDiscovered => (Value & 0x02) != 0;

    /// <summary>
    /// 힌트 보유 여부 (Bit 2)
    /// </summary>
    public bool HasHint => (Value & 0x04) != 0;

    /// <summary>
    /// 상태 텍스트
    /// 0x08 = 미발견, 힌트 없음
    /// 0x0B = 발견, 힌트 없이
    /// 0x0D = 미발견, 힌트 있음
    /// 0x0F = 발견, 힌트 있음
    /// </summary>
    public string StatusText => Value switch
    {
        0x08 => "미발견",
        0x0B => "발견(힌트X)",
        0x0D => "힌트있음",
        0x0F => "발견",
        _ => $"0x{Value:X2}"
    };

    public string DisplayText => $"{Index}: {Name}";

    /// <summary>
    /// 관련 책의 언어 (여러 책이면 쉼표로 구분)
    /// </summary>
    public string BookLanguage { get; set; } = "";

    /// <summary>
    /// 관련 책의 필요 스킬 (여러 책이면 쉼표로 구분)
    /// </summary>
    public string BookRequired { get; set; } = "";

    /// <summary>
    /// 관련 책의 개제조건/선행 발견물 (여러 책이면 쉼표로 구분)
    /// </summary>
    public string BookCondition { get; set; } = "";
}
