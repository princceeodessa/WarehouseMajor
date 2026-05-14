using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SalesReturnEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] ReturnStatuses = ["Черновик", "Проведено", "Отменено"];

    private readonly SalesWorkspace _workspace;
    private readonly SalesReturnRecord _draft;
    private readonly ObservableCollection<SalesReturnLineRow> _lines = [];

    public SalesReturnEditorWindow(SalesWorkspace workspace, SalesReturnRecord returnDocument)
    {
        _workspace = workspace;
        _draft = returnDocument.Clone();

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        LinesGrid.ItemsSource = _lines;
        StatusComboBox.ItemsSource = ReturnStatuses;
        WarehouseComboBox.ItemsSource = _workspace.Warehouses.Select(Ui).ToArray();
        ManagerComboBox.ItemsSource = _workspace.Managers
            .Select(SalesManagerDisplayResolver.Resolve)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        LoadDraft();
        RefreshTotal();
    }

    public SalesReturnRecord? ResultReturn { get; private set; }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value ?? string.Empty);

    private static bool IsLegacyDraftReason(string? value)
    {
        return Ui(value).Contains("Черновик возврата", StringComparison.OrdinalIgnoreCase);
    }

    private void LoadDraft()
    {
        NumberTextBox.Text = Ui(_draft.Number);
        ReturnDatePicker.SelectedDate = _draft.ReturnDate == default ? DateTime.Today : _draft.ReturnDate;
        OrderTextBox.Text = Ui(_draft.SalesOrderNumber);
        CustomerTextBox.Text = Ui(_draft.CustomerName);
        SelectComboValue(StatusComboBox, Ui(_draft.Status));
        SelectComboValue(WarehouseComboBox, Ui(_draft.Warehouse));
        SelectComboValue(ManagerComboBox, SalesManagerDisplayResolver.Resolve(_draft.Manager));
        ReasonTextBox.Text = IsLegacyDraftReason(_draft.Reason) ? string.Empty : Ui(_draft.Reason);
        CommentTextBox.Text = Ui(_draft.Comment);
        ReplaceLines(_draft.Lines);

        HeaderTitleText.Text = string.IsNullOrWhiteSpace(_draft.Number)
            ? "Новый возврат"
            : $"Возврат {_draft.Number}";
        HeaderSubtitleText.Text = string.IsNullOrWhiteSpace(_draft.SalesOrderNumber)
            ? "Возврат покупателя с позициями и ручным комментарием."
            : $"Заказ-основание: {_draft.SalesOrderNumber}. Покупатель: {Ui(_draft.CustomerName)}.";
    }

    private void HandleRestoreOrderLinesClick(object sender, RoutedEventArgs e)
    {
        var order = FindOrder(_draft.SalesOrderId, _draft.SalesOrderNumber);
        if (order is null)
        {
            ValidationText.Text = "Заказ-основание не найден. Позиции нельзя подтянуть автоматически.";
            return;
        }

        ReplaceLines(order.Lines);
        ValidationText.Text = string.Empty;
        RefreshTotal();
    }

    private void HandleEditLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not SalesReturnLineRow row)
        {
            ValidationText.Text = "Выберите позицию возврата.";
            return;
        }

        var quantity = PromptDecimal(
            "Количество возврата",
            $"Введите количество возврата ({row.Unit}).",
            row.Quantity.ToString("N2", RuCulture));
        if (quantity <= 0m)
        {
            return;
        }

        var price = PromptDecimal(
            "Цена возврата",
            "Введите цену для расчета суммы возврата.",
            row.Price.ToString("N2", RuCulture));
        if (price < 0m)
        {
            return;
        }

        var index = _lines.IndexOf(row);
        if (index >= 0)
        {
            _lines[index] = row with { Quantity = quantity, Price = price };
            LinesGrid.SelectedItem = _lines[index];
            ValidationText.Text = string.Empty;
            RefreshTotal();
        }
    }

    private void HandleLinesGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        HandleEditLineClick(sender, e);
    }

    private void HandleRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not SalesReturnLineRow row)
        {
            ValidationText.Text = "Выберите позицию возврата.";
            return;
        }

        var result = MessageBox.Show(this, "Удалить выбранную позицию из возврата?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _lines.Remove(row);
        ValidationText.Text = string.Empty;
        RefreshTotal();
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(NumberTextBox.Text))
        {
            ValidationText.Text = "Укажите номер возврата.";
            return;
        }

        if (ReturnDatePicker.SelectedDate is null)
        {
            ValidationText.Text = "Укажите дату возврата.";
            return;
        }

        if (string.IsNullOrWhiteSpace(WarehouseComboBox.Text))
        {
            ValidationText.Text = "Укажите склад возврата.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ManagerComboBox.Text))
        {
            ValidationText.Text = "Укажите менеджера.";
            return;
        }

        if (!ValidateLines())
        {
            return;
        }

        var result = _draft.Clone();
        result.Number = NumberTextBox.Text.Trim();
        result.ReturnDate = ReturnDatePicker.SelectedDate.Value.Date;
        result.Status = StatusComboBox.SelectedItem?.ToString() ?? StatusComboBox.Text.Trim();
        result.Warehouse = WarehouseComboBox.Text.Trim();
        result.Manager = ManagerComboBox.Text.Trim();
        result.Reason = ReasonTextBox.Text.Trim();
        result.Comment = CommentTextBox.Text.Trim();
        result.Lines = ToSalesLines();

        ResultReturn = result;
        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private bool ValidateLines()
    {
        if (_lines.Count == 0)
        {
            ValidationText.Text = "Оставьте хотя бы одну позицию возврата.";
            return false;
        }

        for (var index = 0; index < _lines.Count; index++)
        {
            var line = _lines[index];
            if (string.IsNullOrWhiteSpace(line.ItemCode) && string.IsNullOrWhiteSpace(line.ItemName))
            {
                ValidationText.Text = $"Позиция {index + 1}: укажите товар.";
                return false;
            }

            if (line.Quantity <= 0m)
            {
                ValidationText.Text = $"Позиция {index + 1}: количество должно быть больше нуля.";
                return false;
            }

            if (line.Price < 0m)
            {
                ValidationText.Text = $"Позиция {index + 1}: цена не может быть отрицательной.";
                return false;
            }
        }

        return true;
    }

    private SalesOrderRecord? FindOrder(Guid orderId, string orderNumber)
    {
        return _workspace.Orders.FirstOrDefault(item => item.Id == orderId)
            ?? _workspace.Orders.FirstOrDefault(item => Ui(item.Number).Equals(Ui(orderNumber), StringComparison.OrdinalIgnoreCase));
    }

    private void ReplaceLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        _lines.Clear();
        foreach (var line in lines)
        {
            _lines.Add(new SalesReturnLineRow(
                Ui(line.ItemCode),
                Ui(line.ItemName),
                SalesDocumentDisplayFormatter.NormalizeUnit(line.Unit, line.ItemName),
                line.Quantity,
                line.Price));
        }
    }

    private BindingList<SalesOrderLineRecord> ToSalesLines()
    {
        return new BindingList<SalesOrderLineRecord>(_lines.Select(line => new SalesOrderLineRecord
        {
            Id = Guid.NewGuid(),
            ItemCode = line.ItemCode,
            ItemName = line.ItemName,
            Unit = SalesDocumentDisplayFormatter.NormalizeUnit(line.Unit, line.ItemName),
            Quantity = line.Quantity,
            Price = line.Price
        }).ToList());
    }

    private void RefreshTotal()
    {
        var subtotal = Math.Round(_lines.Sum(item => item.Amount), 2, MidpointRounding.AwayFromZero);
        var discount = CalculateDiscount(subtotal, _draft.ManualDiscountPercent, _draft.ManualDiscountAmount);
        var total = Math.Round(Math.Max(0m, subtotal - discount), 2, MidpointRounding.AwayFromZero);
        TotalText.Text = $"Позиции: {_lines.Count:N0}. Сумма возврата: {FormatMoney(total, _draft.CurrencyCode)}.";
    }

    private static decimal CalculateDiscount(decimal subtotal, decimal percent, decimal amount)
    {
        if (subtotal <= 0m)
        {
            return 0m;
        }

        var rawDiscount = amount > 0m
            ? amount
            : subtotal * Math.Clamp(percent, 0m, 100m) / 100m;
        return Math.Min(subtotal, Math.Round(Math.Max(0m, rawDiscount), 2, MidpointRounding.AwayFromZero));
    }

    private decimal PromptDecimal(string title, string prompt, string initialValue)
    {
        var dialog = new ProductTextInputWindow(title, prompt, initialValue, Array.Empty<string>());
        WpfDialogOwner.TrySetOwner(dialog, this);

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResultText))
        {
            return -1m;
        }

        if (TryParseDecimal(dialog.ResultText, out var value))
        {
            return value;
        }

        MessageBox.Show(this, "Введите корректное число.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
        return -1m;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        value = Ui(value)
            .Replace("₽", string.Empty, StringComparison.Ordinal)
            .Replace('\u00A0', ' ')
            .Replace(" ", string.Empty);

        return decimal.TryParse(
                   value,
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                   RuCulture,
                   out result)
               || decimal.TryParse(
                   value.Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out result);
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

            if (comboBox.IsEditable)
            {
                comboBox.SelectedItem = null;
                comboBox.Text = value;
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

    private sealed record SalesReturnLineRow(
        string ItemCode,
        string ItemName,
        string Unit,
        decimal Quantity,
        decimal Price)
    {
        public decimal Amount => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);

        public string QuantityDisplay => Quantity.ToString("N2", RuCulture);

        public string PriceDisplay => $"{Price:N2} ₽";

        public string AmountDisplay => $"{Amount:N2} ₽";
    }
}
