using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;
using WpfButton = System.Windows.Controls.Button;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class DashboardWorkspaceView : UserControl
{
    private readonly SalesWorkspace _salesWorkspace;
    private readonly DemoWorkspace _demoWorkspace;

    public DashboardWorkspaceView(SalesWorkspace salesWorkspace, DemoWorkspace demoWorkspace)
    {
        _salesWorkspace = salesWorkspace;
        _demoWorkspace = demoWorkspace;

        InitializeComponent();
        Loaded += HandleLoaded;
        SizeChanged += HandleSizeChanged;
    }

    public event EventHandler<string>? NavigationRequested;

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HandleLoaded;
        Render();
        UpdateResponsiveLayout();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            DrawRevenueChart();
            DrawStatusDonut();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void HandleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
        DrawRevenueChart();
        DrawStatusDonut();
    }

    private void Render()
    {
        GreetingTitleText.Text = $"{BuildGreeting()}, Администратор!";
        GreetingSubtitleText.Text = BuildNowLabel();
        NotificationBadgeText.Text = BuildUrgentTasks().Count.ToString(CultureInfo.InvariantCulture);
        AnalyticsRangeText.Text = BuildAnalyticsRange();

        QuickLinksItemsControl.ItemsSource = BuildQuickLinks();
        var urgentTasks = BuildUrgentTasks();
        UrgentTasksItemsControl.ItemsSource = urgentTasks;
        UrgentTaskBadgeText.Text = urgentTasks.Count.ToString(CultureInfo.InvariantCulture);
        AnalyticsMetricsItemsControl.ItemsSource = BuildAnalyticsCards();
        StatusLegendItemsControl.ItemsSource = BuildStatusLegend();
        QuickActionItemsControl.ItemsSource = BuildQuickActions();
    }

    private void UpdateResponsiveLayout()
    {
        if (ActualWidth < 1180)
        {
            MainInsightsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            MainInsightsGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(UrgentTasksCard, 0);
            Grid.SetColumn(AnalyticsCard, 0);
            Grid.SetRow(AnalyticsCard, 1);
            if (MainInsightsGrid.RowDefinitions.Count == 1)
            {
                MainInsightsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            UrgentTasksCard.Margin = new Thickness(0, 0, 0, 18);
            AnalyticsChartsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            AnalyticsChartsGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(RevenueChartCard, 0);
            Grid.SetColumn(StatusChartCard, 0);
            Grid.SetRow(StatusChartCard, 1);
            if (AnalyticsChartsGrid.RowDefinitions.Count == 1)
            {
                AnalyticsChartsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            RevenueChartCard.Margin = new Thickness(0, 0, 0, 18);
        }
        else
        {
            MainInsightsGrid.ColumnDefinitions[0].Width = new GridLength(1.1, GridUnitType.Star);
            MainInsightsGrid.ColumnDefinitions[1].Width = new GridLength(2, GridUnitType.Star);
            Grid.SetColumn(UrgentTasksCard, 0);
            Grid.SetColumn(AnalyticsCard, 1);
            Grid.SetRow(AnalyticsCard, 0);
            while (MainInsightsGrid.RowDefinitions.Count > 1)
            {
                MainInsightsGrid.RowDefinitions.RemoveAt(MainInsightsGrid.RowDefinitions.Count - 1);
            }

            UrgentTasksCard.Margin = new Thickness(0, 0, 18, 0);
            AnalyticsChartsGrid.ColumnDefinitions[0].Width = new GridLength(1.65, GridUnitType.Star);
            AnalyticsChartsGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(RevenueChartCard, 0);
            Grid.SetColumn(StatusChartCard, 1);
            Grid.SetRow(StatusChartCard, 0);
            while (AnalyticsChartsGrid.RowDefinitions.Count > 1)
            {
                AnalyticsChartsGrid.RowDefinitions.RemoveAt(AnalyticsChartsGrid.RowDefinitions.Count - 1);
            }
            RevenueChartCard.Margin = new Thickness(0, 0, 18, 0);
        }

        UpdateStatusChartLayout();
    }

    private void UpdateStatusChartLayout()
    {
        var statusCardWidth = StatusChartCard.ActualWidth;
        var useCompactStatusLayout = statusCardWidth > 0 && statusCardWidth < 460;

        if (useCompactStatusLayout)
        {
            StatusChartBodyGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            StatusChartBodyGrid.ColumnDefinitions[1].Width = new GridLength(0);

            Grid.SetColumn(StatusDonutCanvas, 0);
            Grid.SetRow(StatusDonutCanvas, 0);
            StatusDonutCanvas.HorizontalAlignment = HorizontalAlignment.Center;

            Grid.SetColumn(StatusLegendItemsControl, 0);
            Grid.SetRow(StatusLegendItemsControl, 1);
            StatusLegendItemsControl.Margin = new Thickness(0, 12, 0, 0);
        }
        else
        {
            StatusChartBodyGrid.ColumnDefinitions[0].Width = new GridLength(220);
            StatusChartBodyGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);

            Grid.SetColumn(StatusDonutCanvas, 0);
            Grid.SetRow(StatusDonutCanvas, 0);
            StatusDonutCanvas.HorizontalAlignment = HorizontalAlignment.Left;

            Grid.SetColumn(StatusLegendItemsControl, 1);
            Grid.SetRow(StatusLegendItemsControl, 0);
            StatusLegendItemsControl.Margin = new Thickness(12, 8, 0, 0);
        }
    }

    private IReadOnlyList<DashboardNavigationCardViewModel> BuildQuickLinks()
    {
        return
        [
            NavigationCard("Заказы", "Просмотр и управление заказами клиентов", "sales", "#6C63FF", "#F0EDFF", "\uE14C"),
            NavigationCard("Клиенты", "База клиентов и контакты", "customers", "#59C36A", "#EBF9EF", "\uE716"),
            NavigationCard("Счета", "Выставление и контроль оплат", "invoices", "#FF9F1A", "#FFF4E3", "\uE8C7"),
            NavigationCard("Отгрузки", "Управление отгрузками и доставкой", "shipments", "#4F8CFF", "#EEF4FF", "\uEC47"),
            NavigationCard("Товары", "Каталог товаров и остатки на складах", "catalog", "#7B68EE", "#F1EEFF", "\uEECA")
        ];
    }

    private IReadOnlyList<DashboardUrgentTaskViewModel> BuildUrgentTasks()
    {
        var overdueOrders = _salesWorkspace.Orders.Count(item => item.OrderDate.Date < DateTime.Today.AddDays(-2) && !NormalizeOrderStatus(item.Status).Equals("Выполнен", StringComparison.OrdinalIgnoreCase));
        var invoicesToPay = _salesWorkspace.Invoices.Count(item => item.DueDate.Date <= DateTime.Today.AddDays(3) && !NormalizeInvoiceStatus(item.Status, item.DueDate).Equals("Оплачено", StringComparison.OrdinalIgnoreCase));
        var delayedShipments = _salesWorkspace.Shipments.Count(item => item.ShipmentDate.Date < DateTime.Today && !NormalizeShipmentStatus(item.Status).Equals("Доставлено", StringComparison.OrdinalIgnoreCase));
        var lowStockItems = CountLowStockItems();

        return
        [
            UrgentTask("Просроченные заказы", "Заказы, срок выполнения которых истек", overdueOrders, "sales", "#FF5F6D", "#FFF0F2", "\uEA39"),
            UrgentTask("Счета к оплате", "Ожидают оплаты в ближайшие 3 дня", invoicesToPay, "invoices", "#FF9F1A", "#FFF4E3", "\uE8C7"),
            UrgentTask("Отгрузки с задержкой", "Отгрузки, задержанные более 1 дня", delayedShipments, "shipments", "#4F8CFF", "#EEF4FF", "\uEC47"),
            UrgentTask("Низкий остаток товаров", "Товары с остатком ниже контрольного уровня", lowStockItems, "catalog", "#7B68EE", "#F1EEFF", "\uEECA")
        ];
    }

    private IReadOnlyList<DashboardMetricCardViewModel> BuildAnalyticsCards()
    {
        var currentStart = DateTime.Today.AddDays(-20);
        var currentEnd = DateTime.Today;
        var previousStart = currentStart.AddDays(-21);
        var previousEnd = currentStart.AddDays(-1);

        var currentOrders = _salesWorkspace.Orders.Where(item => IsInPeriod(item.OrderDate, currentStart, currentEnd)).ToArray();
        var previousOrders = _salesWorkspace.Orders.Where(item => IsInPeriod(item.OrderDate, previousStart, previousEnd)).ToArray();
        var currentInvoices = _salesWorkspace.Invoices.Where(item => IsInPeriod(item.InvoiceDate, currentStart, currentEnd)).ToArray();
        var previousInvoices = _salesWorkspace.Invoices.Where(item => IsInPeriod(item.InvoiceDate, previousStart, previousEnd)).ToArray();

        var revenue = currentInvoices.Sum(item => item.TotalAmount);
        var previousRevenue = previousInvoices.Sum(item => item.TotalAmount);
        var orders = currentOrders.Length;
        var paidInvoices = currentInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Оплачено");
        var previousPaidInvoices = previousInvoices.Count(item => NormalizeInvoiceStatus(item.Status, item.DueDate) == "Оплачено");
        var averageCheck = orders == 0 ? 0m : currentOrders.Average(item => item.TotalAmount);
        var previousAverageCheck = previousOrders.Length == 0 ? 0m : previousOrders.Average(item => item.TotalAmount);

        return
        [
            MetricCard("Выручка", FormatMoney(revenue), FormatDeltaPercent(revenue, previousRevenue)),
            MetricCard("Заказы", orders.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")), FormatDeltaPercent(orders, previousOrders.Length)),
            MetricCard("Счета оплачены", paidInvoices.ToString("N0", CultureInfo.GetCultureInfo("ru-RU")), FormatDeltaPercent(paidInvoices, previousPaidInvoices)),
            MetricCard("Средний чек", FormatMoney(averageCheck), FormatDeltaPercent(averageCheck, previousAverageCheck))
        ];
    }

    private IReadOnlyList<DashboardQuickActionViewModel> BuildQuickActions()
    {
        var overdueOrders = _salesWorkspace.Orders.Count(item => item.OrderDate.Date < DateTime.Today.AddDays(-2));
        var invoiceCandidates = _salesWorkspace.Orders.Count(item => NormalizeOrderStatus(item.Status) == "Подтвержден");
        var shipmentCandidates = _salesWorkspace.Shipments.Count(item => NormalizeShipmentStatus(item.Status) != "Доставлено");
        var stockChecks = CountLowStockItems();

        return
        [
            QuickAction("Обработать просроченные заказы", $"{overdueOrders} заказов требуют внимания", "Открыть заказы", "sales", "#FF5F6D", "#FFF0F2", "\uEA39"),
            QuickAction("Выставить счета клиентам", $"{invoiceCandidates} заказа готовы к выставлению", "Выставить счета", "invoices", "#FF9F1A", "#FFF4E3", "\uE8C7"),
            QuickAction("Подтвердить отгрузки", $"{shipmentCandidates} отгрузки ожидают подтверждения", "Перейти к отгрузкам", "shipments", "#4F8CFF", "#EEF4FF", "\uEC47"),
            QuickAction("Проверить остатки товаров", $"{stockChecks} товаров с низким остатком", "Проверить остатки", "catalog", "#7B68EE", "#F1EEFF", "\uEECA"),
            QuickAction("Сформировать отчет", "Анализ продаж и основных показателей", "Открыть отчеты", "audit", "#59C36A", "#EBF9EF", "\uE9D2")
        ];
    }

    private int CountLowStockItems()
    {
        var stockBalances = _salesWorkspace.OperationalSnapshot?.StockBalances ?? Array.Empty<WarehouseStockBalanceRecord>();
        return stockBalances.Count(IsLowStockItem);
    }

    private static bool IsLowStockItem(WarehouseStockBalanceRecord item)
    {
        var status = Clean(item.Status);
        return status.Contains("Критично", StringComparison.OrdinalIgnoreCase)
            || status.Contains("Под контролем", StringComparison.OrdinalIgnoreCase)
            || item.FreeQuantity <= 0m
            || (item.BaselineQuantity > 0m && item.FreeQuantity < item.BaselineQuantity * 0.2m);
    }

    private static bool IsInPeriod(DateTime date, DateTime start, DateTime end)
    {
        return date.Date >= start.Date && date.Date <= end.Date;
    }

    private static string FormatDeltaPercent(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return current == 0m ? "0%" : "+100%";
        }

        var delta = Math.Round((current - previous) / Math.Abs(previous) * 100m, MidpointRounding.AwayFromZero);
        return delta > 0m ? $"+{delta:N0}%" : $"{delta:N0}%";
    }

    private IReadOnlyList<DashboardStatusLegendItem> BuildStatusLegend()
    {
        var groups = new[]
        {
            new { Label = "Новый", Color = "#6C63FF", Count = _salesWorkspace.Orders.Count(item => NormalizeOrderStatus(item.Status) == "Новый") },
            new { Label = "Подтвержден", Color = "#59C36A", Count = _salesWorkspace.Orders.Count(item => NormalizeOrderStatus(item.Status) == "Подтвержден") },
            new { Label = "В сборке", Color = "#FFB648", Count = _salesWorkspace.Orders.Count(item => NormalizeOrderStatus(item.Status) == "В производстве") },
            new { Label = "Отгружен", Color = "#4F8CFF", Count = _salesWorkspace.Shipments.Count(item => NormalizeShipmentStatus(item.Status) == "Доставлено") },
            new { Label = "Частично отгружен", Color = "#7B68EE", Count = Math.Max(0, _salesWorkspace.Shipments.Count - _salesWorkspace.Shipments.Count(item => NormalizeShipmentStatus(item.Status) == "Доставлено") ) },
            new { Label = "Отменен", Color = "#FF6B6B", Count = _salesWorkspace.Orders.Count(item => Clean(item.Status).Contains("Отмен", StringComparison.OrdinalIgnoreCase)) }
        };

        var total = Math.Max(1, groups.Sum(item => item.Count));
        return groups
            .Select(item => new DashboardStatusLegendItem(
                item.Label,
                item.Count,
                $"{item.Count} ({Math.Round(item.Count * 100d / total):N0}%)",
                BrushFromHex(item.Color)))
            .ToArray();
    }

    private void DrawRevenueChart()
    {
        RevenueChartCanvas.Children.Clear();
        var width = Math.Max(320, RevenueChartCanvas.ActualWidth);
        var height = Math.Max(220, RevenueChartCanvas.ActualHeight);
        RevenueChartCanvas.Width = width;
        RevenueChartCanvas.Height = height;

        var points = BuildRevenuePoints();
        if (points.Count == 0)
        {
            return;
        }

        const double leftPad = 18;
        const double topPad = 20;
        const double rightPad = 16;
        const double bottomPad = 28;
        var plotWidth = width - leftPad - rightPad;
        var plotHeight = height - topPad - bottomPad;

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
        }

        var max = Math.Max(1m, points.Max(item => Math.Max(item.CurrentValue, item.PreviousValue)));
        var polyline = new Polyline
        {
            Stroke = BrushFromHex("#4F5BFF"),
            StrokeThickness = 3,
            StrokeLineJoin = PenLineJoin.Round
        };
        var previous = new Polyline
        {
            Stroke = BrushFromHex("#BBC4D9"),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection([4, 4]),
            StrokeLineJoin = PenLineJoin.Round
        };

        for (var index = 0; index < points.Count; index++)
        {
            var x = leftPad + (plotWidth * index / Math.Max(1, points.Count - 1));
            var y = topPad + (plotHeight * (1d - (double)(points[index].CurrentValue / max)));
            var prevY = topPad + (plotHeight * (1d - (double)(points[index].PreviousValue / max)));
            polyline.Points.Add(new Point(x, y));
            previous.Points.Add(new Point(x, prevY));

            RevenueChartCanvas.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = BrushFromHex("#4F5BFF"),
                Margin = new Thickness(x - 3, y - 3, 0, 0)
            });

            RevenueChartCanvas.Children.Add(new TextBlock
            {
                Text = points[index].Label,
                Foreground = BrushFromHex("#9AA5BD"),
                FontSize = 11,
                Margin = new Thickness(x - 14, topPad + plotHeight + 6, 0, 0)
            });
        }

        RevenueChartCanvas.Children.Add(previous);
        RevenueChartCanvas.Children.Add(polyline);
    }

    private void DrawStatusDonut()
    {
        StatusDonutCanvas.Children.Clear();
        var legend = BuildStatusLegend();
        var total = legend.Sum(item => item.Value);
        if (total <= 0)
        {
            return;
        }

        const double outerRadius = 76;
        const double innerRadius = 48;
        const double centerX = 110;
        const double centerY = 110;
        double startAngle = -90;

        foreach (var item in legend.Where(item => item.Value > 0))
        {
            var sweep = 360d * item.Value / total;
            StatusDonutCanvas.Children.Add(CreateDonutSegment(centerX, centerY, innerRadius, outerRadius, startAngle, sweep, item.ColorBrush));
            startAngle += sweep;
        }

        StatusDonutCanvas.Children.Add(new Ellipse
        {
            Width = innerRadius * 2,
            Height = innerRadius * 2,
            Fill = Brushes.White,
            Margin = new Thickness(centerX - innerRadius, centerY - innerRadius, 0, 0)
        });

        AddCenteredCanvasText(
            StatusDonutCanvas,
            total.ToString(CultureInfo.InvariantCulture),
            centerX,
            centerY - 13,
            28,
            FontWeights.SemiBold,
            BrushFromHex("#1A2440"));

        AddCenteredCanvasText(
            StatusDonutCanvas,
            "Всего",
            centerX,
            centerY + 24,
            13,
            FontWeights.Normal,
            BrushFromHex("#7280A0"));
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
        Canvas.SetLeft(textBlock, centerX - textBlock.DesiredSize.Width / 2d);
        Canvas.SetTop(textBlock, centerY - textBlock.DesiredSize.Height / 2d);
        canvas.Children.Add(textBlock);
    }

    private static Path CreateDonutSegment(double centerX, double centerY, double innerRadius, double outerRadius, double startAngle, double sweepAngle, Brush fill)
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

        return new Path
        {
            Data = new PathGeometry([figure]),
            Fill = fill
        };
    }

    private static Point PointOnCircle(double centerX, double centerY, double radius, double angle)
    {
        var radians = angle * Math.PI / 180d;
        return new Point(centerX + radius * Math.Cos(radians), centerY + radius * Math.Sin(radians));
    }

    private IReadOnlyList<RevenuePoint> BuildRevenuePoints()
    {
        var dates = Enumerable.Range(0, 8)
            .Select(offset => DateTime.Today.AddDays(offset - 7))
            .ToArray();

        var invoiceLookup = _salesWorkspace.Invoices
            .GroupBy(item => item.InvoiceDate.Date)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.TotalAmount));

        return dates
            .Select(date =>
            {
                var current = invoiceLookup.TryGetValue(date.Date, out var value) ? value : 0m;
                var previous = invoiceLookup.TryGetValue(date.Date.AddDays(-7), out var previousValue) ? previousValue : 0m;
                return new RevenuePoint(date.ToString("dd.MM", CultureInfo.GetCultureInfo("ru-RU")), current, previous);
            })
            .ToArray();
    }

    private static string BuildGreeting()
    {
        return DateTime.Now.Hour switch
        {
            < 12 => "Доброе утро",
            < 18 => "Добрый день",
            _ => "Добрый вечер"
        };
    }

    private static string BuildNowLabel()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var now = DateTime.Now;
        var weekday = culture.DateTimeFormat.GetDayName(now.DayOfWeek);
        weekday = char.ToUpperInvariant(weekday[0]) + weekday[1..];
        return $"{weekday}, {now:dd MMMM yyyy} · {now:HH:mm}";
    }

    private static string BuildAnalyticsRange()
    {
        var end = DateTime.Today;
        var start = end.AddDays(-20);
        return $"{start:dd.MM.yyyy} - {end:dd.MM.yyyy}";
    }

    private void HandleNavigationButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not string targetKey)
        {
            return;
        }

        NavigationRequested?.Invoke(this, targetKey);
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        HeaderSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(HeaderSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HandleSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        var query = Clean(HeaderSearchBox.Text);
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var targetKey = ResolveSearchTarget(query);
        if (targetKey is null)
        {
            MessageBox.Show(Window.GetWindow(this), "По этому запросу ничего не найдено.", "Глобальный поиск", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NavigationRequested?.Invoke(this, targetKey);
    }

    private string? ResolveSearchTarget(string query)
    {
        if (_salesWorkspace.Orders.Any(item => Matches(query, item.Number, item.CustomerName, item.CustomerCode, item.Warehouse, item.Status, item.Manager)))
        {
            return "sales";
        }

        if (_salesWorkspace.Customers.Any(item => Matches(query, item.Code, item.Name, item.ContractNumber, item.Phone, item.Email, item.Manager, item.Status)))
        {
            return "customers";
        }

        if (_salesWorkspace.Invoices.Any(item => Matches(query, item.Number, item.SalesOrderNumber, item.CustomerName, item.CustomerCode, item.Status, item.Manager)))
        {
            return "invoices";
        }

        if (_salesWorkspace.Shipments.Any(item => Matches(query, item.Number, item.SalesOrderNumber, item.CustomerName, item.CustomerCode, item.Warehouse, item.Carrier, item.Status, item.Manager)))
        {
            return "shipments";
        }

        if (_salesWorkspace.CatalogItems.Any(item => Matches(query, item.Code, item.Name, item.Unit)))
        {
            return "catalog";
        }

        var stockBalances = _salesWorkspace.OperationalSnapshot?.StockBalances ?? Array.Empty<WarehouseStockBalanceRecord>();
        if (stockBalances.Any(item => Matches(query, item.ItemCode, item.ItemName, item.Warehouse, item.Status)))
        {
            return "warehouse";
        }

        return null;
    }

    private static bool Matches(string query, params string?[] values)
    {
        return values.Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static DashboardNavigationCardViewModel NavigationCard(string title, string subtitle, string targetKey, string accentHex, string backgroundHex, string glyph)
    {
        return new DashboardNavigationCardViewModel(title, subtitle, targetKey, BrushFromHex(accentHex), BrushFromHex(backgroundHex), glyph);
    }

    private static DashboardUrgentTaskViewModel UrgentTask(string title, string subtitle, int value, string targetKey, string accentHex, string backgroundHex, string glyph)
    {
        return new DashboardUrgentTaskViewModel(title, subtitle, value.ToString(CultureInfo.InvariantCulture), targetKey, BrushFromHex(accentHex), BrushFromHex(backgroundHex), glyph);
    }

    private static DashboardMetricCardViewModel MetricCard(string title, string value, string delta)
    {
        return new DashboardMetricCardViewModel(title, value, delta, delta.StartsWith('-') ? BrushFromHex("#FF6B6B") : BrushFromHex("#1BC47D"));
    }

    private static DashboardQuickActionViewModel QuickAction(string title, string subtitle, string hint, string targetKey, string accentHex, string backgroundHex, string glyph)
    {
        return new DashboardQuickActionViewModel(title, subtitle, hint, targetKey, BrushFromHex(accentHex), BrushFromHex(backgroundHex), glyph);
    }

    private static Brush BrushFromHex(string hex)
    {
        return BrushPalette.FromHex(hex);
    }

    private static string NormalizeOrderStatus(string status)
    {
        return Clean(status) switch
        {
            "План" or "Черновик" => "Новый",
            "Подтвержден" or "В резерве" => "Подтвержден",
            "Готов к отгрузке" or "К сборке" => "В производстве",
            "Отгружена" or "Выполнен" => "Выполнен",
            _ => "Новый"
        };
    }

    private static string NormalizeInvoiceStatus(string status, DateTime dueDate)
    {
        var normalized = Clean(status);
        if (normalized.Equals("Оплачен", StringComparison.OrdinalIgnoreCase))
        {
            return "Оплачено";
        }

        if (dueDate.Date < DateTime.Today)
        {
            return "Просрочено";
        }

        return normalized.Equals("Частично оплачен", StringComparison.OrdinalIgnoreCase) ? "Частично оплачено" : "Не оплачено";
    }

    private static string NormalizeShipmentStatus(string status)
    {
        return Clean(status) switch
        {
            "Отгружена" or "Доставлено" => "Доставлено",
            "Готова к отгрузке" or "В пути" => "В пути",
            _ => "Запланировано"
        };
    }

    private static string FormatMoney(decimal amount)
    {
        return string.Format(CultureInfo.GetCultureInfo("ru-RU"), "{0:N0} ₽", amount);
    }

    private static string Clean(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }
}

public sealed record DashboardNavigationCardViewModel(string Title, string Subtitle, string TargetKey, Brush AccentBrush, Brush IconBackground, string IconGlyph);
public sealed record DashboardUrgentTaskViewModel(string Title, string Subtitle, string Value, string TargetKey, Brush AccentBrush, Brush IconBackground, string IconGlyph);
public sealed record DashboardMetricCardViewModel(string Title, string Value, string Delta, Brush DeltaBrush);
public sealed record DashboardQuickActionViewModel(string Title, string Subtitle, string Hint, string TargetKey, Brush AccentBrush, Brush IconBackground, string IconGlyph);
public sealed record DashboardStatusLegendItem(string Label, int Value, string ValueText, Brush ColorBrush);
internal sealed record RevenuePoint(string Label, decimal CurrentValue, decimal PreviousValue);
