using System;
using System.Windows;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal interface IHostedWmsOperationWindow
{
    Window? DialogOwnerOverride { get; set; }

    Action? HostCloseRequested { get; set; }
}
