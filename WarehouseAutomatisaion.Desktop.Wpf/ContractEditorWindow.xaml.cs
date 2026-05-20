using System.Windows;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ContractEditorWindow : Window
{
    public ContractEditorWindow()
    {
        InitializeComponent();
    }

    public ContractEditorWindow(string counterpartyName, string counterpartyKind, string contractName)
        : this()
    {
        CounterpartyTextBox.Text = counterpartyName ?? string.Empty;
        CounterpartyKindText.Text = string.IsNullOrWhiteSpace(counterpartyKind) ? "—" : counterpartyKind;
        ContractNameCombo.Text = contractName ?? string.Empty;
        HeaderTitleText.Text = string.IsNullOrWhiteSpace(contractName)
            ? "Договор (Договор)"
            : $"{contractName} (Договор)";
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        // Сохранение в SalesWorkspace будет подключено отдельным шагом, когда
        // появится модель CustomerContract. Сейчас — заглушка, чтобы окно не падало.
        DialogResult = true;
    }

    private void HandleSaveAndCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
