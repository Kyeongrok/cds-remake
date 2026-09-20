using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Main.Local.ViewModels;

public partial class FigureheadContentViewModel : ObservableObject
{
    private readonly FigureheadService _figureheadService;

    private List<Figurehead> _allFigureheads = new();

    #region Collections

    [ObservableProperty] private ObservableCollection<Figurehead> _figureheads = new();

    #endregion

    #region Filter Properties

    [ObservableProperty] private string _figureheadNameSearch = "";
    partial void OnFigureheadNameSearchChanged(string value) => ApplyFilter();

    [ObservableProperty] private string? _selectedFigureheadFunction;
    partial void OnSelectedFigureheadFunctionChanged(string? value) => ApplyFilter();

    [ObservableProperty] private string? _selectedFigureheadLevel;
    partial void OnSelectedFigureheadLevelChanged(string? value) => ApplyFilter();

    public ObservableCollection<string> FigureheadFunctions { get; } = new();
    public ObservableCollection<string> FigureheadLevels { get; } = new();

    #endregion

    #region Status Properties

    [ObservableProperty] private string _statusText = "준비됨";

    #endregion

    public FigureheadContentViewModel(FigureheadService figureheadService)
    {
        _figureheadService = figureheadService;
        Initialize();
    }

    private void Initialize()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        var figureheadsPath = System.IO.Path.Combine(basePath, "figurehead.json");

        if (System.IO.File.Exists(figureheadsPath))
            LoadFigureheads(figureheadsPath);
    }

    private void LoadFigureheads(string filePath)
    {
        try
        {
            _allFigureheads = _figureheadService.LoadFigureheads(filePath);

            FigureheadFunctions.Clear();
            foreach (var func in _figureheadService.GetDistinctFunctions())
                FigureheadFunctions.Add(func);

            FigureheadLevels.Clear();
            foreach (var level in _figureheadService.GetDistinctLevels())
                FigureheadLevels.Add(level);

            ApplyFilter();
            StatusText = $"선수상 로드 완료: {_allFigureheads.Count}개";
        }
        catch (Exception ex)
        {
            StatusText = "선수상 로드 실패";
            System.Windows.MessageBox.Show($"선수상 파일 읽기 실패:\n\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        if (_allFigureheads.Count == 0) return;

        int? level = null;
        if (!string.IsNullOrWhiteSpace(SelectedFigureheadLevel) && SelectedFigureheadLevel != "전체")
        {
            if (int.TryParse(SelectedFigureheadLevel, out var parsed))
                level = parsed;
        }

        var filtered = _figureheadService.Filter(
            string.IsNullOrWhiteSpace(FigureheadNameSearch) ? null : FigureheadNameSearch,
            SelectedFigureheadFunction,
            level);

        Figureheads = new ObservableCollection<Figurehead>(filtered);
        StatusText = $"선수상: {filtered.Count}개";
    }

    [RelayCommand]
    private void ResetFilter()
    {
        FigureheadNameSearch = "";
        SelectedFigureheadFunction = null;
        SelectedFigureheadLevel = null;
    }
}
