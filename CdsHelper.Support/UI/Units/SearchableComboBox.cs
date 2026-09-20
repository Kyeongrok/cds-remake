using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace CdsHelper.Support.UI.Units;

public class SearchableComboBox : Control
{
    private TextBox? _searchBox;
    private ListBox? _listBox;
    private Popup? _popup;
    private Button? _clearButton;
    private ICollectionView? _collectionView;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(SearchableComboBox),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(SearchableComboBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(SearchableComboBox),
            new PropertyMetadata("Name"));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(SearchableComboBox),
            new PropertyMetadata("", OnSearchTextChanged));

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    static SearchableComboBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchableComboBox),
            new FrameworkPropertyMetadata(typeof(SearchableComboBox)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _searchBox = GetTemplateChild("PART_SearchBox") as TextBox;
        _listBox = GetTemplateChild("PART_ListBox") as ListBox;
        _popup = GetTemplateChild("PART_Popup") as Popup;

        if (_searchBox != null)
        {
            _searchBox.TextChanged += OnSearchBoxTextChanged;
            _searchBox.GotFocus += (s, e) => OpenPopup();
            _searchBox.LostFocus += OnSearchBoxLostFocus;
        }

        if (_listBox != null)
        {
            _listBox.SelectionChanged += OnListBoxSelectionChanged;
        }

        _clearButton = GetTemplateChild("PART_ClearButton") as Button;
        if (_clearButton != null)
        {
            _clearButton.Click += OnClearButtonClick;
        }

        UpdateDisplayText();
        SetupCollectionView();
    }

    private void OnClearButtonClick(object sender, RoutedEventArgs e)
    {
        SelectedItem = null;
        if (_searchBox != null)
        {
            _searchBox.Text = "";
        }
        ApplyFilter();
    }

    private void OnSearchBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_searchBox == null) return;
        SearchText = _searchBox.Text;
    }

    private void OnSearchBoxLostFocus(object sender, RoutedEventArgs e)
    {
        // 약간의 지연 후 팝업 닫기 (ListBox 클릭 허용)
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_listBox != null && !_listBox.IsMouseOver)
            {
                ClosePopup();
                UpdateDisplayText();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_listBox?.SelectedItem != null)
        {
            SelectedItem = _listBox.SelectedItem;
            ClosePopup();
            UpdateDisplayText();
        }
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableComboBox combo)
        {
            combo.SetupCollectionView();
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableComboBox combo)
        {
            combo.UpdateDisplayText();
        }
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SearchableComboBox combo)
        {
            combo.ApplyFilter();
        }
    }

    private void SetupCollectionView()
    {
        if (ItemsSource == null) return;

        // 각 컨트롤마다 독립적인 CollectionView 생성 (필터 공유 방지)
        var cvs = new CollectionViewSource { Source = ItemsSource };
        _collectionView = cvs.View;

        if (_listBox != null)
        {
            _listBox.ItemsSource = _collectionView;
            _listBox.DisplayMemberPath = DisplayMemberPath;
        }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_collectionView == null) return;

        var searchText = SearchText?.Trim().ToLower() ?? "";

        if (string.IsNullOrEmpty(searchText))
        {
            _collectionView.Filter = null;
        }
        else
        {
            _collectionView.Filter = item =>
            {
                var prop = item?.GetType().GetProperty(DisplayMemberPath);
                var value = prop?.GetValue(item)?.ToString()?.ToLower() ?? "";
                return value.Contains(searchText);
            };
        }
    }

    private void UpdateDisplayText()
    {
        if (_searchBox == null) return;

        if (SelectedItem != null)
        {
            var prop = SelectedItem.GetType().GetProperty(DisplayMemberPath);
            _searchBox.Text = prop?.GetValue(SelectedItem)?.ToString() ?? "";
        }
        else
        {
            _searchBox.Text = "";
        }
    }

    private void OpenPopup()
    {
        if (_popup != null)
        {
            _popup.IsOpen = true;
        }
    }

    private void ClosePopup()
    {
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }
    }
}
