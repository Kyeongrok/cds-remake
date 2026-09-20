using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using Prism.Commands;

namespace CdsHelper.Main.UI.Views;

public class EventQueueDialog : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public ObservableCollection<AppEvent> Events { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CloseCommand { get; }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public EventQueueDialog()
    {
        Title = "이벤트 큐";
        Width = 800;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        RefreshCommand = new DelegateCommand(Refresh);
        ClearCommand = new DelegateCommand(Clear);
        CloseCommand = new DelegateCommand(() => Close());

        DataContext = this;
        Content = CreateContent();

        // 초기 로드
        Refresh();

        // 이벤트 추가 시 자동 갱신
        EventQueueService.Instance.EventAdded += (s, e) =>
        {
            Dispatcher.Invoke(Refresh);
        };
    }

    private UIElement CreateContent()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Toolbar
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var refreshButton = new Button
        {
            Content = "새로고침",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Command = RefreshCommand
        };
        toolbar.Children.Add(refreshButton);

        var clearButton = new Button
        {
            Content = "전체 삭제",
            Width = 80,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
            Command = ClearCommand
        };
        toolbar.Children.Add(clearButton);

        Grid.SetRow(toolbar, 0);
        grid.Children.Add(toolbar);

        // DataGrid
        var dataGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            AlternatingRowBackground = System.Windows.Media.Brushes.LightGray
        };
        dataGrid.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("Events"));

        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "시간",
            Binding = new Binding("TimestampDisplay"),
            Width = new DataGridLength(100)
        });

        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "유형",
            Binding = new Binding("TypeDisplay"),
            Width = new DataGridLength(120)
        });

        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "소스",
            Binding = new Binding("Source"),
            Width = new DataGridLength(120)
        });

        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "메시지",
            Binding = new Binding("Message"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });

        Grid.SetRow(dataGrid, 1);
        grid.Children.Add(dataGrid);

        // Footer
        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusTextBlock = new TextBlock();
        statusTextBlock.SetBinding(TextBlock.TextProperty, new Binding("StatusText"));
        Grid.SetColumn(statusTextBlock, 0);
        footer.Children.Add(statusTextBlock);

        var closeButton = new Button
        {
            Content = "닫기",
            Width = 80,
            Height = 30,
            Command = CloseCommand
        };
        Grid.SetColumn(closeButton, 1);
        footer.Children.Add(closeButton);

        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        return grid;
    }

    private void Refresh()
    {
        Events.Clear();
        foreach (var evt in EventQueueService.Instance.GetAll())
        {
            Events.Add(evt);
        }
        StatusText = $"이벤트: {Events.Count}개";
    }

    private void Clear()
    {
        var result = MessageBox.Show("모든 이벤트를 삭제하시겠습니까?", "확인",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            EventQueueService.Instance.Clear();
            Refresh();
        }
    }
}
