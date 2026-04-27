using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed record DashboardSnapshot(IReadOnlyList<MetricCard> Metrics, IReadOnlyList<AlertRow> Alerts, IReadOnlyList<WorkQueueRow> WorkQueue, IReadOnlyList<QuickAction> QuickActions);

public sealed record MetricCard(string Title, string Value, string Hint, Color AccentColor);

public sealed record AlertRow([property: DisplayName("Приоритет")] string Приоритет, [property: DisplayName("Ситуация")] string Ситуация, [property: DisplayName("Что нужно сделать")] string ЧтоНужноСделать, [property: DisplayName("Контекст")] string Контекст);

public sealed record WorkQueueRow([property: DisplayName("Приоритет")] string Приоритет, [property: DisplayName("Модуль")] string Модуль, [property: DisplayName("Задача")] string Задача, [property: DisplayName("Ответственный")] string Ответственный, [property: DisplayName("Срок")] string Срок);

public sealed record QuickAction(string Caption, string Hint, string TargetKey);

public sealed record CustomerRow([property: DisplayName("Код")] string Код, [property: DisplayName("Клиент")] string Клиент, [property: DisplayName("Договор")] string Договор, [property: DisplayName("Валюта")] string Валюта, [property: DisplayName("Менеджер")] string Менеджер, [property: DisplayName("Статус")] string Статус);

public sealed record SalesOrderRow([property: DisplayName("Заказ")] string Заказ, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Клиент")] string Клиент, [property: DisplayName("Склад")] string Склад, [property: DisplayName("Позиций")] string Позиций, [property: DisplayName("На сумму")] string НаСумму, [property: DisplayName("Статус")] string Статус, [property: DisplayName("Менеджер")] string Менеджер);

public sealed record SalesInvoiceRow([property: DisplayName("Счет")] string Счет, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Клиент")] string Клиент, [property: DisplayName("На сумму")] string НаСумму, [property: DisplayName("Оплатить до")] string ОплатитьДо, [property: DisplayName("Статус")] string Статус);

public sealed record SalesShipmentRow([property: DisplayName("Отгрузка")] string Отгрузка, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Клиент")] string Клиент, [property: DisplayName("Склад")] string Склад, [property: DisplayName("На сумму")] string НаСумму, [property: DisplayName("Проведение")] string Проведение);

public sealed record SupplierRow([property: DisplayName("Код")] string Код, [property: DisplayName("Поставщик")] string Поставщик, [property: DisplayName("Договор")] string Договор, [property: DisplayName("Закупщик")] string Закупщик, [property: DisplayName("Статус")] string Статус);

public sealed record PurchaseOrderRow([property: DisplayName("Заказ")] string Заказ, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Поставщик")] string Поставщик, [property: DisplayName("Склад")] string Склад, [property: DisplayName("Позиций")] string Позиций, [property: DisplayName("На сумму")] string НаСумму, [property: DisplayName("Статус")] string Статус);

public sealed record SupplierInvoiceRow([property: DisplayName("Счет")] string Счет, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Поставщик")] string Поставщик, [property: DisplayName("На сумму")] string НаСумму, [property: DisplayName("Оплатить до")] string ОплатитьДо, [property: DisplayName("Проведение")] string Проведение);

public sealed record PurchaseReceiptRow([property: DisplayName("Приемка")] string Приемка, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Поставщик")] string Поставщик, [property: DisplayName("Склад")] string Склад, [property: DisplayName("На сумму")] string НаСумму, [property: DisplayName("Расходы")] string Расходы);

public sealed record StockBalanceRow([property: DisplayName("Артикул")] string Артикул, [property: DisplayName("Номенклатура")] string Номенклатура, [property: DisplayName("Склад")] string Склад, [property: DisplayName("Ячейка")] string Ячейка, [property: DisplayName("Остаток")] string Остаток, [property: DisplayName("В резерве")] string ВРезерве, [property: DisplayName("Доступно")] string Доступно, [property: DisplayName("Статус")] string Статус);

public sealed record TransferOrderRow([property: DisplayName("Перемещение")] string Перемещение, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Откуда")] string Откуда, [property: DisplayName("Куда")] string Куда, [property: DisplayName("Позиций")] string Позиций, [property: DisplayName("Статус")] string Статус, [property: DisplayName("К сроку")] string КСроку);

public sealed record ReservationRow([property: DisplayName("Резерв")] string Резерв, [property: DisplayName("Заказ")] string Заказ, [property: DisplayName("Из")] string Из, [property: DisplayName("В")] string В, [property: DisplayName("Позиций")] string Позиций, [property: DisplayName("Проведение")] string Проведение);

public sealed record InventoryCountRow([property: DisplayName("Инвентаризация")] string Инвентаризация, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Склад")] string Склад, [property: DisplayName("Ячейка")] string Ячейка, [property: DisplayName("Строк")] string Строк, [property: DisplayName("Расхождений")] string Расхождений);

public sealed record WriteOffRow([property: DisplayName("Списание")] string Списание, [property: DisplayName("Дата")] string Дата, [property: DisplayName("Склад")] string Склад, [property: DisplayName("Причина")] string Причина, [property: DisplayName("Сумма")] string Сумма, [property: DisplayName("Проведение")] string Проведение);

public sealed record ItemRow([property: DisplayName("Код")] string Код, [property: DisplayName("Номенклатура")] string Номенклатура, [property: DisplayName("Ед. изм.")] string ЕдИзм, [property: DisplayName("Поставщик")] string Поставщик, [property: DisplayName("Базовый склад")] string БазовыйСклад, [property: DisplayName("Ценовая группа")] string ЦеноваяГруппа);

public sealed record PriceTypeRow([property: DisplayName("Вид цены")] string ВидЦены, [property: DisplayName("Валюта")] string Валюта, [property: DisplayName("Базовый вид")] string БазовыйВид, [property: DisplayName("Псих. округление")] string ПсихОкругление, [property: DisplayName("Режим")] string Режим);

public sealed record DiscountRow([property: DisplayName("Скидка")] string Скидка, [property: DisplayName("Значение")] string Значение, [property: DisplayName("Вид цены")] string ВидЦены, [property: DisplayName("Период")] string Период, [property: DisplayName("Кому")] string Кому, [property: DisplayName("Статус")] string Статус);

public sealed class DemoWorkspace
{
    public string WorkspaceTitle { get; }

    public string WorkspaceSubtitle { get; }

    public string CurrentOperator { get; }

    public DashboardSnapshot Dashboard { get; }

    public IReadOnlyList<CustomerRow> Customers { get; }

    public IReadOnlyList<SalesOrderRow> SalesOrders { get; }

    public IReadOnlyList<SalesInvoiceRow> SalesInvoices { get; }

    public IReadOnlyList<SalesShipmentRow> SalesShipments { get; }

    public IReadOnlyList<SupplierRow> Suppliers { get; }

    public IReadOnlyList<PurchaseOrderRow> PurchaseOrders { get; }

    public IReadOnlyList<SupplierInvoiceRow> SupplierInvoices { get; }

    public IReadOnlyList<PurchaseReceiptRow> PurchaseReceipts { get; }

    public IReadOnlyList<StockBalanceRow> StockBalances { get; }

    public IReadOnlyList<TransferOrderRow> TransferOrders { get; }

    public IReadOnlyList<ReservationRow> Reservations { get; }

    public IReadOnlyList<InventoryCountRow> InventoryCounts { get; }

    public IReadOnlyList<WriteOffRow> WriteOffs { get; }

    public IReadOnlyList<ItemRow> Items { get; }

    public IReadOnlyList<PriceTypeRow> PriceTypes { get; }

    public IReadOnlyList<DiscountRow> Discounts { get; }

    private DemoWorkspace(
        string workspaceTitle,
        string workspaceSubtitle,
        string currentOperator,
        DashboardSnapshot dashboard,
        IReadOnlyList<CustomerRow> customers,
        IReadOnlyList<SalesOrderRow> salesOrders,
        IReadOnlyList<SalesInvoiceRow> salesInvoices,
        IReadOnlyList<SalesShipmentRow> salesShipments,
        IReadOnlyList<SupplierRow> suppliers,
        IReadOnlyList<PurchaseOrderRow> purchaseOrders,
        IReadOnlyList<SupplierInvoiceRow> supplierInvoices,
        IReadOnlyList<PurchaseReceiptRow> purchaseReceipts,
        IReadOnlyList<StockBalanceRow> stockBalances,
        IReadOnlyList<TransferOrderRow> transferOrders,
        IReadOnlyList<ReservationRow> reservations,
        IReadOnlyList<InventoryCountRow> inventoryCounts,
        IReadOnlyList<WriteOffRow> writeOffs,
        IReadOnlyList<ItemRow> items,
        IReadOnlyList<PriceTypeRow> priceTypes,
        IReadOnlyList<DiscountRow> discounts)
    {
        WorkspaceTitle = workspaceTitle;
        WorkspaceSubtitle = workspaceSubtitle;
        CurrentOperator = currentOperator;
        Dashboard = dashboard;
        Customers = customers;
        SalesOrders = salesOrders;
        SalesInvoices = salesInvoices;
        SalesShipments = salesShipments;
        Suppliers = suppliers;
        PurchaseOrders = purchaseOrders;
        SupplierInvoices = supplierInvoices;
        PurchaseReceipts = purchaseReceipts;
        StockBalances = stockBalances;
        TransferOrders = transferOrders;
        Reservations = reservations;
        InventoryCounts = inventoryCounts;
        WriteOffs = writeOffs;
        Items = items;
        PriceTypes = priceTypes;
        Discounts = discounts;
    }

    public static DemoWorkspace Create()
    {
        var dashboard = new DashboardSnapshot(
            new MetricCard[]
            {
                new("Заказы в работе", "3", "Сделки, которые нельзя терять из виду.", Color.FromArgb(79, 174, 92)),
                new("Закупки в пути", "2", "Поставки, которые еще не приняты на склад.", Color.FromArgb(78, 160, 190)),
                new("Низкий остаток", "2", "Позиции, где пора пополнять запас.", Color.FromArgb(196, 92, 83)),
                new("Перемещения", "2", "Внутренние движения между зонами хранения.", Color.FromArgb(201, 134, 64)),
                new("Отгрузка недели", "160 091 ₽", "Уже готово к закрытию текущей недели.", Color.FromArgb(123, 104, 163))
            },
            new AlertRow[]
            {
                new("Критично", "Экран световой SCREEN 30", "Пополнить шоурум из главного склада", "Доступно 48 м в шоуруме"),
                new("Важно", "Счет от Профиль-Снаб", "Провести оплату по графику", "Срок первой оплаты завтра"),
                new("План", "Заказ Мир Потолков", "Сформировать счет и резерв", "Заказ еще в черновике")
            },
            new WorkQueueRow[]
            {
                new("Высокий", "Продажи", "Подтвердить счет INV-240323-002", "Антон Мельников", "23.03.2026"),
                new("Высокий", "Склад", "Дособрать TO-240323-001", "Денис Корнеев", "23.03.2026"),
                new("Средний", "Закупки", "Создать PO-240323-002 поставщику", "Ольга Соколова", "24.03.2026"),
                new("Средний", "Склад", "Списать бой по шоуруму", "Денис Корнеев", "24.03.2026")
            },
            new QuickAction[]
            {
                new("Открыть продажи", "Клиенты, заказы, счета, отгрузки", "sales"),
                new("Проверить закупки", "Поставщики, поставки и приемка", "purchasing"),
                new("Открыть склад", "Остатки, перемещения и резервы", "warehouse"),
                new("Номенклатура и цены", "Товары, прайсы и скидки", "catalog")
            });

        return new DemoWorkspace(
            "Мажор Flow",
            "Операционный центр продаж, закупок и склада вместо 1С",
            "Ворожцов Стас",
            dashboard,
            new CustomerRow[]
            {
                new("C-001", "ООО Атриум Дизайн", "AT-24/11", "RUB", "Ирина Киселева", "Активен"),
                new("C-002", "Студия Линия Света", "LS-25/02", "RUB", "Антон Мельников", "Активен"),
                new("C-003", "ИП Мир Потолков", "MP-25/01", "RUB", "Ирина Киселева", "Активен"),
                new("C-004", "ООО Периметр Девелопмент", "PD-25/03", "RUB", "Антон Мельников", "Активен")
            },
            new SalesOrderRow[]
            {
                new("SO-240323-003", "23.03.2026", "ИП Мир Потолков", "Монтажный склад", "2", "24 180 ₽", "План", "Ирина Киселева"),
                new("SO-240323-002", "22.03.2026", "Студия Линия Света", "Шоурум", "2", "68 891 ₽", "Подтвержден", "Антон Мельников"),
                new("SO-240323-001", "21.03.2026", "ООО Атриум Дизайн", "Главный склад", "2", "144 960 ₽", "В резерве", "Ирина Киселева")
            },
            new SalesInvoiceRow[]
            {
                new("INV-240323-002", "23.03.2026", "Студия Линия Света", "68 891 ₽", "26.03.2026", "Черновик"),
                new("INV-240323-001", "22.03.2026", "ООО Атриум Дизайн", "144 960 ₽", "29.03.2026", "Подтвержден")
            },
            new SalesShipmentRow[]
            {
                new("SH-240323-002", "23.03.2026", "Студия Линия Света", "Шоурум", "68 891 ₽", "Черновик"),
                new("SH-240323-001", "22.03.2026", "ООО Атриум Дизайн", "Главный склад", "91 200 ₽", "Проведен")
            },
            new SupplierRow[]
            {
                new("S-001", "ООО Профиль-Снаб", "PS-25/01", "Ольга Соколова", "Активен"),
                new("S-002", "ООО Люмфер", "LF-25/02", "Ольга Соколова", "Активен"),
                new("S-003", "ООО КрепМаркет", "KM-25/03", "Денис Корнеев", "Активен")
            },
            new PurchaseOrderRow[]
            {
                new("PO-240323-002", "23.03.2026", "ООО КрепМаркет", "Монтажный склад", "2", "45 120 ₽", "План"),
                new("PO-240323-001", "20.03.2026", "ООО Профиль-Снаб", "Главный склад", "2", "215 400 ₽", "Подтвержден")
            },
            new SupplierInvoiceRow[]
            {
                new("PINV-240323-001", "22.03.2026", "ООО Профиль-Снаб", "215 400 ₽", "24.03.2026", "Проведен")
            },
            new PurchaseReceiptRow[]
            {
                new("PR-240323-001", "23.03.2026", "ООО Профиль-Снаб", "Главный склад", "128 640 ₽", "2 доп. расхода")
            },
            new StockBalanceRow[]
            {
                new("ALTEZA-P50-BL", "ALTEZA профиль P-50 гардина черный мат", "Главный склад", "Стеллаж A-01-01", "480 м", "160 м", "320 м", "Ок"),
                new("LUM-CLAMP-50", "Профиль LumFer Clamp Level 50", "Главный склад", "Стеллаж B-02-04", "96 м", "24 м", "72 м", "Под контроль"),
                new("GR-5-BLACK", "Гарпун КазПолимер ГР-5 черный", "Главный склад", "Стеллаж A-01-01", "2 200 м", "800 м", "1 400 м", "Ок"),
                new("SCREEN-30", "Экран световой SCREEN 30 белый", "Шоурум", "Шоурум / фронт", "84 м", "36 м", "48 м", "Критично"),
                new("GX53-BASE", "Платформа GX-53 белая", "Монтажный склад", "Монтаж / сборка", "120 шт", "20 шт", "100 шт", "Под контроль"),
                new("KLEM-2X", "Клеммы 2-контактные", "Главный склад", "Стеллаж B-02-04", "3 000 шт", "1 500 шт", "1 500 шт", "Ок")
            },
            new TransferOrderRow[]
            {
                new("TO-240323-002", "23.03.2026", "Главный склад", "Монтажный склад", "2", "План", "24.03.2026"),
                new("TO-240323-001", "23.03.2026", "Главный склад", "Шоурум", "2", "В работе", "23.03.2026")
            },
            new ReservationRow[]
            {
                new("RS-240323-001", "SO-240323-001", "На складе", "В перемещении", "2", "Проведен")
            },
            new InventoryCountRow[]
            {
                new("IC-240323-001", "22.03.2026", "Шоурум", "Шоурум / фронт", "2", "2")
            },
            new WriteOffRow[]
            {
                new("WO-240323-001", "23.03.2026", "Шоурум", "Демонстрационный профиль списан", "11 808 ₽", "Проведен")
            },
            new ItemRow[]
            {
                new("IT-001", "ALTEZA профиль P-50 гардина черный мат", "Метр", "ООО Профиль-Снаб", "Главный склад", "Премиум"),
                new("IT-002", "Профиль LumFer Clamp Level 50", "Метр", "ООО Люмфер", "Главный склад", "Премиум"),
                new("IT-003", "Гарпун КазПолимер ГР-5 черный", "Метр", "ООО Профиль-Снаб", "Главный склад", "Стандарт"),
                new("IT-004", "Экран световой SCREEN 30 белый", "Метр", "ООО Люмфер", "Шоурум", "Премиум"),
                new("IT-005", "Платформа GX-53 белая", "Штука", "ООО КрепМаркет", "Монтажный склад", "Монтаж"),
                new("IT-006", "Клеммы 2-контактные", "Штука", "ООО КрепМаркет", "Главный склад", "Стандарт")
            },
            new PriceTypeRow[]
            {
                new("Розница", "RUB", "-", "Да", "Рабочий"),
                new("Опт", "RUB", "Розница", "Нет", "Рабочий"),
                new("Монтаж", "RUB", "Опт", "Нет", "Рабочий"),
                new("Закупка", "RUB", "-", "Нет", "Ручной")
            },
            new DiscountRow[]
            {
                new("Опт от 100 м2", "7%", "Опт", "03.03.2026 - 21.06.2026", "2 контраг., 1 склад", "Активна"),
                new("Монтажная база", "4%", "Монтаж", "13.03.2026 - 21.07.2026", "1 контраг., 1 склад", "Активна"),
                new("Шоурум / распродажа", "10%", "Розница", "18.03.2026 - 12.04.2026", "2 контраг., 1 склад", "Активна")
            });
    }
}
