using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Receiving;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 5 Task 13: окно «Распознать накладную (AI)».
// Workflow: выбор файла → preview → распознавание через InvoiceRecognitionService →
// отображение шапки + строк с matching → сохранение черновика приёмки.
public partial class InvoiceRecognitionWindow : Window
{
    private InvoiceRecognitionPipelineFactory.Pipeline? _pipeline;
    private string? _selectedFilePath;
    private InvoiceRecognitionWithMatches? _lastRecognition;
    private List<LineRowViewModel>? _currentRows;
    private IReadOnlyList<NomenclatureRef>? _catalogCache;

    public InvoiceRecognitionWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _pipeline = InvoiceRecognitionPipelineFactory.TryCreate();
        if (_pipeline is null)
        {
            StatusText.Text = "❌ AI / БД не сконфигурированы. Проверьте AiProviders:OpenAi:ApiKey и RemoteDatabase в appsettings.local.json.";
            PickFileButton.IsEnabled = false;
            return;
        }

        ProviderInfoText.Text = $"AI: {_pipeline.ProviderName}, БД: {_pipeline.Backplane.GetType().Name}";
        StatusText.Text = "Выберите файл накладной для распознавания.";
    }

    private void OnPickFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файл накладной",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.webp;*.gif|JPEG|*.jpg;*.jpeg|PNG|*.png|Все файлы|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _selectedFilePath = dialog.FileName;
        FilePathTextBox.Text = _selectedFilePath;
        ShowPreview(_selectedFilePath);
        RecognizeButton.IsEnabled = _pipeline is not null;
        SaveDraftButton.IsEnabled = false;
        StatusText.Text = $"Готов к распознаванию: {Path.GetFileName(_selectedFilePath)} ({new FileInfo(_selectedFilePath).Length / 1024.0:N1} KB).";
    }

    private void ShowPreview(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();

            PreviewImage.Source = image;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewPlaceholder.Text = $"Не удалось показать preview: {exception.Message}";
        }
    }

    private async void OnRecognizeClicked(object sender, RoutedEventArgs e)
    {
        if (_pipeline is null || string.IsNullOrEmpty(_selectedFilePath))
        {
            return;
        }

        SetWorkingState(true, "⏳ Распознавание... (3-8 сек)");

        try
        {
            var bytes = await File.ReadAllBytesAsync(_selectedFilePath);
            var contentType = ResolveContentType(_selectedFilePath);
            var payload = new InvoiceImagePayload(bytes, contentType, Path.GetFileName(_selectedFilePath));

            var result = await _pipeline.Orchestrator.RecognizeAndMatchAsync(payload);
            _lastRecognition = result;

            UpdateHeader(result.Recognition);
            UpdateLines(result.Matches);

            var matchedCount = result.Matches.Count(m => m.Kind != MatchKind.NoMatch);
            StatusText.Text = $"✅ Распознано {result.Matches.Count} строк за {result.Recognition.Duration.TotalMilliseconds:N0} мс. Совпадений с каталогом: {matchedCount}/{result.Matches.Count}.";
            SaveDraftButton.IsEnabled = result.Matches.Count > 0;
        }
        catch (InvoiceVisionException exception)
        {
            StatusText.Text = $"❌ Ошибка распознавания [{exception.Kind}]: {exception.Message}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Неожиданная ошибка: {exception.Message}";
        }
        finally
        {
            SetWorkingState(false, null);
        }
    }

    private async void OnSaveDraftClicked(object sender, RoutedEventArgs e)
    {
        if (_pipeline is null || _lastRecognition is null)
        {
            return;
        }

        SetWorkingState(true, "💾 Сохранение черновика...");

        try
        {
            // Sprint 5 learning loop: для каждой вручную изменённой строки —
            // сохраняем override в БД. В следующий раз AI распознавание любой
            // накладной с похожим текстом возьмёт эту связку автоматически.
            var overridesSaved = 0;
            if (_currentRows is not null)
            {
                foreach (var row in _currentRows)
                {
                    if (!row.IsManuallyEdited || row.CurrentMatch is null) continue;
                    try
                    {
                        await _pipeline.OverrideStore.SaveOverrideAsync(
                            row.RecognizedName,
                            row.CurrentMatch,
                            Environment.UserName,
                            _lastRecognition.Recognition.SupplierName);
                        overridesSaved++;
                    }
                    catch
                    {
                        // best-effort — даже если override не сохранится, draft всё равно создадим
                    }
                }
            }

            var draft = BuildDraft(_lastRecognition);
            var documentId = await _pipeline.DraftWriter.CreateDraftAsync(draft);

            StatusText.Text = $"✅ Черновик id={documentId}. Сохранено overrides для будущих накладных: {overridesSaved}.";
            SaveDraftButton.IsEnabled = false;

            var learningSummary = overridesSaved > 0
                ? $"\n\n🧠 Запомнено {overridesSaved} ручных матчей — в следующих накладных система применит их автоматически."
                : string.Empty;

            MessageBox.Show(
                this,
                $"Черновик приёмки создан.\n\nID: {documentId}\nСтрок: {draft.Lines.Count}\n\nПосле OData-интеграции (Sprint 2) документ автоматически уйдёт в 1С.{learningSummary}",
                "Сохранено",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Не удалось сохранить: {exception.Message}";
            MessageBox.Show(this, exception.ToString(), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetWorkingState(false, null);
        }
    }

    private void UpdateHeader(InvoiceRecognitionResult recognition)
    {
        SupplierNameText.Text = recognition.SupplierName ?? "(не распознан)";
        SupplierTaxIdText.Text = recognition.SupplierTaxId ?? "—";
        InvoiceNumberText.Text = recognition.InvoiceNumber ?? "(не распознан)";
        InvoiceDateText.Text = recognition.InvoiceDate?.ToString("dd.MM.yyyy") ?? "—";
        TotalAmountText.Text = recognition.TotalAmount?.ToString("N2") + " " + (recognition.Currency ?? "") ?? "—";
        TotalVatText.Text = recognition.TotalVat?.ToString("N2") ?? "—";
        ProviderInfoText.Text = $"AI: {recognition.ProviderName}, время: {recognition.Duration.TotalMilliseconds:N0} мс";
    }

    private void UpdateLines(IReadOnlyList<MatchedInvoiceLine> matches)
    {
        _currentRows = matches.Select(m => new LineRowViewModel
        {
            LineNumber = m.Source.LineNumber,
            RecognizedName = m.Source.Name,
            RecognizedSku = m.Source.Sku ?? string.Empty,
            MatchSummary = FormatMatchSummary(m),
            Unit = m.Source.Unit ?? string.Empty,
            Quantity = m.Source.Quantity,
            UnitPrice = m.Source.UnitPrice,
            Total = m.Source.Total,
            OriginalMatch = m,
            CurrentMatch = m.BestMatch,
            IsManuallyEdited = false
        }).ToList();

        LinesDataGrid.ItemsSource = _currentRows;
        // MouseDoubleClick подвешиваем после ItemsSource — иначе AttachedEvent не сработает.
        LinesDataGrid.MouseDoubleClick -= OnLineDoubleClick;
        LinesDataGrid.MouseDoubleClick += OnLineDoubleClick;
    }

    private async void OnLineDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_pipeline is null || LinesDataGrid.SelectedItem is not LineRowViewModel row)
        {
            return;
        }

        // Lazy-load каталог при первом открытии picker'а (8893 строк × ~80 байт = ~700 KB).
        if (_catalogCache is null)
        {
            try
            {
                StatusText.Text = "⏳ Загрузка каталога...";
                _catalogCache = await Task.Run(() => _pipeline.Backplane.LoadNomenclatureRefs());
            }
            catch (Exception exception)
            {
                StatusText.Text = $"❌ Не удалось загрузить каталог: {exception.Message}";
                return;
            }
        }

        var picker = new NomenclaturePickerWindow(_catalogCache, row.RecognizedName, row.CurrentMatch)
        {
            Owner = Window.GetWindow(this)
        };

        if (picker.ShowDialog() == true && picker.SelectedItem is not null)
        {
            row.CurrentMatch = picker.SelectedItem;
            row.IsManuallyEdited = true;
            row.MatchSummary = $"✏ вручную: {picker.SelectedItem.Code} · {picker.SelectedItem.Name}";

            var manualCount = _currentRows?.Count(r => r.IsManuallyEdited) ?? 0;
            StatusText.Text = $"Изменено вручную: {manualCount}. При сохранении черновика выборы будут запомнены для будущих накладных.";
        }
    }

    private static string FormatMatchSummary(MatchedInvoiceLine match)
    {
        if (match.Kind == MatchKind.NoMatch || match.BestMatch is null)
        {
            return "❓ нет — выбрать вручную";
        }

        var prefix = match.Kind switch
        {
            MatchKind.ExactCode => "✓ артикул",
            MatchKind.ExactName => "✓ точно",
            MatchKind.PartialName => "≈ частично",
            MatchKind.FuzzyName => "≈ похоже",
            _ => "?"
        };

        var confidence = match.Confidence > 0 ? $" ({match.Confidence:P0})" : string.Empty;
        return $"{prefix}{confidence}: {match.BestMatch.Code} · {match.BestMatch.Name}";
    }

    private void SetWorkingState(bool busy, string? status)
    {
        PickFileButton.IsEnabled = !busy && _pipeline is not null;
        RecognizeButton.IsEnabled = !busy && _selectedFilePath is not null && _pipeline is not null;

        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private static string ResolveContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }

    private ReceiptDraft BuildDraft(InvoiceRecognitionWithMatches recognized)
    {
        // Если оператор поменял match вручную (IsManuallyEdited) — используем его выбор,
        // иначе fallback на BestMatch от AI/Matcher.
        var rowsByLine = _currentRows?.ToDictionary(r => r.LineNumber, r => r);

        var lines = recognized.Matches.Select(m =>
        {
            NomenclatureRef? effectiveMatch = m.BestMatch;
            if (rowsByLine is not null && rowsByLine.TryGetValue(m.Source.LineNumber, out var row))
            {
                effectiveMatch = row.CurrentMatch ?? m.BestMatch;
            }

            Guid? matchedId = null;
            if (effectiveMatch is not null && Guid.TryParse(effectiveMatch.Id, out var parsed))
            {
                matchedId = parsed;
            }

            return new ReceiptDraftLine(
                LineNumber: m.Source.LineNumber,
                MatchedItemId: matchedId,
                OriginalItemName: m.Source.Name,
                OriginalSku: m.Source.Sku,
                Unit: m.Source.Unit,
                Quantity: m.Source.Quantity,
                UnitPrice: m.Source.UnitPrice,
                Vat: m.Source.Vat,
                Subtotal: m.Source.Subtotal,
                Total: m.Source.Total);
        }).ToList();

        return new ReceiptDraft(
            SupplierName: recognized.Recognition.SupplierName ?? "(не распознан)",
            SupplierTaxId: recognized.Recognition.SupplierTaxId,
            InvoiceNumber: recognized.Recognition.InvoiceNumber,
            InvoiceDate: recognized.Recognition.InvoiceDate,
            Currency: recognized.Recognition.Currency,
            TotalAmount: recognized.Recognition.TotalAmount,
            TotalVat: recognized.Recognition.TotalVat,
            Lines: lines,
            SourceLabel: $"ai:{recognized.Recognition.ProviderName}:{Path.GetFileName(_selectedFilePath ?? "unknown")}",
            CreatedByActor: Environment.UserName,
            CommentText: $"Распознано {recognized.Matches.Count} строк через {recognized.Recognition.ProviderName} за {recognized.Recognition.Duration.TotalMilliseconds:N0} мс.");
    }

    // Mutable INPC viewmodel — MatchSummary меняется при ручном выборе через picker.
    private sealed class LineRowViewModel : INotifyPropertyChanged
    {
        private string _matchSummary = string.Empty;
        private bool _isManuallyEdited;

        public int LineNumber { get; init; }
        public string RecognizedName { get; init; } = string.Empty;
        public string RecognizedSku { get; init; } = string.Empty;
        public string Unit { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public decimal? UnitPrice { get; init; }
        public decimal? Total { get; init; }

        // Источник истины для save: original (от AI) и current (с возможным override).
        public MatchedInvoiceLine OriginalMatch { get; init; } = null!;
        public NomenclatureRef? CurrentMatch { get; set; }

        public string MatchSummary
        {
            get => _matchSummary;
            set { if (_matchSummary != value) { _matchSummary = value; OnPropertyChanged(); } }
        }

        public bool IsManuallyEdited
        {
            get => _isManuallyEdited;
            set { if (_isManuallyEdited != value) { _isManuallyEdited = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
