using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Main.Local.Behaviors;

/// <summary>
/// 최신 항목을 맨 위(index 0)에 삽입하는 로그 목록용 첨부 동작.
/// 스크롤이 조금이라도 내려가 있으면 새로 삽입된 항목이 뷰포트 위로 밀려 보이지 않으므로,
/// 항목이 추가될 때마다 맨 위로 스크롤해 최신 로그가 항상 보이게 한다.
/// </summary>
public static class LogList
{
    public static readonly DependencyProperty AutoScrollToTopProperty =
        DependencyProperty.RegisterAttached(
            "AutoScrollToTop",
            typeof(bool),
            typeof(LogList),
            new PropertyMetadata(false, OnAutoScrollToTopChanged));

    public static void SetAutoScrollToTop(DependencyObject element, bool value)
        => element.SetValue(AutoScrollToTopProperty, value);

    public static bool GetAutoScrollToTop(DependencyObject element)
        => (bool)element.GetValue(AutoScrollToTopProperty);

    /// <summary>구독 해제를 위해 ListBox별 핸들러를 보관한다.</summary>
    private static readonly DependencyProperty HandlerProperty =
        DependencyProperty.RegisterAttached(
            "Handler", typeof(NotifyCollectionChangedEventHandler), typeof(LogList),
            new PropertyMetadata(null));

    private static void OnAutoScrollToTopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;

        var items = (INotifyCollectionChanged)listBox.Items;

        // 기존 구독이 있으면 먼저 해제 (중복 구독 방지)
        if (listBox.GetValue(HandlerProperty) is NotifyCollectionChangedEventHandler old)
        {
            items.CollectionChanged -= old;
            listBox.SetValue(HandlerProperty, null);
        }

        if (e.NewValue is not true) return;

        NotifyCollectionChangedEventHandler handler = (_, args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Add) return;
            if (listBox.Items.Count == 0) return;

            // 항목 추가 직후에는 컨테이너가 아직 생성되지 않았을 수 있어 렌더 이후로 미룬다.
            listBox.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (listBox.Items.Count > 0)
                    listBox.ScrollIntoView(listBox.Items[0]);
            }), System.Windows.Threading.DispatcherPriority.Background);
        };

        items.CollectionChanged += handler;
        listBox.SetValue(HandlerProperty, handler);
    }
}
