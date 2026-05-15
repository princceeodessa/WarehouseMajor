using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PriceRegistrationsWorkspaceView : UserControl
{
    private const string AllPriceTypesFilter = "Все виды цен";
    private const string AllAuthorsFilter = "Все авторы";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly SalesWorkspace _salesWorkspace;
    private readonly List<PriceRegistrationRecord> _records = new();
    private bool _initializing = true;

    public PriceRegistrationsWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        BuildDemoRecords();
        RefreshFilters();
        _initializing = false;
        ApplyFilters();
    }

    /// <summary>
    /// Генерирует демо-документы установки цен. После миграции схемы заменить
    /// на чтение из <c>CatalogWorkspace.PriceRegistrations</c>.
    /// </summary>
    private void BuildDemoRecords()
    {
        _records.Clear();
        var today = DateTime.Today;
        var baseDate = new DateTime(today.Year, 1, 1);

        var priceTypeVariants = new[]
        {
            "Цены для операций (г. Иже…",
            "Розничная цена УДМ, Закуп…",
            "Учетная цена, Оптовая, Оп…",
            "Учетная цена, Закупочная",
            "Учетная цена, Оптовая, Зак…",
            "Дилер Тюмень, Дилер Тюме…",
            "Дилер Пермь, Дилер Пермь…",
            "Розничная цена РРЦ, Опто…",
            "Закупочная",
            "Дилер Ижевск",
        };

        var authors = new[]
        {
            "Администратор", "Вологжанина Кристина", "Русакова А",
            "КозачковС", "Богданов Александр", "Чуракова Надежда",
            "Шорников Владимир",
        };

        var comments = new[]
        {
            string.Empty, string.Empty, string.Empty,
            "Глянцевые гардины - выво…",
            string.Empty, string.Empty,
        };

        for (var i = 0; i < 30; i++)
        {
            _records.Add(new PriceRegistrationRecord
            {
                Date = baseDate.AddDays(i * 3),
                Number = $"НФ-{i + 1:D8}",
                PriceTypes = priceTypeVariants[i % priceTypeVariants.Length],
                Comment = comments[i % comments.Length],
                Author = authors[i % authors.Length],
            });
        }
    }

    private void RefreshFilters()
    {
        _initializing = true;

        PriceTypeFilterCombo.ItemsSource = new[] { AllPriceTypesFilter }
            .Concat(_records.Select(r => r.PriceTypes).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        PriceTypeFilterCombo.SelectedIndex = 0;

        AuthorFilterCombo.ItemsSource = new[] { AllAuthorsFilter }
            .Concat(_records.Select(r => r.Author).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        AuthorFilterCombo.SelectedIndex = 0;

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
        PriceTypeFilterCombo.SelectedIndex = 0;
        AuthorFilterCombo.SelectedIndex = 0;
        DateFromPicker.SelectedDate = null;
        DateToPicker.SelectedDate = null;
        _initializing = false;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!IsInitialized || PriceGrid is null) return;

        var query = (SearchBox.Text ?? string.Empty).Trim();
        var priceType = (PriceTypeFilterCombo.SelectedItem as string) ?? string.Empty;
        var author = (AuthorFilterCombo.SelectedItem as string) ?? string.Empty;
        var dateFrom = DateFromPicker.SelectedDate;
        var dateTo = DateToPicker.SelectedDate;

        var rows = _records
            .Where(r => priceType == AllPriceTypesFilter || r.PriceTypes.Equals(priceType, StringComparison.OrdinalIgnoreCase))
            .Where(r => author == AllAuthorsFilter || r.Author.Equals(author, StringComparison.OrdinalIgnoreCase))
            .Where(r => !dateFrom.HasValue || r.Date.Date >= dateFrom.Value.Date)
            .Where(r => !dateTo.HasValue || r.Date.Date <= dateTo.Value.Date)
            .Where(r => string.IsNullOrEmpty(query) || MatchesQuery(r, query))
            .OrderBy(r => r.Date)
            .ThenBy(r => r.Number, StringComparer.OrdinalIgnoreCase)
            .Select(PriceRegistrationRowViewModel.Create)
            .ToArray();

        PriceGrid.ItemsSource = rows;
        RecordsCountText.Text = $"Всего: {rows.Length:N0}";

        if (rows.Length > 0) PriceGrid.SelectedIndex = 0;
        else UpdateSummary(null);
    }

    private static bool MatchesQuery(PriceRegistrationRecord r, string query)
    {
        var haystack = string.Join(" ", r.Number, r.PriceTypes, r.Comment, r.Author);
        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = PriceGrid.SelectedItem as PriceRegistrationRowViewModel;
        UpdateSummary(row?.Record);
    }

    private void UpdateSummary(PriceRegistrationRecord? r)
    {
        if (r is null)
        {
            SelectedSummary.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedSummary.Visibility = Visibility.Visible;
        SelectedSummaryText.Text = $"{r.Number} · {r.PriceTypes}";
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        ShowInfo("Создание документа установки цен", "Карточка установки цен в разработке. После миграции схемы CatalogWorkspace.PriceRegistrations подключится автоматически.");
    }

    private void HandleRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PriceRegistrationRowViewModel row })
        {
            PriceGrid.SelectedItem = row;
            ShowInfo("Открыть документ", $"Карточка {row.Number} в разработке.");
        }
    }

    private void HandleGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || FindVisualParent<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is PriceRegistrationRowViewModel row)
        {
            ShowInfo("Открыть документ", $"Карточка {row.Number} в разработке.");
            e.Handled = true;
        }
    }

    private void HandleFilterPopupToggle(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = !FilterPopup.IsOpen;
    private void HandleFilterPopupClose(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = false;

    private void HandlePrintClick(object sender, RoutedEventArgs e) => ShowInfo("Печать", "Печать документа установки цен в разработке.");
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

    // Inline-модель (демо). После миграции заменить на чтение из
    // CatalogWorkspace.PriceRegistrations (CatalogPriceRegistrationRecord).
    public sealed class PriceRegistrationRecord
    {
        public DateTime Date { get; set; }
        public string Number { get; set; } = string.Empty;
        public string PriceTypes { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
    }

    public sealed class PriceRegistrationRowViewModel
    {
        private PriceRegistrationRowViewModel(PriceRegistrationRecord record) { Record = record; }
        public PriceRegistrationRecord Record { get; }
        public string DateDisplay => Record.Date.ToString("dd.MM.yyyy", RuCulture);
        public string Number => Record.Number;
        public string PriceTypes => Record.PriceTypes;
        public string Comment => Record.Comment;
        public string Author => Record.Author;

        public static PriceRegistrationRowViewModel Create(PriceRegistrationRecord record) => new(record);
    }
}
