using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesReturnsWorkspaceView : UserControl
{
    private const string AllCustomersFilter = "Все покупатели";
    private const string AllWarehousesFilter = "Все склады";
    private const string AllManagersFilter = "Все ответственные";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly SalesWorkspace _salesWorkspace;
    private bool _initializing = true;

    public SalesReturnsWorkspaceView(SalesWorkspace salesWorkspace)
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

        CustomerFilterCombo.ItemsSource = new[] { AllCustomersFilter }
            .Concat(_salesWorkspace.Returns.Select(r => Ui(r.CustomerName))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        CustomerFilterCombo.SelectedIndex = 0;

        WarehouseFilterCombo.ItemsSource = new[] { AllWarehousesFilter }
            .Concat(_salesWorkspace.Returns.Select(r => Ui(r.Warehouse))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        WarehouseFilterCombo.SelectedIndex = 0;

        ManagerFilterCombo.ItemsSource = new[] { AllManagersFilter }
            .Concat(_salesWorkspace.Returns.Select(r => Ui(r.Manager))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        ManagerFilterCombo.SelectedIndex = 0;

        // Release 1.0.116: те же значения в боковой панели.
        SidebarCustomerCombo.ItemsSource = CustomerFilterCombo.ItemsSource;
        SidebarCustomerCombo.SelectedIndex = 0;
        SidebarWarehouseCombo.ItemsSource = WarehouseFilterCombo.ItemsSource;
        SidebarWarehouseCombo.SelectedIndex = 0;
        SidebarManagerCombo.ItemsSource = ManagerFilterCombo.ItemsSource;
        SidebarManagerCombo.SelectedIndex = 0;
        SidebarOperationCombo.ItemsSource = new[] { "Все операции", "Возврат от покупателя" };
        SidebarOperationCombo.SelectedIndex = 0;
        SidebarOrganizationCombo.ItemsSource = new[] { "Все организации" };
        SidebarOrganizationCombo.SelectedIndex = 0;
        SidebarOriginalStatusCombo.ItemsSource = new[] { "Все", "Получен", "Не получен", "Неизвестно" };
        SidebarOriginalStatusCombo.SelectedIndex = 0;

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
        CustomerFilterCombo.SelectedIndex = 0;
        WarehouseFilterCombo.SelectedIndex = 0;
        ManagerFilterCombo.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        _initializing = false;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!IsInitialized || ReturnsGrid is null)
        {
            return;
        }

        var query = (SearchBox.Text ?? string.Empty).Trim();
        var customer = Ui(CustomerFilterCombo.SelectedItem as string);
        var warehouse = Ui(WarehouseFilterCombo.SelectedItem as string);
        var manager = Ui(ManagerFilterCombo.SelectedItem as string);
        var dateFrom = DateFromPicker.SelectedDate;
        var dateTo = DateToPicker.SelectedDate;

        // Release 1.0.133: cap 100.
        const int displayCap = 100;
        var matched = _salesWorkspace.Returns
            .Where(r => string.IsNullOrEmpty(customer) || customer == AllCustomersFilter || Ui(r.CustomerName).Equals(customer, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(warehouse) || warehouse == AllWarehousesFilter || Ui(r.Warehouse).Equals(warehouse, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrEmpty(manager) || manager == AllManagersFilter || Ui(r.Manager).Equals(manager, StringComparison.OrdinalIgnoreCase))
            .Where(r => !dateFrom.HasValue || r.ReturnDate.Date >= dateFrom.Value.Date)
            .Where(r => !dateTo.HasValue || r.ReturnDate.Date <= dateTo.Value.Date)
            .Where(r => string.IsNullOrEmpty(query) || MatchesQuery(r, query));
        var totalMatched = matched.Count();
        var rows = matched
            .OrderByDescending(r => r.ReturnDate)
            .ThenBy(r => Ui(r.Number), StringComparer.OrdinalIgnoreCase)
            .Take(displayCap)
            .Select(ReturnRowViewModel.Create)
            .ToArray();

        ReturnsGrid.ItemsSource = rows;
        ReturnsCountText.Text = totalMatched > displayCap
            ? $"Показано {rows.Length:N0} из {totalMatched:N0} — уточните фильтр"
            : $"Всего: {rows.Length:N0}";

        if (rows.Length > 0)
        {
            ReturnsGrid.SelectedIndex = 0;
        }
        else
        {
            UpdateSummary(null);
        }
    }

    private static bool MatchesQuery(SalesReturnRecord r, string query)
    {
        var haystack = string.Join(" ",
            Ui(r.Number),
            Ui(r.CustomerName),
            Ui(r.Status),
            Ui(r.Manager),
            Ui(r.Warehouse),
            Ui(r.Reason),
            Ui(r.Comment));
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = ReturnsGrid.SelectedItem as ReturnRowViewModel;
        UpdateSummary(row?.Record);
        // Release 1.0.116: правая боковая панель показывает контактную информацию
        // выделенного контрагента — обновляем её на каждое изменение выделения.
        RefreshContactPanel(row?.Record);
    }

    // Release 1.0.116: ищем контрагента по имени из возврата и заполняем правую панель.
    // Если контрагент не найден или поле пустое — соответствующая строка скрывается.
    private void RefreshContactPanel(SalesReturnRecord? record)
    {
        if (record is null)
        {
            ShowContactEmpty("Выделите строку — здесь появится контактная информация контрагента.");
            return;
        }

        var customerName = Ui(record.CustomerName);
        if (string.IsNullOrWhiteSpace(customerName))
        {
            ShowContactEmpty("У документа не указан контрагент.");
            return;
        }

        var customer = _salesWorkspace.Customers.FirstOrDefault(
            c => string.Equals(Ui(c.Name), customerName, StringComparison.OrdinalIgnoreCase));

        if (customer is null)
        {
            ShowContactEmpty($"Контрагент «{customerName}» не найден в справочнике покупателей.");
            return;
        }

        ContactEmptyHintText.Visibility = Visibility.Collapsed;

        SetContactRow(ContactPhoneRow, ContactPhoneText, Ui(customer.Phone));
        SetContactRow(ContactEmailRow, ContactEmailText, Ui(customer.Email));
        SetContactStack(ContactLegalAddressRow, ContactLegalAddressText, Ui(customer.LegalAddress));
        SetContactStack(ContactActualAddressRow, ContactActualAddressText, Ui(customer.ActualAddress));

        // ФИО — берём первый контакт-физлицо, если такой есть в справочнике.
        var contactName = customer.Contacts?.FirstOrDefault();
        if (contactName is not null && !string.IsNullOrWhiteSpace(contactName.Name))
        {
            ContactPersonText.Text = Ui(contactName.Name);
            ContactPersonText.Visibility = Visibility.Visible;
        }
        else
        {
            ContactPersonText.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowContactEmpty(string hint)
    {
        ContactEmptyHintText.Text = hint;
        ContactEmptyHintText.Visibility = Visibility.Visible;
        ContactPhoneRow.Visibility = Visibility.Collapsed;
        ContactEmailRow.Visibility = Visibility.Collapsed;
        ContactLegalAddressRow.Visibility = Visibility.Collapsed;
        ContactActualAddressRow.Visibility = Visibility.Collapsed;
        ContactPersonText.Visibility = Visibility.Collapsed;
    }

    private static void SetContactRow(Grid container, TextBlock target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            container.Visibility = Visibility.Collapsed;
            return;
        }

        target.Text = value;
        container.Visibility = Visibility.Visible;
    }

    private static void SetContactStack(StackPanel container, TextBlock target, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            container.Visibility = Visibility.Collapsed;
            return;
        }

        target.Text = value;
        container.Visibility = Visibility.Visible;
    }

    // === Sidebar filters (Release 1.0.116) ===
    // Боковые ComboBox'ы дублируют Popup-фильтры — синхронизируем выбор: при изменении
    // sidebar-комбо проксируем значение в существующие Popup-комбо и переисчисляем фильтр.
    private void HandleSidebarFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _initializing = true;
        try
        {
            CustomerFilterCombo.SelectedItem = SidebarCustomerCombo.SelectedItem;
            WarehouseFilterCombo.SelectedItem = SidebarWarehouseCombo.SelectedItem;
            ManagerFilterCombo.SelectedItem = SidebarManagerCombo.SelectedItem;
        }
        finally
        {
            _initializing = false;
        }

        ApplyFilters();
    }

    private void HandleSidebarPeriodClick(object sender, RoutedEventArgs e)
    {
        // Заглушка: в 1С эта ссылка открывает выбор периода (день/неделя/месяц/квартал/год).
        // Сейчас просто фокусируем DateFromPicker в popup-фильтре.
        DateFromPicker.Focus();
    }

    private void HandleSidebarMoreFiltersClick(object sender, RoutedEventArgs e)
    {
        // «+ фильтры» — открывает старый popup, чтобы добраться до DateFrom/DateTo и Reset.
        FilterPopup.IsOpen = true;
    }

    private void UpdateSummary(SalesReturnRecord? r)
    {
        if (r is null)
        {
            SelectedSummary.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedSummary.Visibility = Visibility.Visible;
        SelectedSummaryText.Text = $"{Ui(r.Number)} · {r.Lines.Count:N0} поз. · {r.TotalAmount.ToString("N0", RuCulture)} ₽";
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        RecordsWorkspaceCatalog.OpenReturnEditorDialog(_salesWorkspace, null);
    }

    private void HandleRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ReturnRowViewModel row })
        {
            ReturnsGrid.SelectedItem = row;
            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem("Открыть возврат", () => RecordsWorkspaceCatalog.OpenReturnEditorDialog(_salesWorkspace, row.Record)));
            menu.Items.Add(CreateMenuItem("Печать возврата", () => RecordsWorkspaceCatalog.PrintReturn(row.Record)));
            menu.IsOpen = true;
        }
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (ReturnsGrid.SelectedItem is ReturnRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenReturnEditorDialog(_salesWorkspace, row.Record);
        }
    }

    private void HandleGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is ReturnRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenReturnEditorDialog(_salesWorkspace, row.Record);
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

    private void HandlePrintClick(object sender, RoutedEventArgs e)
    {
        if (ReturnsGrid.SelectedItem is ReturnRowViewModel row)
        {
            RecordsWorkspaceCatalog.PrintReturn(row.Record);
        }
        else
        {
            ShowInfo("Печать", "Выберите возврат для печати.");
        }
    }

    private void HandleGenerateClick(object sender, RoutedEventArgs e) => ShowInfo("Сформировать", "Меню «Сформировать» появится в одном из ближайших релизов.");
    private void HandleStructureClick(object sender, RoutedEventArgs e) => ShowInfo("Структура подчинённости", "Связанные документы доступны внутри карточки возврата.");
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

    public sealed class ReturnRowViewModel
    {
        private ReturnRowViewModel(SalesReturnRecord record) { Record = record; }
        public SalesReturnRecord Record { get; }
        public string Number => Ui(Record.Number);
        public string CustomerName => Ui(Record.CustomerName);
        public string DateDisplay => Record.ReturnDate.ToString("dd.MM.yyyy", RuCulture);
        public string TotalDisplay => Record.TotalAmount.ToString("N2", RuCulture);
        public string Warehouse => Ui(Record.Warehouse);
        public string Operation => "Возврат от покупателя";

        public static ReturnRowViewModel Create(SalesReturnRecord record) => new(record);
    }
}
