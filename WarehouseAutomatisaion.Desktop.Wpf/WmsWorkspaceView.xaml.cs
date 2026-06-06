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
    private string _activeModeKey = "assistant";
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
        SelectMode(_activeModeKey);
    }

    public void ActivateMode(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        SelectMode(key);
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
        var normalizedKey = string.IsNullOrWhiteSpace(key) ? "assistant" : key.Trim();
        _activeModeKey = normalizedKey;
        ModeContentHost.Content = GetOrCreateView(normalizedKey);
        ModeTitleText.Text = GetModeTitle(normalizedKey);
        ModeSubtitleText.Text = GetModeSubtitle(normalizedKey);
        ApplyModeSelection(GetParentSectionKey(normalizedKey));
    }

    private FrameworkElement GetOrCreateView(string key)
    {
        if (_viewCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var created = key.ToLowerInvariant() switch
        {
            "assistant" => new WarehouseAssistantView(),
            "work" => CreateHubView(
                "Работа склада",
                "Операции, которые кладовщик делает каждый день.",
                [
                    new HubAction("scan", "Скан", "Быстрый поиск товара или ячейки по штрихкоду/QR.", "\uE8B6"),
                    new HubAction("receive", "Приёмка", "Положить поступивший товар в ячейку.", "\uE7B8"),
                    new HubAction("transfer", "Перемещение", "Перенести товар между ячейками.", "\uE8AB"),
                    new HubAction("assembly", "Сборка", "Списать товар из ячейки по основанию сборки.", "\uE7C3"),
                    new HubAction("stocktake", "Инвентаризация", "Сверить факт по выбранной ячейке.", "\uF0E3")
                ]),
            "data" => CreateHubView(
                "Данные склада",
                "Номенклатура, остатки и адресное хранение без лишних разделов.",
                [
                    new HubAction("stock", "Остатки", "Агрегированные остатки по складам и доступному количеству.", "\uE73E"),
                    new HubAction("cells", "Ячейки", "Адресное хранение, зоны и QR-коды.", "\uE71D"),
                    new HubAction("catalog", "Номенклатура", "Карточки товаров, коды, артикулы и поиск.", "\uE8FD")
                ]),
            "control" => CreateHubView(
                "Контроль WMS",
                "Проверка AI-черновиков и история складских действий.",
                [
                    new HubAction("receipt-drafts", "AI черновики", "Распознанные приходные накладные перед разноской.", "\uE7C3"),
                    new HubAction("operation-log", "Журнал", "История сканов, приёмки, перемещений и инвентаризаций.", "\uE9D9")
                ]),
            "scan" => CreateQuickScanView(),
            "stock" => new StockBalancesWorkspaceView(),
            "cells" => new StorageCellsWorkspaceView(),
            "catalog" => new ProductsWorkspaceView(_salesWorkspace),
            "receipt-drafts" => new ReceiptDraftsWorkspaceView(),
            "operation-log" => new WarehouseOperationLogWorkspaceView(),
            "receive" => CreateReceiveStockView(),
            "transfer" => CreateTransferStockView(),
            "assembly" => CreateAssemblyWriteOffView(),
            "stocktake" => CreateStockTakeView(),
            _ => CreateInfoView("Раздел не найден", "Такой режим склада пока не зарегистрирован.")
        };

        _viewCache[key] = created;
        return created;
    }

    private FrameworkElement CreateHubView(string title, string description, IReadOnlyList<HubAction> actions)
    {
        var panel = new WrapPanel
        {
            Margin = new Thickness(0, 8, 0, 0)
        };

        foreach (var action in actions)
        {
            panel.Children.Add(CreateHubActionButton(action));
        }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(2),
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
                        Foreground = BrushFromHex("#7A86A5"),
                        Margin = new Thickness(0, 4, 0, 16),
                        TextWrapping = TextWrapping.Wrap
                    },
                    panel
                }
            }
        };
    }

    private Button CreateHubActionButton(HubAction action)
    {
        var button = new Button
        {
            Width = 282,
            Height = 132,
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        button.Click += (_, _) => SelectMode(action.Key);

        button.Content = new Border
        {
            Background = Brushes.White,
            BorderBrush = BrushFromHex("#E7ECF7"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
                },
                Children =
                {
                    CreateActionGlyph(action.Glyph),
                    CreateActionTitle(action.Title),
                    CreateActionDescription(action.Description)
                }
            }
        };

        return button;
    }

    private static Border CreateActionGlyph(string glyph)
    {
        var border = new Border
        {
            Width = 38,
            Height = 38,
            Background = BrushFromHex("#EEF2FF"),
            BorderBrush = BrushFromHex("#C8D1FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Child = new TextBlock
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Text = glyph,
                Foreground = BrushFromHex("#4F5BFF"),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetRow(border, 0);
        return border;
    }

    private static TextBlock CreateActionTitle(string title)
    {
        var text = new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFromHex("#17213A"),
            Margin = new Thickness(0, 12, 0, 4)
        };
        Grid.SetRow(text, 1);
        return text;
    }

    private static TextBlock CreateActionDescription(string description)
    {
        var text = new TextBlock
        {
            Text = description,
            Foreground = BrushFromHex("#7A86A5"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5
        };
        Grid.SetRow(text, 2);
        return text;
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

    private FrameworkElement CreateReceiveStockView()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            return CreateInfoView(
                "Приёмка недоступна",
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.");
        }

        var catalogReader = new MySqlNomenclatureCatalogReader(backplane);
        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        return HostOperationWindow(new ReceiveStockWindow(catalogReader, cellCatalog, stockLocations));
    }

    private FrameworkElement CreateTransferStockView()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        var stockOperations = WarehouseStockOperationFactory.TryCreate();
        if (backplane is null || stockOperations is null)
        {
            return CreateInfoView(
                "Перемещение недоступно",
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.");
        }

        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        return HostOperationWindow(new TransferStockWindow(
            cellCatalog,
            stockLocations,
            stockOperations,
            _actorUserName));
    }

    private FrameworkElement CreateAssemblyWriteOffView()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        var stockOperations = WarehouseStockOperationFactory.TryCreate();
        if (backplane is null || stockOperations is null)
        {
            return CreateInfoView(
                "Сборка недоступна",
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.");
        }

        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        return HostOperationWindow(new AssemblyWriteOffWindow(
            cellCatalog,
            stockLocations,
            stockOperations,
            _actorUserName));
    }

    private FrameworkElement CreateStockTakeView()
    {
        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        var stockOperations = WarehouseStockOperationFactory.TryCreate();
        if (backplane is null || stockOperations is null)
        {
            return CreateInfoView(
                "Инвентаризация недоступна",
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.");
        }

        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        var shelfVision = ShelfVisionFactory.TryCreate();
        return HostOperationWindow(new StockTakeWindow(
            cellCatalog,
            stockLocations,
            stockOperations,
            _actorUserName,
            shelfVision));
    }

    private FrameworkElement HostOperationWindow(Window window)
    {
        if (window is not IHostedWmsOperationWindow hosted)
        {
            return CreateInfoView("Операция недоступна", "Форма не поддерживает встроенный режим WMS.");
        }

        hosted.DialogOwnerOverride = Window.GetWindow(this) ?? System.Windows.Application.Current?.MainWindow;
        hosted.HostCloseRequested = () => SelectMode("work");

        var content = window.Content;
        window.Content = null;
        if (content is not FrameworkElement element)
        {
            return CreateInfoView("Операция недоступна", "Не удалось загрузить содержимое формы.");
        }

        element.HorizontalAlignment = HorizontalAlignment.Stretch;
        element.VerticalAlignment = VerticalAlignment.Stretch;
        return element;
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

    private void ApplyModeSelection(string activeKey)
    {
        foreach (var button in EnumerateModeButtons())
        {
            var active = button.Tag is string tag && tag.Equals(activeKey, StringComparison.OrdinalIgnoreCase);
            button.Background = active ? ActiveBackground : DefaultBackground;
            button.BorderBrush = active ? ActiveBorder : DefaultBorder;
            button.Foreground = active ? ActiveForeground : DefaultForeground;
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private IEnumerable<Button> EnumerateModeButtons()
    {
        yield return ModeAssistantButton;
        yield return ModeWorkButton;
        yield return ModeDataButton;
        yield return ModeControlButton;
    }

    private static string GetParentSectionKey(string key) => key.ToLowerInvariant() switch
    {
        "scan" or "receive" or "transfer" or "assembly" or "stocktake" => "work",
        "stock" or "cells" or "catalog" => "data",
        "receipt-drafts" or "operation-log" => "control",
        _ => key
    };

    private static string GetModeTitle(string key) => key.ToLowerInvariant() switch
    {
        "assistant" => "Чат помощник",
        "work" => "Работа склада",
        "data" => "Данные склада",
        "control" => "Контроль WMS",
        "scan" => "Скан",
        "stock" => "Остатки",
        "cells" => "Ячейки",
        "catalog" => "Номенклатура",
        "receive" => "Приёмка товара",
        "transfer" => "Перемещение",
        "assembly" => "Сборка / списание",
        "stocktake" => "Инвентаризация",
        "receipt-drafts" => "AI черновики",
        "operation-log" => "Журнал WMS",
        _ => "Склад"
    };

    private static string GetModeSubtitle(string key) => key.ToLowerInvariant() switch
    {
        "assistant" => "Локальная LLM через 1C/Ollama API плюс точные складские инструменты Major.",
        "work" => "Сканирование, приёмка, перемещение и инвентаризация без лишних вкладок.",
        "data" => "Остатки, ячейки и номенклатура собраны в одном месте.",
        "control" => "AI-черновики и журнал операций для проверки склада.",
        "scan" => "Сканирование товара или ячейки без выхода из WMS.",
        "stock" => "Остатки по складам и стартовое размещение в UNPLACED.",
        "cells" => "Адресное хранение: зоны, ряды, стеллажи, ячейки и QR.",
        "catalog" => "Номенклатура, артикулы и карточки товаров для WMS.",
        "receive" => "Приёмка товара в ячейку.",
        "transfer" => "Перемещение товара между ячейками.",
        "assembly" => "Списание товара из ячейки по основанию сборки.",
        "stocktake" => "Инвентаризация ячейки и AI-проверка по фото.",
        "receipt-drafts" => "AI-черновики приходных накладных, готовые к разноске по ячейкам.",
        "operation-log" => "История WMS-действий и технических событий.",
        _ => "Единое рабочее место WMS."
    };

    private static Brush BrushFromHex(string value)
    {
        return (Brush)new BrushConverter().ConvertFromString(value)!;
    }

    private sealed record HubAction(string Key, string Title, string Description, string Glyph);
}
