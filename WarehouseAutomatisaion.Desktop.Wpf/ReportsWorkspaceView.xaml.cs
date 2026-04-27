using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using WarehouseAutomatisaion.Desktop.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ReportsWorkspaceView : UserControl, IDisposable
{
    private const int PageSize = 7;
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Dictionary<string, string> TabLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sales"] = "Продажи",
        ["orders"] = "Заказы",
        ["invoices"] = "Счета",
        ["shipments"] = "Отгрузки",
        ["customers"] = "Клиенты",
        ["catalog"] = "Товары"
    };

    private static readonly Dictionary<string, string> NavigationTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sales"] = "sales",
        ["orders"] = "sales",
        ["invoices"] = "invoices",
        ["shipments"] = "shipments",
        ["customers"] = "customers",
        ["catalog"] = "catalog"
    };

    private readonly SalesWorkspace _salesWorkspace;
    private bool _isLoaded;
    private bool _suppressFilterEvents;
    private bool _suppressSelectionEvents;
    private string _activeTab = "sales";
    private int _page = 1;
    private DateTime _periodFrom = DateTime.Today.AddDays(-60);
    private DateTime _periodTo = DateTime.Today;
    private DateTime _lastGeneratedAt = DateTime.Now;
    private ReportDashboardState _currentState = new();
    private IReadOnlyList<ReportTrendPointViewModel> _currentTrendPoints = Array.Empty<ReportTrendPointViewModel>();
    private IReadOnlyList<ReportLegendItemViewModel> _currentDonutItems = Array.Empty<ReportLegendItemViewModel>();
    private IReadOnlyList<ReportRegistryRowViewModel> _filteredRegistryRows = Array.Empty<ReportRegistryRowViewModel>();

    public ReportsWorkspaceView(SalesWorkspace salesWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        InitializeComponent();
        HookWorkspaceEvents();
        Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;
        Unloaded += HandleUnloaded;
    }

    public event EventHandler<string>? NavigationRequested;

    public void Dispose()
    {
        UnhookWorkspaceEvents();
        Loaded -= HandleLoaded;
        SizeChanged -= HandleSizeChanged;
        Unloaded -= HandleUnloaded;
    }

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        InitializeFilters();
        RenderAll();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            DrawTrendChart();
            DrawDonutChart();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void HandleUnloaded(object sender, RoutedEventArgs e)
    {
        DrawTrendChart();
        DrawDonutChart();
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
        DrawTrendChart();
        DrawDonutChart();
    }

    private void HookWorkspaceEvents()
    {
        _salesWorkspace.Changed += HandleWorkspaceChanged;
        _salesWorkspace.Customers.ListChanged += HandleWorkspaceListChanged;
        _salesWorkspace.Orders.ListChanged += HandleWorkspaceListChanged;
        _salesWorkspace.Invoices.ListChanged += HandleWorkspaceListChanged;
        _salesWorkspace.Shipments.ListChanged += HandleWorkspaceListChanged;
        _salesWorkspace.OperationLog.ListChanged += HandleWorkspaceListChanged;
    }

    private void UnhookWorkspaceEvents()
    {
        _salesWorkspace.Changed -= HandleWorkspaceChanged;
        _salesWorkspace.Customers.ListChanged -= HandleWorkspaceListChanged;
        _salesWorkspace.Orders.ListChanged -= HandleWorkspaceListChanged;
        _salesWorkspace.Invoices.ListChanged -= HandleWorkspaceListChanged;
        _salesWorkspace.Shipments.ListChanged -= HandleWorkspaceListChanged;
        _salesWorkspace.OperationLog.ListChanged -= HandleWorkspaceListChanged;
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        QueueRefresh();
    }

    private void HandleWorkspaceListChanged(object? sender, ListChangedEventArgs e)
    {
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (!_isLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(RenderAll), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void InitializeFilters()
    {
        _suppressFilterEvents = true;

        ConfigureComboBox(ModuleFilterComboBox);
        ConfigureComboBox(ManagerFilterComboBox);
        ConfigureComboBox(WarehouseFilterComboBox);
        ConfigureComboBox(StatusFilterComboBox);
        ConfigureComboBox(ClientFilterComboBox);

        ModuleFilterComboBox.ItemsSource = BuildModuleOptions();
        ManagerFilterComboBox.ItemsSource = BuildSimpleOptions("all", "Все менеджеры", _salesWorkspace.Managers.Select(Ui));
        WarehouseFilterComboBox.ItemsSource = BuildSimpleOptions("all", "Все склады", _salesWorkspace.Warehouses.Select(Ui));
        ClientFilterComboBox.ItemsSource = BuildSimpleOptions("all", "Все клиенты", _salesWorkspace.Customers.Select(customer => Ui(customer.Name)));

        PeriodFromDatePicker.SelectedDate = _periodFrom;
        PeriodToDatePicker.SelectedDate = _periodTo;

        SelectOptionByValue(ModuleFilterComboBox, _activeTab);
        SelectOptionByValue(ManagerFilterComboBox, "all");
        SelectOptionByValue(WarehouseFilterComboBox, "all");
        SelectOptionByValue(ClientFilterComboBox, "all");
        UpdateStatusOptions();

        _suppressFilterEvents = false;
        UpdateSearchPlaceholder();
    }

    private void RenderAll()
    {
        if (!_isLoaded)
        {
            return;
        }

        NormalizePeriod();
        UpdateSectionTabs();
        UpdateResponsiveLayout();

        var previousRange = GetPreviousPeriod();
        var currentSlice = BuildSlice(_periodFrom, _periodTo);
        var previousSlice = BuildSlice(previousRange.From, previousRange.To);

        _currentState = BuildState(currentSlice, previousSlice);
        _currentTrendPoints = _currentState.TrendPoints;
        _currentDonutItems = _currentState.DonutItems;

        TrendChartTitleText.Text = _currentState.TrendTitle;
        DonutCardTitleText.Text = _currentState.DonutTitle;
        MonthlyChartTitleText.Text = _currentState.MonthlyTitle;
        TopClientsTitleText.Text = _currentState.LeftCardTitle;
        TopProductsTitleText.Text = _currentState.MiddleCardTitle;
        OverdueCardTitleText.Text = _currentState.RightLeftCardTitle;
        DelayedShipmentsTitleText.Text = _currentState.RightMiddleCardTitle;
        RegistryTitleText.Text = _currentState.RegistryTitle;
        RegistryCountText.Text = $"Всего {_currentState.RegistryRows.Count.ToString("N0", RuCulture)} записей";
        SummaryPeriodText.Text = FormatPeriod(_periodFrom, _periodTo);
        SummaryModuleText.Text = _currentState.SummaryModuleLabel;
        SummaryUpdatedText.Text = _lastGeneratedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);
        HeaderPeriodButtonText.Text = FormatPeriod(_periodFrom, _periodTo);
        NavigateModuleButtonText.Text = _currentState.NavigateModuleCaption;

        MetricCardsItemsControl.ItemsSource = _currentState.Metrics;
        ComparisonItemsControl.ItemsSource = _currentState.ComparisonItems;
        SignalsItemsControl.ItemsSource = _currentState.Signals;
        MonthlyBarsItemsControl.ItemsSource = _currentState.MonthlyBars;
        DonutLegendItemsControl.ItemsSource = _currentState.DonutItems;
        TopClientsItemsControl.ItemsSource = _currentState.LeftCardItems;
        TopProductsItemsControl.ItemsSource = _currentState.MiddleCardItems;
        OverdueItemsControl.ItemsSource = _currentState.RightLeftCardItems;
        DelayedShipmentsItemsControl.ItemsSource = _currentState.RightMiddleCardItems;
        RecentEventsItemsControl.ItemsSource = _currentState.RecentEvents;
        SummaryDeviationsItemsControl.ItemsSource = _currentState.SummaryDeltas;

        UpdateRegistryColumns(_currentState);
        RenderRegistry();
        UpdateSearchPlaceholder();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            DrawTrendChart();
            DrawDonutChart();
        }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private void UpdateResponsiveLayout()
    {
        if (ActualWidth < 1420)
        {
            BottomSectionGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            BottomSectionGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(SummaryCard, 0);
            Grid.SetRow(SummaryCard, 1);
            Grid.SetColumnSpan(SummaryCard, 2);
            SummaryCard.Margin = new Thickness(0, 16, 0, 0);
            RegistryCard.Margin = new Thickness(0);

            if (BottomSectionGrid.RowDefinitions.Count == 1)
            {
                BottomSectionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
        }
        else
        {
            BottomSectionGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            BottomSectionGrid.ColumnDefinitions[1].Width = new GridLength(304);
            Grid.SetColumn(SummaryCard, 1);
            Grid.SetRow(SummaryCard, 0);
            Grid.SetColumnSpan(SummaryCard, 1);
            SummaryCard.Margin = new Thickness(0);
            RegistryCard.Margin = new Thickness(0, 0, 16, 0);

            while (BottomSectionGrid.RowDefinitions.Count > 1)
            {
                BottomSectionGrid.RowDefinitions.RemoveAt(BottomSectionGrid.RowDefinitions.Count - 1);
            }
        }
    }

    private void DrawTrendChart()
    {
        RevenueChartCanvas.Children.Clear();

        var width = Math.Max(320d, RevenueChartCanvas.ActualWidth);
        var height = Math.Max(220d, RevenueChartCanvas.ActualHeight);
        RevenueChartCanvas.Width = width;
        RevenueChartCanvas.Height = height;

        if (_currentTrendPoints.Count == 0 || _currentTrendPoints.All(item => item.CurrentValue <= 0 && item.PreviousValue <= 0))
        {
            RevenueChartCanvas.Children.Add(new TextBlock
            {
                Text = "Нет данных",
                Foreground = BrushFromHex("#98A3BC"),
                FontSize = 14,
                Margin = new Thickness(width / 2d - 32d, height / 2d - 10d, 0, 0)
            });
            return;
        }

        const double leftPad = 18;
        const double topPad = 18;
        const double rightPad = 12;
        const double bottomPad = 28;
        var plotWidth = width - leftPad - rightPad;
        var plotHeight = height - topPad - bottomPad;
        var maxValue = Math.Max(1m, _currentTrendPoints.Max(item => Math.Max(item.CurrentValue, item.PreviousValue)));

        for (var step = 0; step <= 4; step++)
        {
            var y = topPad + plotHeight * step / 4d;
            RevenueChartCanvas.Children.Add(new Line
            {
                X1 = leftPad,
                X2 = leftPad + plotWidth,
                Y1 = y,
                Y2 = y,
                Stroke = BrushFromHex("#EEF2FA"),
                StrokeThickness = 1
            });

            var level = maxValue * (4 - step) / 4m;
            RevenueChartCanvas.Children.Add(new TextBlock
            {
                Text = ToCompactMoneyLabel(level),
                Foreground = BrushFromHex("#B2BBD1"),
                FontSize = 10.5,
                Margin = new Thickness(0, y - 8, 0, 0)
            });
        }

        var previousLine = new Polyline
        {
            Stroke = BrushFromHex("#CBD4E8"),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeDashArray = [4, 4]
        };

        var currentLine = new Polyline
        {
            Stroke = BrushFromHex("#4F5BFF"),
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round
        };

        for (var index = 0; index < _currentTrendPoints.Count; index++)
        {
            var point = _currentTrendPoints[index];
            var x = leftPad + plotWidth * index / Math.Max(1, _currentTrendPoints.Count - 1);
            var currentY = topPad + plotHeight * (1d - (double)(point.CurrentValue / maxValue));
            var previousY = topPad + plotHeight * (1d - (double)(point.PreviousValue / maxValue));

            previousLine.Points.Add(new Point(x, previousY));
            currentLine.Points.Add(new Point(x, currentY));

            RevenueChartCanvas.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = BrushFromHex("#4F5BFF"),
                Margin = new Thickness(x - 3, currentY - 3, 0, 0)
            });
            RevenueChartCanvas.Children.Add(new TextBlock
            {
                Text = point.Label,
                Foreground = BrushFromHex("#9AA5BD"),
                FontSize = 10.5,
                Margin = new Thickness(x - 12, topPad + plotHeight + 6, 0, 0)
            });
        }

        RevenueChartCanvas.Children.Add(previousLine);
        RevenueChartCanvas.Children.Add(currentLine);
    }

    private void DrawDonutChart()
    {
        DonutChartCanvas.Children.Clear();
        var items = _currentDonutItems.Where(item => item.Value > 0).ToArray();
        var total = items.Sum(item => item.Value);

        if (total <= 0)
        {
            AddCenteredCanvasText(DonutChartCanvas, "Нет данных", centerX: 66, centerY: 66, 13, FontWeights.Normal, BrushFromHex("#98A3BC"));
            return;
        }

        const double outerRadius = 48;
        const double innerRadius = 30;
        const double centerX = 66;
        const double centerY = 66;
        double startAngle = -90;

        foreach (var item in items)
        {
            var sweep = 360d * item.Value / total;
            DonutChartCanvas.Children.Add(CreateSegment(centerX, centerY, innerRadius, outerRadius, startAngle, sweep, item.ColorBrush));
            startAngle += sweep;
        }

        DonutChartCanvas.Children.Add(new Ellipse
        {
            Width = innerRadius * 2,
            Height = innerRadius * 2,
            Fill = Brushes.White,
            Margin = new Thickness(centerX - innerRadius, centerY - innerRadius, 0, 0)
        });
        AddCenteredCanvasText(DonutChartCanvas, total.ToString(CultureInfo.InvariantCulture), centerX, centerY - 8, 22, FontWeights.SemiBold, BrushFromHex("#1A2440"));
        AddCenteredCanvasText(DonutChartCanvas, "Всего", centerX, centerY + 18, 11.5, FontWeights.Normal, BrushFromHex("#7280A0"));
    }

    private static void AddCenteredCanvasText(Canvas canvas, string text, double centerX, double centerY, double fontSize, FontWeight fontWeight, Brush foreground)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Foreground = foreground
        };

        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        textBlock.Margin = new Thickness(
            centerX - textBlock.DesiredSize.Width / 2d,
            centerY - textBlock.DesiredSize.Height / 2d,
            0,
            0);
        canvas.Children.Add(textBlock);
    }

    private static Path CreateSegment(double centerX, double centerY, double innerRadius, double outerRadius, double startAngle, double sweepAngle, Brush fill)
    {
        var startOuter = PointOnCircle(centerX, centerY, outerRadius, startAngle);
        var endOuter = PointOnCircle(centerX, centerY, outerRadius, startAngle + sweepAngle);
        var startInner = PointOnCircle(centerX, centerY, innerRadius, startAngle);
        var endInner = PointOnCircle(centerX, centerY, innerRadius, startAngle + sweepAngle);
        var largeArc = sweepAngle > 180;

        var figure = new PathFigure { StartPoint = startOuter, IsClosed = true };
        figure.Segments.Add(new ArcSegment(endOuter, new Size(outerRadius, outerRadius), 0, largeArc, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(endInner, true));
        figure.Segments.Add(new ArcSegment(startInner, new Size(innerRadius, innerRadius), 0, largeArc, SweepDirection.Counterclockwise, true));
        figure.Segments.Add(new LineSegment(startOuter, true));

        return new Path { Data = new PathGeometry([figure]), Fill = fill };
    }

    private static Point PointOnCircle(double centerX, double centerY, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(centerX + radius * Math.Cos(radians), centerY + radius * Math.Sin(radians));
    }

    private void RenderRegistry()
    {
        _filteredRegistryRows = _currentState.RegistryRows;
        var total = _filteredRegistryRows.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        _page = Math.Clamp(_page, 1, totalPages);

        var pageRows = _filteredRegistryRows
            .Skip((_page - 1) * PageSize)
            .Take(PageSize)
            .ToArray();

        _suppressSelectionEvents = true;
        ReportRegistryGrid.ItemsSource = pageRows;
        ReportRegistryGrid.Visibility = total == 0 ? Visibility.Collapsed : Visibility.Visible;
        RegistryEmptyStateBorder.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        RegistryFooterText.Text = total == 0
            ? "Показано 0 записей"
            : $"Показано {((_page - 1) * PageSize) + 1}-{((_page - 1) * PageSize) + pageRows.Length} из {total}";
        PageIndicatorText.Text = _page.ToString(CultureInfo.InvariantCulture);
        RegistrySelectAllCheckBox.IsChecked = pageRows.Length > 0 && pageRows.All(item => item.IsSelected);
        _suppressSelectionEvents = false;
    }

    private ReportDashboardState BuildState(ReportSlice currentSlice, ReportSlice previousSlice)
    {
        return _activeTab switch
        {
            "orders" => BuildOrdersState(currentSlice, previousSlice),
            "invoices" => BuildInvoicesState(currentSlice, previousSlice),
            "shipments" => BuildShipmentsState(currentSlice, previousSlice),
            "customers" => BuildCustomersState(currentSlice, previousSlice),
            "catalog" => BuildCatalogState(currentSlice, previousSlice),
            _ => BuildSalesState(currentSlice, previousSlice)
        };
    }

    private ReportDashboardState BuildSalesState(ReportSlice current, ReportSlice previous)
    {
        var rows = current.Orders
            .Select(order =>
            {
                var invoice = current.Invoices.FirstOrDefault(item => item.SalesOrderId == order.Id);
                var shipment = current.Shipments.FirstOrDefault(item => item.SalesOrderId == order.Id);
                var status = ResolveSalesStatus(order, invoice, shipment);
                var amount = invoice?.TotalAmount ?? shipment?.TotalAmount ?? order.TotalAmount;
                return CreateRegistryRow(
                    order.OrderDate,
                    Ui(order.Number),
                    Ui(order.CustomerName),
                    FormatMoney(amount),
                    status,
                    Ui(order.Manager),
                    Ui(order.Comment),
                    NavigationTargets["sales"]);
            })
            .Where(row => MatchesStatusFilter(row.StatusText))
            .OrderByDescending(row => row.SortDate)
            .ToArray();

        var currentRevenue = current.Invoices.Sum(item => item.TotalAmount);
        var previousRevenue = previous.Invoices.Sum(item => item.TotalAmount);
        var currentOrders = current.Orders.Count;
        var previousOrders = previous.Orders.Count;
        var currentPaid = current.Invoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Оплачен");
        var previousPaid = previous.Invoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Оплачен");
        var currentAverageCheck = current.Invoices.Count > 0 ? current.Invoices.Average(item => item.TotalAmount) : 0m;
        var previousAverageCheck = previous.Invoices.Count > 0 ? previous.Invoices.Average(item => item.TotalAmount) : 0m;
        var currentDelivered = current.Shipments.Count(item => NormalizeShipmentStatus(item) == "Отгружена");
        var previousDelivered = previous.Shipments.Count(item => NormalizeShipmentStatus(item) == "Отгружена");
        var currentOverdue = CountOverdueInvoices(current.Invoices) + CountDelayedShipments(current.Shipments);
        var previousOverdue = CountOverdueInvoices(previous.Invoices) + CountDelayedShipments(previous.Shipments);

        var comparison = new[]
        {
            BuildComparison("Выручка", FormatMoney(currentRevenue), currentRevenue, previousRevenue),
            BuildComparison("Заказы", currentOrders.ToString(CultureInfo.InvariantCulture), currentOrders, previousOrders),
            BuildComparison("Средний чек", FormatMoney(currentAverageCheck), currentAverageCheck, previousAverageCheck),
            BuildComparison("Отгрузки", currentDelivered.ToString(CultureInfo.InvariantCulture), currentDelivered, previousDelivered),
            BuildComparison("Просрочено", currentOverdue.ToString(CultureInfo.InvariantCulture), currentOverdue, previousOverdue, lowerIsBetter: true)
        };

        return new ReportDashboardState
        {
            TrendTitle = "Динамика выручки",
            DonutTitle = "Статусы заказов",
            MonthlyTitle = "Выручка по месяцам",
            LeftCardTitle = "Топ клиентов",
            MiddleCardTitle = "Топ товаров",
            RightLeftCardTitle = "Просроченные счета",
            RightMiddleCardTitle = "Отгрузки с задержкой",
            RegistryTitle = "Реестр продаж",
            SummaryModuleLabel = "Продажи",
            NavigateModuleCaption = "Перейти в заказы",
            DateHeader = "Дата",
            DocumentHeader = "Заказ",
            CounterpartyHeader = "Клиент",
            ValueHeader = "Выручка",
            OwnerHeader = "Менеджер",
            Metrics =
            [
                BuildMetric("Выручка", FormatMoney(currentRevenue), currentRevenue, previousRevenue, "#27AE60", "#EBFBF1", "\uE7C3"),
                BuildMetric("Заказы", currentOrders.ToString(CultureInfo.InvariantCulture), currentOrders, previousOrders, "#4F5BFF", "#EEF2FF", "\uE8A5"),
                BuildMetric("Оплаченные счета", currentPaid.ToString(CultureInfo.InvariantCulture), currentPaid, previousPaid, "#FF9B28", "#FFF4E8", "\uE8C7"),
                BuildMetric("Средний чек", FormatMoney(currentAverageCheck), currentAverageCheck, previousAverageCheck, "#7B68EE", "#F2EEFF", "\uEC58"),
                BuildMetric("Выполненные отгрузки", currentDelivered.ToString(CultureInfo.InvariantCulture), currentDelivered, previousDelivered, "#34B56A", "#EBFBF1", "\uE7BF"),
                BuildMetric("Просроченные документы", currentOverdue.ToString(CultureInfo.InvariantCulture), currentOverdue, previousOverdue, "#F45A5A", "#FFF1F1", "\uEA39", lowerIsBetter: true)
            ],
            ComparisonItems = comparison,
            SummaryDeltas = comparison.Select(item => new ReportSummaryDeltaViewModel(item.Label, item.DeltaText, item.DeltaBrush)).ToArray(),
            Signals =
            [
                BuildSignal("Просроченные счета", CountOverdueInvoices(current.Invoices), "#F45A5A", "#FFF1F1"),
                BuildSignal("Отгрузки с задержкой", CountDelayedShipments(current.Shipments), "#FF9B28", "#FFF4E8"),
                BuildSignal("Клиенты с падением выручки", CountDeclinedCustomers(current, previous), "#7B68EE", "#F2EEFF"),
                BuildSignal("Товары с низкой оборачиваемостью", CountLowTurnoverProducts(current), "#4F5BFF", "#EEF2FF")
            ],
            TrendPoints = BuildTrendSeries(
                current.Invoices.Select(item => (item.InvoiceDate, item.TotalAmount)),
                previous.Invoices.Select(item => (item.InvoiceDate, item.TotalAmount)),
                _periodFrom,
                _periodTo),
            MonthlyBars = BuildMonthlyBars(current.Invoices.Select(item => (item.InvoiceDate, item.TotalAmount)), "₽"),
            DonutItems = BuildLegendItems(rows.Select(item => item.StatusText)),
            LeftCardItems = BuildTopRevenueItems(current.Invoices.Select(item => (Ui(item.CustomerName), item.TotalAmount))),
            MiddleCardItems = BuildTopRevenueItems(current.InvoiceLines.Select(item => (item.ItemName, item.Amount))),
            RightLeftCardItems = BuildOverdueInvoiceItems(current.Invoices),
            RightMiddleCardItems = BuildDelayedShipmentItems(current.Shipments),
            RecentEvents = BuildRecentEvents(current),
            RegistryRows = rows
        };
    }

    private ReportDashboardState BuildOrdersState(ReportSlice current, ReportSlice previous)
    {
        var filteredOrders = current.Orders
            .Where(order => MatchesStatusFilter(NormalizeOrderStatus(order.Status)))
            .ToArray();

        var currentAmount = filteredOrders.Sum(item => item.TotalAmount);
        var previousOrders = previous.Orders.Where(order => MatchesStatusFilter(NormalizeOrderStatus(order.Status))).ToArray();
        var previousAmount = previousOrders.Sum(item => item.TotalAmount);
        var currentConfirmed = filteredOrders.Count(item => NormalizeOrderStatus(item.Status) == "Подтвержден");
        var previousConfirmed = previousOrders.Count(item => NormalizeOrderStatus(item.Status) == "Подтвержден");
        var currentReserved = filteredOrders.Count(item => NormalizeOrderStatus(item.Status) == "В резерве");
        var previousReserved = previousOrders.Count(item => NormalizeOrderStatus(item.Status) == "В резерве");
        var currentReady = filteredOrders.Count(item => NormalizeOrderStatus(item.Status) == "Готов к отгрузке");
        var previousReady = previousOrders.Count(item => NormalizeOrderStatus(item.Status) == "Готов к отгрузке");
        var currentNew = filteredOrders.Count(item => NormalizeOrderStatus(item.Status) == "Новый");
        var previousNew = previousOrders.Count(item => NormalizeOrderStatus(item.Status) == "Новый");
        var currentAverage = filteredOrders.Length > 0 ? filteredOrders.Average(item => item.TotalAmount) : 0m;
        var previousAverage = previousOrders.Length > 0 ? previousOrders.Average(item => item.TotalAmount) : 0m;

        var comparison = new[]
        {
            BuildComparison("Сумма заказов", FormatMoney(currentAmount), currentAmount, previousAmount),
            BuildComparison("Кол-во заказов", filteredOrders.Length.ToString(CultureInfo.InvariantCulture), filteredOrders.Length, previousOrders.Length),
            BuildComparison("Средний заказ", FormatMoney(currentAverage), currentAverage, previousAverage),
            BuildComparison("Подтверждено", currentConfirmed.ToString(CultureInfo.InvariantCulture), currentConfirmed, previousConfirmed),
            BuildComparison("Готово к отгрузке", currentReady.ToString(CultureInfo.InvariantCulture), currentReady, previousReady)
        };

        return new ReportDashboardState
        {
            TrendTitle = "Динамика заказов",
            DonutTitle = "Статусы заказов",
            MonthlyTitle = "Заказы по месяцам",
            LeftCardTitle = "Клиенты по заказам",
            MiddleCardTitle = "Товары по заказам",
            RightLeftCardTitle = "Заказы без счета",
            RightMiddleCardTitle = "Готовы к отгрузке",
            RegistryTitle = "Реестр заказов",
            SummaryModuleLabel = "Заказы",
            NavigateModuleCaption = "Перейти в заказы",
            DateHeader = "Дата",
            DocumentHeader = "Заказ",
            CounterpartyHeader = "Клиент",
            ValueHeader = "Сумма",
            OwnerHeader = "Менеджер",
            Metrics =
            [
                BuildMetric("Сумма заказов", FormatMoney(currentAmount), currentAmount, previousAmount, "#27AE60", "#EBFBF1", "\uE8A5"),
                BuildMetric("Заказы", filteredOrders.Length.ToString(CultureInfo.InvariantCulture), filteredOrders.Length, previousOrders.Length, "#4F5BFF", "#EEF2FF", "\uEA14"),
                BuildMetric("Подтверждено", currentConfirmed.ToString(CultureInfo.InvariantCulture), currentConfirmed, previousConfirmed, "#34B56A", "#EBFBF1", "\uE73E"),
                BuildMetric("В резерве", currentReserved.ToString(CultureInfo.InvariantCulture), currentReserved, previousReserved, "#FF9B28", "#FFF4E8", "\uE7C7"),
                BuildMetric("Готово к отгрузке", currentReady.ToString(CultureInfo.InvariantCulture), currentReady, previousReady, "#7B68EE", "#F2EEFF", "\uE7BF"),
                BuildMetric("Новые", currentNew.ToString(CultureInfo.InvariantCulture), currentNew, previousNew, "#F45A5A", "#FFF1F1", "\uE710")
            ],
            ComparisonItems = comparison,
            SummaryDeltas = comparison.Select(item => new ReportSummaryDeltaViewModel(item.Label, item.DeltaText, item.DeltaBrush)).ToArray(),
            Signals =
            [
                BuildSignal("Без счета", CountOrdersWithoutInvoice(filteredOrders, current.Invoices), "#F45A5A", "#FFF1F1"),
                BuildSignal("В резерве", currentReserved, "#FF9B28", "#FFF4E8"),
                BuildSignal("Готовы к отгрузке", currentReady, "#4F5BFF", "#EEF2FF"),
                BuildSignal("Новые заказы", currentNew, "#7B68EE", "#F2EEFF")
            ],
            TrendPoints = BuildTrendSeries(
                filteredOrders.Select(item => (item.OrderDate, item.TotalAmount)),
                previousOrders.Select(item => (item.OrderDate, item.TotalAmount)),
                _periodFrom,
                _periodTo),
            MonthlyBars = BuildMonthlyBars(filteredOrders.Select(item => (item.OrderDate, item.TotalAmount)), "₽"),
            DonutItems = BuildLegendItems(filteredOrders.Select(item => NormalizeOrderStatus(item.Status))),
            LeftCardItems = BuildTopRevenueItems(filteredOrders.Select(item => (Ui(item.CustomerName), item.TotalAmount))),
            MiddleCardItems = BuildTopRevenueItems(current.OrderLines.Where(item => filteredOrders.Any(order => order.Id == item.DocumentId)).Select(item => (item.ItemName, item.Amount))),
            RightLeftCardItems = BuildOrdersWithoutInvoiceItems(filteredOrders, current.Invoices),
            RightMiddleCardItems = BuildReadyOrderItems(filteredOrders),
            RecentEvents = BuildRecentEvents(current),
            RegistryRows = filteredOrders
                .OrderByDescending(item => item.OrderDate)
                .Select(item => CreateRegistryRow(item.OrderDate, Ui(item.Number), Ui(item.CustomerName), FormatMoney(item.TotalAmount), NormalizeOrderStatus(item.Status), Ui(item.Manager), Ui(item.Comment), NavigationTargets["orders"]))
                .ToArray()
        };
    }

    private ReportDashboardState BuildInvoicesState(ReportSlice current, ReportSlice previous)
    {
        var filteredInvoices = current.Invoices
            .Where(invoice => MatchesStatusFilter(NormalizeInvoiceStatus(invoice.Status, invoice.DueDate)))
            .ToArray();
        var previousInvoices = previous.Invoices
            .Where(invoice => MatchesStatusFilter(NormalizeInvoiceStatus(invoice.Status, invoice.DueDate)))
            .ToArray();

        var currentAmount = filteredInvoices.Sum(item => item.TotalAmount);
        var previousAmount = previousInvoices.Sum(item => item.TotalAmount);
        var currentPaid = filteredInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Оплачен");
        var previousPaid = previousInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Оплачен");
        var currentPartial = filteredInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Частично оплачен");
        var previousPartial = previousInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Частично оплачен");
        var currentAverage = filteredInvoices.Length > 0 ? filteredInvoices.Average(item => item.TotalAmount) : 0m;
        var previousAverage = previousInvoices.Length > 0 ? previousInvoices.Average(item => item.TotalAmount) : 0m;
        var currentOverdue = CountOverdueInvoices(filteredInvoices);
        var previousOverdue = CountOverdueInvoices(previousInvoices);
        var currentAwaiting = filteredInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Ожидает оплату");
        var previousAwaiting = previousInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Ожидает оплату");

        var comparison = new[]
        {
            BuildComparison("Сумма счетов", FormatMoney(currentAmount), currentAmount, previousAmount),
            BuildComparison("Оплачено", currentPaid.ToString(CultureInfo.InvariantCulture), currentPaid, previousPaid),
            BuildComparison("Частично оплачено", currentPartial.ToString(CultureInfo.InvariantCulture), currentPartial, previousPartial),
            BuildComparison("Средний счет", FormatMoney(currentAverage), currentAverage, previousAverage),
            BuildComparison("Просрочено", currentOverdue.ToString(CultureInfo.InvariantCulture), currentOverdue, previousOverdue, lowerIsBetter: true)
        };

        return new ReportDashboardState
        {
            TrendTitle = "Динамика счетов",
            DonutTitle = "Статусы счетов",
            MonthlyTitle = "Счета по месяцам",
            LeftCardTitle = "Клиенты по счетам",
            MiddleCardTitle = "Товары по счетам",
            RightLeftCardTitle = "Просроченные счета",
            RightMiddleCardTitle = "Частично оплаченные",
            RegistryTitle = "Реестр счетов",
            SummaryModuleLabel = "Счета",
            NavigateModuleCaption = "Перейти в счета",
            DateHeader = "Дата счета",
            DocumentHeader = "Счет",
            CounterpartyHeader = "Клиент",
            ValueHeader = "Сумма",
            OwnerHeader = "Менеджер",
            Metrics =
            [
                BuildMetric("Сумма счетов", FormatMoney(currentAmount), currentAmount, previousAmount, "#27AE60", "#EBFBF1", "\uE8C7"),
                BuildMetric("Счета", filteredInvoices.Length.ToString(CultureInfo.InvariantCulture), filteredInvoices.Length, previousInvoices.Length, "#4F5BFF", "#EEF2FF", "\uEA14"),
                BuildMetric("Оплачено", currentPaid.ToString(CultureInfo.InvariantCulture), currentPaid, previousPaid, "#34B56A", "#EBFBF1", "\uE73E"),
                BuildMetric("Частично оплачено", currentPartial.ToString(CultureInfo.InvariantCulture), currentPartial, previousPartial, "#7B68EE", "#F2EEFF", "\uE8C7"),
                BuildMetric("Ожидают оплату", currentAwaiting.ToString(CultureInfo.InvariantCulture), currentAwaiting, previousAwaiting, "#FF9B28", "#FFF4E8", "\uE823"),
                BuildMetric("Просрочено", currentOverdue.ToString(CultureInfo.InvariantCulture), currentOverdue, previousOverdue, "#F45A5A", "#FFF1F1", "\uEA39", lowerIsBetter: true)
            ],
            ComparisonItems = comparison,
            SummaryDeltas = comparison.Select(item => new ReportSummaryDeltaViewModel(item.Label, item.DeltaText, item.DeltaBrush)).ToArray(),
            Signals =
            [
                BuildSignal("Просроченные счета", currentOverdue, "#F45A5A", "#FFF1F1"),
                BuildSignal("Ожидают оплату", currentAwaiting, "#FF9B28", "#FFF4E8"),
                BuildSignal("Частично оплачено", currentPartial, "#7B68EE", "#F2EEFF"),
                BuildSignal("Клиенты с долгом", CountCustomersWithDebt(filteredInvoices), "#4F5BFF", "#EEF2FF")
            ],
            TrendPoints = BuildTrendSeries(
                filteredInvoices.Select(item => (item.InvoiceDate, item.TotalAmount)),
                previousInvoices.Select(item => (item.InvoiceDate, item.TotalAmount)),
                _periodFrom,
                _periodTo),
            MonthlyBars = BuildMonthlyBars(filteredInvoices.Select(item => (item.InvoiceDate, item.TotalAmount)), "₽"),
            DonutItems = BuildLegendItems(filteredInvoices.Select(item => NormalizeInvoiceStatus(item.Status, item.DueDate))),
            LeftCardItems = BuildTopRevenueItems(filteredInvoices.Select(item => (Ui(item.CustomerName), item.TotalAmount))),
            MiddleCardItems = BuildTopRevenueItems(current.InvoiceLines.Where(item => filteredInvoices.Any(invoice => invoice.Id == item.DocumentId)).Select(item => (item.ItemName, item.Amount))),
            RightLeftCardItems = BuildOverdueInvoiceItems(filteredInvoices),
            RightMiddleCardItems = BuildPartialInvoiceItems(filteredInvoices),
            RecentEvents = BuildRecentEvents(current),
            RegistryRows = filteredInvoices
                .OrderByDescending(item => item.InvoiceDate)
                .Select(item => CreateRegistryRow(item.InvoiceDate, Ui(item.Number), Ui(item.CustomerName), FormatMoney(item.TotalAmount), NormalizeInvoiceStatus(item.Status, item.DueDate), Ui(item.Manager), Ui(item.Comment), NavigationTargets["invoices"]))
                .ToArray()
        };
    }

    private ReportDashboardState BuildShipmentsState(ReportSlice current, ReportSlice previous)
    {
        var filteredShipments = current.Shipments
            .Where(shipment => MatchesStatusFilter(NormalizeShipmentStatus(shipment)))
            .ToArray();
        var previousShipments = previous.Shipments
            .Where(shipment => MatchesStatusFilter(NormalizeShipmentStatus(shipment)))
            .ToArray();

        var currentAmount = filteredShipments.Sum(item => item.TotalAmount);
        var previousAmount = previousShipments.Sum(item => item.TotalAmount);
        var currentDelivered = filteredShipments.Count(item => NormalizeShipmentStatus(item) == "Отгружена");
        var previousDelivered = previousShipments.Count(item => NormalizeShipmentStatus(item) == "Отгружена");
        var currentPlanned = filteredShipments.Count(item => NormalizeShipmentStatus(item) == "К сборке" || NormalizeShipmentStatus(item) == "Готова к отгрузке");
        var previousPlanned = previousShipments.Count(item => NormalizeShipmentStatus(item) == "К сборке" || NormalizeShipmentStatus(item) == "Готова к отгрузке");
        var currentAverage = filteredShipments.Length > 0 ? filteredShipments.Average(item => item.TotalAmount) : 0m;
        var previousAverage = previousShipments.Length > 0 ? previousShipments.Average(item => item.TotalAmount) : 0m;
        var currentDelayed = CountDelayedShipments(filteredShipments);
        var previousDelayed = CountDelayedShipments(previousShipments);

        var comparison = new[]
        {
            BuildComparison("Объем отгрузок", FormatMoney(currentAmount), currentAmount, previousAmount),
            BuildComparison("Кол-во отгрузок", filteredShipments.Length.ToString(CultureInfo.InvariantCulture), filteredShipments.Length, previousShipments.Length),
            BuildComparison("Отгружено", currentDelivered.ToString(CultureInfo.InvariantCulture), currentDelivered, previousDelivered),
            BuildComparison("Средняя отгрузка", FormatMoney(currentAverage), currentAverage, previousAverage),
            BuildComparison("Задержка", currentDelayed.ToString(CultureInfo.InvariantCulture), currentDelayed, previousDelayed, lowerIsBetter: true)
        };

        return new ReportDashboardState
        {
            TrendTitle = "Динамика отгрузок",
            DonutTitle = "Статусы отгрузок",
            MonthlyTitle = "Отгрузки по месяцам",
            LeftCardTitle = "Клиенты по отгрузкам",
            MiddleCardTitle = "Товары по отгрузкам",
            RightLeftCardTitle = "Отгрузки с задержкой",
            RightMiddleCardTitle = "Перевозчики",
            RegistryTitle = "Реестр отгрузок",
            SummaryModuleLabel = "Отгрузки",
            NavigateModuleCaption = "Перейти в отгрузки",
            DateHeader = "Дата",
            DocumentHeader = "Отгрузка",
            CounterpartyHeader = "Клиент",
            ValueHeader = "Сумма",
            OwnerHeader = "Менеджер",
            Metrics =
            [
                BuildMetric("Объем отгрузок", FormatMoney(currentAmount), currentAmount, previousAmount, "#27AE60", "#EBFBF1", "\uE7BF"),
                BuildMetric("Отгрузки", filteredShipments.Length.ToString(CultureInfo.InvariantCulture), filteredShipments.Length, previousShipments.Length, "#4F5BFF", "#EEF2FF", "\uEA14"),
                BuildMetric("Отгружено", currentDelivered.ToString(CultureInfo.InvariantCulture), currentDelivered, previousDelivered, "#34B56A", "#EBFBF1", "\uE73E"),
                BuildMetric("В работе", currentPlanned.ToString(CultureInfo.InvariantCulture), currentPlanned, previousPlanned, "#FF9B28", "#FFF4E8", "\uE823"),
                BuildMetric("Средняя отгрузка", FormatMoney(currentAverage), currentAverage, previousAverage, "#7B68EE", "#F2EEFF", "\uEC58"),
                BuildMetric("Задержка", currentDelayed.ToString(CultureInfo.InvariantCulture), currentDelayed, previousDelayed, "#F45A5A", "#FFF1F1", "\uEA39", lowerIsBetter: true)
            ],
            ComparisonItems = comparison,
            SummaryDeltas = comparison.Select(item => new ReportSummaryDeltaViewModel(item.Label, item.DeltaText, item.DeltaBrush)).ToArray(),
            Signals =
            [
                BuildSignal("С задержкой", currentDelayed, "#F45A5A", "#FFF1F1"),
                BuildSignal("В работе", currentPlanned, "#FF9B28", "#FFF4E8"),
                BuildSignal("Перевозчики", filteredShipments.Select(item => Ui(item.Carrier)).Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "#4F5BFF", "#EEF2FF"),
                BuildSignal("Клиенты", filteredShipments.Select(item => Ui(item.CustomerName)).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "#7B68EE", "#F2EEFF")
            ],
            TrendPoints = BuildTrendSeries(
                filteredShipments.Select(item => (item.ShipmentDate, item.TotalAmount)),
                previousShipments.Select(item => (item.ShipmentDate, item.TotalAmount)),
                _periodFrom,
                _periodTo),
            MonthlyBars = BuildMonthlyBars(filteredShipments.Select(item => (item.ShipmentDate, item.TotalAmount)), "₽"),
            DonutItems = BuildLegendItems(filteredShipments.Select(NormalizeShipmentStatus)),
            LeftCardItems = BuildTopRevenueItems(filteredShipments.Select(item => (Ui(item.CustomerName), item.TotalAmount))),
            MiddleCardItems = BuildTopRevenueItems(current.ShipmentLines.Where(item => filteredShipments.Any(shipment => shipment.Id == item.DocumentId)).Select(item => (item.ItemName, item.Amount))),
            RightLeftCardItems = BuildDelayedShipmentItems(filteredShipments),
            RightMiddleCardItems = BuildCarrierItems(filteredShipments),
            RecentEvents = BuildRecentEvents(current),
            RegistryRows = filteredShipments
                .OrderByDescending(item => item.ShipmentDate)
                .Select(item => CreateRegistryRow(item.ShipmentDate, Ui(item.Number), Ui(item.CustomerName), FormatMoney(item.TotalAmount), NormalizeShipmentStatus(item), Ui(item.Manager), Ui(item.Comment), NavigationTargets["shipments"]))
                .ToArray()
        };
    }

    private ReportDashboardState BuildCustomersState(ReportSlice current, ReportSlice previous)
    {
        var customers = current.Customers
            .Where(customer => MatchesStatusFilter(Ui(customer.Status)))
            .ToArray();
        var previousCustomers = previous.Customers
            .Where(customer => MatchesStatusFilter(Ui(customer.Status)))
            .ToArray();

        var customerRevenue = current.Invoices
            .GroupBy(item => Ui(item.CustomerName))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.TotalAmount), StringComparer.OrdinalIgnoreCase);
        var previousRevenue = previous.Invoices
            .GroupBy(item => Ui(item.CustomerName))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.TotalAmount), StringComparer.OrdinalIgnoreCase);

        var activeCount = customers.Count(item => Ui(item.Status) == "Активен");
        var reviewCount = customers.Count(item => Ui(item.Status) == "На проверке");
        var pauseCount = customers.Count(item => Ui(item.Status) == "Пауза");
        var currentRevenueSum = customerRevenue.Values.Sum();
        var previousRevenueSum = previousRevenue.Values.Sum();
        var currentClientsWithOrders = customers.Count(item => current.Orders.Any(order => order.CustomerId == item.Id));
        var previousClientsWithOrders = previousCustomers.Count(item => previous.Orders.Any(order => order.CustomerId == item.Id));

        var comparison = new[]
        {
            BuildComparison("Клиенты", customers.Length.ToString(CultureInfo.InvariantCulture), customers.Length, previousCustomers.Length),
            BuildComparison("Активные", activeCount.ToString(CultureInfo.InvariantCulture), activeCount, previousCustomers.Count(item => Ui(item.Status) == "Активен")),
            BuildComparison("Выручка", FormatMoney(currentRevenueSum), currentRevenueSum, previousRevenueSum),
            BuildComparison("С заказами", currentClientsWithOrders.ToString(CultureInfo.InvariantCulture), currentClientsWithOrders, previousClientsWithOrders),
            BuildComparison("На проверке", reviewCount.ToString(CultureInfo.InvariantCulture), reviewCount, previousCustomers.Count(item => Ui(item.Status) == "На проверке"), lowerIsBetter: true)
        };

        return new ReportDashboardState
        {
            TrendTitle = "Динамика клиентской выручки",
            DonutTitle = "Статусы клиентов",
            MonthlyTitle = "Выручка по клиентам",
            LeftCardTitle = "Топ клиентов",
            MiddleCardTitle = "Топ менеджеров",
            RightLeftCardTitle = "Клиенты на проверке",
            RightMiddleCardTitle = "Без активности",
            RegistryTitle = "Реестр клиентов",
            SummaryModuleLabel = "Клиенты",
            NavigateModuleCaption = "Перейти в клиенты",
            DateHeader = "Код",
            DocumentHeader = "Клиент",
            CounterpartyHeader = "Договор / контакт",
            ValueHeader = "Выручка",
            OwnerHeader = "Менеджер",
            Metrics =
            [
                BuildMetric("Клиенты", customers.Length.ToString(CultureInfo.InvariantCulture), customers.Length, previousCustomers.Length, "#4F5BFF", "#EEF2FF", "\uE77B"),
                BuildMetric("Активные", activeCount.ToString(CultureInfo.InvariantCulture), activeCount, previousCustomers.Count(item => Ui(item.Status) == "Активен"), "#27AE60", "#EBFBF1", "\uE73E"),
                BuildMetric("На проверке", reviewCount.ToString(CultureInfo.InvariantCulture), reviewCount, previousCustomers.Count(item => Ui(item.Status) == "На проверке"), "#FF9B28", "#FFF4E8", "\uE946", lowerIsBetter: true),
                BuildMetric("Пауза", pauseCount.ToString(CultureInfo.InvariantCulture), pauseCount, previousCustomers.Count(item => Ui(item.Status) == "Пауза"), "#F45A5A", "#FFF1F1", "\uE71A", lowerIsBetter: true),
                BuildMetric("С заказами", currentClientsWithOrders.ToString(CultureInfo.InvariantCulture), currentClientsWithOrders, previousClientsWithOrders, "#7B68EE", "#F2EEFF", "\uEA14"),
                BuildMetric("Выручка", FormatMoney(currentRevenueSum), currentRevenueSum, previousRevenueSum, "#34B56A", "#EBFBF1", "\uEAFD")
            ],
            ComparisonItems = comparison,
            SummaryDeltas = comparison.Select(item => new ReportSummaryDeltaViewModel(item.Label, item.DeltaText, item.DeltaBrush)).ToArray(),
            Signals =
            [
                BuildSignal("На проверке", reviewCount, "#FF9B28", "#FFF4E8"),
                BuildSignal("Без заказов", customers.Count(item => !current.Orders.Any(order => order.CustomerId == item.Id)), "#F45A5A", "#FFF1F1"),
                BuildSignal("Без счета", customers.Count(item => !current.Invoices.Any(invoice => invoice.CustomerId == item.Id)), "#4F5BFF", "#EEF2FF"),
                BuildSignal("Менеджеры", customers.Select(item => Ui(item.Manager)).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "#7B68EE", "#F2EEFF")
            ],
            TrendPoints = BuildTrendSeries(
                current.Invoices.Select(item => (item.InvoiceDate, item.TotalAmount)),
                previous.Invoices.Select(item => (item.InvoiceDate, item.TotalAmount)),
                _periodFrom,
                _periodTo),
            MonthlyBars = BuildMonthlyBars(current.Invoices.Select(item => (item.InvoiceDate, item.TotalAmount)), "₽"),
            DonutItems = BuildLegendItems(customers.Select(item => Ui(item.Status))),
            LeftCardItems = BuildTopRevenueItems(customerRevenue.Select(pair => (pair.Key, pair.Value))),
            MiddleCardItems = BuildTopRevenueItems(current.Invoices.GroupBy(item => Ui(item.Manager)).Select(group => (group.Key, group.Sum(item => item.TotalAmount)))),
            RightLeftCardItems = customers.Where(item => Ui(item.Status) == "На проверке")
                .Take(5)
                .Select(item => new ReportInfoItemViewModel(Ui(item.Name), Ui(item.Email), Ui(item.Manager), BrushFromHex("#FF9B28")))
                .ToArray(),
            RightMiddleCardItems = customers.Where(item => !current.Orders.Any(order => order.CustomerId == item.Id))
                .Take(5)
                .Select(item => new ReportInfoItemViewModel(Ui(item.Name), Ui(item.Phone), "Нет заказа", BrushFromHex("#F45A5A")))
                .ToArray(),
            RecentEvents = BuildRecentEvents(current),
            RegistryRows = customers
                .OrderBy(item => Ui(item.Name))
                .Select(item => CreateRegistryRow(item.Code, Ui(item.Name), string.IsNullOrWhiteSpace(item.Email) ? Ui(item.ContractNumber) : Ui(item.Email), customerRevenue.TryGetValue(Ui(item.Name), out var revenue) ? FormatMoney(revenue) : "—", Ui(item.Status), Ui(item.Manager), Ui(item.Notes), NavigationTargets["customers"]))
                .ToArray()
        };
    }

    private ReportDashboardState BuildCatalogState(ReportSlice current, ReportSlice previous)
    {
        var currentInvoicesByItem = current.InvoiceLines
            .GroupBy(item => item.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new { Revenue = group.Sum(item => item.Amount), Quantity = group.Sum(item => item.Quantity) }, StringComparer.OrdinalIgnoreCase);
        var previousInvoicesByItem = previous.InvoiceLines
            .GroupBy(item => item.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => new { Revenue = group.Sum(item => item.Amount), Quantity = group.Sum(item => item.Quantity) }, StringComparer.OrdinalIgnoreCase);

        var rows = _salesWorkspace.CatalogItems
            .Select(item =>
            {
                currentInvoicesByItem.TryGetValue(Ui(item.Code), out var currentTotals);
                previousInvoicesByItem.TryGetValue(Ui(item.Code), out var previousTotals);
                var revenue = currentTotals?.Revenue ?? 0m;
                var quantity = currentTotals?.Quantity ?? 0m;
                var status = revenue > 0 ? "В продажах" : "Без движения";
                var owner = quantity > 0 ? $"Кол-во: {quantity:N0}" : "Нет движений";
                return new ReportCatalogRow(
                    Ui(item.Code),
                    Ui(item.Name),
                    Ui(item.Unit),
                    item.DefaultPrice,
                    revenue,
                    previousTotals?.Revenue ?? 0m,
                    status,
                    owner);
            })
            .Where(item => MatchesCatalogSearch(item))
            .Where(item => MatchesStatusFilter(item.Status))
            .OrderByDescending(item => item.Revenue)
            .ToArray();

        var revenueCurrent = rows.Sum(item => item.Revenue);
        var revenuePrevious = rows.Sum(item => item.PreviousRevenue);
        var activeRows = rows.Count(item => item.Status == "В продажах");
        var activePrevious = rows.Count(item => item.PreviousRevenue > 0);
        var averagePrice = rows.Length > 0 ? rows.Average(item => item.Price) : 0m;
        var positions = rows.Count(item => item.Revenue > 0);
        var noMovement = rows.Count(item => item.Status == "Без движения");

        var comparison = new[]
        {
            BuildComparison("Выручка", FormatMoney(revenueCurrent), revenueCurrent, revenuePrevious),
            BuildComparison("Позиции", rows.Length.ToString(CultureInfo.InvariantCulture), rows.Length, rows.Length),
            BuildComparison("В продажах", activeRows.ToString(CultureInfo.InvariantCulture), activeRows, activePrevious),
            BuildComparison("Средняя цена", FormatMoney(averagePrice), averagePrice, averagePrice),
            BuildComparison("Без движения", noMovement.ToString(CultureInfo.InvariantCulture), noMovement, rows.Count(item => item.PreviousRevenue <= 0), lowerIsBetter: true)
        };

        return new ReportDashboardState
        {
            TrendTitle = "Динамика спроса",
            DonutTitle = "Статусы товаров",
            MonthlyTitle = "Спрос по месяцам",
            LeftCardTitle = "Топ товаров",
            MiddleCardTitle = "Топ клиентов",
            RightLeftCardTitle = "Позиции без движения",
            RightMiddleCardTitle = "Товары с выручкой",
            RegistryTitle = "Реестр товаров",
            SummaryModuleLabel = "Товары",
            NavigateModuleCaption = "Перейти в товары",
            DateHeader = "Код",
            DocumentHeader = "Товар",
            CounterpartyHeader = "Ед. изм.",
            ValueHeader = "Цена",
            OwnerHeader = "Источник",
            Metrics =
            [
                BuildMetric("Товаров", rows.Length.ToString(CultureInfo.InvariantCulture), rows.Length, rows.Length, "#4F5BFF", "#EEF2FF", "\uE7BF"),
                BuildMetric("В продажах", activeRows.ToString(CultureInfo.InvariantCulture), activeRows, activePrevious, "#27AE60", "#EBFBF1", "\uE73E"),
                BuildMetric("Средняя цена", FormatMoney(averagePrice), averagePrice, averagePrice, "#FF9B28", "#FFF4E8", "\uEAFD"),
                BuildMetric("Выручка", FormatMoney(revenueCurrent), revenueCurrent, revenuePrevious, "#34B56A", "#EBFBF1", "\uE8C7"),
                BuildMetric("Позиции в спросе", positions.ToString(CultureInfo.InvariantCulture), positions, positions, "#7B68EE", "#F2EEFF", "\uEA14"),
                BuildMetric("Без движения", noMovement.ToString(CultureInfo.InvariantCulture), noMovement, rows.Count(item => item.PreviousRevenue <= 0), "#F45A5A", "#FFF1F1", "\uEA39", lowerIsBetter: true)
            ],
            ComparisonItems = comparison,
            SummaryDeltas = comparison.Select(item => new ReportSummaryDeltaViewModel(item.Label, item.DeltaText, item.DeltaBrush)).ToArray(),
            Signals =
            [
                BuildSignal("Без движения", noMovement, "#F45A5A", "#FFF1F1"),
                BuildSignal("В счетах", positions, "#27AE60", "#EBFBF1"),
                BuildSignal("Клиенты по товарам", current.InvoiceLines.Select(item => item.CustomerName).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "#4F5BFF", "#EEF2FF"),
                BuildSignal("Кодов в каталоге", rows.Length, "#7B68EE", "#F2EEFF")
            ],
            TrendPoints = BuildTrendSeries(
                current.InvoiceLines.Select(item => (item.DocumentDate, item.Amount)),
                previous.InvoiceLines.Select(item => (item.DocumentDate, item.Amount)),
                _periodFrom,
                _periodTo),
            MonthlyBars = BuildMonthlyBars(current.InvoiceLines.Select(item => (item.DocumentDate, item.Amount)), "₽"),
            DonutItems = BuildLegendItems(rows.Select(item => item.Status)),
            LeftCardItems = BuildTopRevenueItems(rows.Select(item => (item.Name, item.Revenue))),
            MiddleCardItems = BuildTopRevenueItems(current.InvoiceLines.GroupBy(item => item.CustomerName).Select(group => (group.Key, group.Sum(item => item.Amount)))),
            RightLeftCardItems = rows.Where(item => item.Status == "Без движения")
                .Take(5)
                .Select(item => new ReportInfoItemViewModel(item.Name, item.Code, "Нет продаж", BrushFromHex("#F45A5A")))
                .ToArray(),
            RightMiddleCardItems = rows.Take(5)
                .Select(item => new ReportInfoItemViewModel(item.Name, item.Code, FormatMoney(item.Revenue), BrushFromHex("#27AE60")))
                .ToArray(),
            RecentEvents = BuildRecentEvents(current),
            RegistryRows = rows
                .Select(item => CreateRegistryRow(item.Code, item.Name, item.Unit, FormatMoney(item.Price), item.Status, item.Owner, item.Code, NavigationTargets["catalog"]))
                .ToArray()
        };
    }

    private void UpdateRegistryColumns(ReportDashboardState state)
    {
        DateColumn.Header = state.DateHeader;
        DocumentColumn.Header = state.DocumentHeader;
        CounterpartyColumn.Header = state.CounterpartyHeader;
        ValueColumn.Header = state.ValueHeader;
        OwnerColumn.Header = state.OwnerHeader;
    }

    private ReportSlice BuildSlice(DateTime from, DateTime to)
    {
        var search = Ui(HeaderSearchBox.Text).Trim();
        var manager = SelectedOptionValue(ManagerFilterComboBox);
        var warehouse = SelectedOptionValue(WarehouseFilterComboBox);
        var client = SelectedOptionValue(ClientFilterComboBox);

        var orders = _salesWorkspace.Orders
            .Where(item => item.OrderDate.Date >= from.Date && item.OrderDate.Date <= to.Date)
            .Where(item => MatchesManager(manager, item.Manager))
            .Where(item => MatchesWarehouse(warehouse, item.Warehouse))
            .Where(item => MatchesClient(client, item.CustomerName))
            .Where(item => MatchesOrderSearch(item, search))
            .Select(item => item.Clone())
            .ToArray();

        var invoices = _salesWorkspace.Invoices
            .Where(item => item.InvoiceDate.Date >= from.Date && item.InvoiceDate.Date <= to.Date)
            .Where(item => MatchesManager(manager, item.Manager))
            .Where(item => MatchesClient(client, item.CustomerName))
            .Where(item => MatchesInvoiceSearch(item, search))
            .Select(item => item.Clone())
            .ToArray();

        var shipments = _salesWorkspace.Shipments
            .Where(item => item.ShipmentDate.Date >= from.Date && item.ShipmentDate.Date <= to.Date)
            .Where(item => MatchesManager(manager, item.Manager))
            .Where(item => MatchesWarehouse(warehouse, item.Warehouse))
            .Where(item => MatchesClient(client, item.CustomerName))
            .Where(item => MatchesShipmentSearch(item, search))
            .Select(item => item.Clone())
            .ToArray();

        var customers = _salesWorkspace.Customers
            .Where(item => MatchesManager(manager, item.Manager))
            .Where(item => MatchesClient(client, item.Name))
            .Where(item => MatchesCustomerSearch(item, search))
            .Select(item => item.Clone())
            .ToArray();

        return new ReportSlice(
            orders,
            invoices,
            shipments,
            customers,
            ExpandLines(orders, order => order.Id, order => order.OrderDate, order => order.CustomerName, order => order.Warehouse, order => order.Manager),
            ExpandLines(invoices, invoice => invoice.Id, invoice => invoice.InvoiceDate, invoice => invoice.CustomerName, _ => string.Empty, invoice => invoice.Manager),
            ExpandLines(shipments, shipment => shipment.Id, shipment => shipment.ShipmentDate, shipment => shipment.CustomerName, shipment => shipment.Warehouse, shipment => shipment.Manager));
    }

    private static IReadOnlyList<ReportLineEntry> ExpandLines<TDocument>(
        IEnumerable<TDocument> documents,
        Func<TDocument, Guid> idSelector,
        Func<TDocument, DateTime> dateSelector,
        Func<TDocument, string> customerSelector,
        Func<TDocument, string> warehouseSelector,
        Func<TDocument, string> managerSelector)
        where TDocument : class
    {
        return documents
            .SelectMany(document =>
            {
                var lines = document switch
                {
                    SalesOrderRecord order => order.Lines,
                    SalesInvoiceRecord invoice => invoice.Lines,
                    SalesShipmentRecord shipment => shipment.Lines,
                    _ => []
                };

                return lines.Select(line => new ReportLineEntry(
                    idSelector(document),
                    dateSelector(document),
                    Ui(line.ItemCode),
                    Ui(line.ItemName),
                    Ui(customerSelector(document)),
                    Ui(warehouseSelector(document)),
                    Ui(managerSelector(document)),
                    line.Quantity,
                    line.Amount));
            })
            .ToArray();
    }

    private static bool MatchesManager(string selectedManager, string manager)
    {
        return string.Equals(selectedManager, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Ui(manager), selectedManager, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesWarehouse(string selectedWarehouse, string warehouse)
    {
        return string.Equals(selectedWarehouse, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Ui(warehouse), selectedWarehouse, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesClient(string selectedClient, string customerName)
    {
        return string.Equals(selectedClient, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Ui(customerName), selectedClient, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOrderSearch(SalesOrderRecord order, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return string.Join(' ',
                Ui(order.Number),
                Ui(order.CustomerName),
                Ui(order.ContractNumber),
                Ui(order.Manager),
                Ui(order.Warehouse),
                Ui(order.Comment),
                string.Join(' ', order.Lines.Select(line => $"{Ui(line.ItemCode)} {Ui(line.ItemName)}")))
            .Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesInvoiceSearch(SalesInvoiceRecord invoice, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return string.Join(' ',
                Ui(invoice.Number),
                Ui(invoice.SalesOrderNumber),
                Ui(invoice.CustomerName),
                Ui(invoice.Manager),
                Ui(invoice.Comment),
                string.Join(' ', invoice.Lines.Select(line => $"{Ui(line.ItemCode)} {Ui(line.ItemName)}")))
            .Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesShipmentSearch(SalesShipmentRecord shipment, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return string.Join(' ',
                Ui(shipment.Number),
                Ui(shipment.SalesOrderNumber),
                Ui(shipment.CustomerName),
                Ui(shipment.Manager),
                Ui(shipment.Warehouse),
                Ui(shipment.Carrier),
                Ui(shipment.Comment),
                string.Join(' ', shipment.Lines.Select(line => $"{Ui(line.ItemCode)} {Ui(line.ItemName)}")))
            .Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesCustomerSearch(SalesCustomerRecord customer, string search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return string.Join(' ',
                Ui(customer.Code),
                Ui(customer.Name),
                Ui(customer.ContractNumber),
                Ui(customer.Manager),
                Ui(customer.Phone),
                Ui(customer.Email),
                Ui(customer.Notes))
            .Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesCatalogSearch(ReportCatalogRow row)
    {
        var search = Ui(HeaderSearchBox.Text).Trim();
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return $"{row.Code} {row.Name} {row.Unit} {row.Owner}".Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchPlaceholder();
        _page = 1;
        RenderAll();
    }

    private void HandleSectionTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
        {
            return;
        }

        _activeTab = tag;
        _page = 1;
        _suppressFilterEvents = true;
        SelectOptionByValue(ModuleFilterComboBox, tag);
        UpdateStatusOptions();
        _suppressFilterEvents = false;
        RenderAll();
    }

    private void HandleHeaderPeriodClick(object sender, RoutedEventArgs e)
    {
        PeriodFromDatePicker.Focus();
        PeriodFromDatePicker.IsDropDownOpen = true;
    }

    private void HandleFilterChanged(object sender, EventArgs e)
    {
        if (_suppressFilterEvents)
        {
            return;
        }

        _page = 1;
        if (PeriodFromDatePicker.SelectedDate is DateTime from)
        {
            _periodFrom = from;
        }

        if (PeriodToDatePicker.SelectedDate is DateTime to)
        {
            _periodTo = to;
        }

        RenderAll();
    }

    private void HandleModuleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilterEvents)
        {
            return;
        }

        var selectedModule = SelectedOptionValue(ModuleFilterComboBox);
        if (!string.IsNullOrWhiteSpace(selectedModule) && !string.Equals(selectedModule, "all", StringComparison.OrdinalIgnoreCase))
        {
            _activeTab = selectedModule;
            UpdateStatusOptions();
        }

        _page = 1;
        RenderAll();
    }

    private void HandleResetFiltersClick(object sender, RoutedEventArgs e)
    {
        _suppressFilterEvents = true;
        HeaderSearchBox.Text = string.Empty;
        _periodFrom = DateTime.Today.AddDays(-60);
        _periodTo = DateTime.Today;
        PeriodFromDatePicker.SelectedDate = _periodFrom;
        PeriodToDatePicker.SelectedDate = _periodTo;
        SelectOptionByValue(ModuleFilterComboBox, _activeTab);
        SelectOptionByValue(ManagerFilterComboBox, "all");
        SelectOptionByValue(WarehouseFilterComboBox, "all");
        SelectOptionByValue(ClientFilterComboBox, "all");
        UpdateStatusOptions();
        _suppressFilterEvents = false;
        _page = 1;
        RenderAll();
    }

    private void HandleGenerateReportClick(object sender, RoutedEventArgs e)
    {
        _lastGeneratedAt = DateTime.Now;
        RenderAll();
    }

    private void HandleExportClick(object sender, RoutedEventArgs e)
    {
        if (_filteredRegistryRows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Нет данных для экспорта по текущим фильтрам.", "Отчеты", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт отчета",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"report-{_activeTab}-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(";",
            EscapeCsv(DateColumn.Header?.ToString() ?? string.Empty),
            EscapeCsv(DocumentColumn.Header?.ToString() ?? string.Empty),
            EscapeCsv(CounterpartyColumn.Header?.ToString() ?? string.Empty),
            EscapeCsv(ValueColumn.Header?.ToString() ?? string.Empty),
            EscapeCsv(StatusColumn.Header?.ToString() ?? string.Empty),
            EscapeCsv(OwnerColumn.Header?.ToString() ?? string.Empty)));

        foreach (var row in _filteredRegistryRows)
        {
            builder.AppendLine(string.Join(";",
                EscapeCsv(row.DateText),
                EscapeCsv(row.DocumentText),
                EscapeCsv(row.CounterpartyText),
                EscapeCsv(row.ValueText),
                EscapeCsv(row.StatusText),
                EscapeCsv(row.OwnerText)));
        }

        IOFile.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
        MessageBox.Show(Window.GetWindow(this), $"Экспорт завершен.\nФайл: {dialog.FileName}", "Отчеты", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HandlePrintClick(object sender, RoutedEventArgs e)
    {
        if (_filteredRegistryRows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "Нет данных для отчета по текущим фильтрам.", "Отчеты", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var directory = IOPath.Combine(AppContext.BaseDirectory, "reports");
        IODirectory.CreateDirectory(directory);

        var filePath = IOPath.Combine(directory, $"report-{_activeTab}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        IOFile.WriteAllText(filePath, BuildReportHtml(), new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }

    private void HandleOpenProblemDocumentsClick(object sender, RoutedEventArgs e)
    {
        _suppressFilterEvents = true;

        switch (_activeTab)
        {
            case "sales":
                _activeTab = "invoices";
                SelectOptionByValue(ModuleFilterComboBox, "invoices");
                UpdateStatusOptions();
                SelectOptionByValue(StatusFilterComboBox, "Просрочен");
                break;
            case "orders":
                SelectOptionByValue(StatusFilterComboBox, "Новый");
                break;
            case "invoices":
                SelectOptionByValue(StatusFilterComboBox, "Просрочен");
                break;
            case "shipments":
                SelectOptionByValue(StatusFilterComboBox, "Задержка");
                break;
            case "customers":
                SelectOptionByValue(StatusFilterComboBox, "На проверке");
                break;
            case "catalog":
                SelectOptionByValue(StatusFilterComboBox, "Без движения");
                break;
        }

        _suppressFilterEvents = false;
        _page = 1;
        RenderAll();
    }

    private void HandleNavigateModuleClick(object sender, RoutedEventArgs e)
    {
        if (NavigationTargets.TryGetValue(_activeTab, out var target))
        {
            NavigationRequested?.Invoke(this, target);
        }
    }

    private void HandleColumnsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var column in new DataGridColumn[] { DateColumn, DocumentColumn, CounterpartyColumn, ValueColumn, StatusColumn, OwnerColumn })
        {
            var item = new MenuItem
            {
                Header = column.Header?.ToString() ?? "Колонка",
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                StaysOpenOnClick = true,
                Tag = column
            };
            item.Click += (_, _) =>
            {
                if (item.Tag is DataGridColumn gridColumn)
                {
                    gridColumn.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            menu.Items.Add(item);
        }

        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private void HandleRowActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement target || target.Tag is not ReportRegistryRowViewModel row)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("Перейти в раздел", (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(row.NavigationTargetKey))
            {
                NavigationRequested?.Invoke(this, row.NavigationTargetKey);
            }
        }));
        menu.Items.Add(CreateMenuItem("Копировать номер", (_, _) => Clipboard.SetText(row.DocumentText)));
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header };
        item.Click += handler;
        return item;
    }

    private void HandlePrevPageClick(object sender, RoutedEventArgs e)
    {
        if (_page <= 1)
        {
            return;
        }

        _page--;
        RenderRegistry();
    }

    private void HandleNextPageClick(object sender, RoutedEventArgs e)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRegistryRows.Count / (double)PageSize));
        if (_page >= totalPages)
        {
            return;
        }

        _page++;
        RenderRegistry();
    }

    private void HandleSelectAllChecked(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionEvents || ReportRegistryGrid.ItemsSource is not IEnumerable<ReportRegistryRowViewModel> rows)
        {
            return;
        }

        var value = RegistrySelectAllCheckBox.IsChecked == true;
        foreach (var row in rows)
        {
            row.IsSelected = value;
        }
    }

    private void HandleRowSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectionEvents || ReportRegistryGrid.ItemsSource is not IEnumerable<ReportRegistryRowViewModel> rows)
        {
            return;
        }

        var currentRows = rows.ToArray();
        _suppressSelectionEvents = true;
        RegistrySelectAllCheckBox.IsChecked = currentRows.Length > 0 && currentRows.All(item => item.IsSelected);
        _suppressSelectionEvents = false;
    }

    private void UpdateSectionTabs()
    {
        foreach (var button in SectionTabsPanel.Children.OfType<Button>())
        {
            var active = string.Equals(button.Tag as string, _activeTab, StringComparison.OrdinalIgnoreCase);
            button.Foreground = active ? BrushFromHex("#4F5BFF") : BrushFromHex("#6E7B98");
            button.BorderBrush = active ? BrushFromHex("#4F5BFF") : Brushes.Transparent;
        }
    }

    private void UpdateStatusOptions()
    {
        var currentValue = SelectedOptionValue(StatusFilterComboBox);
        var options = _activeTab switch
        {
            "orders" => BuildOptions("Все статусы", "Новый", "Подтвержден", "В резерве", "Готов к отгрузке", "Отменен"),
            "invoices" => BuildOptions("Все статусы", "Черновик", "Выставлен", "Ожидает оплату", "Частично оплачен", "Оплачен", "Просрочен"),
            "shipments" => BuildOptions("Все статусы", "Черновик", "К сборке", "Готова к отгрузке", "Отгружена", "Задержка"),
            "customers" => BuildOptions("Все статусы", "Активен", "На проверке", "Пауза"),
            "catalog" => BuildOptions("Все статусы", "В продажах", "Без движения"),
            _ => BuildOptions("Все статусы", "Новый", "Подтвержден", "Счет выставлен", "В сборке", "Отгружен", "Отменен")
        };

        StatusFilterComboBox.ItemsSource = options;
        if (!SelectOptionByValue(StatusFilterComboBox, currentValue))
        {
            SelectOptionByValue(StatusFilterComboBox, "all");
        }
    }

    private static IReadOnlyList<ReportOption> BuildModuleOptions()
    {
        return
        [
            new ReportOption("sales", "Продажи"),
            new ReportOption("orders", "Заказы"),
            new ReportOption("invoices", "Счета"),
            new ReportOption("shipments", "Отгрузки"),
            new ReportOption("customers", "Клиенты"),
            new ReportOption("catalog", "Товары")
        ];
    }

    private static IReadOnlyList<ReportOption> BuildSimpleOptions(string allValue, string allLabel, IEnumerable<string> values)
    {
        return [new ReportOption(allValue, allLabel), .. values.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(item => item).Select(item => new ReportOption(item, item))];
    }

    private static IReadOnlyList<ReportOption> BuildOptions(params string[] labels)
    {
        return [new ReportOption("all", labels[0]), .. labels.Skip(1).Select(label => new ReportOption(label, label))];
    }

    private static void ConfigureComboBox(ComboBox comboBox)
    {
        comboBox.DisplayMemberPath = nameof(ReportOption.Label);
        comboBox.SelectedValuePath = nameof(ReportOption.Value);
    }

    private static bool SelectOptionByValue(ComboBox comboBox, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        comboBox.SelectedValue = value;
        return string.Equals(comboBox.SelectedValue as string, value, StringComparison.OrdinalIgnoreCase);
    }

    private static string SelectedOptionValue(ComboBox comboBox)
    {
        return comboBox.SelectedValue as string ?? "all";
    }

    private bool MatchesStatusFilter(string status)
    {
        var selectedStatus = SelectedOptionValue(StatusFilterComboBox);
        return string.Equals(selectedStatus, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(selectedStatus, status, StringComparison.OrdinalIgnoreCase);
    }

    private void NormalizePeriod()
    {
        if (_periodTo < _periodFrom)
        {
            (_periodFrom, _periodTo) = (_periodTo, _periodFrom);
            _suppressFilterEvents = true;
            PeriodFromDatePicker.SelectedDate = _periodFrom;
            PeriodToDatePicker.SelectedDate = _periodTo;
            _suppressFilterEvents = false;
        }
    }

    private (DateTime From, DateTime To) GetPreviousPeriod()
    {
        var totalDays = Math.Max(1, (_periodTo.Date - _periodFrom.Date).Days + 1);
        var previousTo = _periodFrom.Date.AddDays(-1);
        var previousFrom = previousTo.AddDays(-totalDays + 1);
        return (previousFrom, previousTo);
    }

    private static ReportMetricCardViewModel BuildMetric(string caption, string valueText, decimal current, decimal previous, string accentHex, string softHex, string glyph, bool lowerIsBetter = false)
    {
        var delta = BuildDelta(current, previous, lowerIsBetter);
        return new ReportMetricCardViewModel(
            caption,
            valueText,
            delta.Text,
            delta.Brush,
            BrushFromHex(accentHex),
            BrushFromHex(softHex),
            glyph);
    }

    private static ReportComparisonItemViewModel BuildComparison(string label, string valueText, decimal current, decimal previous, bool lowerIsBetter = false)
    {
        var delta = BuildDelta(current, previous, lowerIsBetter);
        return new ReportComparisonItemViewModel(label, valueText, delta.Text, delta.Brush);
    }

    private static ReportSignalItemViewModel BuildSignal(string label, int count, string accentHex, string backgroundHex)
    {
        return new ReportSignalItemViewModel(label, count.ToString(CultureInfo.InvariantCulture), BrushFromHex(accentHex), BrushFromHex(backgroundHex));
    }

    private static ReportDeltaInfo BuildDelta(decimal current, decimal previous, bool lowerIsBetter = false)
    {
        decimal percent;
        if (previous == 0m)
        {
            percent = current == 0m ? 0m : 100m;
        }
        else
        {
            percent = Math.Round((current - previous) / previous * 100m, 1, MidpointRounding.AwayFromZero);
        }

        var positive = percent >= 0m;
        if (lowerIsBetter)
        {
            positive = percent <= 0m;
        }

        var arrow = percent == 0m ? "?" : (positive ? "↑" : "↓");
        var brush = percent == 0m
            ? BrushFromHex("#98A3BC")
            : positive
                ? BrushFromHex("#29A35A")
                : BrushFromHex("#E1565A");

        return new ReportDeltaInfo($"{arrow} {Math.Abs(percent):N1}% к пред. периоду", brush);
    }

    private static IReadOnlyList<ReportLegendItemViewModel> BuildLegendItems(IEnumerable<string> statuses)
    {
        var groups = statuses
            .GroupBy(Ui)
            .Select(group => new { Label = group.Key, Value = group.Count() })
            .OrderByDescending(item => item.Value)
            .ToArray();
        var total = Math.Max(1, groups.Sum(item => item.Value));

        return groups
            .Select(item => new ReportLegendItemViewModel(
                item.Label,
                item.Value,
                $"{item.Value} ({Math.Round(item.Value * 100d / total):N0}%)",
                GetStatusForegroundBrush(item.Label)))
            .ToArray();
    }

    private IReadOnlyList<ReportTrendPointViewModel> BuildTrendSeries(IEnumerable<(DateTime Date, decimal Value)> currentPoints, IEnumerable<(DateTime Date, decimal Value)> previousPoints, DateTime from, DateTime to)
    {
        const int bucketCount = 6;
        var totalDays = Math.Max(1, (to.Date - from.Date).Days + 1);
        var bucketSize = Math.Max(1, (int)Math.Ceiling(totalDays / (double)bucketCount));

        var currentLookup = currentPoints.ToArray();
        var previousLookup = previousPoints.ToArray();
        var series = new List<ReportTrendPointViewModel>(bucketCount);

        for (var index = 0; index < bucketCount; index++)
        {
            var bucketStart = from.Date.AddDays(index * bucketSize);
            var bucketEnd = index == bucketCount - 1 ? to.Date : bucketStart.AddDays(bucketSize - 1);
            if (bucketStart > to.Date)
            {
                bucketStart = to.Date;
                bucketEnd = to.Date;
            }

            var previousBucketStart = bucketStart.AddDays(-totalDays);
            var previousBucketEnd = bucketEnd.AddDays(-totalDays);

            var currentValue = currentLookup.Where(item => item.Date.Date >= bucketStart && item.Date.Date <= bucketEnd).Sum(item => item.Value);
            var previousValue = previousLookup.Where(item => item.Date.Date >= previousBucketStart && item.Date.Date <= previousBucketEnd).Sum(item => item.Value);
            series.Add(new ReportTrendPointViewModel(bucketEnd.ToString("dd.MM", RuCulture), currentValue, previousValue));
        }

        return series;
    }

    private static IReadOnlyList<ReportBarViewModel> BuildMonthlyBars(IEnumerable<(DateTime Date, decimal Value)> points, string suffix)
    {
        var months = Enumerable.Range(0, 6).Select(offset => DateTime.Today.AddMonths(offset - 5)).ToArray();
        var totals = points
            .GroupBy(item => new DateTime(item.Date.Year, item.Date.Month, 1))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Value));

        var values = months
            .Select(month => totals.TryGetValue(new DateTime(month.Year, month.Month, 1), out var value) ? value : 0m)
            .ToArray();
        var max = Math.Max(1m, values.Max());

        return months
            .Select((month, index) => new ReportBarViewModel(
                month.ToString("MMM", RuCulture),
                suffix == "₽" ? ToCompactMoneyLabel(values[index]) : $"{values[index]:N0}",
                28 + (double)(values[index] / max) * 108d,
                index == values.Length - 1 ? BrushFromHex("#4F5BFF") : BrushFromHex("#D9E0FF")))
            .ToArray();
    }

    private static IReadOnlyList<ReportRankedItemViewModel> BuildTopRevenueItems(IEnumerable<(string Label, decimal Value)> pairs)
    {
        return pairs
            .Where(item => !string.IsNullOrWhiteSpace(item.Label))
            .GroupBy(item => Ui(item.Label))
            .Select(group => new { Label = group.Key, Value = group.Sum(item => item.Value) })
            .OrderByDescending(item => item.Value)
            .Take(5)
            .Select((item, index) => new ReportRankedItemViewModel((index + 1).ToString(CultureInfo.InvariantCulture), item.Label, FormatMoney(item.Value)))
            .ToArray();
    }

    private static IReadOnlyList<ReportInfoItemViewModel> BuildOverdueInvoiceItems(IEnumerable<SalesInvoiceRecord> invoices)
    {
        return invoices
            .Where(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Просрочен")
            .OrderBy(item => item.DueDate)
            .Take(5)
            .Select(item => new ReportInfoItemViewModel(
                Ui(item.Number),
                Ui(item.CustomerName),
                $"{Math.Max(1, (DateTime.Today - item.DueDate.Date).Days)} дн.",
                BrushFromHex("#F45A5A")))
            .ToArray();
    }

    private static IReadOnlyList<ReportInfoItemViewModel> BuildPartialInvoiceItems(IEnumerable<SalesInvoiceRecord> invoices)
    {
        return invoices
            .Where(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Частично оплачен")
            .OrderByDescending(item => item.TotalAmount)
            .Take(5)
            .Select(item => new ReportInfoItemViewModel(
                Ui(item.Number),
                Ui(item.CustomerName),
                FormatMoney(item.TotalAmount),
                BrushFromHex("#7B68EE")))
            .ToArray();
    }

    private static IReadOnlyList<ReportInfoItemViewModel> BuildDelayedShipmentItems(IEnumerable<SalesShipmentRecord> shipments)
    {
        return shipments
            .Where(item => NormalizeShipmentStatus(item) == "Задержка")
            .OrderBy(item => item.ShipmentDate)
            .Take(5)
            .Select(item => new ReportInfoItemViewModel(
                Ui(item.Number),
                Ui(item.CustomerName),
                $"{Math.Max(1, (DateTime.Today - item.ShipmentDate.Date).Days)} дн.",
                BrushFromHex("#F45A5A")))
            .ToArray();
    }

    private static IReadOnlyList<ReportInfoItemViewModel> BuildCarrierItems(IEnumerable<SalesShipmentRecord> shipments)
    {
        return shipments
            .Where(item => !string.IsNullOrWhiteSpace(item.Carrier))
            .GroupBy(item => Ui(item.Carrier))
            .OrderByDescending(group => group.Count())
            .Take(5)
            .Select(group => new ReportInfoItemViewModel(group.Key, $"{group.Count()} отгрузок", FormatMoney(group.Sum(item => item.TotalAmount)), BrushFromHex("#4F5BFF")))
            .ToArray();
    }

    private static IReadOnlyList<ReportInfoItemViewModel> BuildOrdersWithoutInvoiceItems(IEnumerable<SalesOrderRecord> orders, IEnumerable<SalesInvoiceRecord> invoices)
    {
        var invoicesByOrder = invoices.Select(item => item.SalesOrderId).ToHashSet();
        return orders
            .Where(item => !invoicesByOrder.Contains(item.Id))
            .OrderByDescending(item => item.OrderDate)
            .Take(5)
            .Select(item => new ReportInfoItemViewModel(Ui(item.Number), Ui(item.CustomerName), FormatMoney(item.TotalAmount), BrushFromHex("#FF9B28")))
            .ToArray();
    }

    private static IReadOnlyList<ReportInfoItemViewModel> BuildReadyOrderItems(IEnumerable<SalesOrderRecord> orders)
    {
        return orders
            .Where(item => NormalizeOrderStatus(item.Status) == "Готов к отгрузке")
            .OrderByDescending(item => item.TotalAmount)
            .Take(5)
            .Select(item => new ReportInfoItemViewModel(Ui(item.Number), Ui(item.CustomerName), FormatMoney(item.TotalAmount), BrushFromHex("#4F5BFF")))
            .ToArray();
    }

    private IReadOnlyList<ReportEventItemViewModel> BuildRecentEvents(ReportSlice current)
    {
        var logEvents = _salesWorkspace.OperationLog
            .Where(item => item.LoggedAt >= _periodFrom && item.LoggedAt <= _periodTo)
            .OrderByDescending(item => item.LoggedAt)
            .Take(5)
            .Select(item => new ReportEventItemViewModel(item.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture), $"{Ui(item.EntityNumber)} — {Ui(item.Action)}"))
            .ToArray();

        if (logEvents.Length > 0)
        {
            return logEvents;
        }

        return current.Orders
            .Select(item => new ReportEventItemViewModel(item.OrderDate.ToString("dd.MM.yyyy HH:mm", RuCulture), $"{Ui(item.Number)} ? {NormalizeOrderStatus(item.Status)}"))
            .Concat(current.Invoices.Select(item => new ReportEventItemViewModel(item.InvoiceDate.ToString("dd.MM.yyyy HH:mm", RuCulture), $"{Ui(item.Number)} ? {NormalizeInvoiceStatus(item.Status, item.DueDate)}")))
            .Concat(current.Shipments.Select(item => new ReportEventItemViewModel(item.ShipmentDate.ToString("dd.MM.yyyy HH:mm", RuCulture), $"{Ui(item.Number)} ? {NormalizeShipmentStatus(item)}")))
            .OrderByDescending(item => DateTime.Parse(item.TimestampText, RuCulture))
            .Take(5)
            .ToArray();
    }

    private static int CountOverdueInvoices(IEnumerable<SalesInvoiceRecord> invoices)
    {
        return invoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Просрочен");
    }

    private static int CountDelayedShipments(IEnumerable<SalesShipmentRecord> shipments)
    {
        return shipments.Count(item => NormalizeShipmentStatus(item) == "Задержка");
    }

    private static int CountOrdersWithoutInvoice(IEnumerable<SalesOrderRecord> orders, IEnumerable<SalesInvoiceRecord> invoices)
    {
        var invoicesByOrder = invoices.Select(item => item.SalesOrderId).ToHashSet();
        return orders.Count(item => !invoicesByOrder.Contains(item.Id));
    }

    private static int CountCustomersWithDebt(IEnumerable<SalesInvoiceRecord> invoices)
    {
        return invoices
            .Where(item =>
            {
                var status = NormalizeInvoiceStatus(item.Status, item.DueDate);
                return status is "Ожидает оплату" or "Частично оплачен" or "Просрочен";
            })
            .Select(item => Ui(item.CustomerName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static int CountDeclinedCustomers(ReportSlice current, ReportSlice previous)
    {
        var currentRevenue = current.Invoices.GroupBy(item => Ui(item.CustomerName)).ToDictionary(group => group.Key, group => group.Sum(item => item.TotalAmount), StringComparer.OrdinalIgnoreCase);
        var previousRevenue = previous.Invoices.GroupBy(item => Ui(item.CustomerName)).ToDictionary(group => group.Key, group => group.Sum(item => item.TotalAmount), StringComparer.OrdinalIgnoreCase);

        return previousRevenue.Count(pair => pair.Value > 0m && currentRevenue.TryGetValue(pair.Key, out var currentValue) && currentValue < pair.Value);
    }

    private static int CountLowTurnoverProducts(ReportSlice current)
    {
        return current.InvoiceLines
            .GroupBy(item => item.ItemCode, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Sum(item => item.Quantity) <= 10m);
    }

    private static string ResolveSalesStatus(SalesOrderRecord order, SalesInvoiceRecord? invoice, SalesShipmentRecord? shipment)
    {
        if (Ui(order.Status).Contains("Отмен", StringComparison.OrdinalIgnoreCase))
        {
            return "Отменен";
        }

        if (shipment is not null)
        {
            return NormalizeShipmentStatus(shipment) == "Отгружена" ? "Отгружен" : "В сборке";
        }

        if (invoice is not null)
        {
            return "Счет выставлен";
        }

        return NormalizeOrderStatus(order.Status) switch
        {
            "Подтвержден" or "В резерве" or "Готов к отгрузке" => "Подтвержден",
            _ => "Новый"
        };
    }

    private static string NormalizeOrderStatus(string status)
    {
        return Ui(status) switch
        {
            "План" or "Черновик" => "Новый",
            "Подтвержден" => "Подтвержден",
            "В резерве" => "В резерве",
            "Готов к отгрузке" => "Готов к отгрузке",
            var value when value.Contains("Отмен", StringComparison.OrdinalIgnoreCase) => "Отменен",
            _ => "Новый"
        };
    }

    private static string NormalizeInvoiceStatus(string status, DateTime dueDate)
    {
        var normalized = Ui(status);
        if (normalized.Equals("Оплачен", StringComparison.OrdinalIgnoreCase))
        {
            return "Оплачен";
        }

        if (normalized.Contains("Частично", StringComparison.OrdinalIgnoreCase))
        {
            return "Частично оплачен";
        }

        if (dueDate.Date < DateTime.Today)
        {
            return "Просрочен";
        }

        return normalized switch
        {
            "Черновик" => "Черновик",
            "Выставлен" => "Выставлен",
            _ => "Ожидает оплату"
        };
    }

    private static string NormalizeShipmentStatus(SalesShipmentRecord shipment)
    {
        var normalized = Ui(shipment.Status);
        if (normalized.Equals("Отгружена", StringComparison.OrdinalIgnoreCase))
        {
            return "Отгружена";
        }

        if (shipment.ShipmentDate.Date < DateTime.Today.AddDays(-2))
        {
            return "Задержка";
        }

        return normalized switch
        {
            "Черновик" => "Черновик",
            "Готова к отгрузке" => "Готова к отгрузке",
            _ => "К сборке"
        };
    }

    private static ReportRegistryRowViewModel CreateRegistryRow(DateTime date, string document, string counterparty, string value, string status, string owner, string detail, string navigationTarget)
    {
        var badge = GetStatusBadge(status);
        return new ReportRegistryRowViewModel
        {
            SortDate = date,
            DateText = date.ToString("dd.MM.yyyy", RuCulture),
            DocumentText = document,
            CounterpartyText = counterparty,
            ValueText = value,
            StatusText = status,
            StatusBrush = badge.Foreground,
            StatusBackground = badge.Background,
            OwnerText = owner,
            DetailText = detail,
            NavigationTargetKey = navigationTarget
        };
    }

    private static ReportRegistryRowViewModel CreateRegistryRow(string dateText, string document, string counterparty, string value, string status, string owner, string detail, string navigationTarget)
    {
        var badge = GetStatusBadge(status);
        return new ReportRegistryRowViewModel
        {
            SortDate = DateTime.MinValue,
            DateText = dateText,
            DocumentText = document,
            CounterpartyText = counterparty,
            ValueText = value,
            StatusText = status,
            StatusBrush = badge.Foreground,
            StatusBackground = badge.Background,
            OwnerText = owner,
            DetailText = detail,
            NavigationTargetKey = navigationTarget
        };
    }

    private static (Brush Background, Brush Foreground) GetStatusBadge(string status)
    {
        return Ui(status) switch
        {
            "Подтвержден" or "Отгружен" or "Отгружена" or "Оплачен" or "Активен" or "В продажах" =>
                (BrushFromHex("#ECFBF1"), BrushFromHex("#34B56A")),
            "Счет выставлен" or "Выставлен" or "Ожидает оплату" or "В резерве" or "Готов к отгрузке" or "К сборке" or "На проверке" =>
                (BrushFromHex("#FFF4E8"), BrushFromHex("#FF8A26")),
            "Частично оплачен" =>
                (BrushFromHex("#F2EEFF"), BrushFromHex("#7B68EE")),
            "Просрочен" or "Отменен" or "Пауза" or "Без движения" or "Задержка" =>
                (BrushFromHex("#FFF1F1"), BrushFromHex("#E1565A")),
            _ => (BrushFromHex("#F3F5FA"), BrushFromHex("#7E8AA6"))
        };
    }

    private static Brush GetStatusForegroundBrush(string status)
    {
        return GetStatusBadge(status).Foreground;
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    private static string FormatMoney(decimal amount)
    {
        return string.Format(RuCulture, "{0:N0} ₽", amount);
    }

    private static string ToCompactMoneyLabel(decimal amount)
    {
        if (amount >= 1_000_000m)
        {
            return $"{amount / 1_000_000m:0.#}M";
        }

        if (amount >= 1_000m)
        {
            return $"{amount / 1_000m:0.#}K";
        }

        return amount.ToString("N0", RuCulture);
    }

    private static string FormatPeriod(DateTime from, DateTime to)
    {
        return $"{from:dd.MM.yyyy} - {to:dd.MM.yyyy}";
    }

    private void UpdateSearchPlaceholder()
    {
        HeaderSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(HeaderSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private string BuildReportHtml()
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ru\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>Отчеты</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:'Segoe UI',sans-serif;background:#f7f9fd;color:#17213a;margin:24px;}");
        builder.AppendLine(".header{display:flex;justify-content:space-between;align-items:flex-start;margin-bottom:24px;}");
        builder.AppendLine(".title{font-size:32px;font-weight:700;margin:0;}.subtitle{color:#7a86a5;margin-top:8px;}");
        builder.AppendLine(".grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin-bottom:18px;}");
        builder.AppendLine(".card{background:#fff;border:1px solid #e7ecf7;border-radius:16px;padding:16px;}");
        builder.AppendLine(".muted{color:#7a86a5;}.metric{font-size:24px;font-weight:700;margin-top:8px;}");
        builder.AppendLine("table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #e7ecf7;border-radius:16px;overflow:hidden;}");
        builder.AppendLine("th,td{padding:12px 14px;border-bottom:1px solid #eef2fa;text-align:left;font-size:13px;}");
        builder.AppendLine("th{color:#7b86a0;font-weight:600;background:#fff;}.badge{display:inline-block;padding:4px 8px;border-radius:10px;font-size:12px;}");
        builder.AppendLine("</style></head><body>");
        builder.AppendLine("<div class=\"header\">");
        builder.AppendLine($"<div><h1 class=\"title\">{Html(_currentState.RegistryTitle)}</h1><div class=\"subtitle\">Период: {Html(FormatPeriod(_periodFrom, _periodTo))}</div></div>");
        builder.AppendLine($"<div class=\"muted\">Сформировано: {Html(_lastGeneratedAt.ToString("dd.MM.yyyy HH:mm", RuCulture))}</div>");
        builder.AppendLine("</div>");

        builder.AppendLine("<div class=\"grid\">");
        foreach (var metric in _currentState.Metrics.Take(6))
        {
            builder.AppendLine("<div class=\"card\">");
            builder.AppendLine($"<div class=\"muted\">{Html(metric.Caption)}</div>");
            builder.AppendLine($"<div class=\"metric\">{Html(metric.ValueText)}</div>");
            builder.AppendLine($"<div style=\"margin-top:8px;color:{(metric.DeltaBrush is SolidColorBrush brush ? brush.Color.ToString() : "#7a86a5")}\">{Html(metric.DeltaText)}</div>");
            builder.AppendLine("</div>");
        }
        builder.AppendLine("</div>");

        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr>");
        builder.AppendLine($"<th>{Html(DateColumn.Header?.ToString() ?? string.Empty)}</th>");
        builder.AppendLine($"<th>{Html(DocumentColumn.Header?.ToString() ?? string.Empty)}</th>");
        builder.AppendLine($"<th>{Html(CounterpartyColumn.Header?.ToString() ?? string.Empty)}</th>");
        builder.AppendLine($"<th>{Html(ValueColumn.Header?.ToString() ?? string.Empty)}</th>");
        builder.AppendLine($"<th>{Html(StatusColumn.Header?.ToString() ?? string.Empty)}</th>");
        builder.AppendLine($"<th>{Html(OwnerColumn.Header?.ToString() ?? string.Empty)}</th>");
        builder.AppendLine("</tr></thead><tbody>");

        foreach (var row in _filteredRegistryRows.Take(40))
        {
            builder.AppendLine("<tr>");
            builder.AppendLine($"<td>{Html(row.DateText)}</td>");
            builder.AppendLine($"<td>{Html(row.DocumentText)}</td>");
            builder.AppendLine($"<td>{Html(row.CounterpartyText)}</td>");
            builder.AppendLine($"<td>{Html(row.ValueText)}</td>");
            builder.AppendLine($"<td><span class=\"badge\">{Html(row.StatusText)}</span></td>");
            builder.AppendLine($"<td>{Html(row.OwnerText)}</td>");
            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }
}

public sealed class ReportRegistryRowViewModel : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTime SortDate { get; set; }

    public string DateText { get; set; } = string.Empty;

    public string DocumentText { get; set; } = string.Empty;

    public string CounterpartyText { get; set; } = string.Empty;

    public string ValueText { get; set; } = string.Empty;

    public string StatusText { get; set; } = string.Empty;

    public Brush StatusBrush { get; set; } = Brushes.Transparent;

    public Brush StatusBackground { get; set; } = Brushes.Transparent;

    public string OwnerText { get; set; } = string.Empty;

    public string DetailText { get; set; } = string.Empty;

    public string NavigationTargetKey { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

public sealed record ReportMetricCardViewModel(string Caption, string ValueText, string DeltaText, Brush DeltaBrush, Brush AccentBrush, Brush AccentBackground, string Glyph);
public sealed record ReportLegendItemViewModel(string Label, int Value, string ValueText, Brush ColorBrush);
public sealed record ReportTrendPointViewModel(string Label, decimal CurrentValue, decimal PreviousValue);
public sealed record ReportBarViewModel(string Label, string ValueLabel, double Height, Brush FillBrush);
public sealed record ReportComparisonItemViewModel(string Label, string ValueText, string DeltaText, Brush DeltaBrush);
public sealed record ReportSignalItemViewModel(string Label, string CountText, Brush AccentBrush, Brush BackgroundBrush);
public sealed record ReportRankedItemViewModel(string Rank, string Label, string ValueText);
public sealed record ReportInfoItemViewModel(string Label, string Subtitle, string ValueText, Brush ValueBrush);
public sealed record ReportEventItemViewModel(string TimestampText, string Title);
public sealed record ReportSummaryDeltaViewModel(string Label, string ValueText, Brush ValueBrush);

internal sealed class ReportDashboardState
{
    public string TrendTitle { get; init; } = string.Empty;
    public string DonutTitle { get; init; } = string.Empty;
    public string MonthlyTitle { get; init; } = string.Empty;
    public string LeftCardTitle { get; init; } = string.Empty;
    public string MiddleCardTitle { get; init; } = string.Empty;
    public string RightLeftCardTitle { get; init; } = string.Empty;
    public string RightMiddleCardTitle { get; init; } = string.Empty;
    public string RegistryTitle { get; init; } = string.Empty;
    public string SummaryModuleLabel { get; init; } = string.Empty;
    public string NavigateModuleCaption { get; init; } = string.Empty;
    public string DateHeader { get; init; } = "Дата";
    public string DocumentHeader { get; init; } = "Документ";
    public string CounterpartyHeader { get; init; } = "Контрагент";
    public string ValueHeader { get; init; } = "Значение";
    public string OwnerHeader { get; init; } = "Ответственный";
    public IReadOnlyList<ReportMetricCardViewModel> Metrics { get; init; } = Array.Empty<ReportMetricCardViewModel>();
    public IReadOnlyList<ReportComparisonItemViewModel> ComparisonItems { get; init; } = Array.Empty<ReportComparisonItemViewModel>();
    public IReadOnlyList<ReportSignalItemViewModel> Signals { get; init; } = Array.Empty<ReportSignalItemViewModel>();
    public IReadOnlyList<ReportTrendPointViewModel> TrendPoints { get; init; } = Array.Empty<ReportTrendPointViewModel>();
    public IReadOnlyList<ReportBarViewModel> MonthlyBars { get; init; } = Array.Empty<ReportBarViewModel>();
    public IReadOnlyList<ReportLegendItemViewModel> DonutItems { get; init; } = Array.Empty<ReportLegendItemViewModel>();
    public IReadOnlyList<ReportRankedItemViewModel> LeftCardItems { get; init; } = Array.Empty<ReportRankedItemViewModel>();
    public IReadOnlyList<ReportRankedItemViewModel> MiddleCardItems { get; init; } = Array.Empty<ReportRankedItemViewModel>();
    public IReadOnlyList<ReportInfoItemViewModel> RightLeftCardItems { get; init; } = Array.Empty<ReportInfoItemViewModel>();
    public IReadOnlyList<ReportInfoItemViewModel> RightMiddleCardItems { get; init; } = Array.Empty<ReportInfoItemViewModel>();
    public IReadOnlyList<ReportEventItemViewModel> RecentEvents { get; init; } = Array.Empty<ReportEventItemViewModel>();
    public IReadOnlyList<ReportSummaryDeltaViewModel> SummaryDeltas { get; init; } = Array.Empty<ReportSummaryDeltaViewModel>();
    public IReadOnlyList<ReportRegistryRowViewModel> RegistryRows { get; init; } = Array.Empty<ReportRegistryRowViewModel>();
}

internal sealed record ReportOption(string Value, string Label);
internal sealed record ReportDeltaInfo(string Text, Brush Brush);
internal sealed record ReportLineEntry(Guid DocumentId, DateTime DocumentDate, string ItemCode, string ItemName, string CustomerName, string Warehouse, string Manager, decimal Quantity, decimal Amount);
internal sealed record ReportSlice(
    IReadOnlyList<SalesOrderRecord> Orders,
    IReadOnlyList<SalesInvoiceRecord> Invoices,
    IReadOnlyList<SalesShipmentRecord> Shipments,
    IReadOnlyList<SalesCustomerRecord> Customers,
    IReadOnlyList<ReportLineEntry> OrderLines,
    IReadOnlyList<ReportLineEntry> InvoiceLines,
    IReadOnlyList<ReportLineEntry> ShipmentLines);
internal sealed record ReportCatalogRow(string Code, string Name, string Unit, decimal Price, decimal Revenue, decimal PreviousRevenue, string Status, string Owner);
