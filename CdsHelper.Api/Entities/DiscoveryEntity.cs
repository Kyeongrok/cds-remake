using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

[Table("Discoveries")]
public class DiscoveryEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 게재힌트 ID (FK → Hints)
    /// </summary>
    public int? HintId { get; set; }

    /// <summary>
    /// 게재힌트 Navigation property
    /// </summary>
    [ForeignKey(nameof(HintId))]
    public HintEntity? Hint { get; set; }

    /// <summary>
    /// 등장 조건
    /// </summary>
    [MaxLength(50)]
    public string? AppearCondition { get; set; }

    /// <summary>
    /// 관련 도서명
    /// </summary>
    [MaxLength(200)]
    public string? BookName { get; set; }

    /// <summary>위도 시작 (N=양수, S=음수)</summary>
    public int? LatFrom { get; set; }

    /// <summary>위도 끝 (점인 경우 LatFrom과 동일)</summary>
    public int? LatTo { get; set; }

    /// <summary>경도 시작 (E=양수, W=음수)</summary>
    public int? LonFrom { get; set; }

    /// <summary>경도 끝 (점인 경우 LonFrom과 동일)</summary>
    public int? LonTo { get; set; }

    // Navigation properties
    public ICollection<DiscoveryParentEntity> Parents { get; set; } = new List<DiscoveryParentEntity>();
    public ICollection<DiscoveryParentEntity> Children { get; set; } = new List<DiscoveryParentEntity>();
}
