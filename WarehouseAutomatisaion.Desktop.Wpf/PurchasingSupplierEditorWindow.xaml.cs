using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingSupplierEditorWindow : Window
{
    private readonly OperationalPurchasingWorkspace _workspace;
    private readonly OperationalPurchasingSupplierRecord _draft;

    public PurchasingSupplierEditorWindow(
        OperationalPurchasingWorkspace workspace,
        OperationalPurchasingSupplierRecord? supplier = null)
    {
        _workspace = workspace;
        _draft = supplier?.Clone() ?? workspace.CreateSupplierDraft();

        InitializeComponent();
        Title = supplier is null ? "Новый поставщик" : $"Поставщик {_draft.Name}";
        HeaderTitleText.Text = supplier is null ? "Новый поставщик" : "Карточка поставщика";
        HeaderSubtitleText.Text = "Контакты, договор и статус поставщика закупочного контура.";
        StatusComboBox.ItemsSource = _workspace.SupplierStatuses.ToArray();
        LoadDraft();
    }

    public OperationalPurchasingSupplierRecord? ResultSupplier { get; private set; }

    private void LoadDraft()
    {
        NameTextBox.Text = _draft.Name;
        CodeTextBox.Text = _draft.Code;
        StatusComboBox.SelectedItem = _workspace.SupplierStatuses.FirstOrDefault(item => item.Equals(_draft.Status, StringComparison.OrdinalIgnoreCase))
                                     ?? _workspace.SupplierStatuses.FirstOrDefault();
        TaxIdTextBox.Text = _draft.TaxId;
        PhoneTextBox.Text = _draft.Phone;
        EmailTextBox.Text = _draft.Email;
        ContractTextBox.Text = _draft.Contract;
        SourceTextBox.Text = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный контур" : _draft.SourceLabel;
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            ValidationText.Text = "Укажите название поставщика.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CodeTextBox.Text))
        {
            ValidationText.Text = "Укажите код поставщика.";
            return;
        }

        ResultSupplier = new OperationalPurchasingSupplierRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Name = NameTextBox.Text.Trim(),
            Code = CodeTextBox.Text.Trim(),
            Status = StatusComboBox.SelectedItem?.ToString() ?? _workspace.SupplierStatuses.First(),
            TaxId = TaxIdTextBox.Text.Trim(),
            Phone = PhoneTextBox.Text.Trim(),
            Email = EmailTextBox.Text.Trim(),
            Contract = ContractTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(SourceTextBox.Text) ? "Локальный контур" : SourceTextBox.Text.Trim(),
            Fields = _draft.Fields.ToArray()
        };

        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
