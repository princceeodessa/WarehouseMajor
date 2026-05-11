using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class StartupLoadingWindow : Window
{
    public StartupLoadingWindow(DesktopClientStartupResult startupStatus)
    {
        InitializeComponent();

        ConnectionText.Text = startupStatus.UsesSharedDatabase
            ? $"Общая база: {startupStatus.Host}:{startupStatus.Port} / {startupStatus.Database}"
            : "Локальный режим";
    }

    public void SetStatus(string statusText)
    {
        StatusText.Text = statusText;
    }
}
