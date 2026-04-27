using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesCustomerEditorWindow : Window
{
    private readonly SalesWorkspace _workspace;
    private readonly SalesCustomerRecord _draft;

    public SalesCustomerEditorWindow(SalesWorkspace workspace, SalesCustomerRecord? customer = null)
    {
        _workspace = workspace;
        _draft = customer?.Clone() ?? workspace.CreateCustomerDraft();

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = customer is null ? "Новый клиент" : $"Клиент {_draft.Code}";
        HeaderTitleText.Text = customer is null ? "Новый клиент" : "Карточка клиента";

        StatusComboBox.ItemsSource = workspace.CustomerStatuses.Select(Ui).ToArray();
        ManagerComboBox.ItemsSource = workspace.Managers.Select(Ui).ToArray();
        CurrencyComboBox.ItemsSource = workspace.Currencies.Select(Ui).ToArray();

        LoadDraft();
    }

    public SalesCustomerRecord? ResultCustomer { get; private set; }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void LoadDraft()
    {
        CodeTextBox.Text = Ui(_draft.Code);
        NameTextBox.Text = Ui(_draft.Name);
        ContractTextBox.Text = Ui(_draft.ContractNumber);
        PhoneTextBox.Text = Ui(_draft.Phone);
        EmailTextBox.Text = Ui(_draft.Email);
        NotesTextBox.Text = Ui(_draft.Notes);
        SelectComboValue(StatusComboBox, Ui(_draft.Status));
        SelectComboValue(ManagerComboBox, Ui(_draft.Manager));
        SelectComboValue(CurrencyComboBox, Ui(_draft.CurrencyCode));
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            ValidationText.Text = "Укажите название клиента.";
            return;
        }

        ResultCustomer = new SalesCustomerRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Code = string.IsNullOrWhiteSpace(CodeTextBox.Text) ? _draft.Code : CodeTextBox.Text.Trim(),
            Name = NameTextBox.Text.Trim(),
            ContractNumber = ContractTextBox.Text.Trim(),
            CurrencyCode = CurrencyComboBox.SelectedItem?.ToString() ?? CurrencyComboBox.Text.Trim(),
            Manager = ManagerComboBox.SelectedItem?.ToString() ?? ManagerComboBox.Text.Trim(),
            Status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text.Trim(),
            Phone = PhoneTextBox.Text.Trim(),
            Email = EmailTextBox.Text.Trim(),
            Notes = NotesTextBox.Text.Trim()
        };

        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static void SelectComboValue(System.Windows.Controls.ComboBox comboBox, string value)
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
}
