using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class WpfMouseWheelScrollFix
{
    private const double PixelStep = 112d;
    private const double ContentStep = 8d;

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(
            typeof(Window),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(HandlePreviewMouseWheel),
            handledEventsToo: true);
    }

    private static void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        if (FindAncestor<ComboBox>(source) is { IsDropDownOpen: true })
        {
            return;
        }

        var direction = e.Delta < 0 ? 1 : -1;
        var viewer = FindScrollableViewer(source, direction);
        if (viewer is null)
        {
            return;
        }

        var step = viewer.CanContentScroll ? ContentStep : PixelStep;
        var nextOffset = Math.Clamp(
            viewer.VerticalOffset + direction * step,
            0,
            viewer.ScrollableHeight);

        if (Math.Abs(nextOffset - viewer.VerticalOffset) < 0.1)
        {
            return;
        }

        viewer.ScrollToVerticalOffset(nextOffset);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject source, int direction)
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (current is not ScrollViewer viewer || viewer.ScrollableHeight <= 0)
            {
                continue;
            }

            if (direction < 0 && viewer.VerticalOffset > 0)
            {
                return viewer;
            }

            if (direction > 0 && viewer.VerticalOffset < viewer.ScrollableHeight)
            {
                return viewer;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is FrameworkElement element && element.Parent is not null)
        {
            return element.Parent;
        }

        if (current is FrameworkContentElement contentElement && contentElement.Parent is not null)
        {
            return contentElement.Parent;
        }

        return VisualTreeHelper.GetParent(current);
    }
}
