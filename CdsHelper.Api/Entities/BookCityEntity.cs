using System.ComponentModel.DataAnnotations.Schema;

namespace CdsHelper.Api.Entities;

[Table("BookCities")]
public class BookCityEntity
{
    public int BookId { get; set; }

    public byte CityId { get; set; }

    // Navigation properties
    [ForeignKey(nameof(BookId))]
    public BookEntity Book { get; set; } = null!;

    [ForeignKey(nameof(CityId))]
    public CityEntity City { get; set; } = null!;
}
