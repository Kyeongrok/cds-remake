using System.Windows;
using System.Windows.Controls;
using CdsHelper.Support.UI.Units;

namespace CdsHelper.Navigation.UI.Views;

public class NavigationMenu : AccordionControl
{
    private AccordionControl? _innerAccordion;
    private string? _pendingSelectTag;

    static NavigationMenu()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(NavigationMenu),
            new FrameworkPropertyMetadata(typeof(NavigationMenu)));
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _innerAccordion = GetTemplateChild("PART_InnerAccordion") as AccordionControl;

        // 템플릿 적용 후 대기 중인 선택 처리
        if (_innerAccordion != null && !string.IsNullOrEmpty(_pendingSelectTag))
        {
            SelectItemByTag(_pendingSelectTag);
            _pendingSelectTag = null;
        }
    }

    /// <summary>
    /// Tag로 아이템 선택
    /// </summary>
    public void SelectItemByTag(string tag)
    {
        if (_innerAccordion == null)
        {
            // 템플릿이 아직 적용되지 않은 경우 대기
            _pendingSelectTag = tag;
            return;
        }

        SelectItemByTagRecursive(_innerAccordion, tag);
    }

    private bool SelectItemByTagRecursive(ItemsControl parent, string tag)
    {
        foreach (var item in parent.Items)
        {
            if (item is AccordionItem accordionItem)
            {
                if (accordionItem.Tag?.ToString() == tag)
                {
                    accordionItem.IsSelected = true;
                    return true;
                }

                // 하위 항목 검색
                if (SelectItemByTagRecursive(accordionItem, tag))
                    return true;
            }
        }
        return false;
    }
}
