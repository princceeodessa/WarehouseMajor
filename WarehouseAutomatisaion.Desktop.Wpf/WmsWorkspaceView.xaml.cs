using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WmsWorkspaceView : UserControl
{
    private static readonly Brush ActiveBackground = BrushFromHex("#EEF2FF");
    private static readonly Brush ActiveBorder = BrushFromHex("#C8D1FF");
    private static readonly Brush ActiveForeground = BrushFromHex("#2F45D3");
    private static readonly Brush DefaultBackground = Brushes.White;
    private static readonly Brush DefaultBorder = BrushFromHex("#E7ECF7");
    private static readonly Brush DefaultForeground = BrushFromHex("#17213A");

    private readonly SalesWorkspace _salesWorkspace;
    private readonly string _actorUserName;
    private readonly Dictionary<string, FrameworkElement> _viewCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;

    public WmsWorkspaceView(SalesWorkspace salesWorkspace, string actorUserName)
    {
        InitializeComponent();
        _salesWorkspace = salesWorkspace;
        _actorUserName = actorUserName;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        SelectMode("scan");
    }

    private void OnModeClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
        {
            SelectMode(key);
        }
    }

    private void SelectMode(string key)
    {
        ModeContentHost.Content = GetOrCreateView(key);
        ModeSubtitleText.Text = GetModeSubtitle(key);
        ApplyModeSelection(key);
    }

    private FrameworkElement GetOrCreateView(string key)
    {
        if (_viewCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var created = key.ToLowerInvariant() switch
        {
            "scan" => CreateQuickScanView(),
            "stock" => new StockBalancesWorkspaceView(),
            "cells" => new StorageCellsWorkspaceView(),
            "catalog" => new ProductsWorkspaceView(_salesWorkspace),
            "receipt-drafts" => new ReceiptDraftsWorkspaceView(),
            "operation-log" => new WarehouseOperationLogWorkspaceView(),
            "receive" => CreateOperationLauncher(
                "Приёмка товара",
                "Быстрая запись товара в ячейку. Откроется полноэкранная операция поверх рабочего места.",
                "Открыть приёмку",
                OpenReceiveStockDialog),
            "transfer" => CreateOperationLauncher(
                "Перемещение между ячейками",
                "Перенос товара из одной ячейки в другую с контролем доступного количества.",
                "Открыть перемещение",
                OpenTransferStockDialog),
            "stocktake" => CreateOperationLauncher(
                "Инвентаризация ячейки",
                "Сверка факта с системой по выбранной ячейке, включая AI-распознавание фото полки.",
                "Открыть инвентаризацию",
                OpenStockTakeDialog),
            _ => CreateInfoView("Раздел не найден", "Такой режим склада пока не зарегистрирован.")
        };

        _viewCache[key] = created;
        return created;
    }

    private FrameworkElement CreateQuickScanView()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        var scanServices = DesktopScanLookupFactory.TryCreate();
        if (backplane is null || scanServices is null)
        {
            return CreateInfoView(
                "Скан недоступен",
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.");
        }

        var stockLocations = new MySqlStockLocationRepository(backplane);
        return new QuickScanWorkspaceView(
            scanServices.ProductLookup,
            scanServices.CellLookup,
            stockLocations,
            scanServices.OperationLogger,
            _actorUserName);
    }

    private FrameworkElement CreateOperationLauncher(
        string title,
        string description,
        string actionText,
        Action action)
    {
        var button = new Button
        {
            Content = actionText,
            Height = 42,
            Padding = new Thickness(22, 0, 22, 0),
            Background = BrushFromHex("#4F5BFF"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => action();

        return new Grid
        {
            Background = BrushFromHex("#F7F9FD"),
            Children =
            {
                new Border
                {
                    Background = Brushes.White,
                    BorderBrush = BrushFromHex("#E7ECF7"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(22),
                    Width = 560,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                FontSize = 22,
                                FontWeight = FontWeights.SemiBold,
                                Foreground = BrushFromHex("#17213A")
                            },
                            new TextBlock
                            {
                                Text = description,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 8, 0, 18),
                                Foreground = BrushFromHex("#7A86A5")
                            },
                            button
                        }
                    }
                }
            }
        };
    }

    private static FrameworkElement CreateInfoView(string title, string description)
    {
        return new Grid
        {
            Background = BrushFromHex("#F7F9FD"),
            Children =
            {
                new StackPanel
                {
                    Width = 520,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = title,
                            FontSize = 20,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = BrushFromHex("#17213A"),
                            TextAlignment = TextAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = description,
                            Foreground = BrushFromHex("#7A86A5"),
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 8, 0, 0)
                        }
                    }
                }
            }
        };
    }

    private void OpenReceiveStockDialog()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            ShowConnectionWarning("Приёмка товара");
            return;
        }

        var catalogReader = new MySqlNomenclatureCatalogReader(backplane);
        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        var window = new ReceiveStockWindow(catalogReader, cellCatalog, stockLocations);
        ShowFullScreenOperation(window);
    }

    private void OpenTransferStockDialog()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            ShowConnectionWarning("Перемещение между ячейками");
            return;
        }

        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        var window = new TransferStockWindow(cellCatalog, stockLocations);
        ShowFullScreenOperation(window);
    }

    private void OpenStockTakeDialog()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            ShowConnectionWarning("Инвентаризация");
            return;
        }

        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        var shelfVision = ShelfVisionFactory.TryCreate();
        var window = new StockTakeWindow(cellCatalog, stockLocations, shelfVision);
        ShowFullScreenOperation(window);
    }

    private void ShowFullScreenOperation(Window window)
    {
        var owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow;
        window.Owner = owner;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        window.WindowState = WindowState.Maximized;
        window.ShowDialog();
    }

    private void ShowConnectionWarning(string title)
    {
        MessageBox.Show(
            Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
            "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ApplyModeSelection(string key)
    {
        foreach (var button in EnumerateModeButtons())
        {
            var active = button.Tag is string tag && tag.Equals(key, StringComparison.OrdinalIgnoreCase);
            button.Background = active ? ActiveBackground : DefaultBackground;
            button.BorderBrush = active ? ActiveBorder : DefaultBorder;
            button.Foreground = active ? ActiveForeground : DefaultForeground;
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private IEnumerable<Button> EnumerateModeButtons()
    {
        yield return ModeScanButton;
        yield return ModeStockButton;
        yield return ModeCellsButton;
        yield return ModeCatalogButton;
        yield return ModeReceiveButton;
        yield return ModeTransferButton;
        yield return ModeStockTakeButton;
        yield return ModeDraftsButton;
        yield return ModeLogButton;
    }

    private static string GetModeSubtitle(string key) => key.ToLowerInvariant() switch
    {
        "scan" => "Сканирование товара или ячейки без выхода из рабочего места.",
        "stock" => "Остатки по складам и стартовое размещение в UNPLACED.",
        "cells" => "Адресное хранение: зоны, ряды, стеллажи, ячейки и QR.",
        "catalog" => "Номенклатура, артикулы и карточки товаров для WMS.",
        "receive" => "Приёмка товара в ячейку.",
        "transfer" => "Перемещение между ячейками.",
        "stocktake" => "Инвентаризация ячейки и AI-проверка по фото.",
        "receipt-drafts" => "AI-черновики приходных накладных, готовые к разноске по ячейкам.",
        "operation-log" => "История WMS-действий и технических событий.",
        _ => "Единое рабочее место WMS."
    };

    private static Brush BrushFromHex(string value)
    {
        return (Brush)new BrushConverter().ConvertFromString(value)!;
    }
}
