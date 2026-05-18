using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesInvoicesWorkspaceView : UserControl
{
    private const string AllCustomersFilter = "Все покупатели";
    private const string AllManagersFilter = "Все ответственные";
    private const string AllOrganizationsFilter = "Все организации";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly SolidColorBrush GreenBrush = BrushFromHex("#1FA45F");
    private static readonly SolidColorBrush OrangeBrush = BrushFromHex("#FF9F1A");
    private static readonly SolidColorBrush RedBrush = BrushFromHex("#FF5F6D");
    private static readonly SolidColorBrush MutedBrush = BrushFromHex("#7A86A5");
    private static readonly SolidColorBrush BlueBrush = BrushFromHex("#4F5BFF");
    private static readonly SolidColorBrush HollowFill = BrushFromHex("#FFFFFF");

    private readonly SalesWorkspace _salesWorkspace;
    private bool _initializing = true;

    public SalesInvoicesWorkspaceView(SalesWorkspace salesWorkspace)
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
            .Concat(_salesWorkspace.Invoices.Select(i => Ui(i.CustomerName))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        CustomerFilterCombo.SelectedIndex = 0;

        ManagerFilterCombo.ItemsSource = new[] { AllManagersFilter }
            .Concat(_salesWorkspace.Invoices.Select(i => Ui(i.Manager))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        ManagerFilterCombo.SelectedIndex = 0;

        OrganizationFilterCombo.ItemsSource = new[] { AllOrganizationsFilter }
            .Concat(_salesWorkspace.Invoices.Select(i => Ui(i.Organization))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        OrganizationFilterCombo.SelectedIndex = 0;

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
        PaymentFilterCombo.SelectedIndex = 0;
        ManagerFilterCombo.SelectedIndex = 0;
        OrganizationFilterCombo.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        _initializing = false;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!IsInitialized || InvoicesGrid is null)
        {
            return;
        }

        var query = (SearchBox.Text ?? string.Empty).Trim();
        var customer = Ui(CustomerFilterCombo.SelectedItem as string);
        var manager = Ui(ManagerFilterCombo.SelectedItem as string);
        var organization = Ui(OrganizationFilterCombo.SelectedItem as string);
        var paymentFilter = (PaymentFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все";
        var dateFrom = DateFromPicker.SelectedDate;
        var dateTo = DateToPicker.SelectedDate;

        // Release 1.0.133: cap 100 строк, иначе грузим весь поток в UI.
        const int displayCap = 100;
        var matched = _salesWorkspace.Invoices
            .Where(i => string.IsNullOrEmpty(customer) || customer == AllCustomersFilter || Ui(i.CustomerName).Equals(customer, StringComparison.OrdinalIgnoreCase))
            .Where(i => string.IsNullOrEmpty(manager) || manager == AllManagersFilter || Ui(i.Manager).Equals(manager, StringComparison.OrdinalIgnoreCase))
            .Where(i => string.IsNullOrEmpty(organization) || organization == AllOrganizationsFilter || Ui(i.Organization).Equals(organization, StringComparison.OrdinalIgnoreCase))
            .Where(i => !dateFrom.HasValue || i.InvoiceDate.Date >= dateFrom.Value.Date)
            .Where(i => !dateTo.HasValue || i.InvoiceDate.Date <= dateTo.Value.Date)
            .Where(i => MatchesPayment(i, paymentFilter))
            .Where(i => string.IsNullOrEmpty(query) || MatchesQuery(i, query));
        var totalMatched = matched.Count();
        var rows = matched
            .OrderByDescending(i => i.InvoiceDate)
            .ThenBy(i => Ui(i.Number), StringComparer.OrdinalIgnoreCase)
            .Take(displayCap)
            .Select(InvoiceRowViewModel.Create)
            .ToArray();

        InvoicesGrid.ItemsSource = rows;
        InvoicesCountText.Text = totalMatched > displayCap
            ? $"Показано {rows.Length:N0} из {totalMatched:N0} — уточните фильтр"
            : $"Всего: {rows.Length:N0}";

        if (rows.Length > 0)
        {
            InvoicesGrid.SelectedIndex = 0;
        }
        else
        {
            UpdateSummary(null);
        }
    }

    private static bool MatchesPayment(SalesInvoiceRecord invoice, string filter)
    {
        var paid = invoice.Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                   || invoice.Status.Contains("заверш", StringComparison.OrdinalIgnoreCase);
        return filter switch
        {
            "Оплачено" => paid,
            "Не оплачено" => !paid,
            "Частично" => invoice.Status.Contains("частич", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }

    private static bool MatchesQuery(SalesInvoiceRecord invoice, string query)
    {
        var haystack = string.Join(" ",
            Ui(invoice.Number),
            Ui(invoice.CustomerName),
            Ui(invoice.Status),
            Ui(invoice.Manager),
            Ui(invoice.Organization),
            Ui(invoice.Comment));
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = InvoicesGrid.SelectedItem as InvoiceRowViewModel;
        UpdateSummary(row?.Record);
    }

    private void UpdateSummary(SalesInvoiceRecord? invoice)
    {
        if (invoice is null)
        {
            SelectedSummary.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedSummary.Visibility = Visibility.Visible;
        var lines = invoice.Lines.Count;
        SelectedSummaryText.Text = $"{Ui(invoice.Number)} · {lines:N0} поз. · {invoice.TotalAmount.ToString("N0", RuCulture)} ₽";
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        RecordsWorkspaceCatalog.OpenInvoiceEditorTabOrDialog(_salesWorkspace, null);
    }

    private void HandleRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: InvoiceRowViewModel row })
        {
            InvoicesGrid.SelectedItem = row;
            ShowContextMenu(row);
        }
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (InvoicesGrid.SelectedItem is InvoiceRowViewModel row)
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

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is InvoiceRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenInvoiceEditorTabOrDialog(_salesWorkspace, row.Record);
            e.Handled = true;
        }
    }

    private void ShowContextMenu(InvoiceRowViewModel row)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Открыть счёт", () => RecordsWorkspaceCatalog.OpenInvoiceEditorTabOrDialog(_salesWorkspace, row.Record)));
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

    private void HandlePrintClick(object sender, RoutedEventArgs e) => ShowInfo("Печать", "Выберите счёт и используйте печать из карточки.");
    private void HandleGenerateClick(object sender, RoutedEventArgs e) => ShowInfo("Сформировать", "Меню «Сформировать» появится в одном из ближайших релизов.");
    private void HandleStructureClick(object sender, RoutedEventArgs e) => ShowInfo("Структура подчинённости", "Связанные документы доступны внутри карточки счёта.");
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

    public sealed class InvoiceRowViewModel
    {
        private InvoiceRowViewModel(SalesInvoiceRecord record)
        {
            Record = record;
        }

        public SalesInvoiceRecord Record { get; }

        public string Number => Ui(Record.Number);
        public string CustomerName => Ui(Record.CustomerName);
        public string DateDisplay => Record.InvoiceDate.ToString("dd.MM.yyyy", RuCulture);
        public string TotalDisplay => Record.TotalAmount.ToString("N2", RuCulture);
        public string Status => Ui(Record.Status);
        public string OriginalStatus => Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                                        || Status.Contains("заверш", StringComparison.OrdinalIgnoreCase)
            ? "Форма напечатана"
            : "<Неизвестно>";

        public Brush OriginalStatusBrush => OriginalStatus.StartsWith("Форма") ? BlueBrush : MutedBrush;

        public Brush PaymentDotBrush => Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                                        || Status.Contains("заверш", StringComparison.OrdinalIgnoreCase)
            ? GreenBrush
            : Status.Contains("частич", StringComparison.OrdinalIgnoreCase) ? OrangeBrush : RedBrush;

        public Brush PaymentDotFill => Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                                       || Status.Contains("заверш", StringComparison.OrdinalIgnoreCase)
            ? GreenBrush
            : HollowFill;

        public string PaymentTooltip => Status.Contains("оплач", StringComparison.OrdinalIgnoreCase)
                                        || Status.Contains("заверш", StringComparison.OrdinalIgnoreCase)
            ? "Оплачено"
            : Status.Contains("частич", StringComparison.OrdinalIgnoreCase) ? "Частичная оплата" : "Не оплачено";

        public Brush EdoBrush => Record.Number.Contains("НФ", StringComparison.OrdinalIgnoreCase) ? BlueBrush : MutedBrush;
        public string EdoTooltip => Record.Number.Contains("НФ", StringComparison.OrdinalIgnoreCase) ? "ЭДО подключен" : "ЭДО не подключен";

        public static InvoiceRowViewModel Create(SalesInvoiceRecord record) => new(record);
    }
}
