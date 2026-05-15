using System.Windows.Media;

namespace WarehouseAutomatisaion.Desktop.Wpf;

/// <summary>
/// Команда внутри витрины раздела. <see cref="TargetSectionKey"/> — ключ для
/// <see cref="MainWindow.OpenSection"/>, опц. <see cref="SubSection"/> активирует
/// подвкладку workspace-view (через <c>MainWindow.ActivateSubSection</c>).
/// </summary>
public sealed record NavigationCommand(
    string Caption,
    string IconGlyph,
    Brush AccentBrush,
    Brush IconBackground,
    string TargetSectionKey,
    string? SubSection = null);

/// <summary>Группа команд внутри витрины (зелёный заголовок + список карточек).</summary>
public sealed record NavigationGroup(string Title, IReadOnlyList<NavigationCommand> Items);

/// <summary>Витрина раздела (Продажи / Закупки / Склад).</summary>
public sealed record NavigationSection(
    string Key,
    string Caption,
    string IconGlyph,
    IReadOnlyList<NavigationGroup> Groups);

/// <summary>
/// Реестр витрин 1С УНФ «Панель разделов». Содержит только реализованные команды
/// — каждая ведёт на существующую страницу через TargetSectionKey + SubSection.
/// </summary>
public static class NavigationCommandCatalog
{
    public const string SalesSectionKey = "section-sales";
    public const string PurchasingSectionKey = "section-purchasing";
    public const string WarehouseSectionKey = "section-warehouse";

    private static readonly Brush BluePrimary = BrushFromHex("#4F5BFF");
    private static readonly Brush BlueSoft = BrushFromHex("#EEF2FF");
    private static readonly Brush GreenPrimary = BrushFromHex("#1FA45F");
    private static readonly Brush GreenSoft = BrushFromHex("#EAF8F0");
    private static readonly Brush OrangePrimary = BrushFromHex("#FF9F1A");
    private static readonly Brush OrangeSoft = BrushFromHex("#FFF4E3");
    private static readonly Brush PurplePrimary = BrushFromHex("#8A4FFF");
    private static readonly Brush PurpleSoft = BrushFromHex("#F1EBFF");
    private static readonly Brush CyanPrimary = BrushFromHex("#0F95B3");
    private static readonly Brush CyanSoft = BrushFromHex("#E3F5FA");
    private static readonly Brush PinkPrimary = BrushFromHex("#E84393");
    private static readonly Brush PinkSoft = BrushFromHex("#FCEAF4");
    private static readonly Brush RedPrimary = BrushFromHex("#FF5F6D");
    private static readonly Brush RedSoft = BrushFromHex("#FFEDEF");
    private static readonly Brush IndigoPrimary = BrushFromHex("#4F8CFF");
    private static readonly Brush IndigoSoft = BrushFromHex("#EEF4FF");

    // Segoe Fluent Icons глифы
    private const string IconContact = "";
    private const string IconCart = "";
    private const string IconMoney = "";
    private const string IconPackage = "";
    private const string IconReturn = "";
    private const string IconBalance = "";
    private const string IconCube = "";
    private const string IconTag = "";
    private const string IconList = "";
    private const string IconPercent = "";
    private const string IconChart = "";
    private const string IconTransfer = "";
    private const string IconInventory = "";
    private const string IconAdjust = "";
    private const string IconWriteOff = "";
    private const string IconDoc = "";
    private const string IconDiscrepancy = "";

    public static NavigationSection Sales { get; } = new(
        SalesSectionKey,
        "Продажи",
        "",
        new[]
        {
            new NavigationGroup("Продажи", new[]
            {
                new NavigationCommand("Покупатели", IconContact, GreenPrimary, GreenSoft, "customers", "buyers"),
                new NavigationCommand("Заказы покупателей", IconCart, BluePrimary, BlueSoft, "sales"),
                new NavigationCommand("Счета на оплату", IconMoney, OrangePrimary, OrangeSoft, "invoices"),
                new NavigationCommand("Расходные накладные", IconPackage, IndigoPrimary, IndigoSoft, "shipments"),
                new NavigationCommand("Возвраты от покупателей", IconReturn, RedPrimary, RedSoft, "returns"),
            }),
            new NavigationGroup("Расчеты с покупателями", new[]
            {
                new NavigationCommand("Сверки взаиморасчетов", IconBalance, CyanPrimary, CyanSoft, "finance"),
            }),
            new NavigationGroup("Товары и услуги", new[]
            {
                new NavigationCommand("Номенклатура", IconCube, PurplePrimary, PurpleSoft, "catalog"),
            }),
            new NavigationGroup("Цены и скидки", new[]
            {
                new NavigationCommand("Установка цен", IconTag, OrangePrimary, OrangeSoft, "catalog", "priceSetup"),
                new NavigationCommand("Виды цен", IconList, BluePrimary, BlueSoft, "catalog", "prices"),
                new NavigationCommand("Скидки", IconPercent, PinkPrimary, PinkSoft, "catalog", "discounts"),
            }),
            new NavigationGroup("Аналитика", new[]
            {
                new NavigationCommand("Отчеты", IconChart, GreenPrimary, GreenSoft, "audit"),
            }),
        });

    public static NavigationSection Purchasing { get; } = new(
        PurchasingSectionKey,
        "Закупки",
        "",
        new[]
        {
            new NavigationGroup("Закупки", new[]
            {
                new NavigationCommand("Поставщики", IconContact, GreenPrimary, GreenSoft, "customers", "suppliers"),
                new NavigationCommand("Заказы поставщикам", IconCart, BluePrimary, BlueSoft, "purchasing", "orders"),
                new NavigationCommand("Счета на оплату (полученные)", IconMoney, OrangePrimary, OrangeSoft, "purchasing", "invoices"),
                new NavigationCommand("Приходные накладные", IconPackage, IndigoPrimary, IndigoSoft, "purchasing", "receipts"),
                new NavigationCommand("Возвраты поставщикам", IconReturn, RedPrimary, RedSoft, "purchasing", "payments"),
                new NavigationCommand("Акты о расхождениях", IconDiscrepancy, PinkPrimary, PinkSoft, "purchasing", "discrepancies"),
            }),
            new NavigationGroup("Расчеты с поставщиками", new[]
            {
                new NavigationCommand("Сверки взаиморасчетов", IconBalance, CyanPrimary, CyanSoft, "purchasing", "payments"),
            }),
            new NavigationGroup("Товары и услуги", new[]
            {
                new NavigationCommand("Номенклатура", IconCube, PurplePrimary, PurpleSoft, "catalog"),
            }),
            new NavigationGroup("Аналитика", new[]
            {
                new NavigationCommand("Отчеты", IconChart, GreenPrimary, GreenSoft, "audit"),
            }),
        });

    public static NavigationSection Warehouse { get; } = new(
        WarehouseSectionKey,
        "Склад",
        "",
        new[]
        {
            new NavigationGroup("Склад", new[]
            {
                new NavigationCommand("Заказы на перемещение", IconCart, BluePrimary, BlueSoft, "warehouse", "transfers"),
                new NavigationCommand("Перемещения", IconTransfer, IndigoPrimary, IndigoSoft, "warehouse", "transfers"),
                new NavigationCommand("Инвентаризации", IconInventory, GreenPrimary, GreenSoft, "warehouse", "inventory"),
                new NavigationCommand("Пересортица", IconAdjust, OrangePrimary, OrangeSoft, "warehouse", "cellstorage"),
                new NavigationCommand("Списания", IconWriteOff, RedPrimary, RedSoft, "warehouse", "writeoffs"),
            }),
            new NavigationGroup("Товары и услуги", new[]
            {
                new NavigationCommand("Номенклатура", IconCube, PurplePrimary, PurpleSoft, "catalog"),
            }),
            new NavigationGroup("Аналитика", new[]
            {
                new NavigationCommand("Отчеты", IconChart, GreenPrimary, GreenSoft, "audit"),
            }),
        });

    public static NavigationSection? FindByKey(string key)
    {
        return key switch
        {
            SalesSectionKey => Sales,
            PurchasingSectionKey => Purchasing,
            WarehouseSectionKey => Warehouse,
            _ => null,
        };
    }

    private static Brush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
