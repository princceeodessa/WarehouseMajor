using System.Windows;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class WpfDialogOwner
{
    public static Window? Resolve(Window? preferredOwner = null)
    {
        if (IsUsableOwner(preferredOwner))
        {
            return preferredOwner;
        }

        var activeWindow = System.Windows.Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive && IsUsableOwner(window));
        if (activeWindow is not null)
        {
            return activeWindow;
        }

        var mainWindow = System.Windows.Application.Current?.MainWindow;
        return IsUsableOwner(mainWindow) ? mainWindow : null;
    }

    public static void TrySetOwner(Window dialog, Window? preferredOwner)
    {
        var owner = Resolve(preferredOwner);
        if (owner is null || ReferenceEquals(owner, dialog))
        {
            return;
        }

        try
        {
            dialog.Owner = owner;
        }
        catch (InvalidOperationException exception)
        {
            App.WriteClientErrorLog(exception, "WpfDialogOwner.TrySetOwner");
        }
    }

    private static bool IsUsableOwner(Window? window)
    {
        return window is { IsVisible: true };
    }
}
