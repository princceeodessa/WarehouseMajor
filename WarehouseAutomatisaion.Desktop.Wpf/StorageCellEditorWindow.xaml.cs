using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 3 Task 20: модальный редактор ячейки склада (create / update).
// DialogResult = true означает что данные сохранены — caller перезагружает список.
public partial class StorageCellEditorWindow : Window
{
    private readonly IStorageCellCatalog _catalog;
    private readonly StorageCell? _source;
    private readonly Guid? _editingId;

    public StorageCellEditorWindow(IStorageCellCatalog catalog, StorageCell? source)
    {
        InitializeComponent();

        _catalog = catalog;
        _source = source;
        _editingId = source?.Id;

        Title = source is null ? "Новая ячейка склада" : $"Ячейка: {source.Code}";

        CellTypeCombo.ItemsSource = CellTypes.All;
        StatusCombo.ItemsSource = CellStatuses.All;

        if (source is not null)
        {
            PopulateFromSource(source);
        }
        else
        {
            CellTypeCombo.Text = CellTypes.Storage;
            StatusCombo.Text = CellStatuses.Active;
            AuditText.Text = "Новая запись.";
        }
    }

    private void PopulateFromSource(StorageCell source)
    {
        WarehouseNameBox.Text = source.WarehouseName;
        CodeBox.Text = source.Code;
        ZoneCodeBox.Text = source.ZoneCode ?? string.Empty;
        ZoneNameBox.Text = source.ZoneName ?? string.Empty;
        RowNoBox.Text = source.RowNo.ToString(CultureInfo.InvariantCulture);
        RackNoBox.Text = source.RackNo.ToString(CultureInfo.InvariantCulture);
        ShelfNoBox.Text = source.ShelfNo.ToString(CultureInfo.InvariantCulture);
        CellNoBox.Text = source.CellNo.ToString(CultureInfo.InvariantCulture);
        CellTypeCombo.Text = source.CellType ?? CellTypes.Storage;
        StatusCombo.Text = source.StatusText ?? CellStatuses.Active;
        CapacityBox.Text = source.Capacity.ToString("N4", CultureInfo.InvariantCulture);
        CommentBox.Text = source.CommentText ?? string.Empty;

        AuditText.Text =
            $"Создана: {source.CreatedAtUtc:dd.MM.yyyy HH:mm} UTC   ·   обновлена: {source.UpdatedAtUtc:dd.MM.yyyy HH:mm} UTC   ·   id={source.Id}";
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (!TryBuildRequest(out var request, out var error))
        {
            StatusText.Text = $"❌ {error}";
            return;
        }

        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusText.Text = "💾 Сохранение...";

        try
        {
            if (_editingId.HasValue)
            {
                await _catalog.UpdateAsync(_editingId.Value, request);
            }
            else
            {
                _ = await _catalog.CreateAsync(request);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Не удалось сохранить: {exception.Message}";
            SaveButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private bool TryBuildRequest(out StorageCellRequest request, out string error)
    {
        request = null!;
        error = string.Empty;

        var warehouseName = WarehouseNameBox.Text.Trim();
        if (string.IsNullOrEmpty(warehouseName))
        {
            error = "Поле «Склад» обязательно.";
            return false;
        }

        var code = CodeBox.Text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            error = "Поле «Код ячейки» обязательно.";
            return false;
        }

        if (!TryParseInt(RowNoBox.Text, out var row, "Ряд", out error)) return false;
        if (!TryParseInt(RackNoBox.Text, out var rack, "Стеллаж", out error)) return false;
        if (!TryParseInt(ShelfNoBox.Text, out var shelf, "Полка", out error)) return false;
        if (!TryParseInt(CellNoBox.Text, out var cell, "Ячейка", out error)) return false;
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
            CellNo: cell,
            CellType: NullIfEmpty(CellTypeCombo.Text),
            Capacity: capacity,
            StatusText: NullIfEmpty(StatusCombo.Text),
            CommentText: NullIfEmpty(CommentBox.Text));

        return true;
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

        error = "Поле «Capacity» должно быть числом.";
        value = 0m;
        return false;
    }

    private static string? NullIfEmpty(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
