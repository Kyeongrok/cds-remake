using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

[Table("BookHints")]
public class BookHintEntity
{
    public int BookId { get; set; }

    public int HintId { get; set; }

    // Navigation properties
    [ForeignKey(nameof(BookId))]
    public BookEntity Book { get; set; } = null!;

    [ForeignKey(nameof(HintId))]
    public HintEntity Hint { get; set; } = null!;
}
