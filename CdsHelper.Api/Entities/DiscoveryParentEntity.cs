using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

/// <summary>
/// 발견물 선행 조건 매핑 (1:N 자기참조)
/// </summary>
[Table("DiscoveryParents")]
public class DiscoveryParentEntity
{
    /// <summary>
    /// 발견물 ID (이 발견물을 얻으려면 ParentDiscoveryId가 필요)
    /// </summary>
    public int DiscoveryId { get; set; }

    /// <summary>
    /// 선행 발견물 ID
    /// </summary>
    public int ParentDiscoveryId { get; set; }

    // Navigation properties
    [ForeignKey(nameof(DiscoveryId))]
    public DiscoveryEntity Discovery { get; set; } = null!;

    [ForeignKey(nameof(ParentDiscoveryId))]
    public DiscoveryEntity ParentDiscovery { get; set; } = null!;
}
