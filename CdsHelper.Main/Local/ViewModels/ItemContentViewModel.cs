using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Main.Local.ViewModels;

public partial class ItemContentViewModel : ObservableObject
{
    private readonly ItemService _itemService;

    private List<Item> _allItems = new();

    #region Collections

    [ObservableProperty] private ObservableCollection<Item> _items = new();

    #endregion

    #region Filter Properties

    [ObservableProperty] private string _itemNameSearch = "";
    partial void OnItemNameSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string? _selectedItemCategory;
    partial void OnSelectedItemCategoryChanged(string? value) => ApplyFilter();

    [ObservableProperty] private string _itemDiscoverySearch = "";
    partial void OnItemDiscoverySearchChanged(string value) => ApplyFilter();

    public ObservableCollection<string> ItemCategories { get; } = new();

    #endregion

    #region Status Properties

    [ObservableProperty] private string _statusText = "준비됨";

    #endregion

    public ItemContentViewModel(ItemService itemService)
    {
        _itemService = itemService;
        Initialize();
    }

    private void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var itemsPath = System.IO.Path.Combine(basePath, "item.json");

        if (System.IO.File.Exists(itemsPath))
            LoadItems(itemsPath);
    }

    private void LoadItems(string filePath)
    {
        try
        {
            _allItems = _itemService.LoadItems(filePath);

            ItemCategories.Clear();
            foreach (var category in _itemService.GetDistinctCategories())
                ItemCategories.Add(category);

            ApplyFilter();
            StatusText = $"아이템 로드 완료: {_allItems.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "아이템 로드 실패";
            System.Windows.MessageBox.Show($"아이템 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allItems.Count == 0) return;

        var filtered = _itemService.Filter(
            string.IsNullOrWhiteSpace(ItemNameSearch) ? null : ItemNameSearch,
            SelectedItemCategory,
            string.IsNullOrWhiteSpace(ItemDiscoverySearch) ? null : ItemDiscoverySearch);

        Items = new ObservableCollection<Item>(filtered);
        StatusText = $"아이템: {filtered.Count}개";
    }

    [RelayCommand]
    private void ResetFilter()
    {
        ItemNameSearch = "";
        SelectedItemCategory = null;
        ItemDiscoverySearch = "";
    }
}
