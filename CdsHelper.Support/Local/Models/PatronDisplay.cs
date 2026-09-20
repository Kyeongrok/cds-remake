namespace CdsHelper.Support.Local.Models;

/// <summary>
/// 후원자 표시용 클래스 (DataGrid 바인딩용)
/// </summary>
public class PatronDisplay
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string City { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string SupportRate { get; set; } = "";
    public int Discernment { get; set; }
    public int? AppearYear { get; set; }
    public int? RetireYear { get; set; }
    public string StatusDisplay { get; set; } = "";
    public int Fame { get; set; }
    public bool IsFameMet { get; set; } // 플레이어 명성 >= 후원자 명성
    public int Wealth { get; set; }
    public string Power { get; set; } = "";
    public string Preferences { get; set; } = "";
    public string Note { get; set; } = "";
}
