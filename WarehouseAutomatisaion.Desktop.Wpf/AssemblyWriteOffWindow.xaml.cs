using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class AssemblyWriteOffWindow : Window, IHostedWmsOperationWindow
{
    private readonly IStorageCellCatalog _cellCatalog;
    private readonly IStockLocationRepository _stockLocations;
    private readonly IWarehouseStockOperationService _stockOperations;
    private readonly string _actor;

    private IReadOnlyList<StorageCell>? _cellCache;
    private StorageCell? _selectedCell;
    private StockLocation? _selectedLocation;

    public Window? DialogOwnerOverride { get; set; }

    public Action? HostCloseRequested { get; set; }

    public AssemblyWriteOffWindow(
        IStorageCellCatalog cellCatalog,
        IStockLocationRepository stockLocations,
        IWarehouseStockOperationService stockOperations,
        string actor)
    {
        InitializeComponent();
        _cellCatalog = cellCatalog;
        _stockLocations = stockLocations;
        _stockOperations = stockOperations;
        _actor = string.IsNullOrWhiteSpace(actor) ? "Кладовщик" : actor.Trim();
        ReasonBox.SelectedIndex = 0;
        StatusText.Text = "Выберите ячейку и позицию для списания по сборке.";
        Loaded += (_, _) => StatusText.Text = "Выберите ячейку и позицию для списания по сборке.";
    }

    private async void OnPickCellClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            PickCellButton.IsEnabled = false;
            _cellCache ??= await _cellCatalog.GetAllAsync();
            var picker = new CellPickerWindow(_cellCache, _selectedCell?.Code) { Owner = GetDialogOwner() };
            if (picker.ShowDialog() == true && picker.SelectedCell is not null)
            {
                _selectedCell = picker.SelectedCell;
                SelectedCellText.Text = $"{_selectedCell.Code} · {_selectedCell.WarehouseName}";
                SelectedCellText.Foreground = System.Windows.Media.Brushes.Black;
                await LoadCellAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось выбрать ячейку: {ex.Message}";
        }
        finally
        {
            PickCellButton.IsEnabled = true;
        }
    }

    private async Task LoadCellAsync()
    {
        if (_selectedCell is null)
        {
            return;
        }

        var rows = (await _stockLocations.GetByCellAsync(_selectedCell.Id))
            .Where(row => row.AvailableQuantity > 0)
            .OrderBy(row => row.ItemCode)
            .ToArray();
        CellContentGrid.ItemsSource = rows;
        _selectedLocation = null;
        SelectedItemText.Text = rows.Length == 0
            ? "В ячейке нет доступных позиций для списания."
            : "Выберите позицию слева.";
        UpdateButton();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedLocation = CellContentGrid.SelectedItem as StockLocation;
        SelectedItemText.Text = _selectedLocation is null
            ? "Выберите позицию слева."
            : $"{_selectedLocation.ItemCode} · {_selectedLocation.ItemName}\nДоступно: {_selectedLocation.AvailableQuantity:N3}";
        UpdateButton();
    }

    private void OnAllClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedLocation is not null)
        {
            QuantityBox.Text = _selectedLocation.AvailableQuantity.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    private void OnReasonChanged(object sender, SelectionChangedEventArgs e) => UpdateButton();

    private void OnFormChanged(object sender, TextChangedEventArgs e) => UpdateButton();

    private async void OnWriteOffClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedCell is null || _selectedLocation is null)
        {
            StatusText.Text = "❌ Выберите ячейку и позицию.";
            return;
        }

        if (!TryParseQuantity(QuantityBox.Text, out var quantity) || quantity <= 0)
        {
            StatusText.Text = "❌ Укажите положительное количество.";
            return;
        }

        var reason = GetReason();
        if (string.IsNullOrWhiteSpace(reason))
        {
            StatusText.Text = "❌ Укажите причину списания.";
            return;
        }

        try
        {
            WriteOffButton.IsEnabled = false;
            StatusText.Text = "⏳ Проводим списание...";
            var result = await _stockOperations.WriteOffAsync(new StockWriteOffRequest(
                ItemId: _selectedLocation.ItemId,
                SourceCellId: _selectedCell.Id,
                Quantity: quantity,
                Actor: _actor,
                Reason: reason,
                RelatedDocument: RelatedDocumentBox.Text,
                Comment: CommentBox.Text));

            if (!result.Succeeded)
            {
                StatusText.Text = $"❌ {result.Message}";
                return;
            }

            StatusText.Text = $"✅ {result.Message}";
            QuantityBox.Clear();
            await LoadCellAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось списать: {ex.Message}";
        }
        finally
        {
            UpdateButton();
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        CloseHostedOrWindow();
    }

    private void UpdateButton()
    {
        var ready = _selectedLocation is not null
                    && TryParseQuantity(QuantityBox.Text, out var quantity)
                    && quantity > 0
                    && quantity <= _selectedLocation.AvailableQuantity
                    && !string.IsNullOrWhiteSpace(GetReason());
        WriteOffButton.IsEnabled = ready;
    }

    private string GetReason()
    {
        return ReasonBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? string.Empty
            : string.Empty;
    }

    private static bool TryParseQuantity(string? text, out decimal value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text)
               && decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private Window GetDialogOwner()
    {
        return DialogOwnerOverride
               ?? System.Windows.Application.Current?.MainWindow
               ?? this;
    }

    private void CloseHostedOrWindow()
    {
        if (HostCloseRequested is not null)
        {
            HostCloseRequested.Invoke();
            return;
        }

        DialogResult = true;
        Close();
    }
}
