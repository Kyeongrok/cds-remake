using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Support.UI.Units;

/// <summary>
/// 초기화 버튼 (X 아이콘)
/// </summary>
public class ResetButton : Button
{
    static ResetButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ResetButton),
            new FrameworkPropertyMetadata(typeof(ResetButton)));
    }
}
