namespace WarehouseAutomatisaion.Desktop.Wpf;

/// <summary>
/// Команда внутри витрины раздела. <see cref="IsImplemented"/> = true означает что
/// клик приведёт пользователя на существующую страницу через <see cref="TargetSectionKey"/>
/// (опционально с активацией внутреннего <see cref="SubSection"/>).
/// При false открывается <see cref="ComingSoonView"/>.
/// </summary>
public sealed record NavigationCommand(
    string Caption,
    bool IsImplemented,
    string? TargetSectionKey = null,
    string? SubSection = null);

/// <summary>Группа команд внутри витрины (зелёный заголовок + список).</summary>
public sealed record NavigationGroup(string Title, IReadOnlyList<NavigationCommand> Items);

/// <summary>Витрина раздела (Продажи / Закупки / Склад).</summary>
public sealed record NavigationSection(
    string Key,
    string Caption,
    string IconGlyph,
    IReadOnlyList<NavigationGroup> Groups);

/// <summary>
/// Статический реестр витрин в стиле 1С УНФ Панель разделов.
/// </summary>
public static class NavigationCommandCatalog
{
    public const string SalesSectionKey = "section-sales";
    public const string PurchasingSectionKey = "section-purchasing";
    public const string WarehouseSectionKey = "section-warehouse";

    public static NavigationSection Sales { get; } = new(
        SalesSectionKey,
        "Продажи",
        "",
        new[]
        {
            new NavigationGroup("Продажи", new[]
            {
                new NavigationCommand("Покупатели", true, "customers"),
                new NavigationCommand("Заказы покупателей", true, "sales"),
                new NavigationCommand("Счета на оплату", true, "sales"),
                new NavigationCommand("Расходные накладные", true, "shipments"),
                new NavigationCommand("Акты выполненных работ", false),
                new NavigationCommand("Корректировки реализаций", false),
                new NavigationCommand("Акты о расхождениях (полученные)", false),
                new NavigationCommand("Возвраты от покупателей", true, "finance"),
                new NavigationCommand("Счета-фактуры", false),
                new NavigationCommand("Счета-фактуры на возврат", false),
            }),
            new NavigationGroup("Розничные продажи", new[]
            {
                new NavigationCommand("Рабочее место кассира (РМК)", false),
                new NavigationCommand("Чеки ККМ", false),
                new NavigationCommand("Отчеты о розничных продажах", false),
                new NavigationCommand("Кассы ККМ", false),
                new NavigationCommand("Палитра товаров", false),
            }),
            new NavigationGroup("Расчеты с покупателями", new[]
            {
                new NavigationCommand("Сверки взаиморасчетов", true, "finance"),
                new NavigationCommand("Корректировки долга", false),
            }),
            new NavigationGroup("Товары и услуги", new[]
            {
                new NavigationCommand("Номенклатура", true, "catalog"),
            }),
            new NavigationGroup("Цены и скидки", new[]
            {
                new NavigationCommand("Установка цен", true, "catalog", "priceSetup"),
                new NavigationCommand("Виды цен", true, "catalog", "prices"),
                new NavigationCommand("Прайс-листы", false),
                new NavigationCommand("Скидки", true, "catalog", "discounts"),
            }),
            new NavigationGroup("Reports", new[]
            {
                new NavigationCommand("Отчет по расходным накладным", false),
                new NavigationCommand("Задолженность по лимитам", false),
            }),
            new NavigationGroup("Аналитика", new[]
            {
                new NavigationCommand("Отчеты", true, "audit"),
            }),
            new NavigationGroup("Сервис", new[]
            {
                new NavigationCommand("Массовые рассылки (E-mail, SMS)", false),
                new NavigationCommand("Сегменты контрагентов", false),
                new NavigationCommand("Состояния заказов покупателей", false),
                new NavigationCommand("Печать этикеток и ценников", false),
                new NavigationCommand("Дополнительные обработки", false),
                new NavigationCommand("Загрузить документы из сканов (фото)", false),
                new NavigationCommand("Электронные перевозочные документы", false),
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
                new NavigationCommand("Поставщики", true, "purchasing", "suppliers"),
                new NavigationCommand("Заказы поставщикам", true, "purchasing", "orders"),
                new NavigationCommand("Счета на оплату (полученные)", true, "purchasing", "invoices"),
                new NavigationCommand("Приходные накладные", true, "purchasing", "receipts"),
                new NavigationCommand("Возвраты поставщикам", true, "purchasing", "payments"),
                new NavigationCommand("Счета-фактуры (полученные)", false),
                new NavigationCommand("Дополнительные расходы", false),
                new NavigationCommand("Доверенности", false),
                new NavigationCommand("Корректировки поступлений", false),
                new NavigationCommand("Акты о расхождениях", true, "purchasing", "discrepancies"),
                new NavigationCommand("Счета-фактуры на возврат", false),
                new NavigationCommand("Расходы предпринимателя", false),
            }),
            new NavigationGroup("Расчеты с поставщиками", new[]
            {
                new NavigationCommand("Сверки взаиморасчетов", true, "purchasing", "payments"),
                new NavigationCommand("Корректировки долга", false),
            }),
            new NavigationGroup("Товары и услуги", new[]
            {
                new NavigationCommand("Номенклатура", true, "catalog"),
            }),
            new NavigationGroup("Аналитика", new[]
            {
                new NavigationCommand("Отчеты", true, "audit"),
            }),
            new NavigationGroup("Сервис", new[]
            {
                new NavigationCommand("Загрузить документы из сканов (фото)", false),
                new NavigationCommand("Выгрузка товаров в ТСД", false),
                new NavigationCommand("Дополнительные обработки", false),
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
                new NavigationCommand("Заказы на перемещение", true, "warehouse", "transfers"),
                new NavigationCommand("Перемещения", true, "warehouse", "transfers"),
                new NavigationCommand("Комплектации", false),
                new NavigationCommand("Инвентаризации", true, "warehouse", "inventory"),
                new NavigationCommand("Пересортица", true, "warehouse", "cellstorage"),
                new NavigationCommand("Складские акты", false),
                new NavigationCommand("Оприходования", false),
                new NavigationCommand("Списания", true, "warehouse", "writeoffs"),
                new NavigationCommand("Склады и магазины", false),
            }),
            new NavigationGroup("Товары и услуги", new[]
            {
                new NavigationCommand("Номенклатура", true, "catalog"),
                new NavigationCommand("Штрихкоды", false),
            }),
            new NavigationGroup("Аналитика", new[]
            {
                new NavigationCommand("Отчеты", true, "audit"),
            }),
            new NavigationGroup("Сервис", new[]
            {
                new NavigationCommand("Выгрузка товаров в ТСД", false),
                new NavigationCommand("Печать этикеток и ценников", false),
                new NavigationCommand("Дополнительные обработки", false),
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
}
