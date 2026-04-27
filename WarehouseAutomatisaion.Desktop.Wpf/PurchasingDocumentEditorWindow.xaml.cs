using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public enum PurchasingDocumentEditorMode
{
    PurchaseOrder,
    SupplierInvoice,
    PurchaseReceipt
}

public partial class PurchasingDocumentEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly OperationalPurchasingWorkspace _workspace;
    private readonly OperationalPurchasingDocumentRecord _draft;
    private readonly ObservableCollection<OperationalPurchasingLineRecord> _lines;
    private readonly PurchasingDocumentEditorMode _mode;

    public PurchasingDocumentEditorWindow(
        OperationalPurchasingWorkspace workspace,
        PurchasingDocumentEditorMode mode = PurchasingDocumentEditorMode.PurchaseOrder,
        OperationalPurchasingDocumentRecord? document = null,
        Guid? preselectedSupplierId = null)
    {
        _workspace = workspace;
        _mode = mode;
        _draft = document?.Clone() ?? CreateDraft(workspace, mode, preselectedSupplierId);
        _lines = new ObservableCollection<OperationalPurchasingLineRecord>(_draft.Lines.Select(line => line.Clone()));

        InitializeComponent();
        ConfigureHeader(document is null);

        SupplierComboBox.ItemsSource = _workspace.Suppliers.ToArray();
        WarehouseComboBox.ItemsSource = _workspace.Warehouses.ToArray();
        StatusComboBox.ItemsSource = ResolveStatuses().ToArray();
        LinesGrid.ItemsSource = _lines;
        LoadDraft();
        RefreshTotals();
    }

    public OperationalPurchasingDocumentRecord? ResultDocument { get; private set; }

    private void LoadDraft()
    {
        NumberTextBox.Text = _draft.Number;
        DocumentDatePicker.SelectedDate = _draft.DocumentDate == default ? DateTime.Today : _draft.DocumentDate;
        SelectSupplier(_draft.SupplierId, _draft.SupplierName);
        SelectValue(WarehouseComboBox, _draft.Warehouse);
        SelectValue(StatusComboBox, _draft.Status);
        ContractTextBox.Text = _draft.Contract;
    }

    private void HandleSupplierSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SupplierComboBox.SelectedItem is not OperationalPurchasingSupplierRecord supplier)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ContractTextBox.Text) || string.Equals(ContractTextBox.Text, _draft.Contract, StringComparison.OrdinalIgnoreCase))
        {
            ContractTextBox.Text = supplier.Contract;
        }
    }

    private void HandleAddLineClick(object sender, RoutedEventArgs e)
    {
        var dialog = new PurchasingLineEditorWindow(
            "Новая позиция",
            "Добавьте позицию закупки: номенклатуру, количество, цену и плановую дату.",
            _workspace.CatalogItems)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.ResultLine is null)
        {
            return;
        }

        _lines.Add(dialog.ResultLine);
        LinesGrid.SelectedItem = dialog.ResultLine;
        RefreshTotals();
    }

    private void HandleEditLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not OperationalPurchasingLineRecord line)
        {
            ValidationText.Text = "Выберите позицию закупки.";
            return;
        }

        var dialog = new PurchasingLineEditorWindow(
            "Изменение позиции",
            "Исправьте товар, количество, цену или плановую дату поставки.",
            _workspace.CatalogItems,
            line)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.ResultLine is null)
        {
            return;
        }

        var index = _lines.IndexOf(line);
        if (index >= 0)
        {
            _lines[index] = dialog.ResultLine;
            LinesGrid.SelectedItem = dialog.ResultLine;
            RefreshTotals();
        }
    }

    private void HandleRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not OperationalPurchasingLineRecord line)
        {
            ValidationText.Text = "Выберите позицию закупки.";
            return;
        }

        var result = MessageBox.Show(this, "Удалить выбранную позицию?", "Закупка", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _lines.Remove(line);
        RefreshTotals();
    }

    private void HandleLinesSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ValidationText.Text) && LinesGrid.SelectedItem is not null)
        {
            ValidationText.Text = string.Empty;
        }
    }

    private void RefreshTotals()
    {
        var total = _lines.Sum(item => item.Amount);
        var caption = _mode switch
        {
            PurchasingDocumentEditorMode.SupplierInvoice => "Итого счета",
            PurchasingDocumentEditorMode.PurchaseReceipt => "Итого приемки",
            _ => "Итого заказа"
        };
        TotalText.Text = $"{caption}: {total:N2} ₽";
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(NumberTextBox.Text))
        {
            ValidationText.Text = "Укажите номер документа.";
            return;
        }

        if (SupplierComboBox.SelectedItem is not OperationalPurchasingSupplierRecord supplier)
        {
            ValidationText.Text = "Выберите поставщика.";
            return;
        }

        var warehouse = WarehouseComboBox.SelectedItem?.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(warehouse))
        {
            ValidationText.Text = "Укажите склад.";
            return;
        }

        if (_lines.Count == 0)
        {
            ValidationText.Text = "Добавьте хотя бы одну позицию закупки.";
            return;
        }

        ResultDocument = new OperationalPurchasingDocumentRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            DocumentType = ResolveDocumentType(),
            Number = NumberTextBox.Text.Trim(),
            DocumentDate = (DocumentDatePicker.SelectedDate ?? DateTime.Today).Date,
            DueDate = ResolveDueDate(),
            SupplierId = supplier.Id,
            SupplierName = supplier.Name,
            Status = StatusComboBox.SelectedItem?.ToString() ?? ResolveStatuses().First(),
            Contract = ContractTextBox.Text.Trim(),
            Warehouse = warehouse,
            RelatedOrderId = _draft.RelatedOrderId,
            RelatedOrderNumber = _draft.RelatedOrderNumber,
            Comment = _draft.Comment,
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный контур" : _draft.SourceLabel,
            Fields = _draft.Fields.ToArray(),
            Lines = new BindingList<OperationalPurchasingLineRecord>(_lines.Select(item => item.Clone()).ToList())
        };

        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SelectSupplier(Guid supplierId, string supplierName)
    {
        var selected = supplierId != Guid.Empty
            ? _workspace.Suppliers.FirstOrDefault(item => item.Id == supplierId)
            : _workspace.Suppliers.FirstOrDefault(item => item.Name.Equals(supplierName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            SupplierComboBox.SelectedItem = selected;
        }
    }

    private static void SelectValue(System.Windows.Controls.ComboBox comboBox, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
            return;
        }

        var item = comboBox.Items.Cast<object>().FirstOrDefault(entry => string.Equals(entry?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        comboBox.SelectedItem = item ?? comboBox.Items.Cast<object>().FirstOrDefault();
    }

    private void ConfigureHeader(bool isNew)
    {
        var caption = _mode switch
        {
            PurchasingDocumentEditorMode.SupplierInvoice => "Счет поставщика",
            PurchasingDocumentEditorMode.PurchaseReceipt => "Приемка",
            _ => "Заказ поставщику"
        };

        Title = isNew ? $"Новый документ: {caption}" : $"{caption} {_draft.Number}";
        HeaderTitleText.Text = isNew ? $"Новый документ: {caption}" : caption;
        HeaderSubtitleText.Text = _mode switch
        {
            PurchasingDocumentEditorMode.SupplierInvoice => "Поставщик, склад, статус и состав входящего счета поставщика.",
            PurchasingDocumentEditorMode.PurchaseReceipt => "Поставщик, склад, статус и позиции входящей приемки.",
            _ => "Поставщик, склад, статус и табличная часть заказа поставщику."
        };
    }

    private static OperationalPurchasingDocumentRecord CreateDraft(
        OperationalPurchasingWorkspace workspace,
        PurchasingDocumentEditorMode mode,
        Guid? preselectedSupplierId)
    {
        return mode switch
        {
            PurchasingDocumentEditorMode.SupplierInvoice => new OperationalPurchasingDocumentRecord
            {
                Id = Guid.NewGuid(),
                DocumentType = "Счет поставщика",
                Number = $"СП-{DateTime.Now:yyyyMMdd-HHmm}",
                DocumentDate = DateTime.Today,
                DueDate = DateTime.Today.AddDays(5),
                SupplierId = preselectedSupplierId ?? workspace.Suppliers.FirstOrDefault()?.Id ?? Guid.Empty,
                SupplierName = preselectedSupplierId is null
                    ? workspace.Suppliers.FirstOrDefault()?.Name ?? string.Empty
                    : workspace.Suppliers.FirstOrDefault(item => item.Id == preselectedSupplierId.Value)?.Name ?? string.Empty,
                Status = workspace.SupplierInvoiceStatuses.First(),
                Warehouse = workspace.Warehouses.FirstOrDefault() ?? string.Empty,
                SourceLabel = "Локальный контур",
                Lines = new BindingList<OperationalPurchasingLineRecord>()
            },
            PurchasingDocumentEditorMode.PurchaseReceipt => new OperationalPurchasingDocumentRecord
            {
                Id = Guid.NewGuid(),
                DocumentType = "Приемка",
                Number = $"ПР-{DateTime.Now:yyyyMMdd-HHmm}",
                DocumentDate = DateTime.Today,
                SupplierId = preselectedSupplierId ?? workspace.Suppliers.FirstOrDefault()?.Id ?? Guid.Empty,
                SupplierName = preselectedSupplierId is null
                    ? workspace.Suppliers.FirstOrDefault()?.Name ?? string.Empty
                    : workspace.Suppliers.FirstOrDefault(item => item.Id == preselectedSupplierId.Value)?.Name ?? string.Empty,
                Status = workspace.PurchaseReceiptStatuses.First(),
                Warehouse = workspace.Warehouses.FirstOrDefault() ?? string.Empty,
                SourceLabel = "Локальный контур",
                Lines = new BindingList<OperationalPurchasingLineRecord>()
            },
            _ => workspace.CreatePurchaseOrderDraft(preselectedSupplierId)
        };
    }

    private IReadOnlyList<string> ResolveStatuses()
    {
        return _mode switch
        {
            PurchasingDocumentEditorMode.SupplierInvoice => _workspace.SupplierInvoiceStatuses,
            PurchasingDocumentEditorMode.PurchaseReceipt => _workspace.PurchaseReceiptStatuses,
            _ => _workspace.PurchaseOrderStatuses
        };
    }

    private string ResolveDocumentType()
    {
        return _mode switch
        {
            PurchasingDocumentEditorMode.SupplierInvoice => "Счет поставщика",
            PurchasingDocumentEditorMode.PurchaseReceipt => "Приемка",
            _ => string.IsNullOrWhiteSpace(_draft.DocumentType) ? "Заказ поставщику" : _draft.DocumentType
        };
    }

    private DateTime? ResolveDueDate()
    {
        if (_mode != PurchasingDocumentEditorMode.SupplierInvoice)
        {
            return _draft.DueDate;
        }

        return _draft.DueDate ?? (DocumentDatePicker.SelectedDate ?? DateTime.Today).Date.AddDays(5);
    }
}
