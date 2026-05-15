using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ContractorsWorkspaceView : UserControl
{
    public const string BuyersFilter = "buyers";
    public const string SuppliersFilter = "suppliers";
    public const string OthersFilter = "others";

    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly SolidColorBrush GreenBrush = BrushFromHex("#1FA45F");
    private static readonly SolidColorBrush BlueBrush = BrushFromHex("#4F5BFF");
    private static readonly SolidColorBrush MutedBrush = BrushFromHex("#7A86A5");

    private readonly SalesWorkspace _salesWorkspace;
    private bool _initializing = true;

    public ContractorsWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        _salesWorkspace.Changed += HandleWorkspaceChanged;
        Unloaded += HandleUnloaded;
        Loaded += HandleLoaded;

        _initializing = false;
        ApplyFilters();
    }

    /// <summary>
    /// Активирует фильтр по типу контрагентов: "buyers" / "suppliers" / "others".
    /// Вызывается из <see cref="MainWindow.ActivateSubSection"/> при переходе из витрин.
    /// </summary>
    public void ActivateSubSection(string subSectionKey)
    {
        _initializing = true;
        BuyersCheckBox.IsChecked = false;
        SuppliersCheckBox.IsChecked = false;
        OthersCheckBox.IsChecked = false;

        switch (subSectionKey?.ToLowerInvariant())
        {
            case SuppliersFilter:
                SuppliersCheckBox.IsChecked = true;
                break;
            case OthersFilter:
                OthersCheckBox.IsChecked = true;
                break;
            default:
                BuyersCheckBox.IsChecked = true;
                break;
        }

        _initializing = false;
        ApplyFilters();
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        ApplyFilters();
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        _salesWorkspace.Changed -= HandleWorkspaceChanged;
        Unloaded -= HandleUnloaded;
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(ApplyFilters));
    }

    private void HandleTypeChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        ApplyFilters();
    }

    private void HandleSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (!IsInitialized || ContractorsGrid is null)
        {
            return;
        }

        var buyers = BuyersCheckBox.IsChecked == true;
        var suppliers = SuppliersCheckBox.IsChecked == true;
        var others = OthersCheckBox.IsChecked == true;

        // По умолчанию — Покупатели
        if (!buyers && !suppliers && !others)
        {
            buyers = true;
            _initializing = true;
            BuyersCheckBox.IsChecked = true;
            _initializing = false;
        }

        var query = (SearchBox.Text ?? string.Empty).Trim();

        var rows = _salesWorkspace.Customers
            .Where(c =>
                (buyers && c.IsBuyer) || (suppliers && c.IsSupplier) || (others && c.IsOther))
            .Where(c => MatchesQuery(c, query))
            .OrderBy(c => Ui(c.Name), StringComparer.CurrentCultureIgnoreCase)
            .Select(ContractorRowViewModel.Create)
            .ToArray();

        ContractorsGrid.ItemsSource = rows;
        ContractorsCountText.Text = $"Всего: {rows.Length:N0}";
        HeaderTitleText.Text = $"Контрагенты: {BuildTypeLabel(buyers, suppliers, others)}";

        if (rows.Length > 0)
        {
            ContractorsGrid.SelectedIndex = 0;
        }
        else
        {
            UpdateDetails(null);
        }
    }

    private static bool MatchesQuery(SalesCustomerRecord customer, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var haystack = string.Join(" ",
            Ui(customer.Name),
            Ui(customer.Code),
            Ui(customer.Phone),
            Ui(customer.Email),
            Ui(customer.Inn),
            Ui(customer.ActualAddress),
            Ui(customer.Tags));

        return haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildTypeLabel(bool buyers, bool suppliers, bool others)
    {
        var parts = new List<string>(3);
        if (buyers) parts.Add("Покупатели");
        if (suppliers) parts.Add("Поставщики");
        if (others) parts.Add("Прочие");
        return parts.Count == 0 ? "Все" : string.Join(" + ", parts);
    }

    private void HandleSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = ContractorsGrid.SelectedItem as ContractorRowViewModel;
        UpdateDetails(row?.Record);
    }

    private void UpdateDetails(SalesCustomerRecord? customer)
    {
        if (customer is null)
        {
            TagText.Text = "—";
            SegmentText.Text = "—";
            SourceText.Text = "—";
            ContactText.Text = "—";
            ResponsibleText.Text = "—";
            DetailPhoneText.Text = "—";
            DetailEmailText.Text = string.Empty;
            DetailAddressText.Text = string.Empty;
            SelectedContactInfo.Visibility = Visibility.Collapsed;
            return;
        }

        // Поля Popup-фильтра — отражают выделенный контрагент как контекст
        TagText.Text = FallbackPlaceholder(customer.Tags);
        SegmentText.Text = FallbackPlaceholder(customer.CounterpartyType);
        SourceText.Text = FallbackPlaceholder(customer.Source);
        ContactText.Text = FallbackPlaceholder(customer.Contacts.FirstOrDefault()?.Name ?? customer.Phone);
        ResponsibleText.Text = FallbackPlaceholder(customer.Responsible.Length > 0 ? customer.Responsible : customer.Manager);

        // Компактный inline-бейдж в футере
        var phone = Ui(customer.Phone);
        var email = Ui(customer.Email);
        var address = FirstNonEmpty(Ui(customer.ActualAddress), Ui(customer.LegalAddress));
        DetailPhoneText.Text = string.IsNullOrWhiteSpace(phone) ? "Телефон не указан" : phone;
        DetailEmailText.Text = string.IsNullOrWhiteSpace(email) ? string.Empty : $"· {email}";
        DetailAddressText.Text = string.IsNullOrWhiteSpace(address) ? string.Empty : $"· {address}";
        SelectedContactInfo.Visibility = Visibility.Visible;
    }

    private void HandleFilterPopupToggle(object sender, RoutedEventArgs e)
    {
        FilterPopup.IsOpen = !FilterPopup.IsOpen;
    }

    private void HandleFilterPopupClose(object sender, RoutedEventArgs e)
    {
        FilterPopup.IsOpen = false;
    }

    private void HandleCreateClick(object sender, RoutedEventArgs e)
    {
        // Открыть редактор нового контрагента во вкладке (если возможно), иначе модально.
        RecordsWorkspaceCatalog.OpenCustomerEditorTabOrDialog(_salesWorkspace, null);
    }

    private void HandleRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ContractorRowViewModel row })
        {
            ContractorsGrid.SelectedItem = row;
            ShowContextMenu(row);
        }
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (ContractorsGrid.SelectedItem is ContractorRowViewModel row)
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

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is ContractorRowViewModel row)
        {
            RecordsWorkspaceCatalog.OpenCustomerEditorTabOrDialog(_salesWorkspace, row.Record);
            e.Handled = true;
        }
    }

    private void ShowContextMenu(ContractorRowViewModel row)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Открыть карточку", () => RecordsWorkspaceCatalog.OpenCustomerEditorTabOrDialog(_salesWorkspace, row.Record)));
        menu.Items.Add(CreateMenuItem("Создать продажу", () => HandleSellClick(this, new RoutedEventArgs())));
        menu.Items.Add(CreateMenuItem("Создать закупку", () => HandleBuyClick(this, new RoutedEventArgs())));
        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void HandleSellClick(object sender, RoutedEventArgs e)
    {
        ShowInfo("Создать продажу", "Откройте раздел «Продажи → Заказы покупателей» для оформления продажи.");
    }

    private void HandleBuyClick(object sender, RoutedEventArgs e)
    {
        ShowInfo("Создать закупку", "Откройте раздел «Закупки → Заказы поставщикам» для оформления закупки.");
    }

    private void HandleEventsClick(object sender, RoutedEventArgs e)
    {
        ShowInfo("События", "Журнал событий по контрагенту в разработке.");
    }

    private void HandleMoreClick(object sender, RoutedEventArgs e)
    {
        ShowInfo("Дополнительные действия", "Дополнительные действия будут доступны в одном из ближайших релизов.");
    }

    private void HandleExternalServiceClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
        {
            var label = tag == "spark" ? "1СПАРК Риски" : "Проверка контрагента";
            ShowInfo(label, $"Интеграция «{label}» в разработке. Структура готова к подключению внешнего API.");
        }
    }

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

    private static string FallbackPlaceholder(string? value)
    {
        var clean = Ui(value).Trim();
        return string.IsNullOrEmpty(clean) ? "—" : clean;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }

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

    public sealed class ContractorRowViewModel
    {
        private ContractorRowViewModel(SalesCustomerRecord record)
        {
            Record = record;
        }

        public SalesCustomerRecord Record { get; }

        public string Name => Ui(Record.Name);

        public string Code => Ui(Record.Code);

        public string KindIcon => Record.CounterpartyType.Contains("физ", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Record.Inn)
                ? ""
                : "";

        public Brush KindIconBrush => Record.IsSupplier ? BlueBrush : GreenBrush;

        public string MainContact
        {
            get
            {
                var phone = Ui(Record.Phone);
                var addr = FirstNonEmpty(Ui(Record.ActualAddress), Ui(Record.LegalAddress));
                if (!string.IsNullOrWhiteSpace(phone) && !string.IsNullOrWhiteSpace(addr))
                {
                    return $"{phone} · {addr}";
                }
                return FirstNonEmpty(phone, addr, Ui(Record.Email));
            }
        }

        public string DebtDisplay => "—";

        public string EdoStatus => string.IsNullOrWhiteSpace(Record.Inn) ? "—" : "Подключен";

        public static ContractorRowViewModel Create(SalesCustomerRecord record) => new(record);
    }
}
