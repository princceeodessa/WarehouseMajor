using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ProductsWorkspaceView : WpfUserControl, INotifyPropertyChanged, IDisposable
{
    private const string ProductsSection = "products";
    private const string PricesSection = "prices";
    private const string DiscountsSection = "discounts";
    private const string PriceSetupSection = "priceSetup";
    private const string JournalSection = "journal";

    private const string AllCategoriesFilter = "Все категории";
    private const string AllWarehousesFilter = "Все склады";
    private const string AllSuppliersFilter = "Все поставщики";
    private const string AllStatusesFilter = "Все статусы";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly SolidColorBrush PrimaryBrush = BrushFromHex("#4F5BFF");
    private static readonly SolidColorBrush MutedBrush = BrushFromHex("#7A86A5");
    private static readonly SolidColorBrush SuccessBrush = BrushFromHex("#1FA45F");
    private static readonly SolidColorBrush SuccessSoftBrush = BrushFromHex("#EAF9F0");
    private static readonly SolidColorBrush WarningBrush = BrushFromHex("#FF8A00");
    private static readonly SolidColorBrush WarningSoftBrush = BrushFromHex("#FFF4E3");
    private static readonly SolidColorBrush DangerBrush = BrushFromHex("#FF3045");
    private static readonly SolidColorBrush DangerSoftBrush = BrushFromHex("#FFF0F3");
    private static readonly SolidColorBrush NeutralBrush = BrushFromHex("#687693");
    private static readonly SolidColorBrush NeutralSoftBrush = BrushFromHex("#F0F3FA");

    private readonly SalesWorkspace _salesWorkspace;
    private readonly CatalogWorkspaceStore _store;
    private CatalogWorkspace _catalogWorkspace;
    private WarehouseWorkspace _warehouseWorkspace;
    private IReadOnlyList<ProductRowViewModel> _allProducts = Array.Empty<ProductRowViewModel>();
    private string _activeSection = ProductsSection;
    private bool _syncingSearch;
    private bool _suppressFilterEvents;
    private bool _persistWarningShown;
    private ProductRowViewModel? _selectedProduct;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<string>? NavigationRequested;

    public ProductsWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        _store = CatalogWorkspaceStore.CreateDefault();
        _catalogWorkspace = _store.LoadOrCreate(
            string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator) ? Environment.UserName : salesWorkspace.CurrentOperator,
            salesWorkspace);
        _warehouseWorkspace = WarehouseWorkspace.Create(salesWorkspace);

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);
        DataContext = this;

        InitializeActionsMenu();
        HookEvents();
        Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;
    }

    public ObservableCollection<ProductRowViewModel> Products { get; } = new();

    public ProductRowViewModel? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (ReferenceEquals(_selectedProduct, value))
            {
                return;
            }

            _selectedProduct = value;
            OnPropertyChanged(nameof(SelectedProduct));
            RefreshDetails();
        }
    }

    public void Dispose()
    {
        SizeChanged -= HandleSizeChanged;
        UnhookEvents();
        TryPersistCatalog();
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private void HookEvents()
    {
        _salesWorkspace.Changed += HandleSalesWorkspaceChanged;
        _catalogWorkspace.Changed += HandleCatalogWorkspaceChanged;
        Unloaded += HandleUnloaded;
    }

    private void UnhookEvents()
    {
        _salesWorkspace.Changed -= HandleSalesWorkspaceChanged;
        _catalogWorkspace.Changed -= HandleCatalogWorkspaceChanged;
        Unloaded -= HandleUnloaded;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        Dispatcher.BeginInvoke(() =>
        {
            RefreshAll();
            UpdateResponsiveLayout();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        TryPersistCatalog();
    }

    private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshAll, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandleCatalogWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TryPersistCatalog();
            RefreshAll();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshAll()
    {
        _warehouseWorkspace = WarehouseWorkspace.Create(_salesWorkspace);
        _allProducts = BuildProducts();

        RefreshMetrics();
        RefreshFilterOptions();
        ApplyFilters(keepSelected: true);
        ApplySection(_activeSection);
        UpdateResponsiveLayout();
    }

    private void UpdateResponsiveLayout()
    {
        var width = ActualWidth;
        var compactMetrics = width < 1260;
        var stackDetails = width < 1340;

        MetricCardsGrid.Columns = compactMetrics ? 2 : 4;
        DetailActionsGrid.Columns = stackDetails ? 2 : 1;

        if (stackDetails)
        {
            ProductsContentGrid.ColumnDefinitions[1].Width = new GridLength(0);
            ProductsContentGrid.ColumnDefinitions[2].Width = new GridLength(0);
            Grid.SetColumn(ProductDetailsCard, 0);
            Grid.SetRow(ProductDetailsCard, 1);
            Grid.SetColumnSpan(ProductDetailsCard, 3);
            ProductsTableCard.Margin = new Thickness(0);
            ProductDetailsCard.Margin = new Thickness(0, 16, 0, 0);
        }
        else
        {
            ProductsContentGrid.ColumnDefinitions[1].Width = new GridLength(18);
            ProductsContentGrid.ColumnDefinitions[2].Width = new GridLength(330);
            Grid.SetColumn(ProductDetailsCard, 2);
            Grid.SetRow(ProductDetailsCard, 0);
            Grid.SetColumnSpan(ProductDetailsCard, 1);
            ProductsTableCard.Margin = new Thickness(0);
            ProductDetailsCard.Margin = new Thickness(0);
        }
    }

    private IReadOnlyList<ProductRowViewModel> BuildProducts()
    {
        var stockByCode = _warehouseWorkspace.StockBalances
            .GroupBy(item => Ui(item.ItemCode), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.FreeQuantity + item.ReservedQuantity + item.ShippedQuantity)
                    .ThenBy(item => Ui(item.Warehouse), StringComparer.OrdinalIgnoreCase)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        return BuildVisibleCatalogItems()
            .OrderBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => Ui(item.Code), StringComparer.OrdinalIgnoreCase)
            .Select(item =>
            {
                stockByCode.TryGetValue(Ui(item.Code), out var stock);
                return ProductRowViewModel.Create(item, stock);
            })
            .ToArray();
    }

    private IReadOnlyList<CatalogItemRecord> BuildVisibleCatalogItems()
    {
        var items = new List<CatalogItemRecord>();
        var knownKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _catalogWorkspace.Items)
        {
            AddItem(item);
        }

        foreach (var option in EnumerateSalesCatalogOptions())
        {
            var key = BuildCatalogItemKey(option.Code, option.Name);
            if (string.IsNullOrWhiteSpace(key) || knownKeys.Contains(key))
            {
                continue;
            }

            AddItem(CreateRuntimeCatalogRecord(option));
        }

        return items;

        void AddItem(CatalogItemRecord item)
        {
            var key = BuildCatalogItemKey(item.Code, item.Name);
            if (!string.IsNullOrWhiteSpace(key) && !knownKeys.Add(key))
            {
                return;
            }

            items.Add(item);
        }
    }

    private IEnumerable<SalesCatalogItemOption> EnumerateSalesCatalogOptions()
    {
        if (_salesWorkspace.OperationalSnapshot?.CatalogItems is { Count: > 0 } operationalItems)
        {
            foreach (var item in operationalItems)
            {
                yield return item;
            }
        }

        foreach (var item in _salesWorkspace.CatalogItems)
        {
            yield return item;
        }
    }

    private CatalogItemRecord CreateRuntimeCatalogRecord(SalesCatalogItemOption item)
    {
        var code = Ui(item.Code);
        var name = FirstNonEmpty(item.Name, code, "Без названия");

        return new CatalogItemRecord
        {
            Id = CreateDeterministicGuid($"visible-catalog-item|{code}|{name}"),
            Code = code,
            Name = name,
            Unit = FirstNonEmpty(item.Unit, "шт"),
            Category = "Без категории",
            Supplier = string.Empty,
            DefaultWarehouse = string.Empty,
            Status = "Активна",
            CurrencyCode = FirstNonEmpty(_salesWorkspace.Currencies.FirstOrDefault() ?? string.Empty, "RUB"),
            DefaultPrice = item.DefaultPrice,
            BarcodeFormat = "Code128",
            SourceLabel = "Операционная база"
        };
    }

    private static string BuildCatalogItemKey(string? code, string? name)
    {
        var normalizedCode = Ui(code).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedCode))
        {
            return $"code:{normalizedCode}";
        }

        var normalizedName = Ui(name).Trim();
        return string.IsNullOrWhiteSpace(normalizedName) ? string.Empty : $"name:{normalizedName}";
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.Take(16).ToArray());
    }

    private void RefreshMetrics()
    {
        MissingPriceMetricText.Text = _allProducts.Count(item => item.MissingPrice).ToString("N0", RuCulture);
        MissingBarcodeMetricText.Text = _allProducts.Count(item => item.MissingBarcode).ToString("N0", RuCulture);
        LowStockMetricText.Text = _allProducts.Count(item => item.LowStock).ToString("N0", RuCulture);
        MissingSupplierMetricText.Text = _allProducts.Count(item => item.MissingSupplier).ToString("N0", RuCulture);
    }

    private void RefreshFilterOptions()
    {
        _suppressFilterEvents = true;

        CategoryFilterCombo.ItemsSource = BuildOptions(AllCategoriesFilter, _allProducts.Select(item => item.Category));
        WarehouseFilterCombo.ItemsSource = BuildOptions(AllWarehousesFilter, _allProducts.Select(item => item.Warehouse));
        SupplierFilterCombo.ItemsSource = BuildOptions(AllSuppliersFilter, _allProducts.Select(item => item.Supplier));
        StatusFilterCombo.ItemsSource = BuildOptions(AllStatusesFilter, _allProducts.Select(item => item.Status));

        CategoryFilterCombo.SelectedIndex = 0;
        WarehouseFilterCombo.SelectedIndex = 0;
        SupplierFilterCombo.SelectedIndex = 0;
        StatusFilterCombo.SelectedIndex = 0;

        _suppressFilterEvents = false;
    }

    private static string[] BuildOptions(string allCaption, IEnumerable<string> source)
    {
        return new[] { allCaption }
            .Concat(source
                .Select(Ui)
                .Where(item => !string.IsNullOrWhiteSpace(item) && item != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
    }

    private void ApplyFilters(bool keepSelected)
    {
        var previousId = keepSelected ? SelectedProduct?.Id : null;
        var search = Ui(TableSearchBox.Text);
        var category = Ui(CategoryFilterCombo.SelectedItem as string);
        var warehouse = Ui(WarehouseFilterCombo.SelectedItem as string);
        var supplier = Ui(SupplierFilterCombo.SelectedItem as string);
        var status = Ui(StatusFilterCombo.SelectedItem as string);
        var onlyProblems = OnlyProblemsCheckBox.IsChecked == true;

        var rows = _allProducts
            .Where(item => MatchesSearch(item, search))
            .Where(item => category == AllCategoriesFilter || item.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(item => warehouse == AllWarehousesFilter || item.Warehouse.Equals(warehouse, StringComparison.OrdinalIgnoreCase))
            .Where(item => supplier == AllSuppliersFilter || item.Supplier.Equals(supplier, StringComparison.OrdinalIgnoreCase))
            .Where(item => status == AllStatusesFilter || item.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(item => !onlyProblems || item.HasProblem)
            .ToArray();

        Products.Clear();
        foreach (var row in rows)
        {
            row.PropertyChanged -= HandleProductPropertyChanged;
            row.PropertyChanged += HandleProductPropertyChanged;
            Products.Add(row);
        }

        SelectedProduct = previousId.HasValue
            ? Products.FirstOrDefault(item => item.Id == previousId.Value) ?? Products.FirstOrDefault()
            : Products.FirstOrDefault();

        ProductsGrid.SelectedItem = SelectedProduct;
        ProductsCountText.Text = Products.Count == 0
            ? $"Показано 0 из {_allProducts.Count:N0}"
            : $"Показано 1–{Products.Count:N0} из {_allProducts.Count:N0}";
        UpdateSearchPlaceholders();
        UpdateBulkActions();
    }

    private static bool MatchesSearch(ProductRowViewModel item, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return item.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleProductPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductRowViewModel.IsSelected))
        {
            UpdateBulkActions();
        }
    }

    private void RefreshDetails()
    {
        var item = SelectedProduct;
        if (item is null)
        {
            DetailNameText.Text = "Выберите товар";
            DetailCodeText.Text = "-";
            DetailCategoryText.Text = "-";
            DetailSupplierText.Text = "-";
            DetailWarehouseText.Text = "-";
            DetailUnitText.Text = "-";
            DetailPriceText.Text = "-";
            DetailBarcodeText.Text = "-";
            DetailFreeText.Text = "0";
            DetailReservedText.Text = "0";
            DetailTransitText.Text = "0";
            DetailMinimumText.Text = "0";
            DetailDeficitText.Text = "0";
            MovementsItemsControl.ItemsSource = Array.Empty<ProductMovementViewModel>();
            DocumentsItemsControl.ItemsSource = Array.Empty<ProductDocumentViewModel>();
            return;
        }

        DetailNameText.Text = item.Name;
        DetailCodeText.Text = item.Code;
        DetailCategoryText.Text = item.Category;
        DetailSupplierText.Text = item.Supplier;
        DetailWarehouseText.Text = item.Warehouse;
        DetailUnitText.Text = item.Unit;
        DetailPriceText.Text = item.PriceDisplay;
        DetailBarcodeText.Text = item.Barcode;
        DetailFreeText.Text = item.FreeDisplay;
        DetailReservedText.Text = item.ReservedDisplay;
        DetailTransitText.Text = item.InTransitDisplay;
        DetailMinimumText.Text = item.MinimumStockDisplay;
        DetailDeficitText.Text = item.DeficitDisplay;
        DetailStatusText.Text = item.Status;
        DetailStatusText.Foreground = item.StatusForeground;
        DetailStatusPill.Background = item.StatusBackground;
        StockCaptionText.Text = $"Остатки на {DateTime.Now:dd.MM.yyyy HH:mm}";
        MovementsItemsControl.ItemsSource = BuildMovementItems(item);
        DocumentsItemsControl.ItemsSource = BuildDocumentItems(item);
    }

    private ProductMovementViewModel[] BuildMovementItems(ProductRowViewModel item)
    {
        var result = new List<ProductMovementViewModel>();

        foreach (var shipment in _salesWorkspace.Shipments
                     .Where(shipment => shipment.Lines.Any(line => LineMatches(line, item)))
                     .OrderByDescending(shipment => shipment.ShipmentDate)
                     .Take(2))
        {
            var quantity = shipment.Lines.Where(line => LineMatches(line, item)).Sum(line => line.Quantity);
            result.Add(new ProductMovementViewModel(
                shipment.ShipmentDate.ToString("dd.MM.yyyy HH:mm", RuCulture),
                $"Отгрузка {shipment.Number}",
                $"-{quantity:N0} {item.Unit}",
                DangerBrush));
        }

        foreach (var order in _salesWorkspace.Orders
                     .Where(order => order.Lines.Any(line => LineMatches(line, item)))
                     .OrderByDescending(order => order.OrderDate)
                     .Take(3))
        {
            var quantity = order.Lines.Where(line => LineMatches(line, item)).Sum(line => line.Quantity);
            result.Add(new ProductMovementViewModel(
                order.OrderDate.ToString("dd.MM.yyyy HH:mm", RuCulture),
                $"Резерв по заказу {order.Number}",
                $"-{quantity:N0} {item.Unit}",
                NeutralBrush));
        }

        return result
            .OrderByDescending(row => row.Date, StringComparer.Ordinal)
            .Take(5)
            .DefaultIfEmpty(new ProductMovementViewModel("—", "Нет связанных движений.", string.Empty, MutedBrush))
            .ToArray();
    }

    private ProductDocumentViewModel[] BuildDocumentItems(ProductRowViewModel item)
    {
        var orders = _salesWorkspace.Orders
            .Where(order => order.Lines.Any(line => LineMatches(line, item)))
            .OrderByDescending(order => order.OrderDate)
            .Take(3)
            .Select(order => new ProductDocumentViewModel(
                $"Заказ {order.Number}",
                $"от {order.OrderDate:dd.MM.yyyy}",
                $"({Ui(order.Status)})"));

        var invoices = _salesWorkspace.Invoices
            .Where(invoice => invoice.Lines.Any(line => LineMatches(line, item)))
            .OrderByDescending(invoice => invoice.InvoiceDate)
            .Take(2)
            .Select(invoice => new ProductDocumentViewModel(
                $"Счет {invoice.Number}",
                $"от {invoice.InvoiceDate:dd.MM.yyyy}",
                $"({Ui(invoice.Status)})"));

        return orders
            .Concat(invoices)
            .Take(5)
            .DefaultIfEmpty(new ProductDocumentViewModel("Нет связанных документов.", string.Empty, string.Empty))
            .ToArray();
    }

    private static bool LineMatches(SalesOrderLineRecord line, ProductRowViewModel product)
    {
        return Ui(line.ItemCode).Equals(product.Code, StringComparison.OrdinalIgnoreCase)
               || Ui(line.ItemName).Equals(product.Name, StringComparison.OrdinalIgnoreCase);
    }

    private void InitializeActionsMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Печать этикеток", (_, _) => PrintLabels(GetSelectedOrCurrentProducts())));
        menu.Items.Add(CreateMenuItem("Изменить категорию", HandleChangeCategoryClick));
        menu.Items.Add(CreateMenuItem("Изменить статус", HandleChangeStatusClick));
        menu.Items.Add(CreateMenuItem("Обновить цены", (_, _) => ShowPriceUpdateMessage()));
        menu.Items.Add(CreateMenuItem("Выгрузить прайс-лист", (_, _) => ExportPriceList(GetPriceListScope())));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Архивировать выбранные", (_, _) => ArchiveSelectedProducts()));
        ActionsButton.ContextMenu = menu;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void HandleActionsClick(object sender, RoutedEventArgs e)
    {
        ActionsButton.ContextMenu.PlacementTarget = ActionsButton;
        ActionsButton.ContextMenu.IsOpen = true;
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: ProductRowViewModel product } button)
        {
            return;
        }

        SelectedProduct = product;
        ProductsGrid.SelectedItem = product;

        var menu = BuildProductMenu(product);
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void HandleProductsGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not ProductRowViewModel product)
        {
            return;
        }

        SelectProduct(product);
        EditProduct(product);
        e.Handled = true;
    }

    private void HandleDetailActionsClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is null || sender is not WpfButton button)
        {
            return;
        }

        var menu = BuildProductMenu(SelectedProduct);
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private ContextMenu BuildProductMenu(ProductRowViewModel product)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Открыть карточку", (_, _) => SelectProduct(product)));
        menu.Items.Add(CreateMenuItem("Изменить", (_, _) => EditProduct(product)));
        menu.Items.Add(CreateMenuItem("Дублировать", (_, _) => DuplicateProduct(product)));
        menu.Items.Add(CreateMenuItem("Печать этикетки", (_, _) => PrintLabels(new[] { product })));
        menu.Items.Add(CreateMenuItem("Обновить цену", (_, _) => ShowPriceUpdateMessage()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Переместить", (_, _) => OpenWarehouseDocument(WarehouseDocumentEditorMode.Transfer, new[] { product })));
        menu.Items.Add(CreateMenuItem("Зарезервировать", (_, _) => CreateSalesReserve(new[] { product })));
        menu.Items.Add(CreateMenuItem("Списать", (_, _) => OpenWarehouseDocument(WarehouseDocumentEditorMode.WriteOff, new[] { product })));
        menu.Items.Add(CreateMenuItem("Создать закупку", (_, _) => CreatePurchaseOrder(new[] { product })));
        menu.Items.Add(CreateMenuItem("История движений", (_, _) => HandleMovementsClick(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("Архивировать", (_, _) => ArchiveProducts(new[] { product })));
        return menu;
    }

    private void SelectProduct(ProductRowViewModel product)
    {
        SelectedProduct = product;
        ProductsGrid.SelectedItem = product;
        ProductsGrid.ScrollIntoView(product);
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject source)
    {
        try
        {
            return VisualTreeHelper.GetParent(source) ?? LogicalTreeHelper.GetParent(source);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(source);
        }
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearch)
        {
            return;
        }

        _syncingSearch = true;
        var text = sender is WpfTextBox textBox ? textBox.Text : string.Empty;
        HeaderSearchBox.Text = text;
        TableSearchBox.Text = text;
        HeaderSearchBox.CaretIndex = HeaderSearchBox.Text.Length;
        TableSearchBox.CaretIndex = TableSearchBox.Text.Length;
        _syncingSearch = false;

        ApplyFilters(keepSelected: true);
    }

    private void HandleFilterChanged(object sender, EventArgs e)
    {
        if (!_suppressFilterEvents)
        {
            ApplyFilters(keepSelected: true);
        }
    }

    private void HandleResetFiltersClick(object sender, RoutedEventArgs e)
    {
        _suppressFilterEvents = true;
        HeaderSearchBox.Clear();
        TableSearchBox.Clear();
        CategoryFilterCombo.SelectedIndex = 0;
        WarehouseFilterCombo.SelectedIndex = 0;
        SupplierFilterCombo.SelectedIndex = 0;
        StatusFilterCombo.SelectedIndex = 0;
        OnlyProblemsCheckBox.IsChecked = false;
        _suppressFilterEvents = false;
        ApplyFilters(keepSelected: false);
    }

    private void HandleShowProblemsClick(object sender, RoutedEventArgs e)
    {
        OnlyProblemsCheckBox.IsChecked = true;
        ApplyFilters(keepSelected: true);
    }

    private void HandleSelectAllClick(object sender, RoutedEventArgs e)
    {
        var value = SelectAllCheckBox.IsChecked == true;
        foreach (var item in Products)
        {
            item.IsSelected = value;
        }

        UpdateBulkActions();
    }

    private void HandleRowCheckBoxClick(object sender, RoutedEventArgs e)
    {
        UpdateBulkActions();
    }

    private void UpdateSearchPlaceholders()
    {
        HeaderSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(HeaderSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        TableSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(TableSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBulkActions()
    {
        var selected = Products.Count(item => item.IsSelected);
        BulkActionsPanel.Visibility = selected > 1 ? Visibility.Visible : Visibility.Collapsed;
        SelectedCountText.Text = $"Выбрано {selected:N0} товара";
        SelectAllCheckBox.IsChecked = Products.Count > 0 && Products.All(item => item.IsSelected);
    }

    private ProductRowViewModel[] GetSelectedOrCurrentProducts()
    {
        var selected = Products.Where(item => item.IsSelected).ToArray();
        if (selected.Length > 0)
        {
            return selected;
        }

        return SelectedProduct is null ? Array.Empty<ProductRowViewModel>() : new[] { SelectedProduct };
    }

    private ProductRowViewModel[] GetPriceListScope()
    {
        var selected = Products.Where(item => item.IsSelected).ToArray();
        if (selected.Length > 0)
        {
            return selected;
        }

        return Products.ToArray();
    }

    private void HandleImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импорт товаров",
            Filter = "CSV/TSV/TXT (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var imported = ImportProductsFromDelimitedFile(dialog.FileName);
            TryPersistCatalog();
            MessageBox.Show(
                Window.GetWindow(this),
                $"Импорт завершен. Обновлено товаров: {imported:N0}.",
                "Товары",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Не удалось импортировать файл.\n{ex.Message}",
                "Товары",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void HandleExportClick(object sender, RoutedEventArgs e)
    {
        ExportProducts(Products.ToArray());
    }

    private void HandleExportSelectedClick(object sender, RoutedEventArgs e)
    {
        ExportProducts(GetSelectedOrCurrentProducts());
    }

    private void HandleNewProductClick(object sender, RoutedEventArgs e)
    {
        OpenProductEditor(null);
    }

    private void HandlePrintLabelsClick(object sender, RoutedEventArgs e)
    {
        PrintLabels(GetSelectedOrCurrentProducts());
    }

    private void HandleChangeCategoryClick(object sender, RoutedEventArgs e)
    {
        var products = GetSelectedOrCurrentProducts();
        if (products.Length == 0)
        {
            ShowPlannedAction("Выберите товары для изменения категории.");
            return;
        }

        var initial = products.Length == 1 ? products[0].Category : string.Empty;
        var value = PromptText(
            "Изменить категорию",
            $"Новая категория для выбранных товаров: {products.Length:N0}.",
            initial,
            _allProducts.Select(item => item.Category));

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var product in products)
        {
            var copy = product.Record.Clone();
            copy.Category = value;
            _catalogWorkspace.UpsertItem(copy);
        }

        TryPersistCatalog();
    }

    private void HandleChangeStatusClick(object sender, RoutedEventArgs e)
    {
        var products = GetSelectedOrCurrentProducts();
        if (products.Length == 0)
        {
            ShowPlannedAction("Выберите товары для изменения статуса.");
            return;
        }

        var initial = products.Length == 1 ? products[0].Status : _catalogWorkspace.ItemStatuses.FirstOrDefault();
        var value = PromptText(
            "Изменить статус",
            $"Новый статус для выбранных товаров: {products.Length:N0}.",
            initial,
            _catalogWorkspace.ItemStatuses);

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var product in products)
        {
            var copy = product.Record.Clone();
            copy.Status = value;
            _catalogWorkspace.UpsertItem(copy);
        }

        TryPersistCatalog();
    }

    private void HandleUpdatePricesClick(object sender, RoutedEventArgs e)
    {
        ShowPriceUpdateMessage();
    }

    private void HandleArchiveSelectedClick(object sender, RoutedEventArgs e)
    {
        ArchiveSelectedProducts();
    }

    private void HandleClearSelectionClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in Products)
        {
            item.IsSelected = false;
        }

        UpdateBulkActions();
    }

    private void HandleEditSelectedClick(object sender, RoutedEventArgs e)
    {
        var product = GetSelectedOrCurrentProducts().FirstOrDefault();
        if (product is null)
        {
            ShowPlannedAction("Выберите товар для редактирования.");
            return;
        }

        EditProduct(product);
    }

    private void HandleMoveSelectedClick(object sender, RoutedEventArgs e)
    {
        OpenWarehouseDocument(WarehouseDocumentEditorMode.Transfer, GetSelectedOrCurrentProducts());
    }

    private void HandleReserveSelectedClick(object sender, RoutedEventArgs e)
    {
        CreateSalesReserve(GetSelectedOrCurrentProducts());
    }

    private void HandleWriteOffSelectedClick(object sender, RoutedEventArgs e)
    {
        OpenWarehouseDocument(WarehouseDocumentEditorMode.WriteOff, GetSelectedOrCurrentProducts());
    }

    private void HandlePurchaseSelectedClick(object sender, RoutedEventArgs e)
    {
        CreatePurchaseOrder(GetSelectedOrCurrentProducts());
    }

    private void HandleMovementsClick(object sender, RoutedEventArgs e)
    {
        ApplySection(JournalSection);
    }

    private void OpenProductEditor(CatalogItemRecord? item)
    {
        var dialog = new ProductEditorWindow(_catalogWorkspace, item)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultItem is null)
        {
            return;
        }

        _catalogWorkspace.UpsertItem(dialog.ResultItem);
        TryPersistCatalog();
    }

    private void EditProduct(ProductRowViewModel product)
    {
        OpenProductEditor(product.Record);
    }

    private string? PromptText(string title, string prompt, string? initialValue, IEnumerable<string> options)
    {
        var dialog = new ProductTextInputWindow(title, prompt, initialValue, options)
        {
            Owner = Window.GetWindow(this)
        };

        return dialog.ShowDialog() == true ? dialog.ResultText : null;
    }

    private int ImportProductsFromDelimitedFile(string path)
    {
        var lines = ReadAllLinesAuto(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        if (lines.Length == 0)
        {
            return 0;
        }

        var delimiter = DetectDelimiter(lines[0]);
        var firstRow = SplitDelimitedLine(lines[0], delimiter);
        var hasHeader = HasImportHeader(firstRow);
        var headerMap = hasHeader
            ? firstRow
                .Select((header, index) => (Header: NormalizeImportHeader(header), Index: index))
                .Where(item => !string.IsNullOrWhiteSpace(item.Header))
                .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var imported = 0;
        foreach (var line in lines.Skip(hasHeader ? 1 : 0))
        {
            var cells = SplitDelimitedLine(line, delimiter);
            var code = Field(cells, headerMap, 0, "код", "артикул", "sku", "code");
            var name = Field(cells, headerMap, 1, "товар", "наименование", "номенклатура", "название", "name");

            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                code = $"ITM-{DateTime.Now:yyMMdd-HHmmss}-{imported + 1:000}";
            }

            var existing = _catalogWorkspace.Items.FirstOrDefault(item => Ui(item.Code).Equals(code, StringComparison.OrdinalIgnoreCase));
            var record = existing?.Clone() ?? _catalogWorkspace.CreateItemDraft();
            record.Code = code;
            record.Name = string.IsNullOrWhiteSpace(name) ? code : name;
            record.Category = FirstNonEmpty(Field(cells, headerMap, 2, "категория", "category"), record.Category);
            record.DefaultWarehouse = FirstNonEmpty(Field(cells, headerMap, 3, "склад", "warehouse"), record.DefaultWarehouse);
            record.Unit = FirstNonEmpty(Field(cells, headerMap, 4, "ед", "едизм", "единица", "единицаизмерения", "unit"), record.Unit, "шт");
            record.Supplier = FirstNonEmpty(Field(cells, headerMap, -1, "поставщик", "supplier"), record.Supplier);
            record.Status = FirstNonEmpty(Field(cells, headerMap, 9, "статус", "status"), record.Status, _catalogWorkspace.ItemStatuses.First());
            record.BarcodeValue = FirstNonEmpty(Field(cells, headerMap, 10, "штрихкод", "barcode", "bar"), record.BarcodeValue);

            var priceRaw = Field(cells, headerMap, 8, "цена", "price");
            if (TryParseImportDecimal(priceRaw, out var price))
            {
                record.DefaultPrice = price;
            }

            _catalogWorkspace.UpsertItem(record);
            imported++;
        }

        return imported;
    }

    private void OpenWarehouseDocument(WarehouseDocumentEditorMode mode, IReadOnlyList<ProductRowViewModel> products)
    {
        var rows = products.Count == 0 ? GetSelectedOrCurrentProducts() : products.ToArray();
        if (rows.Length == 0)
        {
            ShowPlannedAction("Выберите товары для складского документа.");
            return;
        }

        var store = WarehouseOperationalWorkspaceStore.CreateDefault();
        var workspace = store.LoadOrCreate(GetCurrentOperator(), _salesWorkspace);
        var sourceWarehouse = rows.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Warehouse) && item.Warehouse != "-")?.Warehouse;
        var draft = mode switch
        {
            WarehouseDocumentEditorMode.Transfer => workspace.CreateTransferDraft(sourceWarehouse),
            WarehouseDocumentEditorMode.Inventory => workspace.CreateInventoryDraft(sourceWarehouse),
            _ => workspace.CreateWriteOffDraft(sourceWarehouse)
        };

        draft.RelatedDocument = string.Join(", ", rows.Select(item => item.Code).Take(4));
        draft.Comment = $"Создано из карточки товара: {string.Join(", ", rows.Select(item => item.Name).Take(3))}.";
        foreach (var product in rows)
        {
            draft.Lines.Add(new OperationalWarehouseLineRecord
            {
                Id = Guid.NewGuid(),
                ItemCode = product.Code,
                ItemName = product.Name,
                Quantity = 1m,
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "шт" : product.Unit,
                SourceLocation = product.Warehouse == "-" ? draft.SourceWarehouse : product.Warehouse,
                TargetLocation = draft.TargetWarehouse,
                RelatedDocument = draft.RelatedDocument
            });
        }

        var dialog = new WarehouseDocumentEditorWindow(workspace, mode, draft)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        switch (mode)
        {
            case WarehouseDocumentEditorMode.Transfer:
                workspace.AddTransferOrder(dialog.ResultDocument);
                break;
            case WarehouseDocumentEditorMode.Inventory:
                workspace.AddInventoryCount(dialog.ResultDocument);
                break;
            default:
                workspace.AddWriteOff(dialog.ResultDocument);
                break;
        }

        store.Save(workspace);
        NavigationRequested?.Invoke(this, "warehouse");
    }

    private void CreateSalesReserve(IReadOnlyList<ProductRowViewModel> products)
    {
        var rows = products.Count == 0 ? GetSelectedOrCurrentProducts() : products.ToArray();
        if (rows.Length == 0)
        {
            ShowPlannedAction("Выберите товары для резервирования.");
            return;
        }

        if (_salesWorkspace.Customers.Count == 0)
        {
            ShowPlannedAction("Сначала создайте клиента. Резерв оформляется через заказ покупателя.");
            return;
        }

        var customer = ResolveReservationCustomer();
        if (customer is null)
        {
            return;
        }

        var quantity = rows.Length == 1 ? PromptReservationQuantity(rows[0]) : 1m;
        if (quantity <= 0m)
        {
            return;
        }

        var order = _salesWorkspace.CreateOrderDraft(customer.Id);
        order.Warehouse = ResolveReservationWarehouse(rows);
        order.Comment = $"Резерв создан из карточки товара: {string.Join(", ", rows.Select(item => item.Name).Take(3))}.";

        foreach (var product in rows)
        {
            order.Lines.Add(new SalesOrderLineRecord
            {
                Id = Guid.NewGuid(),
                ItemCode = product.Code,
                ItemName = product.Name,
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "шт" : product.Unit,
                Quantity = quantity,
                Price = product.Price
            });
        }

        var check = new SalesInventoryService(_salesWorkspace).AnalyzeDraft(order.Warehouse, order.Lines);
        if (!check.IsFullyCovered)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"{check.StatusText}\n{check.HintText}",
                "Резерв товара",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _salesWorkspace.AddOrder(order);
        var result = _salesWorkspace.ReserveOrder(order.Id);
        NavigationRequested?.Invoke(this, "sales");

        MessageBox.Show(
            Window.GetWindow(this),
            $"{result.Message}\n{result.Detail}\n\nСоздан заказ: {order.Number}",
            "Резерв товара",
            MessageBoxButton.OK,
            result.Succeeded ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private SalesCustomerRecord? ResolveReservationCustomer()
    {
        var activeCustomer = _salesWorkspace.Customers
            .FirstOrDefault(item => Ui(item.Status).Equals("Активен", StringComparison.OrdinalIgnoreCase))
            ?? _salesWorkspace.Customers.FirstOrDefault();

        var customerName = PromptText(
            "Резерв товара",
            "Выберите клиента, под которого нужно поставить товар в резерв.",
            activeCustomer?.Name,
            _salesWorkspace.Customers.Select(item => item.Name));

        if (string.IsNullOrWhiteSpace(customerName))
        {
            return null;
        }

        return _salesWorkspace.Customers.FirstOrDefault(item => Ui(item.Name).Equals(customerName, StringComparison.OrdinalIgnoreCase))
            ?? activeCustomer;
    }

    private decimal PromptReservationQuantity(ProductRowViewModel product)
    {
        var initial = product.FreeQuantity > 0m ? Math.Min(product.FreeQuantity, 1m).ToString("N0", RuCulture) : "1";
        var quantityText = PromptText(
            "Резерв товара",
            $"Количество для резерва: {product.Name}",
            initial,
            Array.Empty<string>());

        if (string.IsNullOrWhiteSpace(quantityText))
        {
            return 0m;
        }

        if (TryParseImportDecimal(quantityText, out var quantity) && quantity > 0m)
        {
            return quantity;
        }

        MessageBox.Show(Window.GetWindow(this), "Введите корректное количество больше нуля.", "Резерв товара", MessageBoxButton.OK, MessageBoxImage.Warning);
        return 0m;
    }

    private string ResolveReservationWarehouse(IReadOnlyList<ProductRowViewModel> products)
    {
        return products
                   .Select(item => item.Warehouse)
                   .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item) && item != "-")
               ?? _salesWorkspace.Warehouses.FirstOrDefault()
               ?? "Главный склад";
    }

    private void CreatePurchaseOrder(IReadOnlyList<ProductRowViewModel> products)
    {
        var rows = products.Count == 0 ? GetSelectedOrCurrentProducts() : products.ToArray();
        if (rows.Length == 0)
        {
            ShowPlannedAction("Выберите товары для закупки.");
            return;
        }

        var store = PurchasingOperationalWorkspaceStore.CreateDefault();
        var workspace = store.LoadOrCreate(GetCurrentOperator(), _salesWorkspace);
        var supplier = ResolvePurchaseSupplier(workspace, rows);
        var document = workspace.CreatePurchaseOrderDraft(supplier?.Id);
        document.Comment = $"Создано из карточки товара: {string.Join(", ", rows.Select(item => item.Name).Take(3))}.";

        foreach (var product in rows)
        {
            document.Lines.Add(new OperationalPurchasingLineRecord
            {
                Id = Guid.NewGuid(),
                SectionName = "Товары",
                ItemCode = product.Code,
                ItemName = product.Name,
                Quantity = Math.Max(1m, product.Deficit > 0m ? product.Deficit : 1m),
                Unit = string.IsNullOrWhiteSpace(product.Unit) ? "шт" : product.Unit,
                Price = product.Price,
                PlannedDate = DateTime.Today.AddDays(3),
                RelatedDocument = product.Code
            });
        }

        workspace.AddPurchaseOrder(document);
        store.Save(workspace);
        NavigationRequested?.Invoke(this, "purchasing");

        MessageBox.Show(
            Window.GetWindow(this),
            $"Создан заказ поставщику {document.Number}. Позиций: {document.Lines.Count:N0}.",
            "Товары",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private OperationalPurchasingSupplierRecord? ResolvePurchaseSupplier(
        OperationalPurchasingWorkspace workspace,
        IReadOnlyList<ProductRowViewModel> products)
    {
        var supplierName = products
            .Select(item => item.Supplier)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item) && item != "-");

        if (string.IsNullOrWhiteSpace(supplierName))
        {
            return workspace.Suppliers.FirstOrDefault();
        }

        var existing = workspace.Suppliers.FirstOrDefault(item => Ui(item.Name).Equals(supplierName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var created = workspace.CreateSupplierDraft();
        created.Name = supplierName;
        created.Code = string.IsNullOrWhiteSpace(created.Code) ? $"SUP-{DateTime.Now:yyMMdd-HHmmss}" : created.Code;
        workspace.AddSupplier(created);
        return workspace.Suppliers.FirstOrDefault(item => item.Id == created.Id) ?? created;
    }

    private string GetCurrentOperator()
    {
        return string.IsNullOrWhiteSpace(_salesWorkspace.CurrentOperator)
            ? Environment.UserName
            : _salesWorkspace.CurrentOperator;
    }

    private void DuplicateProduct(ProductRowViewModel product)
    {
        var copy = product.Record.Clone();
        copy.Id = Guid.NewGuid();
        copy.Code = $"{copy.Code}-COPY";
        copy.Name = $"{copy.Name} копия";
        _catalogWorkspace.UpsertItem(copy);
    }

    private void ArchiveSelectedProducts()
    {
        ArchiveProducts(GetSelectedOrCurrentProducts());
    }

    private void ArchiveProducts(IEnumerable<ProductRowViewModel> products)
    {
        var rows = products.ToArray();
        if (rows.Length == 0)
        {
            ShowPlannedAction("Выберите товары для архивации.");
            return;
        }

        foreach (var row in rows)
        {
            var copy = row.Record.Clone();
            copy.Status = "Архив";
            _catalogWorkspace.UpsertItem(copy);
        }

        TryPersistCatalog();
        MessageBox.Show(Window.GetWindow(this), $"Архивировано товаров: {rows.Length:N0}.", "Товары", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportProducts(IReadOnlyList<ProductRowViewModel> products)
    {
        if (products.Count == 0)
        {
            ShowPlannedAction("Нет товаров для экспорта.");
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "exports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"products-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var builder = new StringBuilder();
        builder.AppendLine("Код;Товар;Категория;Склад;Свободно;Резерв;В пути;Мин. остаток;Цена;Статус;Штрихкод");
        foreach (var item in products)
        {
            builder.AppendLine(string.Join(
                ';',
                Csv(item.Code),
                Csv(item.Name),
                Csv(item.Category),
                Csv(item.Warehouse),
                item.FreeQuantity.ToString("N2", RuCulture),
                item.ReservedQuantity.ToString("N2", RuCulture),
                item.InTransitQuantity.ToString("N2", RuCulture),
                item.MinimumStock.ToString("N2", RuCulture),
                item.Price.ToString("N2", RuCulture),
                Csv(item.Status),
                Csv(item.Barcode)));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show(Window.GetWindow(this), $"Экспортировано товаров: {products.Count:N0}.\nФайл: {path}", "Товары", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportPriceList(IReadOnlyList<ProductRowViewModel> products)
    {
        if (products.Count == 0)
        {
            ShowPlannedAction("Нет товаров для выгрузки прайс-листа.");
            return;
        }

        var priceTypes = _catalogWorkspace.PriceTypes.Count > 0
            ? _catalogWorkspace.PriceTypes.OrderByDescending(item => item.IsDefault).ThenBy(item => Ui(item.Name)).ToArray()
            : [new CatalogPriceTypeRecord { Name = "Базовая цена", CurrencyCode = "RUB", IsDefault = true }];

        var activeDiscounts = _catalogWorkspace.Discounts
            .Where(IsActiveDiscount)
            .ToArray();
        var directory = Path.Combine(AppContext.BaseDirectory, "exports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"price-list-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        var builder = new StringBuilder();
        var header = new List<string>
        {
            "Код",
            "Товар",
            "Категория",
            "Ед. изм.",
            "Поставщик",
            "Склад",
            "Статус",
            "Валюта"
        };
        header.AddRange(priceTypes.Select(item => $"Цена: {Ui(item.Name)}"));
        header.Add("Скидка, %");
        header.Add("Примечание");
        builder.AppendLine(string.Join(';', header.Select(Csv)));

        foreach (var item in products.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase))
        {
            var maxDiscount = ResolvePriceListDiscountPercent(item, activeDiscounts, null);
            var row = new List<string>
            {
                item.Code,
                item.Name,
                item.Category,
                item.Unit,
                item.Supplier,
                item.Warehouse,
                item.Status,
                item.Currency
            };

            foreach (var priceType in priceTypes)
            {
                row.Add(ResolvePriceListPrice(item, priceType, activeDiscounts).ToString("N2", RuCulture));
            }

            row.Add(maxDiscount.ToString("N2", RuCulture));
            row.Add(item.MissingPrice ? "Требуется цена" : string.Empty);
            builder.AppendLine(string.Join(';', row.Select(Csv)));
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show(Window.GetWindow(this), $"Прайс-лист выгружен. Товаров: {products.Count:N0}.\nФайл: {path}", "Товары", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PrintLabels(IReadOnlyList<ProductRowViewModel> products)
    {
        if (products.Count == 0)
        {
            ShowPlannedAction("Выберите товар для печати этикетки.");
            return;
        }

        var labels = products.Select(BuildPrintableLabel).ToArray();
        PrintDocumentComposer.Print(
            Window.GetWindow(this),
            "Этикетки товаров",
            (pageWidth, pageHeight) => PrintDocumentComposer.BuildLabelsDocument("Этикетки товаров", labels, pageWidth, pageHeight));
    }

    private static PrintableLabelDefinition BuildPrintableLabel(ProductRowViewModel item)
    {
        var generatedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm", RuCulture);
        var barcode = ResolveLabelBarcode(item);
        var payload = string.IsNullOrWhiteSpace(item.Record.QrPayload)
            ? BuildProductQrPayload(item, barcode)
            : item.Record.QrPayload.Trim();

        return new PrintableLabelDefinition(
            "Этикетка товара",
            item.Name,
            item.Status,
            new[]
            {
                new PrintableField("Код", item.Code),
                new PrintableField("Категория", item.Category),
                new PrintableField("Склад", item.Warehouse),
                new PrintableField("Цена", item.PriceDisplay),
                new PrintableField("Ед. изм.", item.Unit),
                new PrintableField("Поставщик", item.Supplier)
            },
            barcode,
            payload,
            $"Сформировано: {generatedAt}");
    }

    private static string ResolveLabelBarcode(ProductRowViewModel item)
    {
        return ResolveDisplayBarcode(item.Record, item.Code, item.Name, item.Warehouse);
    }

    private static string ResolveDisplayBarcode(CatalogItemRecord item, string code, string name, string warehouse)
    {
        var barcode = Ui(item.BarcodeValue).Trim();
        if (string.IsNullOrWhiteSpace(barcode) || BarcodeLooksLikeSku(barcode, code))
        {
            return LabelPrintHtmlBuilder.BuildStableNumericCode(code, name, warehouse, item.Id.ToString("N", CultureInfo.InvariantCulture));
        }

        return barcode;
    }

    private static bool BarcodeLooksLikeSku(string barcode, string code)
    {
        var normalizedBarcode = NormalizeBarcodeComparable(barcode);
        var normalizedCode = NormalizeBarcodeComparable(code);
        return !string.IsNullOrWhiteSpace(normalizedCode) && string.Equals(normalizedBarcode, normalizedCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBarcodeComparable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : new string(value.Trim().Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static decimal ResolvePriceListPrice(
        ProductRowViewModel item,
        CatalogPriceTypeRecord priceType,
        IReadOnlyList<CatalogDiscountRecord> activeDiscounts)
    {
        var discountPercent = ResolvePriceListDiscountPercent(item, activeDiscounts, priceType.Name);
        var price = item.Price <= 0m
            ? 0m
            : item.Price * (1m - Math.Clamp(discountPercent, 0m, 100m) / 100m);
        return ApplyPriceListRounding(price, priceType);
    }

    private static decimal ResolvePriceListDiscountPercent(
        ProductRowViewModel item,
        IReadOnlyList<CatalogDiscountRecord> activeDiscounts,
        string? priceTypeName)
    {
        return activeDiscounts
            .Where(discount => DiscountAppliesToItem(discount, item, priceTypeName))
            .Select(discount => Math.Clamp(discount.Percent, 0m, 100m))
            .DefaultIfEmpty(0m)
            .Max();
    }

    private static bool DiscountAppliesToItem(CatalogDiscountRecord discount, ProductRowViewModel item, string? priceTypeName)
    {
        var discountPriceType = Ui(discount.PriceTypeName);
        if (!string.IsNullOrWhiteSpace(priceTypeName)
            && !string.IsNullOrWhiteSpace(discountPriceType)
            && !discountPriceType.Equals(Ui(priceTypeName), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var scope = Ui(discount.Scope);
        if (string.IsNullOrWhiteSpace(scope) || scope.Equals("Все товары", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return scope.Contains(item.Category, StringComparison.OrdinalIgnoreCase)
               || scope.Contains(item.Supplier, StringComparison.OrdinalIgnoreCase)
               || scope.Contains(item.Warehouse, StringComparison.OrdinalIgnoreCase)
               || scope.Contains(item.Code, StringComparison.OrdinalIgnoreCase)
               || scope.Contains(item.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveDiscount(CatalogDiscountRecord discount)
    {
        var status = Ui(discount.Status);
        return string.IsNullOrWhiteSpace(status)
               || status.Contains("актив", StringComparison.OrdinalIgnoreCase)
               || status.Contains("active", StringComparison.OrdinalIgnoreCase);
    }

    private static decimal ApplyPriceListRounding(decimal price, CatalogPriceTypeRecord priceType)
    {
        if (price <= 0m)
        {
            return 0m;
        }

        var rounding = Ui(priceType.RoundingRule);
        if (priceType.UsesPsychologicalRounding || rounding.Contains("псих", StringComparison.OrdinalIgnoreCase))
        {
            var step = price >= 1000m ? 100m : 10m;
            return Math.Max(1m, Math.Ceiling(price / step) * step - 1m);
        }

        if (rounding.Contains("без", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(price, 2, MidpointRounding.AwayFromZero);
        }

        return Math.Round(price, 0, MidpointRounding.AwayFromZero);
    }

    private static string BuildProductQrPayload(ProductRowViewModel item, string barcode)
    {
        return string.Join(
            Environment.NewLine,
            $"Код: {item.Code}",
            $"Товар: {item.Name}",
            $"Категория: {item.Category}",
            $"Склад: {item.Warehouse}",
            $"Цена: {item.PriceDisplay}",
            $"Штрихкод: {barcode}");
    }

    private static string Csv(string value)
    {
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static string Html(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static string[] ReadAllLinesAuto(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return SplitTextLines(strictUtf8.GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return SplitTextLines(Encoding.GetEncoding(1251).GetString(bytes));
        }
    }

    private static string[] SplitTextLines(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static char DetectDelimiter(string line)
    {
        var semicolon = line.Count(ch => ch == ';');
        var tab = line.Count(ch => ch == '\t');
        var comma = line.Count(ch => ch == ',');

        if (tab >= semicolon && tab >= comma)
        {
            return '\t';
        }

        return semicolon >= comma ? ';' : ',';
    }

    private static string[] SplitDelimitedLine(string line, char delimiter)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                result.Add(Ui(builder.ToString().Trim()));
                builder.Clear();
                continue;
            }

            builder.Append(ch);
        }

        result.Add(Ui(builder.ToString().Trim()));
        return result.ToArray();
    }

    private static bool HasImportHeader(IReadOnlyList<string> row)
    {
        return row
            .Select(NormalizeImportHeader)
            .Any(header => header is "код" or "артикул" or "товар" or "наименование" or "номенклатура" or "штрихкод" or "цена");
    }

    private static string NormalizeImportHeader(string value)
    {
        var normalized = Ui(value).Trim().ToLowerInvariant();
        return new string(normalized.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string Field(
        IReadOnlyList<string> cells,
        IReadOnlyDictionary<string, int> headerMap,
        int fallbackIndex,
        params string[] aliases)
    {
        foreach (var alias in aliases.Select(NormalizeImportHeader))
        {
            if (headerMap.TryGetValue(alias, out var index) && index >= 0 && index < cells.Count)
            {
                return Ui(cells[index]);
            }
        }

        return headerMap.Count == 0 && fallbackIndex >= 0 && fallbackIndex < cells.Count ? Ui(cells[fallbackIndex]) : string.Empty;
    }

    private static bool TryParseImportDecimal(string value, out decimal result)
    {
        value = Ui(value)
            .Replace("₽", string.Empty, StringComparison.Ordinal)
            .Replace('\u00A0', ' ')
            .Replace(" ", string.Empty);

        return decimal.TryParse(
                   value,
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                   RuCulture,
                   out result)
               || decimal.TryParse(
                   value.Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out result);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.Select(Ui).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private void ShowPriceUpdateMessage()
    {
        var products = GetSelectedOrCurrentProducts();
        if (products.Length == 0)
        {
            ShowPlannedAction("Выберите товары для обновления цен.");
            return;
        }

        var dialog = new ProductPriceUpdateWindow(_catalogWorkspace, products)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        _catalogWorkspace.UpsertPriceRegistration(dialog.ResultDocument);
        TryPersistCatalog();
        ApplySection(PriceSetupSection);
    }

    private void ShowPlannedAction(string message)
    {
        MessageBox.Show(Window.GetWindow(this), message, "Товары", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HandleModuleTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string section })
        {
            ApplySection(section);
        }
    }

    private void ApplySection(string section)
    {
        _activeSection = section;

        ProductsContentGrid.Visibility = section == ProductsSection ? Visibility.Visible : Visibility.Collapsed;
        ProductsFilterPanel.Visibility = section == ProductsSection ? Visibility.Visible : Visibility.Collapsed;
        BulkActionsPanel.Visibility = section == ProductsSection && Products.Count(item => item.IsSelected) > 1 ? Visibility.Visible : Visibility.Collapsed;
        SecondaryContentPanel.Visibility = section == ProductsSection ? Visibility.Collapsed : Visibility.Visible;
        SecondaryToolbarPanel.Visibility = section == ProductsSection ? Visibility.Collapsed : Visibility.Visible;

        ApplyTabStyle(ProductsTabButton, section == ProductsSection);
        ApplyTabStyle(PriceTypesTabButton, section == PricesSection);
        ApplyTabStyle(DiscountsTabButton, section == DiscountsSection);
        ApplyTabStyle(PriceSetupTabButton, section == PriceSetupSection);
        ApplyTabStyle(JournalTabButton, section == JournalSection);

        if (section != ProductsSection)
        {
            RefreshSecondarySection(section);
        }
    }

    private static void ApplyTabStyle(WpfButton button, bool active)
    {
        button.Foreground = active ? PrimaryBrush : MutedBrush;
        button.BorderBrush = active ? PrimaryBrush : Brushes.Transparent;
    }

    private void RefreshSecondarySection(string section)
    {
        SecondaryDataGrid.Columns.Clear();
        SecondaryCreateButton.Visibility = section == JournalSection ? Visibility.Collapsed : Visibility.Visible;
        SecondaryEditButton.Visibility = section == JournalSection ? Visibility.Collapsed : Visibility.Visible;
        SecondaryApplyButton.Visibility = section == PriceSetupSection ? Visibility.Visible : Visibility.Collapsed;

        switch (section)
        {
            case PricesSection:
                SecondaryTitleText.Text = "Виды цен";
                SecondaryHintText.Text = "Типы цен, валюта, округление и вид цены по умолчанию.";
                AddTextColumn("Код", "Code", 130);
                AddTextColumn("Название", "Name", 240);
                AddTextColumn("Валюта", "Currency", 90);
                AddTextColumn("Базовый вид", "BaseType", 180);
                AddTextColumn("Округление", "Rounding", 160);
                AddTextColumn("По умолчанию", "Default", 120);
                AddTextColumn("Статус", "Status", 120);
                SecondaryDataGrid.ItemsSource = _catalogWorkspace.PriceTypes
                    .Select(item => new PriceTypeTableRow(Ui(item.Code), Ui(item.Name), Ui(item.CurrencyCode), Ui(item.BasePriceTypeName), Ui(item.RoundingRule), item.IsDefault ? "Да" : "Нет", Ui(item.Status)))
                    .ToArray();
                break;
            case DiscountsSection:
                SecondaryTitleText.Text = "Скидки";
                SecondaryHintText.Text = "Правила скидок и период действия.";
                AddTextColumn("Скидка", "Name", 220);
                AddTextColumn("Процент", "Percent", 100);
                AddTextColumn("Вид цены", "PriceType", 180);
                AddTextColumn("Период", "Period", 190);
                AddTextColumn("Область", "Scope", 180);
                AddTextColumn("Статус", "Status", 120);
                SecondaryDataGrid.ItemsSource = _catalogWorkspace.Discounts
                    .Select(item => new DiscountTableRow(Ui(item.Name), $"{item.Percent:N2}%", Ui(item.PriceTypeName), Ui(item.Period), Ui(item.Scope), Ui(item.Status)))
                    .ToArray();
                break;
            case PriceSetupSection:
                SecondaryTitleText.Text = "Установка цен";
                SecondaryHintText.Text = "Документы изменения цен и история проведения.";
                AddTextColumn("Документ", "Number", 150);
                AddTextColumn("Дата", "Date", 120);
                AddTextColumn("Вид цены", "PriceType", 200);
                AddTextColumn("Позиций", "LinesCount", 100);
                AddTextColumn("Статус", "Status", 120);
                AddTextColumn("Комментарий", "Comment", 320);
                SecondaryDataGrid.ItemsSource = _catalogWorkspace.PriceRegistrations
                    .OrderByDescending(item => item.DocumentDate)
                    .Select(item => new PriceDocumentTableRow(item.Id, Ui(item.Number), item.DocumentDate.ToString("dd.MM.yyyy", RuCulture), Ui(item.PriceTypeName), item.Lines.Count.ToString("N0", RuCulture), Ui(item.Status), Ui(item.Comment)))
                    .ToArray();
                break;
            default:
                SecondaryTitleText.Text = "Журнал";
                SecondaryHintText.Text = "История действий по каталогу, ценам и скидкам.";
                AddTextColumn("Время", "LoggedAt", 150);
                AddTextColumn("Пользователь", "Actor", 160);
                AddTextColumn("Объект", "EntityType", 140);
                AddTextColumn("Номер", "EntityNumber", 150);
                AddTextColumn("Действие", "Action", 220);
                AddTextColumn("Результат", "Result", 120);
                AddTextColumn("Сообщение", "Message", 360);
                SecondaryDataGrid.ItemsSource = _catalogWorkspace.OperationLog
                    .OrderByDescending(item => item.LoggedAt)
                    .Select(item => new JournalTableRow(item.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture), Ui(item.Actor), Ui(item.EntityType), Ui(item.EntityNumber), Ui(item.Action), Ui(item.Result), Ui(item.Message)))
                    .ToArray();
                break;
        }
    }

    private void AddTextColumn(string header, string binding, double width)
    {
        SecondaryDataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new System.Windows.Data.Binding(binding),
            Width = width,
            IsReadOnly = true
        });
    }

    private void HandleSecondaryCreateClick(object sender, RoutedEventArgs e)
    {
        switch (_activeSection)
        {
            case PricesSection:
                CreatePriceType();
                break;
            case DiscountsSection:
                CreateDiscount();
                break;
            case PriceSetupSection:
                ShowPriceUpdateMessage();
                break;
        }
    }

    private void HandleSecondaryEditClick(object sender, RoutedEventArgs e)
    {
        switch (_activeSection)
        {
            case PricesSection:
                EditPriceType();
                break;
            case DiscountsSection:
                EditDiscount();
                break;
            case PriceSetupSection:
                HandleSecondaryApplyClick(sender, e);
                break;
        }
    }

    private void CreatePriceType()
    {
        var name = PromptText(
            "Новый вид цены",
            "Название вида цены.",
            "Розничная цена",
            _catalogWorkspace.PriceTypes.Select(item => item.Name));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var draft = _catalogWorkspace.CreatePriceTypeDraft();
        draft.Name = name;
        draft.Status = "Активна";
        _catalogWorkspace.UpsertPriceType(draft);
        TryPersistCatalog();
        RefreshSecondarySection(PricesSection);
    }

    private void EditPriceType()
    {
        if (SecondaryDataGrid.SelectedItem is not PriceTypeTableRow row)
        {
            ShowPlannedAction("Выберите вид цены.");
            return;
        }

        var record = _catalogWorkspace.PriceTypes.FirstOrDefault(item => Ui(item.Code).Equals(row.Code, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            ShowPlannedAction("Вид цены не найден.");
            return;
        }

        var name = PromptText(
            "Изменить вид цены",
            "Название вида цены.",
            record.Name,
            _catalogWorkspace.PriceTypes.Select(item => item.Name));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var copy = record.Clone();
        copy.Name = name;
        _catalogWorkspace.UpsertPriceType(copy);
        TryPersistCatalog();
        RefreshSecondarySection(PricesSection);
    }

    private void CreateDiscount()
    {
        var name = PromptText(
            "Новая скидка",
            "Название правила скидки.",
            "Новая скидка",
            _catalogWorkspace.Discounts.Select(item => item.Name));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var percentText = PromptText("Процент скидки", "Введите процент скидки.", "5", Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(percentText) || !TryParseImportDecimal(percentText, out var percent))
        {
            return;
        }

        var draft = _catalogWorkspace.CreateDiscountDraft();
        draft.Name = name;
        draft.Percent = percent;
        draft.PriceTypeName = _catalogWorkspace.GetDefaultPriceTypeName();
        draft.Scope = "Все товары";
        _catalogWorkspace.UpsertDiscount(draft);
        TryPersistCatalog();
        RefreshSecondarySection(DiscountsSection);
    }

    private void EditDiscount()
    {
        if (SecondaryDataGrid.SelectedItem is not DiscountTableRow row)
        {
            ShowPlannedAction("Выберите скидку.");
            return;
        }

        var record = _catalogWorkspace.Discounts.FirstOrDefault(item => Ui(item.Name).Equals(row.Name, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            ShowPlannedAction("Запись не найдена.");
            return;
        }

        var name = PromptText(
            "Изменить скидку",
            "Название правила скидки.",
            record.Name,
            _catalogWorkspace.Discounts.Select(item => item.Name));
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var percentText = PromptText(
            "Процент скидки",
            "Введите процент скидки.",
            record.Percent.ToString("N2", RuCulture),
            Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(percentText) || !TryParseImportDecimal(percentText, out var percent))
        {
            return;
        }

        var copy = record.Clone();
        copy.Name = name;
        copy.Percent = percent;
        _catalogWorkspace.UpsertDiscount(copy);
        TryPersistCatalog();
        RefreshSecondarySection(DiscountsSection);
    }

    private void HandleSecondaryApplyClick(object sender, RoutedEventArgs e)
    {
        if (_activeSection != PriceSetupSection || SecondaryDataGrid.SelectedItem is not PriceDocumentTableRow row)
        {
            ShowPlannedAction("Выберите документ установки цен для проведения.");
            return;
        }

        var ok = _catalogWorkspace.ApplyPriceRegistration(row.Id, out var message);
        TryPersistCatalog();
        MessageBox.Show(Window.GetWindow(this), message, "Товары", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void TryPersistCatalog()
    {
        try
        {
            _store.Save(_catalogWorkspace);
            _persistWarningShown = false;
        }
        catch (Exception exception)
        {
            if (!_store.IsRemoteDatabaseRequired || _persistWarningShown)
            {
                return;
            }

            _persistWarningShown = true;
            MessageBox.Show(
                Window.GetWindow(this),
                $"Не удалось сохранить товары в общей базе. Локальное сохранение отключено для серверного режима.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Товары",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed class ProductRowViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        private ProductRowViewModel(
            CatalogItemRecord record,
            Guid id,
            string code,
            string name,
            string unit,
            string category,
            string supplier,
            string warehouse,
            decimal freeQuantity,
            decimal reservedQuantity,
            decimal inTransitQuantity,
            decimal minimumStock,
            decimal price,
            string currency,
            string status,
            string barcode,
            Brush statusForeground,
            Brush statusBackground,
            double freeBarWidth,
            double reservedBarWidth,
            double transitBarWidth,
            string searchText)
        {
            Record = record;
            Id = id;
            Code = code;
            Name = name;
            Unit = unit;
            Category = category;
            Supplier = supplier;
            Warehouse = warehouse;
            FreeQuantity = freeQuantity;
            ReservedQuantity = reservedQuantity;
            InTransitQuantity = inTransitQuantity;
            MinimumStock = minimumStock;
            Price = price;
            Currency = currency;
            Status = status;
            Barcode = barcode;
            StatusForeground = statusForeground;
            StatusBackground = statusBackground;
            FreeBarWidth = freeBarWidth;
            ReservedBarWidth = reservedBarWidth;
            TransitBarWidth = transitBarWidth;
            SearchText = searchText;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public CatalogItemRecord Record { get; }

        public Guid Id { get; }

        public string Code { get; }

        public string Name { get; }

        public string Unit { get; }

        public string UnitDisplay => $"Ед. изм.: {Unit}";

        public string Category { get; }

        public string Supplier { get; }

        public string Warehouse { get; }

        public decimal FreeQuantity { get; }

        public decimal ReservedQuantity { get; }

        public decimal InTransitQuantity { get; }

        public decimal MinimumStock { get; }

        public decimal Deficit => Math.Max(0m, MinimumStock - FreeQuantity);

        public decimal Price { get; }

        public string Currency { get; }

        public string Status { get; }

        public string Barcode { get; }

        public Brush StatusForeground { get; }

        public Brush StatusBackground { get; }

        public double FreeBarWidth { get; }

        public double ReservedBarWidth { get; }

        public double TransitBarWidth { get; }

        public string SearchText { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string FreeDisplay => FreeQuantity.ToString("N0", RuCulture);

        public string ReservedDisplay => ReservedQuantity.ToString("N0", RuCulture);

        public string InTransitDisplay => InTransitQuantity.ToString("N0", RuCulture);

        public string MinimumStockDisplay => $"{MinimumStock:N0} {Unit}";

        public string DeficitDisplay => $"{Deficit:N0} {Unit}";

        public string StockRatio => $"{FreeQuantity:N0} / {ReservedQuantity:N0} / {InTransitQuantity:N0}";

        public string PriceDisplay => Price <= 0m ? "0 ₽" : $"{Price:N0} ₽";

        public bool MissingPrice => Price <= 0m;

        public bool MissingBarcode => string.IsNullOrWhiteSpace(Barcode) || Barcode == "-";

        public bool MissingSupplier => string.IsNullOrWhiteSpace(Supplier) || Supplier == "-";

        public bool LowStock => Deficit > 0m;

        public bool HasProblem => MissingPrice || MissingBarcode || MissingSupplier || LowStock;

        public static ProductRowViewModel Create(CatalogItemRecord item, WarehouseStockBalanceRecord? stock)
        {
            var code = Fallback(Ui(item.Code), "ITEM");
            var name = Fallback(Ui(item.Name), "Без названия");
            var unit = Fallback(Ui(item.Unit), Ui(stock?.Unit), "шт");
            var category = Fallback(Ui(item.Category), "Без категории");
            var supplier = Fallback(Ui(item.Supplier), "-");
            var warehouse = Fallback(Ui(item.DefaultWarehouse), Ui(stock?.Warehouse), "-");
            var free = stock?.FreeQuantity ?? 0m;
            var reserved = stock?.ReservedQuantity ?? 0m;
            var inTransit = stock?.ShippedQuantity ?? 0m;
            var minimum = ResolveMinimumStock(free, reserved, inTransit);
            var status = NormalizeStatus(Ui(item.Status));
            var barcode = ResolveDisplayBarcode(item, code, name, warehouse);
            var (foreground, background) = ResolveStatusBrushes(status);
            var total = Math.Max(1m, free + reserved + inTransit);
            const double barWidth = 82d;
            var searchText = string.Join(
                " ",
                code,
                name,
                category,
                supplier,
                warehouse,
                status,
                barcode);

            return new ProductRowViewModel(
                item,
                item.Id,
                code,
                name,
                unit,
                category,
                supplier,
                warehouse,
                free,
                reserved,
                inTransit,
                minimum,
                item.DefaultPrice,
                Fallback(Ui(item.CurrencyCode), "RUB"),
                status,
                barcode,
                foreground,
                background,
                Scale(free),
                Scale(reserved),
                Scale(inTransit),
                searchText);

            double Scale(decimal value)
            {
                return Math.Max(value <= 0m ? 0d : 5d, (double)(value / total) * barWidth);
            }
        }

        private static decimal ResolveMinimumStock(decimal free, decimal reserved, decimal inTransit)
        {
            var basis = Math.Max(reserved + inTransit, 10m);
            return Math.Max(10m, Math.Round(basis / 10m, 0, MidpointRounding.AwayFromZero) * 10m);
        }

        private static string NormalizeStatus(string status)
        {
            if (status.Contains("архив", StringComparison.OrdinalIgnoreCase))
            {
                return "Архив";
            }

            if (status.Contains("настрой", StringComparison.OrdinalIgnoreCase) || status.Contains("чернов", StringComparison.OrdinalIgnoreCase))
            {
                return "На настройке";
            }

            return "Активный";
        }

        private static (Brush Foreground, Brush Background) ResolveStatusBrushes(string status)
        {
            return status switch
            {
                "Активный" => (SuccessBrush, SuccessSoftBrush),
                "На настройке" => (WarningBrush, WarningSoftBrush),
                "Архив" => (NeutralBrush, NeutralSoftBrush),
                _ => (DangerBrush, DangerSoftBrush)
            };
        }

        private static string Fallback(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "-";
        }
    }

    private sealed record ProductMovementViewModel(string Date, string Title, string Quantity, Brush QuantityBrush);

    private sealed record ProductDocumentViewModel(string Title, string Date, string Status);

    private sealed record PriceTypeTableRow(string Code, string Name, string Currency, string BaseType, string Rounding, string Default, string Status);

    private sealed record DiscountTableRow(string Name, string Percent, string PriceType, string Period, string Scope, string Status);

    private sealed record PriceDocumentTableRow(Guid Id, string Number, string Date, string PriceType, string LinesCount, string Status, string Comment);

    private sealed record JournalTableRow(string LoggedAt, string Actor, string EntityType, string EntityNumber, string Action, string Result, string Message);
}

