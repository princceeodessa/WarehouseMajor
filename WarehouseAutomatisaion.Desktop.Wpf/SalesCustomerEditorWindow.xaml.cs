using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesCustomerEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] CounterpartyTypes =
    [
        "Юридическое лицо",
        "Индивидуальный предприниматель",
        "Физическое лицо",
        "Государственный орган"
    ];

    private static readonly string[] Sources =
    [
        "Сайт",
        "Рекомендация",
        "Холодный звонок",
        "Повторные продажи",
        "Выставка",
        "1С / импорт"
    ];

    private static readonly Dictionary<string, string[]> RegionCities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Самарская область"] = ["Самара", "Тольятти", "Сызрань"],
        ["Саратовская область"] = ["Саратов", "Энгельс", "Балаково"],
        ["Республика Башкортостан"] = ["Уфа", "Стерлитамак", "Салават"],
        ["Республика Татарстан"] = ["Казань", "Набережные Челны", "Альметьевск"],
        ["Краснодарский край"] = ["Краснодар", "Сочи", "Новороссийск"],
        ["Москва"] = ["Москва"],
        ["Санкт-Петербург"] = ["Санкт-Петербург"]
    };

    private readonly SalesWorkspace _workspace;
    private readonly SalesCustomerRecord _draft;
    private readonly ObservableCollection<SalesCustomerContactEditorRow> _contacts = [];
    private readonly ObservableCollection<CustomerDocumentRelationRow> _documents = [];
    private bool _hostedInWorkspace;

    public SalesCustomerEditorWindow(SalesWorkspace workspace, SalesCustomerRecord? customer = null)
    {
        _workspace = workspace;
        _draft = customer?.Clone() ?? workspace.CreateCustomerDraft();

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = customer is null ? Ui("Новый клиент") : Ui($"Клиент {_draft.Code}");
        HeaderTitleText.Text = customer is null ? Ui("Новый клиент") : Ui("Карточка клиента");

        CounterpartyTypeComboBox.ItemsSource = CounterpartyTypes.Select(Ui).ToArray();
        StatusComboBox.ItemsSource = workspace.CustomerStatuses.Select(Ui).ToArray();
        CurrencyComboBox.ItemsSource = workspace.Currencies.Select(Ui).ToArray();
        RegionComboBox.ItemsSource = RegionCities.Keys.OrderBy(Ui, StringComparer.CurrentCultureIgnoreCase).ToArray();
        SourceComboBox.ItemsSource = Sources.Select(Ui).ToArray();
        ResponsibleComboBox.ItemsSource = workspace.Managers.Select(Ui).ToArray();
        ContactsGrid.ItemsSource = _contacts;
        DocumentsGrid.ItemsSource = _documents;

        LoadDraft();
        RenderCustomerSummary();
        RenderDocuments();
        ApplyCounterpartyTypeLayout();
    }

    public SalesCustomerRecord? ResultCustomer { get; private set; }

    public event EventHandler? HostedSaved;

    public event EventHandler? HostedCanceled;

    public FrameworkElement DetachContentForWorkspaceTab()
    {
        _hostedInWorkspace = true;
        var content = Content as FrameworkElement
            ?? throw new InvalidOperationException("Editor content is not available.");
        Content = null;
        return content;
    }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void LoadDraft()
    {
        CodeTextBox.Text = Ui(_draft.Code);
        NameTextBox.Text = Ui(_draft.Name);
        ContractTextBox.Text = Ui(_draft.ContractNumber);
        PhoneTextBox.Text = Ui(_draft.Phone);
        EmailTextBox.Text = Ui(_draft.Email);
        InnTextBox.Text = Ui(_draft.Inn);
        KppTextBox.Text = Ui(_draft.Kpp);
        OgrnTextBox.Text = Ui(_draft.Ogrn);
        LegalAddressTextBox.Text = Ui(_draft.LegalAddress);
        ActualAddressTextBox.Text = Ui(_draft.ActualAddress);
        TagsTextBox.Text = Ui(_draft.Tags);
        BankAccountTextBox.Text = Ui(_draft.BankAccount);
        NotesTextBox.Text = Ui(_draft.Notes);
        BuyerCheckBox.IsChecked = _draft.IsBuyer;
        SupplierCheckBox.IsChecked = _draft.IsSupplier;
        OtherRoleCheckBox.IsChecked = _draft.IsOther;

        SelectComboValue(CounterpartyTypeComboBox, Ui(string.IsNullOrWhiteSpace(_draft.CounterpartyType) ? CounterpartyTypes[0] : _draft.CounterpartyType));
        SelectComboValue(StatusComboBox, Ui(_draft.Status));
        SelectComboValue(CurrencyComboBox, Ui(_draft.CurrencyCode));
        SelectComboValue(RegionComboBox, Ui(_draft.Region));
        RefreshCities(Ui(_draft.City));
        SelectComboValue(SourceComboBox, Ui(_draft.Source));
        SelectComboValue(ResponsibleComboBox, Ui(string.IsNullOrWhiteSpace(_draft.Responsible) ? _draft.Manager : _draft.Responsible));

        _contacts.Clear();
        foreach (var contact in _draft.Contacts.Select(item => item.Clone()))
        {
            _contacts.Add(SalesCustomerContactEditorRow.FromRecord(contact));
        }

        if (_contacts.Count == 0 && (!string.IsNullOrWhiteSpace(_draft.Phone) || !string.IsNullOrWhiteSpace(_draft.Email)))
        {
            _contacts.Add(new SalesCustomerContactEditorRow
            {
                Name = Ui(_draft.Name),
                Role = "Основной контакт",
                Phone = Ui(_draft.Phone),
                Email = Ui(_draft.Email)
            });
        }
    }

    private void RenderCustomerSummary()
    {
        var orders = _workspace.Orders.Where(item => item.CustomerId == _draft.Id).ToArray();
        var invoices = _workspace.Invoices.Where(item => item.CustomerId == _draft.Id).ToArray();
        var returns = _workspace.Returns.Where(item => item.CustomerId == _draft.Id).ToArray();
        var cashReceipts = _workspace.CashReceipts.Where(item => item.CustomerId == _draft.Id).ToArray();
        var salesTotal = orders.Sum(item => item.TotalAmount);
        var debt = invoices
            .Where(item => !Ui(item.Status).Equals("Оплачен", StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.TotalAmount);
        var lastSale = orders
            .OrderByDescending(item => item.OrderDate)
            .Select(item => item.OrderDate.ToString("dd.MM.yyyy", RuCulture))
            .FirstOrDefault() ?? "нет";

        CustomerDebtText.Text = FormatMoney(debt, _draft.CurrencyCode);
        CustomerSalesText.Text = FormatMoney(salesTotal, _draft.CurrencyCode);
        CustomerLastSaleText.Text = lastSale;
        EventsSummaryText.Text = $"В журнале операций по клиенту: {_workspace.OperationLog.Count(item => item.EntityId == _draft.Id):N0}.";
        ReportsSummaryText.Text = $"Заказы: {orders.Length:N0}, счета: {invoices.Length:N0}, отгрузки: {_workspace.Shipments.Count(item => item.CustomerId == _draft.Id):N0}, возвраты: {returns.Length:N0}, касса: {cashReceipts.Length:N0}.";
    }

    private void RenderDocuments()
    {
        _documents.Clear();

        foreach (var order in _workspace.Orders.Where(item => item.CustomerId == _draft.Id).OrderByDescending(item => item.OrderDate))
        {
            _documents.Add(new CustomerDocumentRelationRow("Заказ", order.Number, order.OrderDate.ToString("dd.MM.yyyy", RuCulture), order.Status, FormatMoney(order.TotalAmount, order.CurrencyCode)));
        }

        foreach (var invoice in _workspace.Invoices.Where(item => item.CustomerId == _draft.Id).OrderByDescending(item => item.InvoiceDate))
        {
            _documents.Add(new CustomerDocumentRelationRow("Счет", invoice.Number, invoice.InvoiceDate.ToString("dd.MM.yyyy", RuCulture), invoice.Status, FormatMoney(invoice.TotalAmount, invoice.CurrencyCode)));
        }

        foreach (var shipment in _workspace.Shipments.Where(item => item.CustomerId == _draft.Id).OrderByDescending(item => item.ShipmentDate))
        {
            _documents.Add(new CustomerDocumentRelationRow("Расходная накладная", shipment.Number, shipment.ShipmentDate.ToString("dd.MM.yyyy", RuCulture), shipment.Status, FormatMoney(shipment.TotalAmount, shipment.CurrencyCode)));
        }

        foreach (var returnDocument in _workspace.Returns.Where(item => item.CustomerId == _draft.Id).OrderByDescending(item => item.ReturnDate))
        {
            _documents.Add(new CustomerDocumentRelationRow("Приходная накладная (возврат)", returnDocument.Number, returnDocument.ReturnDate.ToString("dd.MM.yyyy", RuCulture), returnDocument.Status, FormatMoney(returnDocument.TotalAmount, returnDocument.CurrencyCode)));
        }

        foreach (var cashReceipt in _workspace.CashReceipts.Where(item => item.CustomerId == _draft.Id).OrderByDescending(item => item.ReceiptDate))
        {
            _documents.Add(new CustomerDocumentRelationRow("Поступление в кассу", cashReceipt.Number, cashReceipt.ReceiptDate.ToString("dd.MM.yyyy", RuCulture), cashReceipt.Status, FormatMoney(cashReceipt.Amount, cashReceipt.CurrencyCode)));
        }

        DocumentsSummaryText.Text = _documents.Count == 0
            ? "По этому клиенту пока нет заказов, счетов, расходных или возвратных документов."
            : $"Связанные документы клиента: {_documents.Count:N0}. Показаны заказы, счета, расходные накладные, возвраты и поступления в кассу.";
    }

    private void HandleCounterpartyTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyCounterpartyTypeLayout();
    }

    private void ApplyCounterpartyTypeLayout()
    {
        var type = CounterpartyTypeComboBox.SelectedItem?.ToString() ?? CounterpartyTypeComboBox.Text;
        type = Ui(type);

        KppPanel.Visibility = type is "Юридическое лицо" or "Государственный орган"
            ? Visibility.Visible
            : Visibility.Collapsed;
        OgrnPanel.Visibility = type.Equals("Физическое лицо", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;

        LegalDetailsTitleText.Text = type switch
        {
            "Индивидуальный предприниматель" => "Данные ИП",
            "Физическое лицо" => "Персональные данные",
            "Государственный орган" => "Данные государственного органа",
            _ => "Юридические данные"
        };
        OgrnLabelText.Text = type.Equals("Индивидуальный предприниматель", StringComparison.OrdinalIgnoreCase)
            ? "ОГРНИП"
            : "ОГРН";
    }

    private void HandleRegionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshCities(null);
    }

    private void RefreshCities(string? preferredCity)
    {
        var region = RegionComboBox.SelectedItem?.ToString() ?? RegionComboBox.Text;
        if (RegionCities.TryGetValue(Ui(region), out var cities))
        {
            CityComboBox.ItemsSource = cities.Select(Ui).ToArray();
        }
        else
        {
            CityComboBox.ItemsSource = Array.Empty<string>();
        }

        SelectComboValue(CityComboBox, Ui(preferredCity));
    }

    private void HandleAddContactClick(object sender, RoutedEventArgs e)
    {
        _contacts.Add(new SalesCustomerContactEditorRow
        {
            Name = Ui(NameTextBox.Text),
            Role = "Контакт",
            Phone = string.Empty,
            Email = string.Empty
        });
        ContactsGrid.SelectedIndex = _contacts.Count - 1;
    }

    private void HandleRemoveContactClick(object sender, RoutedEventArgs e)
    {
        if (ContactsGrid.SelectedItem is SalesCustomerContactEditorRow row)
        {
            _contacts.Remove(row);
        }
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            ValidationText.Text = "Укажите название клиента.";
            return;
        }

        var contacts = _contacts
            .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                || !string.IsNullOrWhiteSpace(item.Role)
                || !string.IsNullOrWhiteSpace(item.Phone)
                || !string.IsNullOrWhiteSpace(item.Email)
                || !string.IsNullOrWhiteSpace(item.Comment))
            .Select(item => item.ToRecord())
            .ToList();

        var primaryContact = contacts.FirstOrDefault();
        var phone = string.IsNullOrWhiteSpace(PhoneTextBox.Text) ? primaryContact?.Phone ?? string.Empty : PhoneTextBox.Text.Trim();
        var email = string.IsNullOrWhiteSpace(EmailTextBox.Text) ? primaryContact?.Email ?? string.Empty : EmailTextBox.Text.Trim();
        var responsible = ResponsibleComboBox.SelectedItem?.ToString() ?? ResponsibleComboBox.Text.Trim();

        ResultCustomer = new SalesCustomerRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Code = string.IsNullOrWhiteSpace(CodeTextBox.Text) ? _draft.Code : CodeTextBox.Text.Trim(),
            Name = NameTextBox.Text.Trim(),
            CounterpartyType = CounterpartyTypeComboBox.SelectedItem?.ToString() ?? CounterpartyTypeComboBox.Text.Trim(),
            IsBuyer = BuyerCheckBox.IsChecked == true,
            IsSupplier = SupplierCheckBox.IsChecked == true,
            IsOther = OtherRoleCheckBox.IsChecked == true,
            ContractNumber = ContractTextBox.Text.Trim(),
            CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim(),
            Manager = string.IsNullOrWhiteSpace(responsible) ? _draft.Manager : responsible,
            Status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text.Trim(),
            Phone = phone,
            Email = email,
            Inn = InnTextBox.Text.Trim(),
            Kpp = KppTextBox.Text.Trim(),
            Ogrn = OgrnTextBox.Text.Trim(),
            LegalAddress = LegalAddressTextBox.Text.Trim(),
            ActualAddress = ActualAddressTextBox.Text.Trim(),
            Region = RegionComboBox.SelectedItem?.ToString() ?? RegionComboBox.Text.Trim(),
            City = CityComboBox.SelectedItem?.ToString() ?? CityComboBox.Text.Trim(),
            Source = SourceComboBox.SelectedItem?.ToString() ?? SourceComboBox.Text.Trim(),
            Responsible = responsible,
            Tags = TagsTextBox.Text.Trim(),
            BankAccount = BankAccountTextBox.Text.Trim(),
            Notes = NotesTextBox.Text.Trim(),
            Contacts = new System.ComponentModel.BindingList<SalesCustomerContactRecord>(contacts)
        };

        CompleteEditing(success: true);
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        CompleteEditing(success: false);
    }

    private void CompleteEditing(bool success)
    {
        if (_hostedInWorkspace)
        {
            if (success)
            {
                HostedSaved?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                HostedCanceled?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        DialogResult = success;
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var selected = comboBox.Items
                .Cast<object>()
                .Select(item => item?.ToString() ?? string.Empty)
                .FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                comboBox.SelectedItem = selected;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static string FormatMoney(decimal amount, string currencyCode)
    {
        var currency = string.Equals(currencyCode, "RUB", StringComparison.OrdinalIgnoreCase)
            ? "₽"
            : Ui(currencyCode);
        return $"{amount:N2} {currency}";
    }
}

public sealed class SalesCustomerContactEditorRow
{
    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public static SalesCustomerContactEditorRow FromRecord(SalesCustomerContactRecord record)
    {
        return new SalesCustomerContactEditorRow
        {
            Name = record.Name,
            Role = record.Role,
            Phone = record.Phone,
            Email = record.Email,
            Comment = record.Comment
        };
    }

    public SalesCustomerContactRecord ToRecord()
    {
        return new SalesCustomerContactRecord
        {
            Name = Name.Trim(),
            Role = Role.Trim(),
            Phone = Phone.Trim(),
            Email = Email.Trim(),
            Comment = Comment.Trim()
        };
    }
}

public sealed record CustomerDocumentRelationRow(
    string Section,
    string Number,
    string Date,
    string Status,
    string Amount);
