using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 5 learning loop: окно выбора товара из каталога при ручном переопределении
// AI-предложенного матча. Используется в InvoiceRecognitionWindow для подтверждения
// строк которые AI распознал но не нашёл соответствующий товар.
public partial class NomenclaturePickerWindow : Window
{
    private readonly IReadOnlyList<NomenclatureRef> _catalog;
    private DispatcherTimer? _searchDebounceTimer;

    public NomenclatureRef? SelectedItem { get; private set; }

    public NomenclaturePickerWindow(
        IReadOnlyList<NomenclatureRef> catalog,
        string recognizedText,
        NomenclatureRef? initialSelection = null)
    {
        InitializeComponent();
        _catalog = catalog;
        RecognizedText.Text = string.IsNullOrWhiteSpace(recognizedText)
            ? "(нет текста)"
            : recognizedText;

        // По умолчанию ищем по распознанному имени.
        SearchBox.Text = recognizedText ?? string.Empty;
        ApplyFilter(SearchBox.Text);

        // Если есть текущий выбор — выделить в списке.
        if (initialSelection is not null && ResultsGrid.Items.Count > 0)
        {
            foreach (var item in ResultsGrid.Items)
            {
                if (item is NomenclatureRef nr && nr.Id == initialSelection.Id)
                {
                    ResultsGrid.SelectedItem = item;
                    ResultsGrid.ScrollIntoView(item);
                    break;
                }
            }
        }

        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= OnSearchDebounceTick;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
        _searchDebounceTimer.Start();
    }

    private void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        ApplyFilter(SearchBox.Text);
    }

    private void ApplyFilter(string? query)
    {
        var normalized = (query ?? string.Empty).Trim().ToLowerInvariant();

        IEnumerable<NomenclatureRef> filtered;
        if (string.IsNullOrEmpty(normalized))
        {
            // Без фильтра — показываем первые 200 строк (быстрая ориентация).
            filtered = _catalog.Take(200);
        }
        else
        {
            // Простой client-side filter — code или name содержит query.
            filtered = _catalog
                .Where(item =>
                    (item.Code?.ToLowerInvariant().Contains(normalized) ?? false)
                    || (item.Name?.ToLowerInvariant().Contains(normalized) ?? false))
                .Take(500);
        }

        var rows = filtered.ToList();
        ResultsGrid.ItemsSource = rows;

        StatusText.Text = string.IsNullOrEmpty(normalized)
            ? $"Показаны первые 200 из {_catalog.Count} позиций. Начните печатать для поиска."
            : $"Найдено: {rows.Count} (из {_catalog.Count})";
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectButton.IsEnabled = ResultsGrid.SelectedItem is NomenclatureRef;
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is NomenclatureRef item)
        {
            SelectItem(item);
        }
    }

    private void OnSelectClicked(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is NomenclatureRef item)
        {
            SelectItem(item);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SelectItem(NomenclatureRef item)
    {
        SelectedItem = item;
        DialogResult = true;
        Close();
    }
}
