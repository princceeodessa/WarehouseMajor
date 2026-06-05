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
        SelectMode("assistant");
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
        ModeTitleText.Text = GetModeTitle(key);
        ModeSubtitleText.Text = GetModeSubtitle(key);
        ApplyModeSelection(GetParentSectionKey(key));
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
            "receive" => CreateOperationLauncher(
                "Приёмка товара",
                "Быстрая запись товара в ячейку. Операция открывается полноэкранно поверх текущего рабочего места.",
                "Открыть приёмку",
                OpenReceiveStockDialog),
            "transfer" => CreateOperationLauncher(
                "Перемещение между ячейками",
                "Перенос товара из одной ячейки в другую с контролем доступного количества.",
                "Открыть перемещение",
                OpenTransferStockDialog),
            "stocktake" => CreateOperationLauncher(
                "Инвентаризация ячейки",
                "Сверка факта с системой по выбранной ячейке, включая AI-проверку фото полки.",
                "Открыть инвентаризацию",
                OpenStockTakeDialog),
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

        var backButton = new Button
        {
            Content = "Назад к работе",
            Height = 38,
            Padding = new Thickness(16, 0, 16, 0),
            Background = Brushes.White,
            BorderBrush = BrushFromHex("#E7ECF7"),
            BorderThickness = new Thickness(1),
            Foreground = BrushFromHex("#17213A"),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 10, 0)
        };
        backButton.Click += (_, _) => SelectMode("work");

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
                    Width = 600,
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
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Children = { backButton, button }
                            }
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
        "scan" or "receive" or "transfer" or "stocktake" => "work",
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
