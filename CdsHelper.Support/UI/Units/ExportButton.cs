using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Support.UI.Units;

public class ExportButton : Button
{
    static ExportButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ExportButton),
            new FrameworkPropertyMetadata(typeof(ExportButton)));
    }

    public ExportButton()
    {
        Content = "데이터 내보내기";
    }
}
