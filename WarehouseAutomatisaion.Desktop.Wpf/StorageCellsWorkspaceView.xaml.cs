using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 3 Task 20: workspace «Ячейки склада».
// CRUD-список ячеек из app_warehouse_storage_cells.
public partial class StorageCellsWorkspaceView : UserControl
{
    private static readonly IReadOnlyList<EditorOption> BaseCellTypeOptions =
    [
        new(CellTypes.Storage, "Хранение"),
        new(CellTypes.Receiving, "Приёмка"),
        new(CellTypes.Shipping, "Отгрузка"),
        new(CellTypes.Quarantine, "Карантин"),
        new(CellTypes.Defective, "Брак"),
        new(CellTypes.Production, "Производство")
    ];

    private static readonly IReadOnlyList<EditorOption> BaseStatusOptions =
    [
        new(CellStatuses.Active, "Активна"),
        new(CellStatuses.Reserved, "Зарезервирована"),
        new(CellStatuses.Blocked, "Заблокирована"),
        new(CellStatuses.Maintenance, "Обслуживание")
    ];

    private DesktopMySqlBackplaneService? _backplane;
    private MySqlStorageCellCatalog? _catalog;
    private MySqlStockLocationRepository? _stockLocations;
    private bool _isInitialized;
    private bool _isLoadingEditor;
    private string? _selectedWarehouseFilter;
    private IReadOnlyList<WarehouseFilterOption> _warehouseEditorOptions = Array.Empty<WarehouseFilterOption>();
    private Guid? _editingId;

    public StorageCellsWorkspaceView()
    {
        InitializeComponent();
        HideEditor();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (_backplane is null)
        {
            StatusText.Text = "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.";
            DisableActions();
            return;
        }

        _catalog = new MySqlStorageCellCatalog(_backplane);
        _stockLocations = new MySqlStockLocationRepository(_backplane);
        LoadWarehouses();
        _isInitialized = true;
        ReloadCells();
    }

    private void LoadWarehouses()
    {
        if (_backplane is null)
        {
            return;
        }

        var warehouseNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var summaries = _backplane.LoadStockWarehouses();
        foreach (var summary in summaries)
        {
            if (!string.IsNullOrWhiteSpace(summary.WarehouseName))
            {
                warehouseNames.Add(summary.WarehouseName.Trim());
            }
        }

        foreach (var cell in _backplane.LoadStorageCells())
        {
            if (!string.IsNullOrWhiteSpace(cell.WarehouseName))
            {
                warehouseNames.Add(cell.WarehouseName.Trim());
            }
        }

        var items = new List<WarehouseFilterOption>
        {
            new(null, "Все склады")
        };

        foreach (var name in warehouseNames)
        {
            items.Add(new WarehouseFilterOption(name, name));
        }

        WarehouseFilterCombo.ItemsSource = items;
        WarehouseFilterCombo.DisplayMemberPath = nameof(WarehouseFilterOption.DisplayName);
        WarehouseFilterCombo.SelectedValuePath = nameof(WarehouseFilterOption.Value);
        WarehouseFilterCombo.SelectedIndex = 0;

        _warehouseEditorOptions = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToArray();
        WarehouseNameCombo.ItemsSource = _warehouseEditorOptions;
    }

    private async void ReloadCells()
    {
        if (_catalog is null)
        {
            return;
        }

        try
        {
            var cells = await _catalog.GetAllAsync(_selectedWarehouseFilter);
            var rows = cells.Select(cell => new CellRowViewModel
            {
                Source = cell,
                Code = cell.Code,
                WarehouseName = cell.WarehouseName,
                ZoneLabel = string.IsNullOrWhiteSpace(cell.ZoneName)
                    ? cell.ZoneCode ?? string.Empty
                    : $"{cell.ZoneCode} · {cell.ZoneName}",
                AddressLabel = $"С{cell.RackNo:D2}-Э{cell.ShelfNo:D2}-Р{cell.RowNo:D2}",
                CellTypeDisplay = DisplayCellType(cell.CellType),
                Capacity = cell.Capacity,
                StatusDisplay = DisplayStatus(cell.StatusText),
                CommentText = cell.CommentText ?? string.Empty,
            }).ToList();

            CellsDataGrid.ItemsSource = rows;

            if (rows.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                CellsDataGrid.Visibility = Visibility.Collapsed;
                StatusText.Text = "Ячеек ещё нет. Создайте первую ячейку или импортируйте CSV.";
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                CellsDataGrid.Visibility = Visibility.Visible;
                var distinctWarehouses = rows.Select(row => row.WarehouseName).Distinct().Count();
                var distinctZones = rows.Where(row => !string.IsNullOrEmpty(row.ZoneLabel))
                    .Select(row => row.ZoneLabel).Distinct().Count();
                StatusText.Text = $"Ячеек: {rows.Count:N0}   ·   складов: {distinctWarehouses:N0}   ·   зон: {distinctZones:N0}";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Ошибка загрузки: {exception.Message}";
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        _selectedWarehouseFilter = (WarehouseFilterCombo.SelectedItem as WarehouseFilterOption)?.Value;
        ReloadCells();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            ReloadCells();
        }
    }

    private void OnCellSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = CellsDataGrid.SelectedItem is not null;
        EditCellButton.IsEnabled = hasSelection;
        DeleteCellButton.IsEnabled = hasSelection;
    }

    private void OnCellDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CellsDataGrid.SelectedItem is CellRowViewModel selected)
        {
            ShowEditor(selected.Source);
        }
    }

    private void OnNewCellClicked(object sender, RoutedEventArgs e)
    {
        ShowEditor(source: null);
    }

    private void OnEditCellClicked(object sender, RoutedEventArgs e)
    {
        if (CellsDataGrid.SelectedItem is CellRowViewModel selected)
        {
            ShowEditor(selected.Source);
        }
    }

    private async void OnDeleteCellClicked(object sender, RoutedEventArgs e)
    {
        if (_catalog is null || CellsDataGrid.SelectedItem is not CellRowViewModel selected)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
            $"Удалить ячейку «{selected.Code}» на складе «{selected.WarehouseName}»?\n\nДействие нельзя отменить.",
            "Удаление ячейки",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _catalog.DeleteAsync(selected.Source.Id);
            HideEditor();
            ReloadCells();
            StatusText.Text = $"Ячейка «{selected.Code}» удалена.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Не удалось удалить: {exception.Message}";
        }
    }

    private void OnPrintLabelsClicked(object sender, RoutedEventArgs e)
    {
        var rows = CellsDataGrid.ItemsSource as IEnumerable<CellRowViewModel>;
        if (rows is null)
        {
            return;
        }

        var cells = rows.Select(row => row.Source).ToList();
        if (cells.Count == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
                "Нет ячеек для печати. Снимите фильтр или импортируйте ячейки.",
                "QR-этикетки",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var printWindow = new CellLabelPrintWindow(cells)
        {
            Owner = Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow
        };
        printWindow.ShowDialog();
    }

    private async void OnImportCsvClicked(object sender, RoutedEventArgs e)
    {
        if (_backplane is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Выберите CSV-файл со списком ячеек",
            Filter = "CSV / TSV|*.csv;*.tsv;*.txt|Все файлы|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) != true)
        {
            return;
        }

        try
        {
            var csvContent = await File.ReadAllTextAsync(dialog.FileName, System.Text.Encoding.UTF8);
            var importer = new StorageCellCsvImporter();
            var result = importer.Parse(csvContent);

            var requestsCount = result.Requests.Count;
            var errorsCount = result.Errors.Count;

            if (requestsCount == 0)
            {
                var errorPreview = string.Join("\n", result.Errors.Take(5));
                MessageBox.Show(
                    Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
                    $"Из файла не извлечено ни одной строки.\n\nОшибки ({errorsCount}):\n{errorPreview}",
                    "Импорт ячеек",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var confirmation = MessageBox.Show(
                Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
                $"Файл: {Path.GetFileName(dialog.FileName)}\n\n" +
                $"К импорту: {requestsCount:N0} строк\n" +
                $"Ошибок в парсинге: {errorsCount:N0}\n\n" +
                "Существующие ячейки по сочетанию склад + код будут обновлены, новые будут созданы.\n\n" +
                "Продолжить?",
                "Подтверждение импорта",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            ImportCsvButton.IsEnabled = false;
            ImportCsvButton.Content = "Импорт...";
            StatusText.Text = $"Импорт {requestsCount:N0} ячеек...";

            var importResult = await Task.Run(() => _backplane.BulkUpsertStorageCells(result.Requests));

            ReloadCells();

            var resultMessage =
                $"Импорт завершён.\n\n" +
                $"Добавлено новых: {importResult.Inserted:N0}\n" +
                $"Обновлено существующих: {importResult.Updated:N0}\n" +
                $"Ошибок: {importResult.Failed + errorsCount:N0}";

            if (importResult.Errors.Count > 0)
            {
                resultMessage += "\n\nПервые ошибки:\n" + string.Join("\n", importResult.Errors.Take(5));
            }

            MessageBox.Show(
                Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
                resultMessage,
                "Импорт ячеек",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusText.Text = $"Импортировано: {importResult.Inserted + importResult.Updated:N0} (новых: {importResult.Inserted:N0}, обновлено: {importResult.Updated:N0})";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Ошибка импорта: {exception.Message}";
            MessageBox.Show(
                Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow,
                $"Не удалось импортировать: {exception.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            ImportCsvButton.IsEnabled = true;
            ImportCsvButton.Content = "Импорт CSV";
        }
    }

    private void ShowEditor(StorageCell? source)
    {
        if (_catalog is null)
        {
            return;
        }

        _editingId = source?.Id;
        EditorColumn.Width = new GridLength(430);
        EditorPanel.Visibility = Visibility.Visible;
        _isLoadingEditor = true;

        EditorTitleText.Text = source is null ? "Новая ячейка" : $"Ячейка {source.Code}";
        EditorSubtitleText.Text = source is null
            ? "Выберите склад и заполните адрес хранения. Код ячейки сформируется автоматически."
            : "Редактирование выполняется прямо в разделе ячеек, без отдельного окна.";
        EditorStatusText.Text = source is null ? "Новая запись." : "Изменения ещё не сохранены.";

        CellTypeCombo.ItemsSource = EnsureOption(BaseCellTypeOptions, source?.CellType);
        CellTypeCombo.DisplayMemberPath = nameof(EditorOption.DisplayName);
        CellTypeCombo.SelectedValuePath = nameof(EditorOption.Value);
        StatusCombo.ItemsSource = EnsureOption(BaseStatusOptions, source?.StatusText);
        StatusCombo.DisplayMemberPath = nameof(EditorOption.DisplayName);
        StatusCombo.SelectedValuePath = nameof(EditorOption.Value);

        var warehouseName = source?.WarehouseName ?? ResolveDefaultWarehouseName();
        WarehouseNameCombo.ItemsSource = EnsureWarehouseOption(_warehouseEditorOptions, warehouseName);

        if (source is null)
        {
            WarehouseNameCombo.SelectedValue = warehouseName;
            CodeBox.Clear();
            ZoneCodeBox.Clear();
            ZoneNameBox.Clear();
            RackNoBox.Text = "0";
            ShelfNoBox.Text = "0";
            RowNoBox.Text = "0";
            CellTypeCombo.SelectedValue = CellTypes.Storage;
            StatusCombo.SelectedValue = CellStatuses.Active;
            CapacityBox.Text = "0";
            CommentBox.Clear();
            AuditText.Text = "Новая запись.";
        }
        else
        {
            WarehouseNameCombo.SelectedValue = source.WarehouseName;
            CodeBox.Text = source.Code;
            ZoneCodeBox.Text = source.ZoneCode ?? string.Empty;
            ZoneNameBox.Text = source.ZoneName ?? string.Empty;
            RackNoBox.Text = source.RackNo.ToString(CultureInfo.InvariantCulture);
            ShelfNoBox.Text = source.ShelfNo.ToString(CultureInfo.InvariantCulture);
            RowNoBox.Text = source.RowNo.ToString(CultureInfo.InvariantCulture);
            CellTypeCombo.SelectedValue = source.CellType ?? CellTypes.Storage;
            StatusCombo.SelectedValue = source.StatusText ?? CellStatuses.Active;
            CapacityBox.Text = source.Capacity.ToString("0.####", CultureInfo.InvariantCulture);
            CommentBox.Text = source.CommentText ?? string.Empty;
            AuditText.Text =
                $"Создана: {source.CreatedAtUtc:dd.MM.yyyy HH:mm} UTC   ·   обновлена: {source.UpdatedAtUtc:dd.MM.yyyy HH:mm} UTC   ·   id={source.Id}";
        }

        _isLoadingEditor = false;
        if (source is null)
        {
            UpdateGeneratedCode();
        }

        WarehouseNameCombo.Focus();
    }

    private void HideEditor()
    {
        _editingId = null;
        EditorPanel.Visibility = Visibility.Collapsed;
        EditorColumn.Width = new GridLength(0);
    }

    private void OnCancelEditClicked(object sender, RoutedEventArgs e)
    {
        HideEditor();
    }

    private async void OnSaveCellClicked(object sender, RoutedEventArgs e)
    {
        if (_catalog is null)
        {
            return;
        }

        if (!TryBuildRequest(out var request, out var error))
        {
            EditorStatusText.Text = error;
            return;
        }

        SaveCellButton.IsEnabled = false;
        EditorStatusText.Text = "Сохранение...";

        try
        {
            if (_editingId.HasValue)
            {
                await _catalog.UpdateAsync(_editingId.Value, request);
                StatusText.Text = $"Ячейка «{request.Code}» обновлена.";
            }
            else
            {
                _ = await _catalog.CreateAsync(request);
                StatusText.Text = $"Ячейка «{request.Code}» создана.";
            }

            HideEditor();
            ReloadCells();
        }
        catch (Exception exception)
        {
            EditorStatusText.Text = $"Не удалось сохранить: {exception.Message}";
        }
        finally
        {
            SaveCellButton.IsEnabled = true;
        }
    }

    private bool TryBuildRequest(out StorageCellRequest request, out string error)
    {
        request = null!;
        error = string.Empty;

        var warehouseName = GetSelectedWarehouseName();
        if (string.IsNullOrEmpty(warehouseName))
        {
            error = "Выберите склад из списка.";
            return false;
        }

        UpdateGeneratedCode();
        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            error = "Заполните зону, стеллаж, этаж и ряд — код ячейки сформируется автоматически.";
            return false;
        }

        if (!TryParseRack(RackNoBox.Text, out var rack, out error)) return false;
        if (!TryParseInt(ShelfNoBox.Text, out var shelf, "Этаж", out error)) return false;
        if (!TryParseInt(RowNoBox.Text, out var row, "Ряд", out error)) return false;
        if (!TryParseDecimal(CapacityBox.Text, out var capacity, out error)) return false;

        request = new StorageCellRequest(
            Code: code,
            WarehouseNodeId: null,
            WarehouseName: warehouseName,
            ZoneCode: NullIfEmpty(ZoneCodeBox.Text),
            ZoneName: NullIfEmpty(ZoneNameBox.Text),
            RowNo: row,
            RackNo: rack,
            ShelfNo: shelf,
            CellNo: 0,
            CellType: GetSelectedValue(CellTypeCombo),
            Capacity: capacity,
            StatusText: GetSelectedValue(StatusCombo),
            CommentText: NullIfEmpty(CommentBox.Text));

        return true;
    }

    private string ResolveDefaultWarehouseName()
    {
        if (!string.IsNullOrWhiteSpace(_selectedWarehouseFilter))
        {
            return _selectedWarehouseFilter!;
        }

        var rows = (CellsDataGrid.ItemsSource as IEnumerable<CellRowViewModel>)?.ToArray() ?? [];
        return rows.Select(row => row.WarehouseName).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() == 1
            ? rows[0].WarehouseName
            : _warehouseEditorOptions.Count == 1
                ? _warehouseEditorOptions[0].Value ?? string.Empty
                : string.Empty;
    }

    private void OnCellCodePartChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoadingEditor)
        {
            return;
        }

        UpdateGeneratedCode();
    }

    private void UpdateGeneratedCode()
    {
        if (!AreCellCodeControlsReady())
        {
            return;
        }

        CodeBox.Text = BuildGeneratedCellCode();
    }

    private string BuildGeneratedCellCode()
    {
        var zone = NormalizeCodePart(!string.IsNullOrWhiteSpace(ZoneCodeBox.Text)
            ? ZoneCodeBox.Text
            : ZoneNameBox.Text);
        var rack = NormalizeCodePart(RackNoBox.Text);
        var floor = NormalizeCodePart(ShelfNoBox.Text);
        var row = NormalizeCodePart(RowNoBox.Text);

        if (string.IsNullOrWhiteSpace(zone)
            || string.IsNullOrWhiteSpace(rack)
            || string.IsNullOrWhiteSpace(floor)
            || string.IsNullOrWhiteSpace(row))
        {
            return string.Empty;
        }

        return string.Join("-", new[] { zone, rack, floor, row });
    }

    private bool AreCellCodeControlsReady()
    {
        return CodeBox is not null
            && ZoneCodeBox is not null
            && ZoneNameBox is not null
            && RackNoBox is not null
            && ShelfNoBox is not null
            && RowNoBox is not null;
    }

    private static string NormalizeCodePart(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "0")
        {
            return string.Empty;
        }

        var result = new List<char>(trimmed.Length);
        var lastWasSeparator = false;
        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                result.Add(char.ToUpperInvariant(ch));
                lastWasSeparator = false;
                continue;
            }

            if (lastWasSeparator)
            {
                continue;
            }

            result.Add('-');
            lastWasSeparator = true;
        }

        return new string(result.ToArray()).Trim('-');
    }

    private string GetSelectedWarehouseName()
    {
        if (WarehouseNameCombo.SelectedItem is WarehouseFilterOption option)
        {
            return option.Value?.Trim() ?? string.Empty;
        }

        return (WarehouseNameCombo.SelectedValue as string)?.Trim() ?? string.Empty;
    }

    private static IReadOnlyList<WarehouseFilterOption> EnsureWarehouseOption(
        IReadOnlyList<WarehouseFilterOption> options,
        string? currentValue)
    {
        if (string.IsNullOrWhiteSpace(currentValue)
            || options.Any(option => option.Value?.Equals(currentValue.Trim(), StringComparison.OrdinalIgnoreCase) == true))
        {
            return options;
        }

        return options.Concat([new WarehouseFilterOption(currentValue.Trim(), currentValue.Trim())]).ToArray();
    }

    private static IReadOnlyList<EditorOption> EnsureOption(
        IReadOnlyList<EditorOption> options,
        string? currentValue)
    {
        if (string.IsNullOrWhiteSpace(currentValue)
            || options.Any(option => option.Value.Equals(currentValue, StringComparison.OrdinalIgnoreCase)))
        {
            return options;
        }

        return options.Concat([new EditorOption(currentValue.Trim(), currentValue.Trim())]).ToArray();
    }

    private static string? GetSelectedValue(ComboBox comboBox)
    {
        return comboBox.SelectedItem is EditorOption option ? option.Value : null;
    }

    private static string DisplayCellType(string? value)
    {
        return DisplayOption(BaseCellTypeOptions, value, "Не задан");
    }

    private static string DisplayStatus(string? value)
    {
        return DisplayOption(BaseStatusOptions, value, "Не задан");
    }

    private static string DisplayOption(
        IReadOnlyList<EditorOption> options,
        string? value,
        string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var option = options.FirstOrDefault(item =>
            item.Value.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        return option?.DisplayName ?? value.Trim();
    }

    private static bool TryParseInt(string text, out int value, string fieldName, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0;
            return true;
        }

        if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        error = $"Поле «{fieldName}» должно быть целым числом.";
        value = 0;
        return false;
    }

    private static bool TryParseRack(string text, out int value, out string error)
    {
        error = string.Empty;
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            value = 0;
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
        {
            var letter = char.ToUpperInvariant(trimmed[0]);
            if (letter is >= 'A' and <= 'Z')
            {
                value = letter - 'A' + 1;
                return true;
            }

            const string russianRackLetters = "АБВГДЕЖЗИКЛМНОПРСТУФХЦЧШЩЭЮЯ";
            var index = russianRackLetters.IndexOf(letter);
            if (index >= 0)
            {
                value = index + 1;
                return true;
            }
        }

        error = "Поле «Стеллаж» должно быть числом или одной буквой.";
        value = 0;
        return false;
    }

    private static bool TryParseDecimal(string text, out decimal value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0m;
            return true;
        }

        var normalized = text.Trim().Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        error = "Поле «Вместимость» должно быть числом.";
        value = 0m;
        return false;
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private void DisableActions()
    {
        NewCellButton.IsEnabled = false;
        EditCellButton.IsEnabled = false;
        DeleteCellButton.IsEnabled = false;
        ImportCsvButton.IsEnabled = false;
        PrintLabelsButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        HideEditor();
    }

    private sealed class CellRowViewModel
    {
        public StorageCell Source { get; init; } = null!;
        public string Code { get; init; } = string.Empty;
        public string WarehouseName { get; init; } = string.Empty;
        public string ZoneLabel { get; init; } = string.Empty;
        public string AddressLabel { get; init; } = string.Empty;
        public string CellTypeDisplay { get; init; } = string.Empty;
        public decimal Capacity { get; init; }
        public string StatusDisplay { get; init; } = string.Empty;
        public string CommentText { get; init; } = string.Empty;
    }

    private sealed record WarehouseFilterOption(string? Value, string DisplayName);

    private sealed record EditorOption(string Value, string DisplayName);
}
