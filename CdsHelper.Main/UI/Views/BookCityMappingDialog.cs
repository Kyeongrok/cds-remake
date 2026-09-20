using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdsHelper.Support.Local.Models;
using Prism.Commands;

namespace CdsHelper.Main.UI.Views;

public class BookCityMappingDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private Book _book = null!;
    public Book Book
    {
        get => _book;
        set { _book = value; OnPropertyChanged(); }
    }

    public ObservableCollection<CitySelection> CitySelections { get; } = new();

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public BookCityMappingDialog()
    {
        Title = "도서관 매핑";
        Width = 400;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        SaveCommand = new DelegateCommand(OnSave);
        CancelCommand = new DelegateCommand(OnCancel);

        DataContext = this;
        Content = CreateContent();
    }

    private UIElement CreateContent()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var header = new TextBlock
        {
            Text = "도서관이 있는 도시를 선택하세요:",
            Margin = new Thickness(10),
            FontWeight = FontWeights.Bold
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // City list
        var listBox = new ListBox
        {
            Margin = new Thickness(10, 0, 10, 10),
            SelectionMode = SelectionMode.Multiple
        };
        listBox.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding("CitySelections"));

        var itemTemplate = new DataTemplate();
        var checkBoxFactory = new FrameworkElementFactory(typeof(CheckBox));
        checkBoxFactory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("IsSelected") { Mode = System.Windows.Data.BindingMode.TwoWay });
        checkBoxFactory.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding("CityName"));
        itemTemplate.VisualTree = checkBoxFactory;
        listBox.ItemTemplate = itemTemplate;

        Grid.SetRow(listBox, 1);
        grid.Children.Add(listBox);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10)
        };

        var saveButton = new Button
        {
            Content = "저장",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Command = SaveCommand
        };
        buttonPanel.Children.Add(saveButton);

        var cancelButton = new Button
        {
            Content = "취소",
            Width = 80,
            Height = 30,
            Command = CancelCommand
        };
        buttonPanel.Children.Add(cancelButton);

        Grid.SetRow(buttonPanel, 2);
        grid.Children.Add(buttonPanel);

        return grid;
    }

    public void Initialize(Book book, IEnumerable<City> allCities)
    {
        Book = book;
        Title = $"도서관 매핑 - {book.Name}";

        CitySelections.Clear();

        // 도서관이 있는 도시만 표시
        var citiesWithLibrary = allCities.Where(c => c.HasLibrary).OrderBy(c => c.Name);

        foreach (var city in citiesWithLibrary)
        {
            var isSelected = book.LibraryCityIds.Contains(city.Id) ||
                             book.LibraryCityNames.Contains(city.Name);

            CitySelections.Add(new CitySelection
            {
                CityId = city.Id,
                CityName = city.Name,
                IsSelected = isSelected
            });
        }
    }

    public List<byte> GetSelectedCityIds()
    {
        return CitySelections
            .Where(c => c.IsSelected)
            .Select(c => c.CityId)
            .ToList();
    }

    public List<string> GetSelectedCityNames()
    {
        return CitySelections
            .Where(c => c.IsSelected)
            .Select(c => c.CityName)
            .ToList();
    }

    private void OnSave()
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel()
    {
        DialogResult = false;
        Close();
    }
}

public class CitySelection : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public byte CityId { get; set; }
    public string CityName { get; set; } = string.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
