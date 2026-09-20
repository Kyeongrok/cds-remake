using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CdsHelper.Main.Local.ViewModels;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Prism.Events;
using Prism.Ioc;

namespace CdsHelper.Main.UI.Views;

public class PlayerContent : ContentControl
{
    private DataGrid? _hintsDataGrid;
    private CheckBox? _filterCheckBox;
    private HintService? _hintService;

    static PlayerContent()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PlayerContent),
            new FrameworkPropertyMetadata(typeof(PlayerContent)));
    }

    public PlayerContent()
    {
        Loaded += OnLoaded;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _hintsDataGrid = GetTemplateChild("PART_HintsDataGrid") as DataGrid;
        if (_hintsDataGrid != null)
        {
            _hintsDataGrid.SelectionChanged += HintsDataGrid_SelectionChanged;
        }

        _filterCheckBox = GetTemplateChild("PART_FilterUndiscovered") as CheckBox;
        if (_filterCheckBox != null)
        {
            _filterCheckBox.Checked += FilterCheckBox_Changed;
            _filterCheckBox.Unchecked += FilterCheckBox_Changed;
        }

        // ItemsSource 변경 시 필터 재적용
        if (_hintsDataGrid != null)
        {
            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                System.Windows.Controls.ItemsControl.ItemsSourceProperty, typeof(DataGrid));
            dpd?.AddValueChanged(_hintsDataGrid, (s, e) => ReapplyFilter());
        }
    }

    private void FilterCheckBox_Changed(object sender, RoutedEventArgs e) => ReapplyFilter();

    private void ReapplyFilter()
    {
        if (_hintsDataGrid?.ItemsSource == null || _filterCheckBox == null) return;

        var view = CollectionViewSource.GetDefaultView(_hintsDataGrid.ItemsSource);
        if (_filterCheckBox.IsChecked == true)
        {
            view.Filter = item => item is HintData hint && !hint.IsDiscovered;
        }
        else
        {
            view.Filter = null;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PlayerContentViewModel)
            return;

        var saveDataService = ContainerLocator.Container.Resolve<SaveDataService>();
        _hintService = ContainerLocator.Container.Resolve<HintService>();
        var eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        DataContext = new PlayerContentViewModel(saveDataService, _hintService, eventAggregator);
    }

    private async void HintsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_hintsDataGrid?.SelectedItem is not HintData hint) return;
        _hintService ??= ContainerLocator.Container.Resolve<HintService>();

        var books = await _hintService.GetBooksByHintIndexAsync(hint.Index - 1);
        if (books.Count == 0)
        {
            MessageBox.Show($"'{hint.Name}'을(를) 수록한 도서가 없습니다.",
                "도서 정보", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var vm = DataContext as PlayerContentViewModel;

        var window = new Window
        {
            Title = $"{hint.Name} - 수록 도서 ({books.Count}권)",
            SizeToContent = SizeToContent.Height,
            Width = 420,
            MaxHeight = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(15) };

        foreach (var (bookName, language, required, condition, cities) in books)
        {
            bool meetsReq = CheckPartyMeetsRequirements(vm, language, required);

            // 제목: "힌트이름 - 책제목"
            var titlePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 2)
            };
            titlePanel.Children.Add(new TextBlock
            {
                Text = $"■ {hint.Name} - {bookName}",
                FontWeight = FontWeights.Bold,
                FontSize = 14
            });
            if (meetsReq)
            {
                titlePanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "가능",
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    }
                });
            }
            panel.Children.Add(titlePanel);

            if (!string.IsNullOrEmpty(language))
                panel.Children.Add(CreateConditionLine("언어", language, vm, isLanguage: true));
            if (!string.IsNullOrEmpty(required))
                panel.Children.Add(CreateConditionLine("필요", required, vm, isLanguage: false));
            if (!string.IsNullOrEmpty(condition))
                panel.Children.Add(CreateInfoLine("선행", condition));
            if (cities.Count > 0)
                panel.Children.Add(CreateInfoLine("도시", string.Join(", ", cities)));
        }

        window.Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        window.ShowDialog();
    }

    private static TextBlock CreateInfoLine(string label, string value)
    {
        return new TextBlock
        {
            Text = $"  {label}: {value}",
            FontSize = 13,
            Margin = new Thickness(0, 1, 0, 0)
        };
    }

    /// <summary>
    /// 조건 충족 시 파란색으로 표시하는 라인
    /// </summary>
    private static UIElement CreateConditionLine(string label, string value, PlayerContentViewModel? vm, bool isLanguage)
    {
        if (vm == null)
            return CreateInfoLine(label, value);

        if (isLanguage)
        {
            bool met = FindPartyLevel(vm, value.Trim()) > 0;
            return new TextBlock
            {
                Text = $"  {label}: {value}",
                Foreground = met ? Brushes.Blue : Brushes.Black,
                FontSize = 13,
                Margin = new Thickness(0, 1, 0, 0)
            };
        }

        // Required: 쉼표 구분 가능 ("회계2, 역사학1")
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length <= 1)
        {
            bool met = CheckSingleRequirement(vm, value.Trim());
            return new TextBlock
            {
                Text = $"  {label}: {value}",
                Foreground = met ? Brushes.Blue : Brushes.Black,
                FontSize = 13,
                Margin = new Thickness(0, 1, 0, 0)
            };
        }

        // 복합 조건: 각 조건별로 색상 적용
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
        panel.Children.Add(new TextBlock { Text = $"  {label}: ", FontSize = 13 });
        for (int i = 0; i < parts.Length; i++)
        {
            bool met = CheckSingleRequirement(vm, parts[i]);
            if (i > 0) panel.Children.Add(new TextBlock { Text = ", ", FontSize = 13 });
            panel.Children.Add(new TextBlock
            {
                Text = parts[i],
                Foreground = met ? Brushes.Blue : Brushes.Black,
                FontSize = 13
            });
        }
        return panel;
    }

    private static bool CheckSingleRequirement(PlayerContentViewModel vm, string part)
    {
        int idx = part.Length;
        while (idx > 0 && char.IsDigit(part[idx - 1]))
            idx--;
        string prefix = part[..idx].Trim();
        int requiredLevel = idx < part.Length ? int.Parse(part[idx..]) : 1;
        return FindPartyLevel(vm, prefix) >= requiredLevel;
    }

    private static bool CheckPartyMeetsRequirements(PlayerContentViewModel? vm, string language, string required)
    {
        if (vm == null) return false;

        bool langOk = string.IsNullOrEmpty(language?.Trim()) ||
                      FindPartyLevel(vm, language!.Trim()) > 0;

        bool skillOk = string.IsNullOrEmpty(required?.Trim()) ||
                       CheckAllRequirements(vm, required!);

        return langOk && skillOk;
    }

    /// <summary>
    /// 스킬/언어 이름으로 파티 최고 레벨 조회 (CombinedSkills + CombinedLanguages 모두 검색)
    /// </summary>
    private static byte FindPartyLevel(PlayerContentViewModel vm, string name)
    {
        var skill = vm.CombinedSkills.FirstOrDefault(i => i.Name == name || i.Name.StartsWith(name));
        var lang = vm.CombinedLanguages.FirstOrDefault(i => i.Name == name || i.Name.StartsWith(name));
        return Math.Max(skill?.BestLevel ?? 0, lang?.BestLevel ?? 0);
    }

    /// <summary>
    /// Required 필드 체크 ("회계2, 역사학1" 같은 복합 조건 지원)
    /// </summary>
    private static bool CheckAllRequirements(PlayerContentViewModel vm, string required)
    {
        var parts = required.Split(',', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part)) continue;

            int idx = part.Length;
            while (idx > 0 && char.IsDigit(part[idx - 1]))
                idx--;

            string prefix = part[..idx].Trim();
            int requiredLevel = idx < part.Length ? int.Parse(part[idx..]) : 1;

            if (FindPartyLevel(vm, prefix) < requiredLevel)
                return false;
        }
        return true;
    }
}
