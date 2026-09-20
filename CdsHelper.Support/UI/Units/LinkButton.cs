using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Support.UI.Units;

/// <summary>
/// 링크 스타일 버튼 (테두리가 있는 파란색 버튼)
/// </summary>
public class LinkButton : Button
{
    static LinkButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(LinkButton),
            new FrameworkPropertyMetadata(typeof(LinkButton)));
    }
}
