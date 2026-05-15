using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesShipmentsWorkspaceView : UserControl
{
    private const string ShipmentsTabKey = "shipments";
    private const string PendingTabKey = "pending";
    private const string AllCustomersFilter = "Все покупатели";
    private const string AllWarehousesFilter = "Все склады";
    private const string AllManagersFilter = "Все ответственные";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly SolidColorBrush GreenBrush = BrushFromHex("#1FA45F");
    private static readonly SolidColorBrush OrangeBrush = BrushFromHex("#FF9F1A");
    private static readonly SolidColorBrush RedBrush = BrushFromHex("#FF5F6D");
    private static readonly SolidColorBrush MutedBrush = BrushFromHex("#7A86A5");
    private static readonly SolidColorBrush BlueBrush = BrushFromHex("#4F5BFF");
    private static readonly SolidColorBrush HollowFill = BrushFromHex("#FFFFFF");

    private readonly SalesWorkspace _salesWorkspace;
    private bool _initializing = true;
    private string _activeTab = ShipmentsTabKey;

    public SalesShipmentsWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        _salesWorkspace.Changed += HandleWorkspaceChanged;
        Unloaded += HandleUnloaded;

        RefreshFilters();
        _initializing = false;
        ApplyTab(ShipmentsTabKey);
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        _salesWorkspace.Changed -= HandleWorkspaceChanged;
        Unloaded -= HandleUnloaded;
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshFilters();
            ApplyFilters();
        }));
    }

    private void RefreshFilters()
    {
        _initializing = true;

        var customers = _salesWorkspace.Shipments.Select(s => Ui(s.CustomerName))
            .Concat(_salesWorkspace.Orders.Select(o => Ui(o.CustomerName)))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase);
        CustomerFilterCombo.ItemsSource = new[] { AllCustomersFilter }.Concat(customers).ToArray();
        CustomerFilterCombo.SelectedIndex = 0;

        var warehouses = _salesWorkspace.Shipments.Select(s => Ui(s.Warehouse))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase);
        WarehouseFilterCombo.ItemsSource = new[] { AllWarehousesFilter }.Concat(warehouses).ToArray();
        WarehouseFilterCombo.SelectedIndex = 0;

        var managers = _salesWorkspace.Shipments.Select(s => Ui(s.Manager))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase);
        ManagerFilterCombo.ItemsSource = new[] { AllManagersFilter }.Concat(managers).ToArray();
        ManagerFilterCombo.SelectedIndex = 0;

        _initializing = false;
    }

    private void HandleSubTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
        {
            ApplyTab(key);
        }
    }

    private void ApplyTab(string tabKey)
    {
        _activeTab = tabKey;
        var isShipments = tabKey == ShipmentsTabKey;
        ShipmentsTabButton.Foreground = isShipments ? BlueBrush : MutedBrush;
        ShipmentsTabButton.BorderBrush = isShipments ? BlueBrush : System.Windows.Media.Brushes.Transparent;
        PendingTabButton.Foreground = !isShipments ? BlueBrush : MutedBrush;
        PendingTabButton.BorderBrush = !isShipments ? BlueBrush : System.Windows.Media.Brushes.Transparent;

        ShipmentsToolbarPanel.Visibility = isShipments ? Visibility.Visible : Visibility.Collapsed;
        PendingToolbarPanel.Visibility = isShipments ? Visibility.Collapsed : Visibility.Visible;
        ShipmentsGrid.Visibility = isShipments ? Visibility.Visible : Visibility.Collapsed;
        PendingGrid.Visibility = isShipments ? Visibility.Collapsed : Visibility.Visible;

        ApplyFilters();
    }

    private void HandleSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilters();
    }

    private void HandleFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ApplyFilters();
    }

    private void HandleResetFiltersClick(object sender, RoutedEventArgs e)
    {
        _initializing = true;
        SearchBox.Clear();
        SearchPlaceholderText.Visibility = Visibility.Visible;
        CustomerFilterCombo.SelectedIndex = 0;
        WarehouseFilterCombo.SelectedIndex = 0;
        PaymentFilterCombo.SelectedIndex = 0;
        ManagerFilterCombo.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        _initializing = false;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!IsInitialized || ShipmentsGrid is null || PendingGrid is null)
        {
            return;
        }

        var query = (SearchBox.Text ?? string.Empty).Trim();

        if (_activeTab == ShipmentsTabKey)
        {
            var customer = Ui(CustomerFilterCombo.SelectedItem as string);
            var warehouse = Ui(WarehouseFilterCombo.SelectedItem as string);
            var manager = Ui(ManagerFilterCombo.SelectedItem as string);
            var paymentFilter = (PaymentFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все";
            var dateFrom = DateFromPicker.SelectedDate;
            var dateTo = DateToPicker.SelectedDate;

            var rows = _salesWorkspace.Shipments
                .Where(s => string.IsNullOrEmpty(customer) || customer == AllCustomersFilter || Ui(s.CustomerName).Equals(customer, StringComparison.OrdinalIgnoreCase))
                .Where(s => string.IsNullOrEmpty(warehouse) || warehouse == AllWarehousesFilter || Ui(s.Warehouse).Equals(warehouse, StringComparison.OrdinalIgnoreCase))
                .Where(s => string.IsNullOrEmpty(manager) || manager == AllManagersFilter || Ui(s.Manager).Equals(manager, StringComparison.OrdinalIgnoreCase))
                .Where(s => !dateFrom.HasValue || s.ShipmentDate.Date >= dateFrom.Value.Date)
                .Where(s => !dateTo.HasValue || s.ShipmentDate.Date <= dateTo.Value.Date)
                .Where(s => MatchesPayment(s, paymentFilter))
                .Where(s => string.IsNullOrEmpty(query) || MatchesQuery(s, query))
                .OrderByDescending(s => s.ShipmentDate)
                .ThenBy(s => Ui(s.Number), StringComparer.OrdinalIgnoreCase)
                .Select(ShipmentRowViewModel.Create)
                .ToArray();

            ShipmentsGrid.ItemsSource = rows;
            RecordsCountText.Text = $"Всего: {rows.Length:N0}";
            if (rows.Length > 0) ShipmentsGrid.SelectedIndex = 0;
            else UpdateSummary(null, null);
        }
        else
        {
            // Заказы, которые нужно отгрузить — все заказы, где нет связанной отгрузки
            var shippedOrderIds = _salesWorkspace.Shipments
                .Where(s => s.SalesOrderId != Guid.Empty)
                .Select(s => s.SalesOrderId)
                .ToHashSet();

            var pendingRows = _salesWorkspace.Orders
                .Where(o => !shippedOrderIds.Contains(o.Id))
                .Where(o => string.IsNullOrEmpty(query) || MatchesOrderQuery(o, query))
                .OrderByDescending(o => o.OrderDate)
                .Select(PendingOrderRowViewModel.Create)
                .ToArray();

            PendingGrid.ItemsSource = pendingRows;
            RecordsCountText.Text = $"К отгрузке: {pendingRows.Length:N0}";
            if (pendingRows.Length > 0) PendingGrid.SelectedIndex = 0;
            else UpdateSummary(null, null);
        }
    }

    private static bool MatchesPayment(SalesShipmentRecord shipment, string filter)
    {
        var paid = shipment.Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                   || shipment.Status.Contains("заверш", StringComparison.OrdinalIgnoreCase);
        return filter switch
        {
            "Оплачено" => paid,
            "Не оплачено" => !paid,
            _ => true,
        };
    }

    private static bool MatchesQuery(SalesShipmentRecord shipment, string query)
    {
        var haystack = string.Join(" ",
            Ui(shipment.Number),
            Ui(shipment.CustomerName),
            Ui(shipment.Status),
            Ui(shipment.Manager),
            Ui(shipment.Warehouse),
            Ui(shipment.Comment));
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOrderQuery(SalesOrderRecord order, string query)
    {
        var haystack = string.Join(" ",
            Ui(order.Number),
            Ui(order.CustomerName),
            Ui(order.Status));
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleShipmentSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShipmentsGrid.SelectedItem is ShipmentRowViewModel row)
        {
            UpdateSummary(row.Record, null);
        }
    }

    private void HandlePendingSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PendingGrid.SelectedItem is PendingOrderRowViewModel row)
        {
            UpdateSummary(null, row.Record);
        }
    }

    private void UpdateSummary(SalesShipmentRecord? shipment, SalesOrderRecord? order)
    {
        if (shipment is not null)
        {
            SelectedSummary.Visibility = Visibility.Visible;
            SelectedSummaryText.Text = $"{Ui(shipment.Number)} · {shipment.Lines.Count:N0} поз. · {shipment.TotalAmount.ToString("N0", RuCulture)} ₽";
        }
        else if (order is not null)
        {
            SelectedSummary.Visibility = Visibility.Visible;
            SelectedSummaryText.Text = $"{Ui(order.Number)} · {order.Lines.Count:N0} поз. · {order.TotalAmount.ToString("N0", RuCulture)} ₽";
        }
        else
        {
            SelectedSummary.Visibility = Visibility.Collapsed;
        }
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        RecordsWorkspaceCatalog.OpenShipmentEditorTabOrDialog(_salesWorkspace, null);
    }

    private void HandleCreateShipmentFromOrderClick(object sender, RoutedEventArgs e)
    {
        if (PendingGrid.SelectedItem is PendingOrderRowViewModel row)
        {
            // Создаём новую отгрузку, ассоциированную с заказом
            var template = new SalesShipmentRecord
            {
                Id = Guid.NewGuid(),
                SalesOrderId = row.Record.Id,
                SalesOrderNumber = row.Record.Number,
                CustomerId = row.Record.CustomerId,
                CustomerCode = row.Record.CustomerCode,
                CustomerName = row.Record.CustomerName,
                ContractNumber = row.Record.ContractNumber,
                CurrencyCode = row.Record.CurrencyCode,
                Warehouse = row.Record.Warehouse,
                Manager = row.Record.Manager,
                Organization = row.Record.Organization,
                ShipmentDate = DateTime.Today,
            };
            foreach (var line in row.Record.Lines)
            {
                template.Lines.Add(line);
            }
            RecordsWorkspaceCatalog.OpenShipmentEditorTabOrDialog(_salesWorkspace, template);
        }
        else
        {
            ShowInfo("Оформление накладной", "Выберите заказ покупателя в списке.");
        }
    }

    private void HandleRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ShipmentRowViewModel row })
        {
            ShipmentsGrid.SelectedItem = row;
            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem("Открыть накладную", () => RecordsWorkspaceCatalog.OpenShipmentEditorTabOrDialog(_salesWorkspace, row.Record)));
            menu.IsOpen = true;
        }
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (ShipmentsGrid.SelectedItem is ShipmentRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenShipmentEditorTabOrDialog(_salesWorkspace, row.Record);
        }
    }

    private void HandleShipmentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is ShipmentRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenShipmentEditorTabOrDialog(_salesWorkspace, row.Record);
            e.Handled = true;
        }
    }

    private void HandlePendingDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is PendingOrderRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenOrderEditorTabOrDialog(_salesWorkspace, row.Record);
            e.Handled = true;
        }
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void HandleFilterPopupToggle(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = !FilterPopup.IsOpen;
    private void HandleFilterPopupClose(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = false;

    private void HandlePrintClick(object sender, RoutedEventArgs e) => ShowInfo("Печать", "Выберите накладную и используйте печать из карточки.");
    private void HandleGenerateClick(object sender, RoutedEventArgs e) => ShowInfo("Сформировать", "Меню «Сформировать» появится в одном из ближайших релизов.");
    private void HandleStructureClick(object sender, RoutedEventArgs e) => ShowInfo("Структура подчинённости", "Связанные документы доступны внутри карточки.");
    private void HandleMoreClick(object sender, RoutedEventArgs e) => ShowInfo("Дополнительные действия", "Дополнительные действия появятся позже.");

    private void ShowInfo(string title, string message)
    {
        var owner = Window.GetWindow(this);
        if (owner is not null)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match) return match;
            try
            {
                current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }
        return null;
    }

    public sealed class ShipmentRowViewModel
    {
        private ShipmentRowViewModel(SalesShipmentRecord record) { Record = record; }
        public SalesShipmentRecord Record { get; }
        public string Number => Ui(Record.Number);
        public string CustomerName => Ui(Record.CustomerName);
        public string DateDisplay => Record.ShipmentDate.ToString("dd.MM.yyyy", RuCulture);
        public string TotalDisplay => Record.TotalAmount.ToString("N2", RuCulture);
        public string Warehouse => Ui(Record.Warehouse);
        public string Operation => "Продажа покупателю";

        public Brush PaymentDotBrush => Record.Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                                        || Record.Status.Contains("заверш", StringComparison.OrdinalIgnoreCase)
            ? GreenBrush : RedBrush;
        public Brush PaymentDotFill => Record.Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                                       || Record.Status.Contains("заверш", StringComparison.OrdinalIgnoreCase)
            ? GreenBrush : HollowFill;
        public string PaymentTooltip => Record.Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                                        || Record.Status.Contains("заверш", StringComparison.OrdinalIgnoreCase)
            ? "Оплачено" : "Не оплачено";

        public static ShipmentRowViewModel Create(SalesShipmentRecord record) => new(record);
    }

    public sealed class PendingOrderRowViewModel
    {
        private PendingOrderRowViewModel(SalesOrderRecord record) { Record = record; }
        public SalesOrderRecord Record { get; }
        public string Number => Ui(Record.Number);
        public string CustomerName => Ui(Record.CustomerName);
        public string DateDisplay => Record.OrderDate.ToString("dd.MM.yyyy", RuCulture);
        public string TotalDisplay => Record.TotalAmount.ToString("N2", RuCulture);
        public string StatusDisplay => Record.Status.Contains("работ", StringComparison.OrdinalIgnoreCase) ? "В работе" : "Не обработан";
        public Brush StatusBrush => Record.Status.Contains("работ", StringComparison.OrdinalIgnoreCase) ? BlueBrush : MutedBrush;

        public static PendingOrderRowViewModel Create(SalesOrderRecord record) => new(record);
    }
}
