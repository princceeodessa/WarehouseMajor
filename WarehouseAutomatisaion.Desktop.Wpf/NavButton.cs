using System.Windows;

namespace WarehouseAutomatisaion.Desktop.Wpf;

/// <summary>
/// Attached properties for sidebar navigation buttons. Lets XAML templates react to the
/// "active section" state by toggling the Fluent accent pill indicator on the active item.
/// </summary>
public static class NavButton
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive",
        typeof(bool),
        typeof(NavButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static bool GetIsActive(DependencyObject element)
    {
        return (bool)element.GetValue(IsActiveProperty);
    }

    public static void SetIsActive(DependencyObject element, bool value)
    {
        element.SetValue(IsActiveProperty, value);
    }
}
