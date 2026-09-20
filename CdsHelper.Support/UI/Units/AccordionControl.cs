using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CdsHelper.Support.UI.Units;

/// <summary>
/// AccordionControl - 아코디언 메뉴 컨트롤
/// </summary>
public class AccordionControl : TreeView
{
    #region Dependency Properties

    public static readonly DependencyProperty HeaderBackgroundProperty =
        DependencyProperty.Register(nameof(HeaderBackground), typeof(Brush), typeof(AccordionControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(16, 79, 137)))); // #104F89

    public static readonly DependencyProperty ItemClickCommandProperty =
        DependencyProperty.Register(nameof(ItemClickCommand), typeof(ICommand), typeof(AccordionControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderForegroundProperty =
        DependencyProperty.Register(nameof(HeaderForeground), typeof(Brush), typeof(AccordionControl),
            new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty ItemBackgroundProperty =
        DependencyProperty.Register(nameof(ItemBackground), typeof(Brush), typeof(AccordionControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(240, 240, 240)))); // #F0F0F0

    public static readonly DependencyProperty ItemForegroundProperty =
        DependencyProperty.Register(nameof(ItemForeground), typeof(Brush), typeof(AccordionControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(50, 50, 50)))); // #323232

    public static readonly DependencyProperty ItemHoverBackgroundProperty =
        DependencyProperty.Register(nameof(ItemHoverBackground), typeof(Brush), typeof(AccordionControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(232, 244, 252)))); // #E8F4FC

    public static readonly DependencyProperty ItemHoverForegroundProperty =
        DependencyProperty.Register(nameof(ItemHoverForeground), typeof(Brush), typeof(AccordionControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(16, 79, 137)))); // #104F89

    public static readonly DependencyProperty IsMinimizedProperty =
        DependencyProperty.Register(nameof(IsMinimized), typeof(bool), typeof(AccordionControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SelectedContentProperty =
        DependencyProperty.Register(nameof(SelectedContent), typeof(object), typeof(AccordionControl),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public Brush HeaderBackground
    {
        get => (Brush)GetValue(HeaderBackgroundProperty);
        set => SetValue(HeaderBackgroundProperty, value);
    }

    public Brush HeaderForeground
    {
        get => (Brush)GetValue(HeaderForegroundProperty);
        set => SetValue(HeaderForegroundProperty, value);
    }

    public Brush ItemBackground
    {
        get => (Brush)GetValue(ItemBackgroundProperty);
        set => SetValue(ItemBackgroundProperty, value);
    }

    public Brush ItemForeground
    {
        get => (Brush)GetValue(ItemForegroundProperty);
        set => SetValue(ItemForegroundProperty, value);
    }

    public Brush ItemHoverBackground
    {
        get => (Brush)GetValue(ItemHoverBackgroundProperty);
        set => SetValue(ItemHoverBackgroundProperty, value);
    }

    public Brush ItemHoverForeground
    {
        get => (Brush)GetValue(ItemHoverForegroundProperty);
        set => SetValue(ItemHoverForegroundProperty, value);
    }

    public bool IsMinimized
    {
        get => (bool)GetValue(IsMinimizedProperty);
        set => SetValue(IsMinimizedProperty, value);
    }

    public ICommand? ItemClickCommand
    {
        get => (ICommand?)GetValue(ItemClickCommandProperty);
        set => SetValue(ItemClickCommandProperty, value);
    }

    public object? SelectedContent
    {
        get => GetValue(SelectedContentProperty);
        set => SetValue(SelectedContentProperty, value);
    }

    #endregion

    static AccordionControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AccordionControl),
            new FrameworkPropertyMetadata(typeof(AccordionControl)));
    }

    public AccordionControl()
    {
        Background = new SolidColorBrush(Color.FromRgb(16, 79, 137)); // #104F89
        BorderThickness = new Thickness(0);

        SelectedItemChanged += OnSelectedItemChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 초기 선택된 아이템의 컨텐츠 표시
        var selectedItem = FindSelectedItem(this);
        if (selectedItem != null && !selectedItem.IsGroup)
        {
            SelectedContent = selectedItem.ItemContent;
        }
    }

    private AccordionItem? FindSelectedItem(ItemsControl parent)
    {
        foreach (var item in parent.Items)
        {
            if (item is AccordionItem accordionItem)
            {
                if (accordionItem.IsSelected && !accordionItem.IsGroup)
                    return accordionItem;

                // 하위 항목 검색
                var found = FindSelectedItem(accordionItem);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is AccordionItem item)
        {
            if (!item.IsGroup)
            {
                SelectedContent = item.ItemContent;
                ItemClickCommand?.Execute(item.Tag?.ToString());
            }
        }
    }
}

/// <summary>
/// AccordionItem - 아코디언 항목
/// </summary>
public class AccordionItem : TreeViewItem
{
    #region Dependency Properties

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(object), typeof(AccordionItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsGroupProperty =
        DependencyProperty.Register(nameof(IsGroup), typeof(bool), typeof(AccordionItem),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ItemContentProperty =
        DependencyProperty.Register(nameof(ItemContent), typeof(object), typeof(AccordionItem),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsGroup
    {
        get => (bool)GetValue(IsGroupProperty);
        set => SetValue(IsGroupProperty, value);
    }

    public object? ItemContent
    {
        get => GetValue(ItemContentProperty);
        set => SetValue(ItemContentProperty, value);
    }

    #endregion

    static AccordionItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AccordionItem),
            new FrameworkPropertyMetadata(typeof(AccordionItem)));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        // 이미 처리된 이벤트는 무시
        if (e.Handled) return;

        if (IsGroup)
        {
            IsExpanded = !IsExpanded;
            e.Handled = true;
        }
        else
        {
            IsSelected = true;
            e.Handled = true;
        }
    }
}
