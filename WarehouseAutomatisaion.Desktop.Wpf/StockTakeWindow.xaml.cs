using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Фаза D (Sprint 7): окно инвентаризации содержимого ячейки.
// Поток: выбрать ячейку → загрузить её содержимое (StockLocation rows) →
// оператор вписывает фактическое qty в редактируемую колонку «Факт» →
// «Применить факт» делает UpsertAsync с фактическим значением для каждой строки
// где факт отличается от системы.
//
// Дельта подсвечивается красным (недостача) / зелёным (излишек).
// «Факт = система» — быстрая кнопка «всё сошлось».
//
// Out-of-scope сейчас: добавление позиций которых в системе нет
// (для них есть приёмка). Out-of-scope: история факт-актов / документы.
public partial class StockTakeWindow : Window, IHostedWmsOperationWindow
{
    private readonly IStorageCellCatalog _cellCatalog;
    private readonly IStockLocationRepository _stockLocations;
    private readonly IWarehouseStockOperationService _stockOperations;
    private readonly string _actor;
    private readonly IShelfInventoryVisionService? _shelfVision;
    private IReadOnlyList<StorageCell>? _cellCache;
    private StorageCell? _selectedCell;
    private readonly ObservableCollection<StockTakeRow> _rows = new();
    private bool _completed;

    public Window? DialogOwnerOverride { get; set; }

    public Action? HostCloseRequested { get; set; }

    public StockTakeWindow(
        IStorageCellCatalog cellCatalog,
        IStockLocationRepository stockLocations,
        IWarehouseStockOperationService stockOperations,
        string actor,
        IShelfInventoryVisionService? shelfVision = null)
    {
        InitializeComponent();
        _cellCatalog = cellCatalog ?? throw new ArgumentNullException(nameof(cellCatalog));
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));
        _stockOperations = stockOperations ?? throw new ArgumentNullException(nameof(stockOperations));
        _actor = string.IsNullOrWhiteSpace(actor) ? "Кладовщик" : actor.Trim();
        _shelfVision = shelfVision;

        InventoryGrid.ItemsSource = _rows;
        StatusText.Text = "Выберите ячейку, чтобы загрузить её содержимое для инвентаризации.";
        if (_shelfVision is null)
        {
            PhotoHintText.Text = "Claude API не настроен. Чтобы включить AI инвентаризацию по фото — добавьте Anthropic.ApiKey в appsettings.local.json.";
            PhotoInventoryButton.IsEnabled = false;
        }

        Loaded += (_, _) =>
        {
            StatusText.Text = "Выберите ячейку, чтобы загрузить её содержимое для инвентаризации.";
            if (_shelfVision is null)
            {
                PhotoHintText.Text = "Claude API не настроен. Чтобы включить AI инвентаризацию по фото — добавьте Anthropic.ApiKey в appsettings.local.json.";
                PhotoInventoryButton.IsEnabled = false;
            }
        };
    }

    private async void OnPickCellClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            PickCellButton.IsEnabled = false;
            PickCellButton.Content = "⏳ Загрузка ячеек...";

            _cellCache ??= await _cellCatalog.GetAllAsync();

            if (_cellCache.Count == 0)
            {
                StatusText.Text = "❌ Нет созданных ячеек. Откройте раздел «Ячейки» и добавьте.";
                return;
            }

            var picker = new CellPickerWindow(_cellCache, initialSearch: _selectedCell?.Code) { Owner = GetDialogOwner() };
            if (picker.ShowDialog() == true && picker.SelectedCell is not null)
            {
                _selectedCell = picker.SelectedCell;
                SelectedCellText.Text = $"{_selectedCell.Code}   ·   {_selectedCell.WarehouseName}";
                SelectedCellText.Foreground = System.Windows.Media.Brushes.Black;
                await LoadInventoryAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось открыть выбор ячейки: {ex.Message}";
        }
        finally
        {
            PickCellButton.IsEnabled = true;
            PickCellButton.Content = "🔍 Выбрать ячейку";
        }
    }

    private async Task LoadInventoryAsync()
    {
        if (_selectedCell is null)
        {
            return;
        }

        try
        {
            StatusText.Text = $"⏳ Загрузка инвентаря «{_selectedCell.Code}»...";
            var locations = await _stockLocations.GetByCellAsync(_selectedCell.Id);

            // Отписываемся от событий старых строк (если перезагружаем).
            foreach (var oldRow in _rows)
            {
                oldRow.PropertyChanged -= OnRowChanged;
            }
            _rows.Clear();

            foreach (var loc in locations.OrderBy(l => l.ItemCode))
            {
                var row = new StockTakeRow(loc);
                row.PropertyChanged += OnRowChanged;
                _rows.Add(row);
            }

            UpdateActionButtons();

            StatusText.Text = locations.Count == 0
                ? $"Ячейка «{_selectedCell.Code}» пуста — нечего инвентаризировать."
                : $"Загружено: {locations.Count} позиций. Впишите факт в колонку «Факт».";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось загрузить инвентарь: {ex.Message}";
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StockTakeRow.ActualQuantityText)
            or nameof(StockTakeRow.ResolutionCode)
            or nameof(StockTakeRow.Reason))
        {
            UpdateActionButtons();
        }
    }

    private void OnMatchAllClicked(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.SetActualEqualSystem();
        }
        StatusText.Text = "✓ Фактические значения подставлены равными системным. Нажмите «Применить факт» чтобы подтвердить.";
        UpdateActionButtons();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        if (!_completed && _selectedCell is not null && _rows.Any(row => row.HasValidActual))
        {
            var message = CanCommitInventory()
                ? "Факт заполнен, но инвентаризация ещё не проведена. Нажмите «Провести инвентаризацию»."
                : "Нельзя закончить инвентаризацию: заполните факт по всем строкам и обоснуйте каждое расхождение.";
            MessageBox.Show(GetDialogOwner(), message, "Инвентаризация не завершена", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CloseHostedOrWindow();
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedCell is null)
        {
            return;
        }

        if (_rows.Any(row => !row.HasValidActual))
        {
            StatusText.Text = "❌ Укажите фактическое количество по каждой строке.";
            return;
        }

        var unresolved = _rows.Where(row => row.HasDifference && !row.IsResolved).ToArray();
        if (unresolved.Length > 0)
        {
            StatusText.Text = $"❌ Не обоснованы расхождения: {string.Join(", ", unresolved.Take(4).Select(row => row.ItemCode))}.";
            return;
        }

        var changes = _rows.Where(r => r.HasDifference).ToList();
        var confirm = MessageBox.Show(
            GetDialogOwner(),
            $"Ячейка: {_selectedCell.Code}\nПозиций пересчитано: {_rows.Count}\nРасхождений: {changes.Count}\n\n" +
            string.Join("\n", changes.Take(8).Select(r =>
                $"• {r.ItemCode}: {r.SystemQuantity:N3} → {r.ActualQuantityValue:N3} ({r.DiffLabel}); {r.ResolutionCode}; {r.Reason}"))
            + (changes.Count > 8 ? $"\n…и ещё {changes.Count - 8}" : "")
            + "\n\nПровести инвентаризацию?",
            "Проведение инвентаризации",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ApplyButton.IsEnabled = false;
            MatchAllButton.IsEnabled = false;
            StatusText.Text = $"⏳ Проводим инвентаризацию «{_selectedCell.Code}»...";

            var result = await _stockOperations.CommitCellInventoryAsync(new CellInventoryCommitRequest(
                CellId: _selectedCell.Id,
                CellCode: _selectedCell.Code,
                WarehouseName: _selectedCell.WarehouseName,
                Actor: _actor,
                Lines: _rows.Select(row => new CellInventoryLineInput(
                    StockLocationId: row.Source.Id,
                    ItemId: row.Source.ItemId,
                    ItemCode: row.Source.ItemCode,
                    ItemName: row.Source.ItemName,
                    SystemQuantity: row.SystemQuantity,
                    ActualQuantity: row.ActualQuantityValue,
                    ReservedQuantity: row.Source.ReservedQuantity,
                    ResolutionCode: row.HasDifference ? row.ResolutionCode : "match",
                    Reason: row.HasDifference ? row.Reason : "Факт соответствует системе",
                    InvestigationCellCode: row.InvestigationCellCode)).ToArray()));

            if (!result.Succeeded)
            {
                StatusText.Text = $"❌ {result.Message}";
                return;
            }

            _completed = true;
            MessageBox.Show(
                GetDialogOwner(),
                $"{result.Message}\n\nДокумент: {result.DocumentNumber}\n" +
                $"Недостача: {result.ShortageQuantity:N3}\nИзлишек: {result.SurplusQuantity:N3}",
                "Инвентаризация проведена",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            CloseHostedOrWindow();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось применить: {ex.Message}";
        }
        finally
        {
            UpdateActionButtons();
        }
    }

    private void UpdateActionButtons()
    {
        var hasRows = _rows.Count > 0;
        ApplyButton.IsEnabled = CanCommitInventory();
        MatchAllButton.IsEnabled = hasRows;
        PhotoInventoryButton.IsEnabled = hasRows && _shelfVision is not null;
    }

    private bool CanCommitInventory()
    {
        return _rows.Count > 0
               && _rows.All(row => row.HasValidActual)
               && _rows.Where(row => row.HasDifference).All(row => row.IsResolved);
    }

    private async void OnCheckOtherCellsClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: StockTakeRow row })
        {
            return;
        }

        try
        {
            var locations = await _stockLocations.GetByItemAsync(row.Source.ItemId);
            var otherLocations = locations
                .Where(location => location.StorageCellId != row.Source.StorageCellId && location.Quantity > 0)
                .OrderByDescending(location => location.Quantity)
                .ToArray();

            if (otherLocations.Length == 0)
            {
                row.InvestigationCellCode = "Других ячеек с остатком нет";
                MessageBox.Show(
                    GetDialogOwner(),
                    "В других ячейках положительный остаток этого товара не найден.",
                    row.ItemCode,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            row.InvestigationCellCode = string.Join(", ", otherLocations.Take(5).Select(location => location.StorageCellCode));
            MessageBox.Show(
                GetDialogOwner(),
                string.Join("\n", otherLocations.Take(12).Select(location =>
                    $"{location.StorageCellCode} · {location.WarehouseName}: {location.Quantity:N3}")),
                $"Другие ячейки: {row.ItemCode}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось проверить другие ячейки: {ex.Message}";
        }
    }

    private async void OnPhotoInventoryClicked(object sender, RoutedEventArgs e)
    {
        if (_shelfVision is null || _rows.Count == 0)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Выберите фото полки",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.webp|JPEG|*.jpg;*.jpeg|PNG|*.png|WebP|*.webp",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(GetDialogOwner()) != true)
        {
            return;
        }

        try
        {
            PhotoInventoryButton.IsEnabled = false;
            PhotoInventoryButton.Content = "⏳ Claude распознаёт...";
            StatusText.Text = "⏳ Отправляю фото в Claude vision...";

            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var contentType = ResolveContentType(dialog.FileName);

            var payload = new InvoiceImagePayload(
                ImageBytes: bytes,
                ContentType: contentType,
                SourceFileName: Path.GetFileName(dialog.FileName));

            var result = await _shelfVision.RecognizeShelfAsync(payload);

            if (result.Items.Count == 0)
            {
                StatusText.Text = $"⚠ Claude не нашёл товаров на фото (за {result.Duration.TotalSeconds:F1} с).";
                return;
            }

            // Матчим распознанное со строками инвентаря.
            var matched = 0;
            var unmatched = new List<string>();
            foreach (var item in result.Items)
            {
                var row = FindMatchingRow(item);
                if (row is not null)
                {
                    row.ActualQuantityText = item.Quantity.ToString("0.###", CultureInfo.InvariantCulture);
                    matched++;
                }
                else
                {
                    unmatched.Add($"«{item.Name}» × {item.Quantity:N0}");
                }
            }

            UpdateActionButtons();

            var summary = $"✨ Claude распознал {result.Items.Count} позиций за {result.Duration.TotalSeconds:F1} с. " +
                          $"Сопоставлено с системой: {matched}.";
            if (unmatched.Count > 0)
            {
                summary += $"\n⚠ Не нашёл в этой ячейке: {string.Join(", ", unmatched.Take(3))}" +
                           (unmatched.Count > 3 ? $" и ещё {unmatched.Count - 3}" : "") +
                           ". Возможно товары которых нет в системе — добавьте через «Приёмку».";
            }
            StatusText.Text = summary;
        }
        catch (InvoiceVisionException ex)
        {
            StatusText.Text = $"❌ AI vision: {ex.Kind} — {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Не удалось распознать фото: {ex.Message}";
        }
        finally
        {
            PhotoInventoryButton.IsEnabled = _rows.Count > 0 && _shelfVision is not null;
            PhotoInventoryButton.Content = "📷 Загрузить фото полки";
        }
    }

    private StockTakeRow? FindMatchingRow(ShelfItem item)
    {
        // 1. Exact match по item_code (если у строки есть код и в распознанном есть sku)
        if (!string.IsNullOrWhiteSpace(item.Sku))
        {
            var bySku = _rows.FirstOrDefault(r =>
                string.Equals(r.ItemCode, item.Sku, StringComparison.OrdinalIgnoreCase));
            if (bySku is not null)
            {
                return bySku;
            }
        }

        // 2. Fuzzy match по name — token overlap (lowercase, без знаков препинания)
        var tokens = TokenizeForMatching(item.Name);
        if (tokens.Count == 0)
        {
            return null;
        }

        StockTakeRow? best = null;
        var bestScore = 0.0;
        foreach (var row in _rows)
        {
            var rowTokens = TokenizeForMatching(row.ItemName);
            if (rowTokens.Count == 0)
            {
                continue;
            }

            var common = tokens.Intersect(rowTokens).Count();
            var union = tokens.Union(rowTokens).Count();
            var jaccard = (double)common / union;
            if (jaccard > bestScore)
            {
                bestScore = jaccard;
                best = row;
            }
        }

        // Порог: 0.35 — нестрого, но без явного мусора.
        return bestScore >= 0.35 ? best : null;
    }

    private static HashSet<string> TokenizeForMatching(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>();
        }
        var lower = text.ToLowerInvariant();
        var clean = new System.Text.StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch))
            {
                clean.Append(ch);
            }
            else
            {
                clean.Append(' ');
            }
        }
        return clean.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3) // отбрасываем шум типа "по", "до"
            .ToHashSet();
    }

    private static string ResolveContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
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

    // Row VM — INotifyPropertyChanged нужен чтобы DataGrid обновлял колонки «Δ» при правке «Факт».
    public sealed class StockTakeRow : INotifyPropertyChanged
    {
        public static IReadOnlyList<string> ResolutionOptions { get; } =
        [
            "Списание недостачи",
            "Найдено в другой ячейке",
            "Излишек подтверждён пересчётом",
            "Ошибка прежнего размещения",
            "Ошибка учёта / документа",
            "Иная подтверждённая причина"
        ];

        public IReadOnlyList<string> AvailableResolutions => ResolutionOptions;

        public StockLocation Source { get; }
        public string ItemCode => Source.ItemCode;
        public string ItemName => Source.ItemName;
        public decimal SystemQuantity => Source.Quantity;

        private string _actualQuantityText = string.Empty;
        public string ActualQuantityText
        {
            get => _actualQuantityText;
            set
            {
                if (_actualQuantityText == value)
                {
                    return;
                }
                _actualQuantityText = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DiffLabel));
                OnPropertyChanged(nameof(DiffSign));
                OnPropertyChanged(nameof(HasDifference));
                OnPropertyChanged(nameof(IsResolved));
            }
        }

        private string _resolutionCode = string.Empty;
        public string ResolutionCode
        {
            get => _resolutionCode;
            set
            {
                if (_resolutionCode == value)
                {
                    return;
                }

                _resolutionCode = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsResolved));
            }
        }

        private string _reason = string.Empty;
        public string Reason
        {
            get => _reason;
            set
            {
                if (_reason == value)
                {
                    return;
                }

                _reason = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsResolved));
            }
        }

        private string _investigationCellCode = string.Empty;
        public string InvestigationCellCode
        {
            get => _investigationCellCode;
            set
            {
                if (_investigationCellCode == value)
                {
                    return;
                }

                _investigationCellCode = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public StockTakeRow(StockLocation source)
        {
            Source = source;
        }

        public bool HasValidActual =>
            !string.IsNullOrWhiteSpace(_actualQuantityText)
            && decimal.TryParse(
                _actualQuantityText.Trim().Replace(',', '.'),
                NumberStyles.Number, CultureInfo.InvariantCulture, out _);

        public decimal ActualQuantityValue =>
            decimal.Parse(_actualQuantityText.Trim().Replace(',', '.'),
                NumberStyles.Number, CultureInfo.InvariantCulture);

        public bool HasDifference =>
            HasValidActual && ActualQuantityValue != SystemQuantity;

        public bool IsResolved =>
            !HasDifference
            || !string.IsNullOrWhiteSpace(ResolutionCode)
            && !string.IsNullOrWhiteSpace(Reason);

        public string DiffLabel
        {
            get
            {
                if (!HasValidActual)
                {
                    return string.Empty;
                }
                var diff = ActualQuantityValue - SystemQuantity;
                if (diff == 0)
                {
                    return "= 0";
                }
                var sign = diff > 0 ? "+" : "−";
                return $"{sign}{Math.Abs(diff):N3}";
            }
        }

        public string DiffSign
        {
            get
            {
                if (!HasValidActual)
                {
                    return string.Empty;
                }
                var diff = ActualQuantityValue - SystemQuantity;
                if (diff > 0)
                {
                    return "positive";
                }
                if (diff < 0)
                {
                    return "negative";
                }
                return "match";
            }
        }

        public void SetActualEqualSystem()
        {
            ActualQuantityText = SystemQuantity.ToString("0.###", CultureInfo.InvariantCulture);
            ResolutionCode = string.Empty;
            Reason = string.Empty;
            InvestigationCellCode = string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
