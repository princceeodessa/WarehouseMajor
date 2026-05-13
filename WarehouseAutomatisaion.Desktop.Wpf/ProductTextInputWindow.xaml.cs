using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ProductTextInputWindow : Window
{
    private readonly string[] _allOptions;
    private TextBox? _editableTextBox;
    private bool _updatingOptions;

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
        _allOptions = options?
            .Select(TextMojibakeFixer.NormalizeText)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray()
            ?? [];
        ValueComboBox.ItemsSource = _allOptions;
        ValueComboBox.Text = TextMojibakeFixer.NormalizeText(initialValue);

        Loaded += HandleLoaded;
    }

    public string ResultText { get; private set; } = string.Empty;

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        ValueComboBox.ApplyTemplate();
        if (ValueComboBox.Template.FindName("PART_EditableTextBox", ValueComboBox) is not TextBox textBox)
        {
            return;
        }

        _editableTextBox = textBox;
        _editableTextBox.TextChanged += HandleValueTextChanged;
    }

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

    private void HandleValueTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingOptions || _allOptions.Length == 0 || sender is not TextBox textBox)
        {
            return;
        }

        var text = textBox.Text;
        var caretIndex = textBox.CaretIndex;

        _updatingOptions = true;
        try
        {
            ValueComboBox.ItemsSource = FilterOptions(text);
            ValueComboBox.SelectedItem = null;
            ValueComboBox.Text = text;
            textBox.CaretIndex = Math.Min(caretIndex, textBox.Text.Length);
            ValueComboBox.IsDropDownOpen = ValueComboBox.IsKeyboardFocusWithin && ValueComboBox.Items.Count > 0;
        }
        finally
        {
            _updatingOptions = false;
        }
    }

    private string[] FilterOptions(string text)
    {
        var query = TextMojibakeFixer.NormalizeText(text).Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return _allOptions;
        }

        var tokens = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return _allOptions
            .Where(option => tokens.All(token => option.Contains(token, StringComparison.CurrentCultureIgnoreCase)))
            .Take(80)
            .ToArray();
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

    protected override void OnClosed(EventArgs e)
    {
        if (_editableTextBox is not null)
        {
            _editableTextBox.TextChanged -= HandleValueTextChanged;
        }

        Loaded -= HandleLoaded;
        base.OnClosed(e);
    }
}


