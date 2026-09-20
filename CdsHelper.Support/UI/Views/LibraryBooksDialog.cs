using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Ioc;

namespace CdsHelper.Support.UI.Views;

[TemplatePart(Name = PART_BooksDataGrid, Type = typeof(DataGrid))]
[TemplatePart(Name = PART_CloseButton, Type = typeof(Button))]
public class LibraryBooksDialog : Window
{
    private const string PART_BooksDataGrid = "PART_BooksDataGrid";
    private const string PART_CloseButton = "PART_CloseButton";

    private DataGrid? _booksDataGrid;

    public static readonly DependencyProperty CityNameProperty =
        DependencyProperty.Register(nameof(CityName), typeof(string), typeof(LibraryBooksDialog),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty BooksProperty =
        DependencyProperty.Register(nameof(Books), typeof(ObservableCollection<Book>), typeof(LibraryBooksDialog),
            new PropertyMetadata(new ObservableCollection<Book>()));

    public string CityName
    {
        get => (string)GetValue(CityNameProperty);
        set => SetValue(CityNameProperty, value);
    }

    public ObservableCollection<Book> Books
    {
        get => (ObservableCollection<Book>)GetValue(BooksProperty);
        set => SetValue(BooksProperty, value);
    }

    static LibraryBooksDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LibraryBooksDialog),
            new FrameworkPropertyMetadata(typeof(LibraryBooksDialog)));
    }

    public LibraryBooksDialog()
    {
        Width = 600;
        Height = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Books = new ObservableCollection<Book>();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _booksDataGrid = GetTemplateChild(PART_BooksDataGrid) as DataGrid;

        if (GetTemplateChild(PART_CloseButton) is Button closeButton)
            closeButton.Click += OnCloseClick;

        if (_booksDataGrid != null)
            _booksDataGrid.ItemsSource = Books;
    }

    public async Task LoadBooksAsync(byte cityId, string cityName)
    {
        CityName = cityName;
        Title = $"{cityName} Library";

        try
        {
            var bookService = ContainerLocator.Container.Resolve<BookService>();
            var books = await bookService.GetBooksByCityIdAsync(cityId);

            Books.Clear();
            foreach (var book in books)
            {
                Books.Add(book);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LibraryBooksDialog] Error: {ex.Message}");
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
