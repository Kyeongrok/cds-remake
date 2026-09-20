using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

[Table("Books")]
public class BookEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Language { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Hint { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Required { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Condition { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<BookCityEntity> BookCities { get; set; } = new List<BookCityEntity>();
    public ICollection<BookHintEntity> BookHints { get; set; } = new List<BookHintEntity>();
}
