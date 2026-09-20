using System.Data;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_TableComboBox, Type = typeof(ComboBox))]
[TemplatePart(Name = PART_DataGrid, Type = typeof(DataGrid))]
[TemplatePart(Name = PART_CloseButton, Type = typeof(Button))]
[TemplatePart(Name = PART_RecordCountText, Type = typeof(TextBlock))]
public class DbTableViewerDialog : Window
{
    private const string PART_TableComboBox = "PART_TableComboBox";
    private const string PART_DataGrid = "PART_DataGrid";
    private const string PART_CloseButton = "PART_CloseButton";
    private const string PART_RecordCountText = "PART_RecordCountText";

    private ComboBox? _tableComboBox;
    private DataGrid? _dataGrid;
    private TextBlock? _recordCountText;

    private readonly AppDbContext _dbContext;
    private List<string> _tables = new();

    static DbTableViewerDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(DbTableViewerDialog),
            new FrameworkPropertyMetadata(typeof(DbTableViewerDialog)));
    }

    public DbTableViewerDialog(AppDbContext dbContext)
    {
        _dbContext = dbContext;
        Title = "DB 테이블 보기";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        LoadTableNames();
    }

    private void LoadTableNames()
    {
        try
        {
            var connection = _dbContext.Database.GetDbConnection();
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                _tables.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"테이블 목록을 가져오는 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _tableComboBox = GetTemplateChild(PART_TableComboBox) as ComboBox;
        _dataGrid = GetTemplateChild(PART_DataGrid) as DataGrid;
        _recordCountText = GetTemplateChild(PART_RecordCountText) as TextBlock;

        if (GetTemplateChild(PART_CloseButton) is Button closeButton)
            closeButton.Click += (s, e) => Close();

        if (_tableComboBox != null)
        {
            _tableComboBox.ItemsSource = _tables;
            _tableComboBox.SelectionChanged += OnTableSelectionChanged;
            if (_tables.Count > 0)
                _tableComboBox.SelectedIndex = 0;
        }
    }

    private void OnTableSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tableComboBox?.SelectedItem is not string selectedTable || _dataGrid == null)
            return;

        LoadTableData(selectedTable);
    }

    private void LoadTableData(string tableName)
    {
        if (_dataGrid == null) return;

        try
        {
            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM [{tableName}]";

            var dataTable = new DataTable();
            using (var reader = command.ExecuteReader())
            {
                dataTable.Load(reader);
            }

            _dataGrid.ItemsSource = dataTable.DefaultView;

            if (_recordCountText != null)
            {
                _recordCountText.Text = $"총 {dataTable.Rows.Count}개 레코드";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"데이터 로드 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
