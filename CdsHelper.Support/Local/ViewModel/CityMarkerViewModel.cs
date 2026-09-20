using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using CdsHelper.Support.UI.Views;
using Prism.Ioc;

namespace CdsHelper.Support.Local.ViewModel;

public partial class CityMarkerViewModel
{
    public byte CityId { get; set; }
    public string CityName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public int? Latitude { get; set; }
    public int? Longitude { get; set; }
    public bool HasLibrary { get; set; }
    public double MarkerSize { get; set; }
    public bool ShowLabel { get; set; }
    public bool ShowCoordinates { get; set; }

    public CityMarkerViewModel() { }

    public CityMarkerViewModel(City city, double markerSize)
    {
        CityId = city.Id;
        CityName = city.Name;
        X = city.PixelX ?? 0;
        Y = city.PixelY ?? 0;
        Latitude = city.Latitude;
        Longitude = city.Longitude;
        HasLibrary = city.HasLibrary;
        MarkerSize = markerSize;
    }

    [RelayCommand]
    private async Task LibraryClick()
    {
        try
        {
            var bookService = ContainerLocator.Container.Resolve<BookService>();
            var books = await bookService.GetBooksByCityIdAsync(CityId);

            var dialog = new LibraryBooksDialog
            {
                Owner = Application.Current.MainWindow
            };

            dialog.CityName = CityName;
            dialog.Title = $"{CityName} Library";

            foreach (var book in books)
                dialog.Books.Add(book);

            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading books: {ex.Message}", "Error");
        }
    }
}
