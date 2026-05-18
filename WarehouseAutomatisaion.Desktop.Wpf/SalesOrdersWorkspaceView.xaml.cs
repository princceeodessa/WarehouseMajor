using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesOrdersWorkspaceView : UserControl
{
    private const string AllStatusesFilter = "Все состояния";
    private const string AllCustomersFilter = "Все покупатели";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly SolidColorBrush GreenBrush = BrushFromHex("#1FA45F");
    private static readonly SolidColorBrush OrangeBrush = BrushFromHex("#FF9F1A");
    private static readonly SolidColorBrush RedBrush = BrushFromHex("#FF5F6D");
    private static readonly SolidColorBrush MutedBrush = BrushFromHex("#7A86A5");
    private static readonly SolidColorBrush BlueBrush = BrushFromHex("#4F5BFF");
    private static readonly SolidColorBrush GreenSoftFill = BrushFromHex("#1FA45F");
    private static readonly SolidColorBrush HollowFill = BrushFromHex("#FFFFFF");

    private readonly SalesWorkspace _salesWorkspace;
    private bool _initializing = true;

    public SalesOrdersWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        _salesWorkspace.Changed += HandleWorkspaceChanged;
        Unloaded += HandleUnloaded;

        RefreshFilters();
        _initializing = false;
        ApplyFilters();
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

        StatusFilterCombo.ItemsSource = new[] { AllStatusesFilter }
            .Concat(_salesWorkspace.OrderStatuses.Select(Ui).Where(s => !string.IsNullOrWhiteSpace(s)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        StatusFilterCombo.SelectedIndex = 0;

        CustomerFilterCombo.ItemsSource = new[] { AllCustomersFilter }
            .Concat(_salesWorkspace.Orders.Select(o => Ui(o.CustomerName))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        CustomerFilterCombo.SelectedIndex = 0;

        _initializing = false;
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
        StatusFilterCombo.SelectedIndex = 0;
        CustomerFilterCombo.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        _initializing = false;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!IsInitialized || OrdersGrid is null)
        {
            return;
        }

        var query = (SearchBox.Text ?? string.Empty).Trim();
        var status = Ui(StatusFilterCombo.SelectedItem as string);
        var customer = Ui(CustomerFilterCombo.SelectedItem as string);
        var dateFrom = DateFromPicker.SelectedDate;
        var dateTo = DateToPicker.SelectedDate;

        // Release 1.0.133: жёсткий cap 100 строк в гриде. Если match больше —
        // подскажем «отфильтруй поиском/датой». UI остаётся отзывчивым даже
        // при 20k заказах в workspace.
        const int displayCap = 100;
        var matchedQuery = _salesWorkspace.Orders
            .Where(o => string.IsNullOrEmpty(status) || status == AllStatusesFilter || Ui(o.Status).Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(o => string.IsNullOrEmpty(customer) || customer == AllCustomersFilter || Ui(o.CustomerName).Equals(customer, StringComparison.OrdinalIgnoreCase))
            .Where(o => !dateFrom.HasValue || o.OrderDate.Date >= dateFrom.Value.Date)
            .Where(o => !dateTo.HasValue || o.OrderDate.Date <= dateTo.Value.Date)
            .Where(o => string.IsNullOrEmpty(query) || MatchesQuery(o, query));
        var totalMatched = matchedQuery.Count();
        var rows = matchedQuery
            .OrderByDescending(o => o.OrderDate)
            .ThenBy(o => Ui(o.Number), StringComparer.OrdinalIgnoreCase)
            .Take(displayCap)
            .Select(OrderRowViewModel.Create)
            .ToArray();

        OrdersGrid.ItemsSource = rows;
        OrdersCountText.Text = totalMatched > displayCap
            ? $"Показано {rows.Length:N0} из {totalMatched:N0} — уточните фильтр (дата/контрагент/поиск)"
            : $"Всего: {rows.Length:N0}";

        if (rows.Length > 0)
        {
            OrdersGrid.SelectedIndex = 0;
        }
        else
        {
            UpdateSummary(null);
        }
    }

    private static bool MatchesQuery(SalesOrderRecord order, string query)
    {
        var haystack = string.Join(" ",
            Ui(order.Number),
            Ui(order.CustomerName),
            Ui(order.Status),
            Ui(order.Manager),
            Ui(order.Comment));
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = OrdersGrid.SelectedItem as OrderRowViewModel;
        UpdateSummary(row?.Record);
    }

    private void UpdateSummary(SalesOrderRecord? order)
    {
        if (order is null)
        {
            SelectedSummary.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedSummary.Visibility = Visibility.Visible;
        var lines = order.Lines.Count;
        SelectedSummaryText.Text = $"{Ui(order.Number)} · {lines:N0} поз. · {order.TotalAmount.ToString("N0", RuCulture)} ₽";
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        RecordsWorkspaceCatalog.OpenOrderEditorTabOrDialog(_salesWorkspace, null);
    }

    private void HandleRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: OrderRowViewModel row })
        {
            OrdersGrid.SelectedItem = row;
            ShowContextMenu(row);
        }
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is OrderRowViewModel row)
        {
            ShowContextMenu(row);
        }
    }

    private void HandleGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is OrderRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenOrderEditorTabOrDialog(_salesWorkspace, row.Record);
            e.Handled = true;
        }
    }

    private void ShowContextMenu(OrderRowViewModel row)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Открыть заказ", () => RecordsWorkspaceCatalog.OpenOrderEditorTabOrDialog(_salesWorkspace, row.Record)));
        menu.Items.Add(CreateMenuItem("Печать заказа", () => RecordsWorkspaceCatalog.PrintOrderCustomer(row.Record)));
        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void HandleFilterPopupToggle(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = !FilterPopup.IsOpen;
    private void HandleFilterPopupClose(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = false;

    private void HandlePrintClick(object sender, RoutedEventArgs e)
    {
        if (OrdersGrid.SelectedItem is OrderRowViewModel row)
        {
            RecordsWorkspaceCatalog.PrintOrderCustomer(row.Record);
        }
        else
        {
            ShowInfo("Печать", "Выберите заказ для печати.");
        }
    }

    private void HandleCreateBasedOnClick(object sender, RoutedEventArgs e) => ShowInfo("Создать на основании", "Меню «Создать на основании» появится в одном из ближайших релизов.");
    private void HandleStructureClick(object sender, RoutedEventArgs e) => ShowInfo("Структура подчинённости", "Связанные документы доступны внутри карточки заказа.");
    private void HandleEdoClick(object sender, RoutedEventArgs e) => ShowInfo("ЭДО", "Интеграция электронного документооборота в разработке.");
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

    public sealed class OrderRowViewModel
    {
        private OrderRowViewModel(SalesOrderRecord record)
        {
            Record = record;
        }

        public SalesOrderRecord Record { get; }

        public string Number => Ui(Record.Number);
        public string Status => Ui(Record.Status);
        public string CustomerName => Ui(Record.CustomerName);
        public string OrderDateDisplay => Record.OrderDate.ToString("dd.MM.yyyy", RuCulture);
        public string ShipmentDateDisplay => Record.OrderDate.ToString("dd.MM.yyyy", RuCulture);
        public string TotalDisplay => Record.TotalAmount.ToString("N2", RuCulture);
        public string OriginalStatus => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? "Форма напечатана" : "<Неизвестно>";

        public Brush StatusBrush => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? MutedBrush
            : Status.Contains("обработ", StringComparison.OrdinalIgnoreCase) ? GreenBrush
            : OrangeBrush;

        public Brush OriginalStatusBrush => OriginalStatus.StartsWith("Форма") ? BlueBrush : MutedBrush;

        // Зелёная заливка если статус «Завершен» (отгружено), иначе пустая (контур цветной по приоритету)
        public Brush ShipmentDotBrush => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? GreenBrush
            : Status.Contains("обработ", StringComparison.OrdinalIgnoreCase) ? GreenBrush
            : RedBrush;

        public Brush ShipmentDotFill => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? GreenSoftFill : HollowFill;

        public string ShipmentTooltip => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? "Отгружено" : "Не отгружено";

        // Оплата — упрощённо: считаем «оплачено» если статус Завершен, частично если Обработан
        public Brush PaymentDotBrush => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? GreenBrush : GreenBrush;
        public Brush PaymentDotFill => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? GreenSoftFill : HollowFill;
        public string PaymentTooltip => Status.Contains("заверш", StringComparison.OrdinalIgnoreCase) ? "Оплачено" : "Ожидает оплаты";

        public Brush EdoBrush => Record.Number.Contains("НФ", StringComparison.OrdinalIgnoreCase) ? BlueBrush : MutedBrush;
        public string EdoTooltip => Record.Number.Contains("НФ", StringComparison.OrdinalIgnoreCase) ? "ЭДО подключен" : "ЭДО не подключен";

        public static OrderRowViewModel Create(SalesOrderRecord record) => new(record);
    }
}
