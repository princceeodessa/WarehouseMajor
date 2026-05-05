using System.Windows;
using System.Windows.Input;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ProductTextInputWindow : Window
{
    public ProductTextInputWindow(
        string title,
        string prompt,
        string? initialValue = null,
        IEnumerable<string>? options = null)
    {
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = TextMojibakeFixer.NormalizeText(title);
        TitleText.Text = TextMojibakeFixer.NormalizeText(title);
        PromptText.Text = TextMojibakeFixer.NormalizeText(prompt);
        ValueComboBox.ItemsSource = options?
            .Select(TextMojibakeFixer.NormalizeText)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        ValueComboBox.Text = TextMojibakeFixer.NormalizeText(initialValue);
    }

    public string ResultText { get; private set; } = string.Empty;

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ResultText = ResolveCurrentValue();
        if (string.IsNullOrWhiteSpace(ResultText))
        {
            MessageBox.Show(this, "Введите значение.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void HandleValueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        HandleSaveClick(sender, e);
    }

    private string ResolveCurrentValue()
    {
        var selected = ValueComboBox.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            return TextMojibakeFixer.NormalizeText(selected).Trim();
        }

        return TextMojibakeFixer.NormalizeText(ValueComboBox.Text).Trim();
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}


