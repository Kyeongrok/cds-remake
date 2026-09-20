using System.IO;
using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.Local.Settings;
using CdsHelper.Game.Local.Settings;

namespace CdsHelper.Form.UI.Views;

[TemplatePart(Name = PART_MarkerSizeSlider, Type = typeof(Slider))]
[TemplatePart(Name = PART_DefaultViewComboBox, Type = typeof(ComboBox))]
[TemplatePart(Name = PART_AutoOpenShipMapCheckBox, Type = typeof(CheckBox))]
[TemplatePart(Name = PART_OkButton, Type = typeof(Button))]
[TemplatePart(Name = PART_CancelButton, Type = typeof(Button))]
[TemplatePart(Name = PART_OpenDbFolderButton, Type = typeof(Button))]
public class SettingsDialog : Window
{
    private const string PART_MarkerSizeSlider = "PART_MarkerSizeSlider";
    private const string PART_DefaultViewComboBox = "PART_DefaultViewComboBox";
    private const string PART_AutoOpenShipMapCheckBox = "PART_AutoOpenShipMapCheckBox";
    private const string PART_OkButton = "PART_OkButton";
    private const string PART_CancelButton = "PART_CancelButton";
    private const string PART_OpenDbFolderButton = "PART_OpenDbFolderButton";

    private Slider? _markerSizeSlider;
    private ComboBox? _defaultViewComboBox;
    private CheckBox? _autoOpenShipMapCheckBox;

    public static readonly DependencyProperty MarkerSizeProperty =
        DependencyProperty.Register(nameof(MarkerSize), typeof(double), typeof(SettingsDialog),
            new PropertyMetadata(AppSettings.DefaultMarkerSize));

    public double MarkerSize
    {
        get => (double)GetValue(MarkerSizeProperty);
        set => SetValue(MarkerSizeProperty, value);
    }

    public List<ViewOption> AvailableViews => AppSettings.AvailableViews;

    static SettingsDialog()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SettingsDialog),
            new FrameworkPropertyMetadata(typeof(SettingsDialog)));
    }

    public SettingsDialog()
    {
        Title = "설정";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        // 현재 설정 로드
        MarkerSize = AppSettings.MarkerSize;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _markerSizeSlider = GetTemplateChild(PART_MarkerSizeSlider) as Slider;
        _defaultViewComboBox = GetTemplateChild(PART_DefaultViewComboBox) as ComboBox;
        _autoOpenShipMapCheckBox = GetTemplateChild(PART_AutoOpenShipMapCheckBox) as CheckBox;

        if (GetTemplateChild(PART_OkButton) is Button okButton)
            okButton.Click += OnOkClick;

        if (GetTemplateChild(PART_CancelButton) is Button cancelButton)
            cancelButton.Click += OnCancelClick;

        if (GetTemplateChild(PART_OpenDbFolderButton) is Button openDbFolderButton)
            openDbFolderButton.Click += OnOpenDbFolderClick;

        if (_markerSizeSlider != null)
            _markerSizeSlider.Value = MarkerSize;

        if (_defaultViewComboBox != null)
        {
            _defaultViewComboBox.ItemsSource = AvailableViews;
            _defaultViewComboBox.DisplayMemberPath = "DisplayName";
            _defaultViewComboBox.SelectedValuePath = "Name";
            _defaultViewComboBox.SelectedValue = AppSettings.DefaultView;
        }

        if (_autoOpenShipMapCheckBox != null)
            _autoOpenShipMapCheckBox.IsChecked = GameSettings.AutoOpenShipMap;
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (_markerSizeSlider != null)
        {
            MarkerSize = _markerSizeSlider.Value;
            AppSettings.MarkerSize = MarkerSize;
        }

        if (_defaultViewComboBox?.SelectedValue is string selectedView)
        {
            AppSettings.DefaultView = selectedView;
        }

        if (_autoOpenShipMapCheckBox != null)
        {
            GameSettings.AutoOpenShipMap = _autoOpenShipMapCheckBox.IsChecked == true;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnOpenDbFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var dbPath = Path.Combine(basePath, "cdshelper.db");

            if (File.Exists(dbPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dbPath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", basePath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"폴더 열기 중 오류가 발생했습니다.\n{ex.Message}",
                "오류",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
