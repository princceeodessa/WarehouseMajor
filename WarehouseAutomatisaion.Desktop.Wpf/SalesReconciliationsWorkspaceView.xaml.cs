using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesReconciliationsWorkspaceView : UserControl
{
    private const string AllCounterpartiesFilter = "Все контрагенты";
    private const string AllOrganizationsFilter = "Все организации";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly SolidColorBrush MutedBrush = BrushFromHex("#7A86A5");
    private static readonly SolidColorBrush GreenBrush = BrushFromHex("#1FA45F");
    private static readonly SolidColorBrush BlueBrush = BrushFromHex("#4F5BFF");

    private readonly SalesWorkspace _salesWorkspace;
    private readonly List<ReconciliationRecord> _records = new();
    private bool _initializing = true;

    public SalesReconciliationsWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        BuildDemoRecords();
        RefreshFilters();
        _initializing = false;
        ApplyFilters();

        Unloaded += (_, _) => { /* no-op */ };
    }

    /// <summary>
    /// Пока в SalesWorkspace нет поля Reconciliations и таблицы в БД, оставляем
    /// пустой список — пользователь видит честное «нет данных», а не демо.
    /// После миграции схемы (добавление SalesReconciliationRecord +
    /// BindingList&lt;Reconciliations&gt; в SalesWorkspace) заменить на чтение
    /// _salesWorkspace.Reconciliations.
    /// </summary>
    private void BuildDemoRecords()
    {
        _records.Clear();
    }

    private void RefreshFilters()
    {
        _initializing = true;

        CounterpartyFilterCombo.ItemsSource = new[] { AllCounterpartiesFilter }
            .Concat(_records.Select(r => r.CounterpartyName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        CounterpartyFilterCombo.SelectedIndex = 0;

        OrganizationFilterCombo.ItemsSource = new[] { AllOrganizationsFilter }
            .Concat(_records.Select(r => r.Organization)
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
        CounterpartyFilterCombo.SelectedIndex = 0;
        StatusFilterCombo.SelectedIndex = 0;
        OrganizationFilterCombo.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        _initializing = false;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!IsInitialized || ReconciliationsGrid is null)
        {
            return;
        }

        var query = (SearchBox.Text ?? string.Empty).Trim();
        var counterparty = (CounterpartyFilterCombo.SelectedItem as string) ?? string.Empty;
        var organization = (OrganizationFilterCombo.SelectedItem as string) ?? string.Empty;
        var status = (StatusFilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Все";
        var dateFrom = DateFromPicker.SelectedDate;
        var dateTo = DateToPicker.SelectedDate;

        // Release 1.0.133: cap 100.
        const int displayCap = 100;
        var matched = _records
            .Where(r => counterparty == AllCounterpartiesFilter || r.CounterpartyName.Equals(counterparty, StringComparison.OrdinalIgnoreCase))
            .Where(r => organization == AllOrganizationsFilter || r.Organization.Equals(organization, StringComparison.OrdinalIgnoreCase))
            .Where(r => status == "Все" || r.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .Where(r => !dateFrom.HasValue || (r.PeriodTo.HasValue && r.PeriodTo.Value.Date >= dateFrom.Value.Date))
            .Where(r => !dateTo.HasValue || (r.PeriodFrom.HasValue && r.PeriodFrom.Value.Date <= dateTo.Value.Date))
            .Where(r => string.IsNullOrEmpty(query) || MatchesQuery(r, query));
        var totalMatched = matched.Count();
        var rows = matched
            .Take(displayCap)
            .Select(ReconciliationRowViewModel.Create)
            .ToArray();

        ReconciliationsGrid.ItemsSource = rows;
        RecordsCountText.Text = totalMatched > displayCap
            ? $"Показано {rows.Length:N0} из {totalMatched:N0} — уточните фильтр"
            : $"Всего: {rows.Length:N0}";

        if (rows.Length > 0)
        {
            ReconciliationsGrid.SelectedIndex = 0;
        }
        else
        {
            UpdateSummary(null);
        }
    }

    private static bool MatchesQuery(ReconciliationRecord r, string query)
    {
        var haystack = string.Join(" ", r.Number, r.Organization, r.CounterpartyName, r.CounterpartyInn, r.Status, r.Author);
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = ReconciliationsGrid.SelectedItem as ReconciliationRowViewModel;
        UpdateSummary(row?.Record);
    }

    private void UpdateSummary(ReconciliationRecord? r)
    {
        if (r is null)
        {
            SelectedSummary.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedSummary.Visibility = Visibility.Visible;
        SelectedSummaryText.Text = $"{r.Number} · {r.CounterpartyName} · {r.Status}";
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        ShowInfo("Создание сверки", "Карточка сверки взаиморасчётов в разработке. Структура готова к подключению.");
    }

    private void HandleRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ReconciliationRowViewModel row })
        {
            ReconciliationsGrid.SelectedItem = row;
            ShowInfo("Открыть сверку", $"Карточка сверки {row.Number} в разработке.");
        }
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (ReconciliationsGrid.SelectedItem is ReconciliationRowViewModel row)
        {
            ShowInfo("Открыть сверку", $"Карточка сверки {row.Number} в разработке.");
        }
    }

    private void HandleGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is ReconciliationRowViewModel row)
        {
            ShowInfo("Открыть сверку", $"Карточка сверки {row.Number} в разработке.");
            e.Handled = true;
        }
    }

    private void HandleFilterPopupToggle(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = !FilterPopup.IsOpen;
    private void HandleFilterPopupClose(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = false;

    private void HandlePrintClick(object sender, RoutedEventArgs e) => ShowInfo("Печать", "Печать акта сверки в разработке.");
    private void HandleGenerateClick(object sender, RoutedEventArgs e) => ShowInfo("Сформировать", "Меню «Сформировать» появится в одном из ближайших релизов.");
    private void HandleStructureClick(object sender, RoutedEventArgs e) => ShowInfo("Структура подчинённости", "Связанные документы доступны внутри карточки сверки.");
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

    // ---- Inline-модель сверки (демо). После миграции схемы 1С/MySQL заменить
    // на полноценный SalesReconciliationRecord в SalesWorkspace.cs.
    public sealed class ReconciliationRecord
    {
        public string Number { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string CounterpartyName { get; set; } = string.Empty;
        public string CounterpartyInn { get; set; } = string.Empty;
        public DateTime? PeriodFrom { get; set; }
        public DateTime? PeriodTo { get; set; }
        public string Status { get; set; } = "Создана";
        public string Author { get; set; } = string.Empty;
    }

    public sealed class ReconciliationRowViewModel
    {
        private ReconciliationRowViewModel(ReconciliationRecord record) { Record = record; }
        public ReconciliationRecord Record { get; }
        public string Number => Record.Number;
        public string Organization => Record.Organization;
        public string CounterpartyName => Record.CounterpartyName;
        public string PeriodFromDisplay => Record.PeriodFrom?.ToString("dd.MM.yyyy", RuCulture) ?? string.Empty;
        public string PeriodToDisplay => Record.PeriodTo?.ToString("dd.MM.yyyy", RuCulture) ?? string.Empty;
        public string Status => Record.Status;
        public string Author => Record.Author;
        public Brush StatusBrush => Status.Equals("Сверена", StringComparison.OrdinalIgnoreCase) ? GreenBrush : MutedBrush;

        public static ReconciliationRowViewModel Create(ReconciliationRecord record) => new(record);
    }
}
