using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;
using WarehouseAutomatisaion.Infrastructure.Importing;

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
    private readonly ObservableCollection<SalesCustomerFileEditorRow> _files = [];
    private readonly ObservableCollection<CustomerEventRow> _events = [];
    private readonly ObservableCollection<PersonalDataRow> _personalData = [];
    private SalesOrderRecord[] _customerOrders = Array.Empty<SalesOrderRecord>();
    private SalesInvoiceRecord[] _customerInvoices = Array.Empty<SalesInvoiceRecord>();
    private SalesShipmentRecord[] _customerShipments = Array.Empty<SalesShipmentRecord>();
    private SalesReturnRecord[] _customerReturns = Array.Empty<SalesReturnRecord>();
    private SalesCashReceiptRecord[] _customerCashReceipts = Array.Empty<SalesCashReceiptRecord>();
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
        FilesGrid.ItemsSource = _files;
        EventsItems.ItemsSource = _events;
        PersonalDataItems.ItemsSource = _personalData;

        LoadDraft();
        RenderCustomerSummary();
        RenderDocuments();
        RenderFiles();
        RenderContractTab();
        RenderBankAccountTab();
        RenderEventsTimeline();
        RenderReportsMetrics();
        RenderPersonalDataChecklist();
        ApplyCounterpartyTypeLayout();

        ContractTextBox.TextChanged += (_, _) => RenderContractTab();
        BankAccountTextBox.TextChanged += (_, _) => RenderBankAccountTab();
        _contacts.CollectionChanged += HandleContactsCollectionChanged;
    }

    private void HandleContactsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RenderPersonalDataChecklist();
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
        _customerOrders = _workspace.Orders.Where(item => item.CustomerId == _draft.Id).ToArray();
        _customerInvoices = _workspace.Invoices.Where(item => item.CustomerId == _draft.Id).ToArray();
        _customerShipments = _workspace.Shipments.Where(item => item.CustomerId == _draft.Id).ToArray();
        _customerReturns = _workspace.Returns.Where(item => item.CustomerId == _draft.Id).ToArray();
        _customerCashReceipts = _workspace.CashReceipts.Where(item => item.CustomerId == _draft.Id).ToArray();

        var salesTotal = _customerOrders.Sum(item => item.TotalAmount);
        var debt = _customerInvoices
            .Where(item => !Ui(item.Status).Equals("Оплачен", StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.TotalAmount);
        var lastSale = _customerOrders
            .OrderByDescending(item => item.OrderDate)
            .Select(item => item.OrderDate.ToString("dd.MM.yyyy", RuCulture))
            .FirstOrDefault() ?? "нет";

        CustomerDebtText.Text = FormatMoney(debt, _draft.CurrencyCode);
        CustomerSalesText.Text = FormatMoney(salesTotal, _draft.CurrencyCode);
        CustomerLastSaleText.Text = lastSale;
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

    private void RenderFiles()
    {
        _files.Clear();
        foreach (var file in _draft.Files.Select(item => item.Clone()).OrderByDescending(item => item.UploadedAt))
        {
            _files.Add(SalesCustomerFileEditorRow.FromRecord(file));
        }

        RefreshFilesSummary();
    }

    private void RefreshFilesSummary()
    {
        FilesSummaryText.Text = _files.Count == 0
            ? "Файлов пока нет."
            : $"Файлов в карточке: {_files.Count:N0}.";
    }

    private void RenderContractTab()
    {
        var contract = ContractTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(contract))
        {
            ContractFilledPanel.Visibility = Visibility.Collapsed;
            ContractEmptyPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ContractValueText.Text = Ui(contract);
            ContractFilledPanel.Visibility = Visibility.Visible;
            ContractEmptyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void RenderBankAccountTab()
    {
        var account = BankAccountTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(account))
        {
            BankAccountFilledPanel.Visibility = Visibility.Collapsed;
            BankAccountEmptyPanel.Visibility = Visibility.Visible;
        }
        else
        {
            BankAccountFormattedText.Text = FormatAccountNumber(account);
            BankAccountFilledPanel.Visibility = Visibility.Visible;
            BankAccountEmptyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private static string FormatAccountNumber(string raw)
    {
        var digits = new string(raw.Where(char.IsLetterOrDigit).ToArray());
        if (digits.Length == 0)
        {
            return raw;
        }

        var builder = new System.Text.StringBuilder(digits.Length + digits.Length / 4);
        for (var index = 0; index < digits.Length; index++)
        {
            if (index > 0 && index % 4 == 0)
            {
                builder.Append(' ');
            }

            builder.Append(digits[index]);
        }

        return builder.ToString();
    }

    private void RenderEventsTimeline()
    {
        _events.Clear();

        var allForCustomer = _workspace.OperationLog
            .Where(item => item.EntityId == _draft.Id)
            .ToArray();
        var recent = allForCustomer
            .OrderByDescending(item => item.LoggedAt)
            .Take(20)
            .ToArray();

        foreach (var entry in recent)
        {
            var isSuccess = !Ui(entry.Result).Equals("Ошибка", StringComparison.OrdinalIgnoreCase);
            var entityType = Ui(entry.EntityType);
            var entityNumber = Ui(entry.EntityNumber);
            var message = Ui(entry.Message);
            var detailParts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                detailParts.Add(string.IsNullOrWhiteSpace(entityNumber) ? entityType : $"{entityType} {entityNumber}");
            }
            else if (!string.IsNullOrWhiteSpace(entityNumber))
            {
                detailParts.Add(entityNumber);
            }
            if (!string.IsNullOrWhiteSpace(message))
            {
                detailParts.Add(message);
            }

            var meta = entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);
            var actor = Ui(entry.Actor);
            if (!string.IsNullOrWhiteSpace(actor))
            {
                meta = $"{meta} · {actor}";
            }

            _events.Add(new CustomerEventRow
            {
                Title = string.IsNullOrWhiteSpace(Ui(entry.Action)) ? "Операция" : Ui(entry.Action),
                Detail = string.Join(" · ", detailParts),
                Meta = meta,
                Result = string.IsNullOrWhiteSpace(Ui(entry.Result)) ? (isSuccess ? "Успех" : "Ошибка") : Ui(entry.Result),
                IsSuccess = isSuccess
            });
        }

        var hasAny = allForCustomer.Length > 0;
        EventsEmptyPanel.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;
        EventsScroll.Visibility = hasAny ? Visibility.Visible : Visibility.Collapsed;

        EventsSummaryText.Text = hasAny
            ? $"Всего записей: {allForCustomer.Length:N0}. Показаны последние {Math.Min(recent.Length, allForCustomer.Length):N0}."
            : "Журнал по этому клиенту пока пуст.";
    }

    private void RenderReportsMetrics()
    {
        var currency = string.IsNullOrWhiteSpace(_draft.CurrencyCode) ? "RUB" : _draft.CurrencyCode;

        ReportsOrdersCountText.Text = _customerOrders.Length.ToString("N0", RuCulture);
        ReportsOrdersTotalText.Text = "Сумма: " + FormatMoney(_customerOrders.Sum(item => item.TotalAmount), currency);

        ReportsInvoicesCountText.Text = _customerInvoices.Length.ToString("N0", RuCulture);
        ReportsInvoicesTotalText.Text = "Сумма: " + FormatMoney(_customerInvoices.Sum(item => item.TotalAmount), currency);

        ReportsShipmentsCountText.Text = _customerShipments.Length.ToString("N0", RuCulture);
        ReportsShipmentsTotalText.Text = "Сумма: " + FormatMoney(_customerShipments.Sum(item => item.TotalAmount), currency);

        ReportsReturnsCountText.Text = _customerReturns.Length.ToString("N0", RuCulture);
        ReportsReturnsTotalText.Text = "Сумма: " + FormatMoney(_customerReturns.Sum(item => item.TotalAmount), currency);

        ReportsCashCountText.Text = _customerCashReceipts.Length.ToString("N0", RuCulture);
        ReportsCashTotalText.Text = "Сумма: " + FormatMoney(_customerCashReceipts.Sum(item => item.Amount), currency);

        var totalDocs = _customerOrders.Length
            + _customerInvoices.Length
            + _customerShipments.Length
            + _customerReturns.Length
            + _customerCashReceipts.Length;

        var activity = BuildLastActivity();
        if (totalDocs == 0)
        {
            ReportsSummaryText.Text = "По этому клиенту пока нет заказов, счетов и других документов.";
            ReportsLastActivityText.Text = string.Empty;
            ReportsLastActivityText.Visibility = Visibility.Collapsed;
            ReportsEmptyPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ReportsSummaryText.Text = $"Всего документов: {totalDocs:N0}. Суммы указаны в валюте {currency}.";
            ReportsLastActivityText.Text = activity ?? string.Empty;
            ReportsLastActivityText.Visibility = string.IsNullOrEmpty(activity) ? Visibility.Collapsed : Visibility.Visible;
            ReportsEmptyPanel.Visibility = Visibility.Collapsed;
        }
    }

    private string? BuildLastActivity()
    {
        var candidates = new List<(DateTime Date, string Label, string Number)>();
        var lastOrder = _customerOrders.OrderByDescending(item => item.OrderDate).FirstOrDefault();
        if (lastOrder is not null)
        {
            candidates.Add((lastOrder.OrderDate, "Заказ", Ui(lastOrder.Number)));
        }
        var lastInvoice = _customerInvoices.OrderByDescending(item => item.InvoiceDate).FirstOrDefault();
        if (lastInvoice is not null)
        {
            candidates.Add((lastInvoice.InvoiceDate, "Счёт", Ui(lastInvoice.Number)));
        }
        var lastShipment = _customerShipments.OrderByDescending(item => item.ShipmentDate).FirstOrDefault();
        if (lastShipment is not null)
        {
            candidates.Add((lastShipment.ShipmentDate, "Отгрузка", Ui(lastShipment.Number)));
        }
        var lastReturn = _customerReturns.OrderByDescending(item => item.ReturnDate).FirstOrDefault();
        if (lastReturn is not null)
        {
            candidates.Add((lastReturn.ReturnDate, "Возврат", Ui(lastReturn.Number)));
        }
        var lastCash = _customerCashReceipts.OrderByDescending(item => item.ReceiptDate).FirstOrDefault();
        if (lastCash is not null)
        {
            candidates.Add((lastCash.ReceiptDate, "Касса", Ui(lastCash.Number)));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.OrderByDescending(item => item.Date).First();
        var number = string.IsNullOrWhiteSpace(best.Number) ? string.Empty : $" № {best.Number}";
        return $"Последняя активность: {best.Date:dd.MM.yyyy} — {best.Label}{number}.";
    }

    private void RenderPersonalDataChecklist()
    {
        _personalData.Clear();

        var type = Ui(CounterpartyTypeComboBox.SelectedItem?.ToString() ?? CounterpartyTypeComboBox.Text);
        var isApplicable = !type.Equals("Юридическое лицо", StringComparison.OrdinalIgnoreCase)
                            && !type.Equals("Государственный орган", StringComparison.OrdinalIgnoreCase);

        if (!isApplicable)
        {
            PersonalDataAppliesPanel.Visibility = Visibility.Collapsed;
            PersonalDataNotApplicablePanel.Visibility = Visibility.Visible;
            return;
        }

        PersonalDataAppliesPanel.Visibility = Visibility.Visible;
        PersonalDataNotApplicablePanel.Visibility = Visibility.Collapsed;

        AddPersonalRow("Имя / ФИО", NameTextBox.Text);
        AddPersonalRow("ИНН", InnTextBox.Text);
        if (type.Equals("Индивидуальный предприниматель", StringComparison.OrdinalIgnoreCase))
        {
            AddPersonalRow("ОГРНИП", OgrnTextBox.Text);
        }
        AddPersonalRow("Телефон", PhoneTextBox.Text);
        AddPersonalRow("E-mail", EmailTextBox.Text);
        AddPersonalRow("Юр. адрес", LegalAddressTextBox.Text);
        AddPersonalRow("Факт. адрес", ActualAddressTextBox.Text);
        AddPersonalRow("Тип контрагента", string.IsNullOrWhiteSpace(type) ? string.Empty : type);
        AddPersonalRow("Контактных лиц", _contacts.Count > 0 ? _contacts.Count.ToString("N0", RuCulture) : string.Empty);
    }

    private void AddPersonalRow(string label, string? value)
    {
        var trimmed = Ui(value?.Trim() ?? string.Empty);
        var isFilled = !string.IsNullOrWhiteSpace(trimmed);
        _personalData.Add(new PersonalDataRow
        {
            Label = label,
            DisplayValue = isFilled ? trimmed : "—",
            IsFilled = isFilled
        });
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

        RenderPersonalDataChecklist();
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

    private void HandleAddFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл договора",
            Filter = "Документы и изображения|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.jpg;*.jpeg;*.png;*.txt|Все файлы|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            foreach (var sourcePath in dialog.FileNames)
            {
                AddCustomerFile(sourcePath);
            }

            RefreshFilesSummary();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Не удалось загрузить файл договора.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void HandleOpenFileClick(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not SalesCustomerFileEditorRow row)
        {
            ValidationText.Text = "Выберите файл для открытия.";
            return;
        }

        if (!File.Exists(row.StoredPath))
        {
            ValidationText.Text = "Файл не найден в хранилище приложения.";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = row.StoredPath,
            UseShellExecute = true
        });
    }

    private void HandleRemoveFileClick(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not SalesCustomerFileEditorRow row)
        {
            ValidationText.Text = "Выберите файл, который нужно убрать из карточки.";
            return;
        }

        _files.Remove(row);
        RefreshFilesSummary();
    }

    private void AddCustomerFile(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var customerId = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id;
        _draft.Id = customerId;

        var storageDirectory = Path.Combine(
            WorkspacePathResolver.ResolveWorkspaceRoot(),
            "app_data",
            "customer-files",
            customerId.ToString("N"));
        Directory.CreateDirectory(storageDirectory);

        var sourceName = Path.GetFileName(sourcePath);
        var extension = Path.GetExtension(sourceName);
        var storedName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}";
        var storedPath = Path.Combine(storageDirectory, storedName);
        File.Copy(sourcePath, storedPath, overwrite: false);

        var info = new FileInfo(storedPath);
        var row = new SalesCustomerFileEditorRow
        {
            Id = Guid.NewGuid(),
            FileName = sourceName,
            StoredPath = storedPath,
            Description = GuessFileDescription(sourceName),
            UploadedAt = DateTime.Now,
            UploadedBy = Environment.UserName,
            SizeBytes = info.Length
        };

        _files.Add(row);
        FilesGrid.SelectedItem = row;
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
            Contacts = new System.ComponentModel.BindingList<SalesCustomerContactRecord>(contacts),
            Files = new System.ComponentModel.BindingList<SalesCustomerFileRecord>(_files.Select(item => item.ToRecord()).ToList())
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

    private static string GuessFileDescription(string fileName)
    {
        var name = Ui(fileName);
        var extension = Path.GetExtension(name).ToLowerInvariant();
        if (name.Contains("договор", StringComparison.OrdinalIgnoreCase)
            || name.Contains("contract", StringComparison.OrdinalIgnoreCase))
        {
            return "Договор";
        }

        return extension switch
        {
            ".pdf" or ".doc" or ".docx" => "Документ",
            ".jpg" or ".jpeg" or ".png" => "Скан",
            ".xls" or ".xlsx" => "Таблица",
            _ => "Файл"
        };
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

public sealed class SalesCustomerFileEditorRow
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string StoredPath { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; }

    public string UploadedBy { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string UploadedAtDisplay => UploadedAt == default
        ? "-"
        : UploadedAt.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU"));

    public string SizeDisplay => SizeBytes <= 0
        ? "-"
        : SizeBytes < 1024 * 1024
            ? $"{SizeBytes / 1024d:N1} КБ"
            : $"{SizeBytes / 1024d / 1024d:N1} МБ";

    public static SalesCustomerFileEditorRow FromRecord(SalesCustomerFileRecord record)
    {
        return new SalesCustomerFileEditorRow
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            FileName = record.FileName,
            StoredPath = record.StoredPath,
            Description = record.Description,
            UploadedAt = record.UploadedAt,
            UploadedBy = record.UploadedBy,
            SizeBytes = record.SizeBytes
        };
    }

    public SalesCustomerFileRecord ToRecord()
    {
        return new SalesCustomerFileRecord
        {
            Id = Id == Guid.Empty ? Guid.NewGuid() : Id,
            FileName = FileName.Trim(),
            StoredPath = StoredPath.Trim(),
            Description = Description.Trim(),
            UploadedAt = UploadedAt == default ? DateTime.Now : UploadedAt,
            UploadedBy = UploadedBy.Trim(),
            SizeBytes = SizeBytes
        };
    }
}

public sealed class CustomerEventRow
{
    private static readonly SolidColorBrush SuccessDotBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#1AA65F")!;
    private static readonly SolidColorBrush ErrorDotBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FF3045")!;
    private static readonly SolidColorBrush SuccessPillBgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#E8F8EF")!;
    private static readonly SolidColorBrush ErrorPillBgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FFE8EA")!;
    private static readonly SolidColorBrush SuccessPillFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#1AA65F")!;
    private static readonly SolidColorBrush ErrorPillFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FF3045")!;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string Meta { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public bool IsSuccess { get; set; } = true;

    public Brush DotBrush => IsSuccess ? SuccessDotBrush : ErrorDotBrush;

    public Brush PillBackground => IsSuccess ? SuccessPillBgBrush : ErrorPillBgBrush;

    public Brush PillForeground => IsSuccess ? SuccessPillFgBrush : ErrorPillFgBrush;
}

public sealed class PersonalDataRow
{
    private static readonly SolidColorBrush FilledBgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#E8F8EF")!;
    private static readonly SolidColorBrush EmptyBgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#F3F6FF")!;
    private static readonly SolidColorBrush FilledFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#1AA65F")!;
    private static readonly SolidColorBrush EmptyFgBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#A9B7F7")!;
    private static readonly SolidColorBrush FilledValueBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#17213A")!;
    private static readonly SolidColorBrush EmptyValueBrush = (SolidColorBrush)new BrushConverter().ConvertFromString("#A9B7F7")!;

    public string Label { get; set; } = string.Empty;

    public string DisplayValue { get; set; } = string.Empty;

    public bool IsFilled { get; set; }

    public string BulletGlyph => IsFilled ? "" : "";

    public Brush BulletBackground => IsFilled ? FilledBgBrush : EmptyBgBrush;

    public Brush BulletForeground => IsFilled ? FilledFgBrush : EmptyFgBrush;

    public Brush ValueForeground => IsFilled ? FilledValueBrush : EmptyValueBrush;

    public FontWeight ValueFontWeight => IsFilled ? FontWeights.SemiBold : FontWeights.Normal;
}
