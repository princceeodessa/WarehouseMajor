using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class QuickScanWorkspaceView : UserControl
{
    private readonly IProductBarcodeLookup _productLookup;
    private readonly IStorageCellLookup _cellLookup;
    private readonly IStockLocationRepository _stockLocations;
    private readonly IScanOperationLogger _operationLogger;
    private readonly string _actorUserName;

    private IReadOnlyList<StockLocation> _currentLocations = Array.Empty<StockLocation>();
    private string _currentHeader = string.Empty;
    private string _currentSubheader = string.Empty;
    private CancellationTokenSource? _lookupCancellation;

    public QuickScanWorkspaceView(
        IProductBarcodeLookup productLookup,
        IStorageCellLookup cellLookup,
        IStockLocationRepository stockLocations,
        IScanOperationLogger operationLogger,
        string actorUserName)
    {
        InitializeComponent();
        _productLookup = productLookup;
        _cellLookup = cellLookup;
        _stockLocations = stockLocations;
        _operationLogger = operationLogger;
        _actorUserName = string.IsNullOrWhiteSpace(actorUserName) ? "WMS" : actorUserName.Trim();
        Loaded += (_, _) => ScanTextBox.Focus();
    }

    private async void OnSearchClicked(object sender, RoutedEventArgs e)
    {
        await RunLookupAsync();
    }

    private async void OnScanKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await RunLookupAsync();
    }

    private async Task RunLookupAsync()
    {
        var scanValue = ScanTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(scanValue))
        {
            SetEmptyState("Введите или отсканируйте значение.");
            return;
        }

        _lookupCancellation?.Cancel();
        _lookupCancellation?.Dispose();
        _lookupCancellation = new CancellationTokenSource();
        var cancellationToken = _lookupCancellation.Token;

        SearchButton.IsEnabled = false;
        OpenPopupButton.IsEnabled = false;
        StatusText.Text = "Ищу по складу...";

        try
        {
            var preferCell = scanValue.StartsWith("MWH|", StringComparison.OrdinalIgnoreCase);
            if (preferCell && await TryShowCellAsync(scanValue, cancellationToken))
            {
                return;
            }

            if (await TryShowProductAsync(scanValue, cancellationToken))
            {
                return;
            }

            if (!preferCell && await TryShowCellAsync(scanValue, cancellationToken))
            {
                return;
            }

            SetEmptyState($"Не найдено: {scanValue}");
            await WriteLogAsync("WMS скан", "Не найдено", scanValue, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            SetEmptyState("Ошибка сканирования.");
            StatusText.Text = exception.Message;
        }
        finally
        {
            SearchButton.IsEnabled = true;
            ScanTextBox.SelectAll();
            ScanTextBox.Focus();
        }
    }

    private async Task<bool> TryShowProductAsync(string scanValue, CancellationToken cancellationToken)
    {
        var product = await _productLookup.FindAsync(scanValue, cancellationToken);
        if (product is null || product.Id == Guid.Empty)
        {
            return false;
        }

        var locations = await _stockLocations.GetByItemAsync(product.Id, cancellationToken);
        _currentLocations = locations;
        _currentHeader = $"Где лежит: {product.Name}";
        _currentSubheader = $"Код {product.Code}";

        ResultKindText.Text = "Товар";
        ResultTitleText.Text = product.Name;
        ResultSubtitleText.Text = $"{product.Code}   ·   найдено ячеек: {locations.Count}";
        LocationsGrid.ItemsSource = locations;
        OpenPopupButton.IsEnabled = true;
        StatusText.Text = locations.Count == 0
            ? "Товар найден, но в ячейках пока не размещён."
            : $"Товар найден. Количество по ячейкам: {locations.Sum(l => l.Quantity):N3}";

        await WriteLogAsync("WMS скан товара", "Найдено", $"{product.Code} · {product.Name}", product.Id, cancellationToken);
        return true;
    }

    private async Task<bool> TryShowCellAsync(string scanValue, CancellationToken cancellationToken)
    {
        var cell = await _cellLookup.FindAsync(scanValue, cancellationToken);
        if (cell is null || cell.Id == Guid.Empty)
        {
            return false;
        }

        var locations = await _stockLocations.GetByCellAsync(cell.Id, cancellationToken);
        _currentLocations = locations;
        _currentHeader = $"Содержимое ячейки {cell.Code}";
        _currentSubheader = cell.WarehouseName;

        ResultKindText.Text = "Ячейка";
        ResultTitleText.Text = cell.Code;
        ResultSubtitleText.Text = $"{cell.WarehouseName}   ·   позиций: {locations.Count}";
        LocationsGrid.ItemsSource = locations;
        OpenPopupButton.IsEnabled = true;
        StatusText.Text = locations.Count == 0
            ? "Ячейка найдена, но сейчас пустая."
            : $"Ячейка найдена. Количество внутри: {locations.Sum(l => l.Quantity):N3}";

        await WriteLogAsync("WMS скан ячейки", "Найдено", $"{cell.WarehouseName} · {cell.Code}", cell.Id, cancellationToken);
        return true;
    }

    private async Task WriteLogAsync(
        string action,
        string result,
        string message,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _operationLogger.WriteAsync(
                new ScanLogEntry(
                    Guid.NewGuid(),
                    _actorUserName,
                    "WmsScan",
                    entityId,
                    string.Empty,
                    action,
                    result,
                    message),
                cancellationToken);
        }
        catch
        {
            // Скан должен работать даже если аудит временно недоступен.
        }
    }

    private void OnOpenPopupClicked(object sender, RoutedEventArgs e)
    {
        var popup = new StockLocationsPopupWindow(_currentHeader, _currentSubheader, _currentLocations)
        {
            Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
        };
        popup.ShowDialog();
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        ScanTextBox.Clear();
        SetEmptyState("Готово к сканированию.");
        ScanTextBox.Focus();
    }

    private void SetEmptyState(string status)
    {
        _currentLocations = Array.Empty<StockLocation>();
        _currentHeader = string.Empty;
        _currentSubheader = string.Empty;
        ResultKindText.Text = "Нет результата";
        ResultTitleText.Text = "Скан не распознан";
        ResultSubtitleText.Text = "Можно сканировать штрихкод товара, код товара или QR ячейки.";
        LocationsGrid.ItemsSource = _currentLocations;
        OpenPopupButton.IsEnabled = false;
        StatusText.Text = status;
    }
}
