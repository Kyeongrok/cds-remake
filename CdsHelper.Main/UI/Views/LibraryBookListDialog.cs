using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Main.UI.Views;

public class LibraryBookListDialog : Window
{
    public LibraryBookListDialog(byte cityId, string cityName, BookService bookService)
    {
        Title = $"도서관 - {cityName}";
        Width = 900;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = $"{cityName} 도서관 도서 목록",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeight = 24
        };
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "도서명", Binding = new System.Windows.Data.Binding(nameof(Book.Name)), Width = new DataGridLength(200) });
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "언어", Binding = new System.Windows.Data.Binding(nameof(Book.Language)), Width = new DataGridLength(120) });
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "게제 힌트", Binding = new System.Windows.Data.Binding(nameof(Book.Hint)), Width = new DataGridLength(200) });
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "필요", Binding = new System.Windows.Data.Binding(nameof(Book.Required)), Width = new DataGridLength(100) });
        dataGrid.Columns.Add(new DataGridTextColumn { Header = "개제조건", Binding = new System.Windows.Data.Binding(nameof(Book.Condition)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(dataGrid, 1);
        grid.Children.Add(dataGrid);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var btnClose = new Button
        {
            Content = "닫기",
            Width = 80,
            Height = 28,
            IsCancel = true,
            IsDefault = true
        };
        btnPanel.Children.Add(btnClose);
        Grid.SetRow(btnPanel, 2);
        grid.Children.Add(btnPanel);

        Content = grid;

        Loaded += async (_, _) =>
        {
            try
            {
                var books = await bookService.GetBooksByCityIdAsync(cityId);
                dataGrid.ItemsSource = books;
                if (books.Count == 0)
                {
                    header.Text = $"{cityName} 도서관 도서 목록 (등록된 도서 없음)";
                    header.Foreground = Brushes.Gray;
                }
                else
                {
                    header.Text = $"{cityName} 도서관 도서 목록 ({books.Count}권)";
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"도서 목록 로드 실패: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }
}
