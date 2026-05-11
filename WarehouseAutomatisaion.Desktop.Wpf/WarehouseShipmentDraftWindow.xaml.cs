using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseShipmentDraftWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly ObservableCollection<WarehouseShipmentDraftLine> _lines;
    private readonly List<CustomerOption> _customerOptions;

    public WarehouseShipmentDraftWindow(
        SalesWorkspace workspace,
        IEnumerable<WarehouseShipmentDraftLine> lines,
        string title = "Подготовка отгрузки",
        string subtitle = "Выберите клиента и уточните количество по складским позициям.",
        string confirmText = "Создать отгрузку")
    {
        _lines = new ObservableCollection<WarehouseShipmentDraftLine>(lines);
        _customerOptions = workspace.Customers
            .OrderByDescending(customer => Ui(customer.Status).Equals("Активен", StringComparison.OrdinalIgnoreCase))
            .ThenBy(customer => Ui(customer.Name), StringComparer.CurrentCultureIgnoreCase)
            .Select(customer => new CustomerOption(customer))
            .ToList();

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        Title = title;
        HeaderTitleText.Text = title;
        HeaderSubtitleText.Text = subtitle;
        SaveButton.Content = confirmText;

        CustomerComboBox.ItemsSource = _customerOptions;
        CustomerComboBox.SelectedItem = _customerOptions.FirstOrDefault();
        LinesGrid.ItemsSource = _lines;
        WarehouseTextBox.Text = string.Join(", ", _lines.Select(line => line.Warehouse).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().Take(3));

        RefreshTotal();
    }

    public SalesCustomerRecord? ResultCustomer { get; private set; }

    public IReadOnlyList<WarehouseShipmentDraftLine> ResultLines { get; private set; } = Array.Empty<WarehouseShipmentDraftLine>();

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private void HandleCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(RefreshTotal, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (CustomerComboBox.SelectedItem is not CustomerOption customerOption)
        {
            ValidationText.Text = "Выберите клиента.";
            return;
        }

        var selectedLines = _lines
            .Where(line => line.Quantity > 0m)
            .ToArray();
        if (selectedLines.Length == 0)
        {
            ValidationText.Text = "Укажите количество хотя бы по одной позиции.";
            return;
        }

        var invalidLine = selectedLines.FirstOrDefault(line => line.AvailableQuantity > 0m && line.Quantity > line.AvailableQuantity);
        if (invalidLine is not null)
        {
            ValidationText.Text = $"Количество по позиции {invalidLine.Code} больше свободного остатка.";
            return;
        }

        if (selectedLines.Any(line => line.AvailableQuantity <= 0m))
        {
            ValidationText.Text = "В отгрузке есть позиция без свободного остатка.";
            return;
        }

        ResultCustomer = customerOption.Customer;
        ResultLines = selectedLines.Select(line => line.Clone()).ToArray();
        DialogResult = true;
    }

    private void RefreshTotal()
    {
        foreach (var line in _lines)
        {
            line.RefreshCalculatedValues();
        }

        var total = _lines.Sum(line => line.Amount);
        TotalText.Text = $"Позиций: {_lines.Count:N0}. Сумма: {total:N2} ₽";
    }

    private sealed class CustomerOption
    {
        public CustomerOption(SalesCustomerRecord customer)
        {
            Customer = customer;
            Label = $"{Ui(customer.Name)} - {Ui(customer.Code)}";
        }

        public SalesCustomerRecord Customer { get; }

        public string Label { get; }
    }
}

public sealed class WarehouseShipmentDraftLine : INotifyPropertyChanged
{
    private decimal _quantity;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Warehouse { get; init; } = string.Empty;

    public string Unit { get; init; } = "шт";

    public decimal AvailableQuantity { get; init; }

    public decimal Price { get; init; }

    public decimal Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value)
            {
                return;
            }

            _quantity = value;
            OnPropertyChanged(nameof(Quantity));
            RefreshCalculatedValues();
        }
    }

    public string AvailableDisplay => AvailableQuantity.ToString("N2", CultureInfo.GetCultureInfo("ru-RU"));

    public string PriceDisplay => Price.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    public decimal Amount => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);

    public string AmountDisplay => Amount.ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";

    public WarehouseShipmentDraftLine Clone()
    {
        return new WarehouseShipmentDraftLine
        {
            Code = Code,
            Name = Name,
            Warehouse = Warehouse,
            Unit = Unit,
            AvailableQuantity = AvailableQuantity,
            Price = Price,
            Quantity = Quantity
        };
    }

    public void RefreshCalculatedValues()
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(AmountDisplay));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
