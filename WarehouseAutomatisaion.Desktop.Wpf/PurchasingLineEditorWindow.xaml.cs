using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingLineEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private readonly CatalogChoice[] _catalogChoices;
    private readonly IReadOnlyList<string> _targetLocationOptions;
    private readonly OperationalPurchasingLineRecord? _original;
    private readonly bool _allowTargetLocation;

    public PurchasingLineEditorWindow(
        string title,
        string subtitle,
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        OperationalPurchasingLineRecord? line = null,
        bool allowTargetLocation = false,
        IReadOnlyList<string>? targetLocationOptions = null)
    {
        _original = line;
        _allowTargetLocation = allowTargetLocation;
        _targetLocationOptions = (targetLocationOptions ?? Array.Empty<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _catalogChoices = catalogItems
            .Select(item => new CatalogChoice(item.Code, item.Name, item.Unit, item.DefaultPrice))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        InitializeComponent();
        HeaderTitleText.Text = title;
        HeaderSubtitleText.Text = subtitle;
        ItemComboBox.ItemsSource = _catalogChoices;
        TargetLocationTextBox.ItemsSource = _targetLocationOptions;
        TargetLocationPanel.Visibility = _allowTargetLocation ? Visibility.Visible : Visibility.Collapsed;
        LoadDraft();
    }

    public OperationalPurchasingLineRecord? ResultLine { get; private set; }

    private void LoadDraft()
    {
        if (_original is null)
        {
            QuantityTextBox.Text = 1m.ToString("N2", RuCulture);
            PriceTextBox.Text = 0m.ToString("N2", RuCulture);
            PlannedDatePicker.SelectedDate = DateTime.Today.AddDays(7);
            return;
        }

        var selected = _catalogChoices.FirstOrDefault(item => item.Code.Equals(_original.ItemCode, StringComparison.OrdinalIgnoreCase))
                       ?? _catalogChoices.FirstOrDefault(item => item.Name.Equals(_original.ItemName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            ItemComboBox.SelectedItem = selected;
        }
        else
        {
            CodeTextBox.Text = _original.ItemCode;
        }

        if (string.IsNullOrWhiteSpace(UnitTextBox.Text))
        {
            UnitTextBox.Text = _original.Unit;
        }

        QuantityTextBox.Text = _original.Quantity.ToString("N2", RuCulture);
        PriceTextBox.Text = _original.Price.ToString("N2", RuCulture);
        PlannedDatePicker.SelectedDate = _original.PlannedDate;
        TargetLocationTextBox.Text = _original.TargetLocation;
        RelatedDocumentTextBox.Text = _original.RelatedDocument;
    }

    private void HandleItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemComboBox.SelectedItem is not CatalogChoice choice)
        {
            return;
        }

        CodeTextBox.Text = choice.Code;
        if (string.IsNullOrWhiteSpace(UnitTextBox.Text) || _original is null)
        {
            UnitTextBox.Text = choice.Unit;
        }

        if (_original is null || _original.Price <= 0m)
        {
            PriceTextBox.Text = choice.Price.ToString("N2", RuCulture);
        }
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (ItemComboBox.SelectedItem is not CatalogChoice choice)
        {
            ValidationText.Text = "Выберите номенклатуру.";
            return;
        }

        if (!TryParseDecimal(QuantityTextBox.Text, out var quantity) || quantity <= 0m)
        {
            ValidationText.Text = "Количество должно быть больше нуля.";
            return;
        }

        if (!TryParseDecimal(PriceTextBox.Text, out var price) || price < 0m)
        {
            ValidationText.Text = "Цена указана в неверном формате.";
            return;
        }

        var unit = string.IsNullOrWhiteSpace(UnitTextBox.Text) ? choice.Unit : UnitTextBox.Text.Trim();
        ResultLine = new OperationalPurchasingLineRecord
        {
            Id = _original?.Id ?? Guid.NewGuid(),
            SectionName = string.IsNullOrWhiteSpace(_original?.SectionName) ? "Товары" : _original.SectionName,
            ItemCode = choice.Code,
            ItemName = choice.Name,
            Quantity = quantity,
            Unit = unit,
            Price = price,
            PlannedDate = PlannedDatePicker.SelectedDate,
            TargetLocation = _allowTargetLocation
                ? TargetLocationTextBox.Text.Trim()
                : _original?.TargetLocation ?? string.Empty,
            RelatedDocument = RelatedDocumentTextBox.Text.Trim(),
            Fields = _original?.Fields?.ToArray() ?? Array.Empty<WarehouseAutomatisaion.Infrastructure.Importing.OneCFieldValue>()
        };

        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        return decimal.TryParse(
            value,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
            RuCulture,
            out result);
    }

    private sealed record CatalogChoice(string Code, string Name, string Unit, decimal Price)
    {
        public string Label => string.IsNullOrWhiteSpace(Code) ? Name : $"{Name} ({Code})";
    }
}
