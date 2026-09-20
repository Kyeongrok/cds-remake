using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace CdsHelper.Support.UI.Units;

[TemplatePart(Name = PART_MinimizeButton, Type = typeof(Button))]
[TemplatePart(Name = PART_MaximizeButton, Type = typeof(Button))]
[TemplatePart(Name = PART_RestoreButton, Type = typeof(Button))]
[TemplatePart(Name = PART_CloseButton, Type = typeof(Button))]
[TemplatePart(Name = PART_TitleBar, Type = typeof(Border))]
public class CdsWindow : Window
{
    private const string PART_MinimizeButton = "PART_MinimizeButton";
    private const string PART_MaximizeButton = "PART_MaximizeButton";
    private const string PART_RestoreButton = "PART_RestoreButton";
    private const string PART_CloseButton = "PART_CloseButton";
    private const string PART_TitleBar = "PART_TitleBar";

    static CdsWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CdsWindow),
            new FrameworkPropertyMetadata(typeof(CdsWindow)));
    }

    public CdsWindow()
    {
        // WindowChrome 설정
        var chrome = new WindowChrome
        {
            CaptionHeight = 32,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(5),
            UseAeroCaptionButtons = false
        };
        WindowChrome.SetWindowChrome(this, chrome);

        // 명령 바인딩
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, OnMinimize));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, OnMaximize));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, OnRestore));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, OnClose));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (GetTemplateChild(PART_MinimizeButton) is Button minimizeButton)
            minimizeButton.Click += (s, e) => SystemCommands.MinimizeWindow(this);

        if (GetTemplateChild(PART_MaximizeButton) is Button maximizeButton)
            maximizeButton.Click += (s, e) => SystemCommands.MaximizeWindow(this);

        if (GetTemplateChild(PART_RestoreButton) is Button restoreButton)
            restoreButton.Click += (s, e) => SystemCommands.RestoreWindow(this);

        if (GetTemplateChild(PART_CloseButton) is Button closeButton)
            closeButton.Click += (s, e) => SystemCommands.CloseWindow(this);

        if (GetTemplateChild(PART_TitleBar) is Border titleBar)
            titleBar.MouseLeftButtonDown += OnTitleBarMouseDown;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }
        else
        {
            DragMove();
        }
    }

    private void OnMinimize(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, ExecutedRoutedEventArgs e) => SystemCommands.MaximizeWindow(this);
    private void OnRestore(object sender, ExecutedRoutedEventArgs e) => SystemCommands.RestoreWindow(this);
    private void OnClose(object sender, ExecutedRoutedEventArgs e) => SystemCommands.CloseWindow(this);
}
