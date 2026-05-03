using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Text;
using WpfButton = System.Windows.Controls.Button;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class RecordsWorkspaceView : UserControl, IDisposable
{
    private const int PageSize = 10;

    private readonly RecordsWorkspaceDefinition _definition;
    private readonly ObservableCollection<WorkspaceMetricCardViewModel> _metrics = [];
    private readonly ObservableCollection<RecordsGroupNodeViewModel> _groupNodes = [];
    private readonly System.Windows.Threading.DispatcherTimer _searchDebounceTimer;
    private IReadOnlyList<RecordsGridItem> _allRows = Array.Empty<RecordsGridItem>();
    private IReadOnlyList<RecordsGridItem> _filteredRows = Array.Empty<RecordsGridItem>();
    private string _selectedGroupPath = string.Empty;
    private int _currentPage = 1;
    private bool _disposed;

    public RecordsWorkspaceView(RecordsWorkspaceDefinition definition)
    {
        _definition = definition;
        _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _searchDebounceTimer.Tick += HandleSearchDebounceTick;

        InitializeComponent();

        TitleText.Text = Clean(definition.Title);
        SubtitleText.Text = Clean(definition.Subtitle);
        HeaderSearchPlaceholderText.Text = Clean(definition.SearchPlaceholder);
        PrimaryActionButton.Content = Clean(definition.PrimaryActionText);
        AutomationProperties.SetName(ImportButton, "Импорт");
        AutomationProperties.SetName(PrimaryActionButton, Clean(definition.PrimaryActionText));
        PrimaryActionButton.Visibility = definition.ShowPrimaryAction && !string.IsNullOrWhiteSpace(definition.PrimaryActionText) ? Visibility.Visible : Visibility.Collapsed;
        PrimaryActionButton.IsEnabled = definition.PrimaryAction is not null;
        PrimaryActionButton.Opacity = PrimaryActionButton.IsEnabled ? 1d : 0.55d;
        ImportButton.Visibility = definition.ShowImportAction && definition.ImportAction is not null ? Visibility.Visible : Visibility.Collapsed;
        ImportButton.IsEnabled = definition.ImportAction is not null;
        ImportButton.Opacity = ImportButton.IsEnabled ? 1d : 0.55d;
        DateRangePanel.Visibility = definition.ShowDateRange ? Visibility.Visible : Visibility.Collapsed;
        GroupTreeTitleText.Text = Clean(definition.GroupTreeTitle);
        GroupTreeView.ItemsSource = _groupNodes;
        GroupTreePanel.Visibility = definition.GroupTreeFactory is null ? Visibility.Collapsed : Visibility.Visible;
        GroupTreeSpacer.Visibility = GroupTreePanel.Visibility;

        MetricsItemsControl.ItemsSource = _metrics;
        PrimaryFilterCombo.ItemsSource = definition.PrimaryFilterOptions.Select(Clean).ToArray();
        PrimaryFilterCombo.SelectedIndex = 0;
        _definition.SubscribeToChanges?.Invoke(HandleWorkspaceChanged);
        RecordsGrid.PreviewMouseLeftButtonUp += HandleRecordsGridMouseLeftButtonUp;
        RecordsGrid.KeyDown += HandleRecordsGridKeyDown;

        BuildColumns();
        Loaded += HandleLoaded;
        Unloaded += HandleUnloaded;
        SizeChanged += HandleSizeChanged;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        RefreshView();
        UpdateResponsiveLayout();
        ScheduleAutomationNormalization();
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        _searchDebounceTimer.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _searchDebounceTimer.Stop();
        RecordsGrid.PreviewMouseLeftButtonUp -= HandleRecordsGridMouseLeftButtonUp;
        RecordsGrid.KeyDown -= HandleRecordsGridKeyDown;
        _definition.UnsubscribeFromChanges?.Invoke(HandleWorkspaceChanged);
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RefreshView();
            ScheduleAutomationNormalization();
        });
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
    }

    private void RefreshView(bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            _definition.RefreshAction?.Invoke();
        }

        RenderMetrics();
        _allRows = _definition.RowsFactory();
        RenderGroupTree();
        ApplyFilters(resetPage: true);
        UpdateResponsiveLayout();
        ScheduleAutomationNormalization();
    }

    private void ScheduleAutomationNormalization()
    {
        Dispatcher.BeginInvoke(
            new Action(() => WpfTextNormalizer.NormalizeTree(this)),
            System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void UpdateResponsiveLayout()
    {
        if (ActualWidth < 1220)
        {
            Grid.SetColumn(HeaderActionsPanel, 0);
            Grid.SetRow(HeaderActionsPanel, 1);
            Grid.SetColumnSpan(HeaderActionsPanel, 2);
            HeaderActionsPanel.Margin = new Thickness(0, 16, 0, 0);
            HeaderActionsPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
            HeaderSearchBorder.Width = 260;
        }
        else
        {
            Grid.SetColumn(HeaderActionsPanel, 1);
            Grid.SetRow(HeaderActionsPanel, 0);
            Grid.SetColumnSpan(HeaderActionsPanel, 1);
            HeaderActionsPanel.Margin = new Thickness(0);
            HeaderActionsPanel.HorizontalAlignment = HorizontalAlignment.Right;
            HeaderSearchBorder.Width = 320;
        }
    }

    private void RenderMetrics()
    {
        _metrics.Clear();
        foreach (var metric in _definition.MetricsFactory())
        {
            _metrics.Add(new WorkspaceMetricCardViewModel(
                Clean(metric.Title),
                Clean(metric.Value),
                Clean(metric.Delta),
                Clean(metric.Hint),
                BrushPalette.FromHex(metric.AccentHex),
                BrushPalette.FromHex(metric.IconBackgroundHex),
                BrushPalette.FromHex(metric.DeltaHex),
                Clean(metric.IconGlyph)));
        }
    }

    private void BuildColumns()
    {
        RecordsGrid.Columns.Clear();

        foreach (var column in _definition.Columns)
        {
            DataGridColumn built = column.Kind switch
            {
                RecordsColumnKind.Status => CreateStatusColumn(column),
                RecordsColumnKind.Action => CreateActionColumn(column),
                _ => CreateTextColumn(column)
            };

            RecordsGrid.Columns.Add(built);
        }
    }

    private DataGridColumn CreateTextColumn(RecordsGridColumnDefinition column)
    {
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{column.CellIndex}].Text"));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding($"Cells[{column.CellIndex}].ForegroundBrush"));
        text.SetBinding(TextBlock.FontWeightProperty, new Binding($"Cells[{column.CellIndex}].Weight"));
        text.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 8, 0));
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.TextAlignmentProperty, column.Alignment);
        template.VisualTree = text;

        return new DataGridTemplateColumn
        {
            Header = Clean(column.Header),
            Width = column.ToDataGridLength(),
            CellTemplate = template
        };
    }

    private DataGridColumn CreateStatusColumn(RecordsGridColumnDefinition column)
    {
        var template = new DataTemplate();
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding($"Cells[{column.CellIndex}].BackgroundBrush"));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
        border.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        border.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Center);

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding($"Cells[{column.CellIndex}].Text"));
        text.SetBinding(TextBlock.ForegroundProperty, new Binding($"Cells[{column.CellIndex}].ForegroundBrush"));
        text.SetValue(TextBlock.FontSizeProperty, 12d);
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(text);

        template.VisualTree = border;

        return new DataGridTemplateColumn
        {
            Header = Clean(column.Header),
            Width = column.ToDataGridLength(),
            CellTemplate = template
        };
    }

    private DataGridColumn CreateActionColumn(RecordsGridColumnDefinition column)
    {
        var template = new DataTemplate();
        var button = new FrameworkElementFactory(typeof(WpfButton));
        button.SetValue(ContentControl.ContentProperty, "\uE712");
        button.SetValue(AutomationProperties.NameProperty, "Действия строки");
        button.SetBinding(FrameworkElement.TagProperty, new Binding("."));
        button.SetValue(FrameworkElement.StyleProperty, TryFindResource("TableRowActionButtonStyle") as Style);
        button.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        button.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        button.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler(HandleRowActionsClick));
        template.VisualTree = button;

        return new DataGridTemplateColumn
        {
            Header = Clean(column.Header),
            Width = column.ToDataGridLength(),
            CellTemplate = template
        };
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not RecordsGridItem row || row.Actions.Count == 0)
        {
            return;
        }

        RecordsGrid.SelectedItem = row;

        var menu = new ContextMenu
        {
            PlacementTarget = button
        };

        foreach (var action in row.Actions)
        {
            var item = new MenuItem
            {
                Header = Clean(action.Title),
                Foreground = action.IsDanger ? BrushPalette.FromHex("#F15B5B") : BrushPalette.FromHex("#1B2440"),
                FontWeight = action.IsDanger ? FontWeights.SemiBold : FontWeights.Normal,
                Padding = new Thickness(12, 8, 18, 8)
            };
            item.Click += (_, _) =>
            {
                ExecuteWorkspaceAction(action.Execute);
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
        e.Handled = true;
    }

    private void HandleRecordsGridMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindVisualParent<WpfButton>(source) is not null
            || FindVisualParent<DataGridColumnHeader>(source) is not null)
        {
            return;
        }

        var row = FindVisualParent<DataGridRow>(source);
        if (row?.Item is RecordsGridItem item)
        {
            ExecutePrimaryRowAction(item);
        }
    }

    private void RenderGroupTree()
    {
        _groupNodes.Clear();
        if (_definition.GroupTreeFactory is null)
        {
            _selectedGroupPath = string.Empty;
            return;
        }

        foreach (var node in _definition.GroupTreeFactory())
        {
            _groupNodes.Add(RecordsGroupNodeViewModel.FromDefinition(node));
        }

        if (!string.IsNullOrWhiteSpace(_selectedGroupPath)
            && !_groupNodes.SelectMany(FlattenGroupNode).Any(node => node.GroupPath.Equals(_selectedGroupPath, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedGroupPath = string.Empty;
        }
    }

    private static IEnumerable<RecordsGroupNodeViewModel> FlattenGroupNode(RecordsGroupNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(FlattenGroupNode))
        {
            yield return child;
        }
    }

    private void HandleRecordsGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || RecordsGrid.SelectedItem is not RecordsGridItem item)
        {
            return;
        }

        ExecutePrimaryRowAction(item);
        e.Handled = true;
    }

    private void ExecutePrimaryRowAction(RecordsGridItem item)
    {
        var primaryAction = item.Actions.FirstOrDefault();
        if (primaryAction is null)
        {
            return;
        }

        RecordsGrid.SelectedItem = item;
        ExecuteWorkspaceAction(primaryAction.Execute);
    }

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void ApplyFilters(bool resetPage = false)
    {
        var search = Clean(HeaderSearchBox.Text).Trim();
        var allFilter = Clean(_definition.PrimaryFilterOptions.FirstOrDefault());
        var selectedFilter = PrimaryFilterCombo.SelectedItem?.ToString() ?? allFilter;
        var start = StartDatePicker.SelectedDate?.Date;
        var end = EndDatePicker.SelectedDate?.Date;

        var rows = _allRows.Where(row =>
        {
            if (!string.IsNullOrWhiteSpace(search)
                && !row.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_selectedGroupPath)
                && !row.GroupPath.StartsWith(_selectedGroupPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(selectedFilter)
                && !selectedFilter.Equals(allFilter, StringComparison.OrdinalIgnoreCase)
                && !row.FilterValue.Equals(selectedFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (start.HasValue && row.DateValue.HasValue && row.DateValue.Value.Date < start.Value)
            {
                return false;
            }

            if (end.HasValue && row.DateValue.HasValue && row.DateValue.Value.Date > end.Value)
            {
                return false;
            }

            return true;
        }).ToArray();

        _filteredRows = rows;

        if (resetPage)
        {
            _currentPage = 1;
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRows.Count / (double)PageSize));
        _currentPage = Math.Max(1, Math.Min(_currentPage, totalPages));
        RenderCurrentPage(totalPages);
    }

    private void RenderCurrentPage(int totalPages)
    {
        var pageRows = _filteredRows
            .Skip((_currentPage - 1) * PageSize)
            .Take(PageSize)
            .ToArray();

        RecordsGrid.ItemsSource = pageRows;
        EmptyStateText.Visibility = pageRows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Text = _definition.EmptyStateText;

        var from = _filteredRows.Count == 0 ? 0 : ((_currentPage - 1) * PageSize) + 1;
        var to = Math.Min(_currentPage * PageSize, _filteredRows.Count);
        FooterCountText.Text = $"Показано {from}-{to} из {_filteredRows.Count}";

        RebuildPagination(totalPages);
    }

    private void RebuildPagination(int totalPages)
    {
        PaginationPanel.Children.Clear();
        if (totalPages <= 1)
        {
            return;
        }

        PaginationPanel.Children.Add(CreatePageButton("<", _currentPage - 1, _currentPage == 1));

        var pages = BuildVisiblePages(totalPages, _currentPage);
        foreach (var page in pages)
        {
            if (page < 0)
            {
                PaginationPanel.Children.Add(new TextBlock
                {
                    Text = "...",
                    Style = TryFindResource("TablePagerEllipsisTextStyle") as Style
                });
                continue;
            }

            PaginationPanel.Children.Add(CreatePageButton(page.ToString(), page, false, page == _currentPage));
        }

        PaginationPanel.Children.Add(CreatePageButton(">", _currentPage + 1, _currentPage == totalPages));
    }

    private static IReadOnlyList<int> BuildVisiblePages(int totalPages, int currentPage)
    {
        if (totalPages <= 5)
        {
            return Enumerable.Range(1, totalPages).ToArray();
        }

        var pages = new List<int> { 1 };
        if (currentPage > 3)
        {
            pages.Add(-1);
        }

        var start = Math.Max(2, currentPage - 1);
        var end = Math.Min(totalPages - 1, currentPage + 1);
        for (var page = start; page <= end; page++)
        {
            pages.Add(page);
        }

        if (currentPage < totalPages - 2)
        {
            pages.Add(-1);
        }

        pages.Add(totalPages);
        return pages.Distinct().ToArray();
    }

    private Button CreatePageButton(string text, int targetPage, bool disabled, bool active = false)
    {
        var button = new Button
        {
            Content = text,
            Style = TryFindResource(active ? "TablePagerActiveButtonStyle" : "TablePagerButtonStyle") as Style,
            Cursor = disabled ? Cursors.Arrow : Cursors.Hand,
            IsEnabled = !disabled,
            Opacity = disabled ? 0.45d : 1d,
            Tag = targetPage
        };
        AutomationProperties.SetName(button, text switch
        {
            "<" => "Предыдущая страница",
            ">" => "Следующая страница",
            _ => $"Страница {text}"
        });
        button.Click += HandlePageClick;
        return button;
    }

    private void HandlePageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int targetPage)
        {
            return;
        }

        _currentPage = targetPage;
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRows.Count / (double)PageSize));
        RenderCurrentPage(totalPages);
    }

    private void HandleHeaderSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        HeaderSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(HeaderSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void HandleSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        ApplyFilters(resetPage: true);
    }

    private void HandleGroupTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not RecordsGroupNodeViewModel node)
        {
            return;
        }

        _selectedGroupPath = node.GroupPath;
        ApplyFilters(resetPage: true);
    }

    private void HandleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyFilters(resetPage: true);
    }

    private void HandleDateFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyFilters(resetPage: true);
    }

    private void HandleFilterButtonClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var resetItem = new MenuItem { Header = "Сбросить фильтры" };
        resetItem.Click += (_, _) =>
        {
            PrimaryFilterCombo.SelectedIndex = 0;
            StartDatePicker.SelectedDate = null;
            EndDatePicker.SelectedDate = null;
            HeaderSearchBox.Clear();
            ApplyFilters(resetPage: true);
        };
        menu.Items.Add(resetItem);

        if (_definition.ShowDateRange)
        {
            var last30Item = new MenuItem { Header = "Последние 30 дней" };
            last30Item.Click += (_, _) =>
            {
                EndDatePicker.SelectedDate = DateTime.Today;
                StartDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
                ApplyFilters(resetPage: true);
            };
            menu.Items.Add(last30Item);

            var currentMonthItem = new MenuItem { Header = "Текущий месяц" };
            currentMonthItem.Click += (_, _) =>
            {
                var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                StartDatePicker.SelectedDate = start;
                EndDatePicker.SelectedDate = start.AddMonths(1).AddDays(-1);
                ApplyFilters(resetPage: true);
            };
            menu.Items.Add(currentMonthItem);
        }

        menu.PlacementTarget = FilterButton;
        menu.IsOpen = true;
    }

    private void HandlePrimaryActionClick(object sender, RoutedEventArgs e)
    {
        if (_definition.PrimaryAction is not null)
        {
            ExecuteWorkspaceAction(_definition.PrimaryAction);
        }
    }

    private void HandleImportClick(object sender, RoutedEventArgs e)
    {
        if (_definition.ImportAction is not null)
        {
            ExecuteWorkspaceAction(_definition.ImportAction);
        }
    }

    private void ExecuteWorkspaceAction(Action action)
    {
        try
        {
            action();
            RefreshView(forceRefresh: true);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string Clean(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }
}

public sealed record RecordsWorkspaceDefinition(
    string Title,
    string Subtitle,
    string SearchPlaceholder,
    string PrimaryActionText,
    IReadOnlyList<string> PrimaryFilterOptions,
    bool ShowDateRange,
    Func<IReadOnlyList<WorkspaceMetricCardDefinition>> MetricsFactory,
    Func<IReadOnlyList<RecordsGridItem>> RowsFactory,
    IReadOnlyList<RecordsGridColumnDefinition> Columns,
    string EmptyStateText = "Нет данных",
    Action? RefreshAction = null,
    Action? PrimaryAction = null,
    Action? ImportAction = null,
    bool ShowPrimaryAction = true,
    bool ShowImportAction = true,
    Action<EventHandler>? SubscribeToChanges = null,
    Action<EventHandler>? UnsubscribeFromChanges = null,
    Func<IReadOnlyList<RecordsGroupNodeDefinition>>? GroupTreeFactory = null,
    string GroupTreeTitle = "Папки");

public sealed record WorkspaceMetricCardDefinition(
    string Title,
    string Value,
    string Delta,
    string Hint,
    string AccentHex,
    string IconBackgroundHex,
    string DeltaHex,
    string IconGlyph);

public sealed record WorkspaceMetricCardViewModel(
    string Title,
    string Value,
    string Delta,
    string Hint,
    Brush AccentBrush,
    Brush IconBackground,
    Brush DeltaBrush,
    string IconGlyph);

public sealed record RecordsGridColumnDefinition(
    string Header,
    int CellIndex,
    RecordsColumnKind Kind = RecordsColumnKind.Text,
    double WidthValue = 1,
    bool IsStar = true,
    TextAlignment Alignment = TextAlignment.Left)
{
    public DataGridLength ToDataGridLength()
    {
        return IsStar
            ? new DataGridLength(WidthValue, DataGridLengthUnitType.Star)
            : new DataGridLength(WidthValue, DataGridLengthUnitType.Pixel);
    }
}

public enum RecordsColumnKind
{
    Text,
    Status,
    Action
}

public sealed record RecordsGridItem(
    string SearchText,
    string FilterValue,
    DateTime? DateValue,
    IReadOnlyList<RecordsGridCellDefinition> Cells,
    IReadOnlyList<RecordsGridActionDefinition>? RowActions = null,
    string GroupPath = "")
{
    public IReadOnlyList<RecordsGridActionDefinition> Actions => RowActions ?? Array.Empty<RecordsGridActionDefinition>();
}

public sealed record RecordsGroupNodeDefinition(
    string Title,
    string GroupPath,
    int Count,
    IReadOnlyList<RecordsGroupNodeDefinition>? Children = null,
    string IconGlyph = "\uE8B7");

public sealed class RecordsGroupNodeViewModel
{
    public RecordsGroupNodeViewModel(
        string title,
        string groupPath,
        int count,
        string iconGlyph,
        IEnumerable<RecordsGroupNodeViewModel>? children = null)
    {
        Title = title;
        GroupPath = groupPath;
        Count = count;
        IconGlyph = iconGlyph;
        Children = new ObservableCollection<RecordsGroupNodeViewModel>(children ?? Array.Empty<RecordsGroupNodeViewModel>());
    }

    public string Title { get; }

    public string GroupPath { get; }

    public int Count { get; }

    public string CountText => $"({Count:N0})";

    public string IconGlyph { get; }

    public ObservableCollection<RecordsGroupNodeViewModel> Children { get; }

    public override string ToString()
    {
        return Title;
    }

    public static RecordsGroupNodeViewModel FromDefinition(RecordsGroupNodeDefinition definition)
    {
        return new RecordsGroupNodeViewModel(
            TextMojibakeFixer.NormalizeText(definition.Title),
            definition.GroupPath,
            definition.Count,
            definition.IconGlyph,
            definition.Children?.Select(FromDefinition));
    }
}

public sealed record RecordsGridActionDefinition(
    string Title,
    Action Execute,
    bool IsDanger = false);

public sealed record RecordsGridCellDefinition(
    string Text,
    string ForegroundHex = "#1B2440",
    string BackgroundHex = "Transparent",
    bool SemiBold = false)
{
    public Brush ForegroundBrush => BrushPalette.FromHex(ForegroundHex);

    public Brush BackgroundBrush => BrushPalette.FromHex(BackgroundHex);

    public FontWeight Weight => SemiBold ? FontWeights.SemiBold : FontWeights.Normal;
}

internal static class BrushPalette
{
    private static readonly Dictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Brush FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Brushes.Transparent;
        }

        if (Cache.TryGetValue(hex, out var brush))
        {
            return brush;
        }

        brush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        Cache[hex] = brush;
        return brush;
    }
}
