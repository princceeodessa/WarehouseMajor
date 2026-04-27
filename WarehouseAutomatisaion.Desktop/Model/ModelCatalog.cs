using System.Drawing;
using WarehouseAutomatisaion.Domain.Entities;

namespace WarehouseAutomatisaion.Desktop.Model;

public static class ModelCatalog
{
    public static IReadOnlyList<DomainArea> Areas { get; } =
    [
        new("master", "Master Data", Color.FromArgb(201, 134, 64), 0),
        new("pricing", "Pricing", Color.FromArgb(100, 143, 255), 1),
        new("sales", "Sales", Color.FromArgb(79, 174, 92), 2),
        new("purchasing", "Purchasing", Color.FromArgb(78, 160, 190), 3),
        new("warehouse", "Warehouse", Color.FromArgb(186, 91, 87), 4),
        new("shared", "Shared Lines", Color.FromArgb(123, 104, 163), 5)
    ];

    public static IReadOnlyList<EntityMetadata> Entities { get; } =
    [
        new(typeof(Organization), "Organization", "master", "Юрлицо/компания, от имени которой оформляются документы.", "Организация", "Присутствует в заголовках практически всех документов 1С."),
        new(typeof(Employee), "Employee", "master", "Ответственный, автор, владелец операции или подписант.", "Ответственный, Автор", "Пока выделен как общий reference-тип."),
        new(typeof(BankAccount), "BankAccount", "master", "Банковский счет организации или контрагента.", "БанковскийСчет, БанковскийСчетКонтрагента, БанковскийСчетПоставщика", "Нужен и в продажах, и в закупках."),
        new(typeof(BusinessPartner), "BusinessPartner", "master", "Единый контрагент с ролями покупателя, поставщика, перевозчика.", "Catalog.Контрагенты", "В 1С здесь уже совмещены клиенты и поставщики."),
        new(typeof(PartnerContact), "PartnerContact", "master", "Контактное лицо контрагента.", "КонтактноеЛицо, КонтактноеЛицоПодписант", "Можно расширить каналами связи."),
        new(typeof(PartnerContract), "PartnerContract", "master", "Договор между организацией и контрагентом.", "Договор", "Используется как в продаже, так и в закупке."),
        new(typeof(UnitOfMeasure), "UnitOfMeasure", "master", "Единица измерения номенклатуры и строк документов.", "ЕдиницаИзмерения", "Опорный справочник для количественных операций."),
        new(typeof(ItemCategory), "ItemCategory", "master", "Категория/группа номенклатуры.", "КатегорияНоменклатуры", "Нужна для цен, скидок и аналитики."),
        new(typeof(PriceGroup), "PriceGroup", "master", "Ценовая группа товара.", "ЦеноваяГруппа", "Участвует в настройке цен и скидок."),
        new(typeof(WarehouseNode), "WarehouseNode", "master", "Склад, магазин или точка хранения в единой модели.", "СтруктурнаяЕдиница, Подразделение, Склад", "Пока оставлен универсальным узлом хранения."),
        new(typeof(StorageBin), "StorageBin", "master", "Ячейка внутри склада/узла хранения.", "Catalog.Ячейки, Ячейка", "Есть и в номенклатуре, и в складских документах."),
        new(typeof(NomenclatureItem), "NomenclatureItem", "master", "Товар/материал/услуга из номенклатуры.", "Catalog.Номенклатура", "Центральная сущность всего товарного контура."),
        new(typeof(PriceType), "PriceType", "pricing", "Вид цены с валютой, базовым видом и правилами округления.", "Catalog.ВидыЦен", "Применяется в продаже, закупке и документах регистрации цен."),
        new(typeof(PriceTypeRoundingRule), "PriceTypeRoundingRule", "pricing", "Правило округления внутри вида цены.", "ПравилаОкругленияЦены", "Отдельная дочерняя сущность к виду цены."),
        new(typeof(DiscountPolicy), "DiscountPolicy", "pricing", "Автоматическая скидка/наценка с условиями и получателями.", "Catalog.АвтоматическиеСкидки", "Связана с партнерами, складами, категориями и ценовыми группами."),
        new(typeof(PriceRegistrationDocument), "PriceRegistrationDocument", "pricing", "Документ установки цен номенклатуры.", "Document.УстановкаЦенНоменклатуры", "Регистрирует цены по товарам и видам цен."),
        new(typeof(PriceRegistrationLine), "PriceRegistrationLine", "pricing", "Строка регистрации цены по товару и виду цены.", "ТЧ Запасы", "Ключ: товар + вид цены + валюта."),
        new(typeof(SalesOrder), "SalesOrder", "sales", "Заказ покупателя на продажу или резерв.", "Document.ЗаказПокупателя", "Содержит строки запасов и платежный календарь."),
        new(typeof(SalesInvoice), "SalesInvoice", "sales", "Счет на оплату клиенту.", "Document.СчетНаОплату", "Может опираться на заказ покупателя."),
        new(typeof(SalesShipment), "SalesShipment", "sales", "Расходная накладная/отгрузка клиенту.", "Document.РасходнаяНакладная", "Связана с заказом, перевозчиком и местом хранения."),
        new(typeof(PurchaseOrder), "PurchaseOrder", "purchasing", "Заказ поставщику на закупку.", "Document.ЗаказПоставщику", "Может быть связан с заказом покупателя."),
        new(typeof(SupplierInvoice), "SupplierInvoice", "purchasing", "Счет на оплату поставщика.", "Document.СчетНаОплатуПоставщика", "Содержит строки и платежный календарь."),
        new(typeof(PurchaseReceipt), "PurchaseReceipt", "purchasing", "Приходная накладная/приемка от поставщика.", "Document.ПриходнаяНакладная", "Содержит строки товаров и дополнительные расходы."),
        new(typeof(TransferOrder), "TransferOrder", "warehouse", "Распоряжение на перемещение запасов.", "Document.ЗаказНаПеремещение", "Формирует потребность в перемещении между узлами хранения."),
        new(typeof(StockTransfer), "StockTransfer", "warehouse", "Фактическое перемещение запасов.", "Document.ПеремещениеЗапасов", "Связь подтверждена логически, но probe по этому документу поврежден."),
        new(typeof(InventoryCount), "InventoryCount", "warehouse", "Инвентаризация запасов по складу/ячейке.", "Document.ИнвентаризацияЗапасов", "Хранит учетное и фактическое количество."),
        new(typeof(StockReservationDocument), "StockReservationDocument", "warehouse", "Документ изменения места резерва запасов.", "Document.РезервированиеЗапасов", "Работает по строкам запасов и заказу покупателя."),
        new(typeof(StockWriteOff), "StockWriteOff", "warehouse", "Списание запасов.", "Document.СписаниеЗапасов", "Может ссылаться на инвентаризацию как основание."),
        new(typeof(StockBalance), "StockBalance", "warehouse", "Остаток товара по складу/ячейке/партии.", "Остатки и регистры запасов", "В доменной модели нужен как рабочее состояние."),
        new(typeof(CommercialDocumentLine), "CommercialDocumentLine", "shared", "Универсальная строка продажи/закупки/списания.", "ТЧ Запасы", "Содержит количество, цену, сумму, НДС и ссылку на товар."),
        new(typeof(StockMovementLine), "StockMovementLine", "shared", "Строка складского движения/резерва/перемещения.", "ТЧ Запасы", "Хранит источник и приемник движения."),
        new(typeof(InventoryCountLine), "InventoryCountLine", "shared", "Строка пересчета с учетным и фактическим количеством.", "ТЧ Запасы", "Используется в инвентаризации."),
        new(typeof(PaymentScheduleLine), "PaymentScheduleLine", "shared", "Строка платежного календаря документа.", "ТЧ ПлатежныйКалендарь", "Есть в счетах и заказах."),
        new(typeof(AdditionalChargeLine), "AdditionalChargeLine", "shared", "Дополнительный расход приемки.", "ТЧ Расходы", "Распределяется на товарные строки.")
    ];

    public static IReadOnlyList<RelationshipMetadata> Relationships { get; } =
    [
        new(typeof(Organization), typeof(BankAccount), "owns", "1:n", "Организация имеет свои банковские счета.", "БанковскийСчет"),
        new(typeof(BusinessPartner), typeof(BankAccount), "uses", "1:n", "У контрагента может быть несколько счетов.", "БанковскийСчетКонтрагента, БанковскийСчетПоставщика"),
        new(typeof(BusinessPartner), typeof(PartnerContact), "has", "1:n", "Контакты принадлежат контрагенту.", "КонтактноеЛицо"),
        new(typeof(BusinessPartner), typeof(PartnerContract), "contracts", "1:n", "Договор заключен с контрагентом.", "Договор"),
        new(typeof(BusinessPartner), typeof(NomenclatureItem), "default supplier", "1:n", "Товар может иметь поставщика по умолчанию.", "Catalog.Номенклатура: Поставщик"),
        new(typeof(UnitOfMeasure), typeof(NomenclatureItem), "measures", "1:n", "Товар хранит базовую единицу измерения.", "ЕдиницаИзмерения"),
        new(typeof(ItemCategory), typeof(NomenclatureItem), "groups", "1:n", "Категория группирует номенклатуру.", "КатегорияНоменклатуры"),
        new(typeof(PriceGroup), typeof(NomenclatureItem), "segments", "1:n", "Ценовая группа связывает товар с ценовой политикой.", "ЦеноваяГруппа"),
        new(typeof(WarehouseNode), typeof(StorageBin), "contains", "1:n", "Склад содержит ячейки.", "Catalog.Ячейки: Владелец"),
        new(typeof(WarehouseNode), typeof(NomenclatureItem), "default storage", "1:n", "У товара может быть склад по умолчанию.", "Catalog.Номенклатура: Склад"),
        new(typeof(StorageBin), typeof(NomenclatureItem), "default bin", "1:n", "У товара может быть ячейка по умолчанию.", "Catalog.Номенклатура: Ячейка"),
        new(typeof(PriceType), typeof(PriceTypeRoundingRule), "rounding rules", "1:n", "Вид цены хранит правила округления.", "ПравилаОкругленияЦены"),
        new(typeof(PriceType), typeof(DiscountPolicy), "drives", "1:n", "Скидка может зависеть от вида цены.", "Catalog.АвтоматическиеСкидки: ВидЦен"),
        new(typeof(BusinessPartner), typeof(DiscountPolicy), "recipient", "n:m", "Скидка может назначаться конкретным контрагентам.", "ПолучателиСкидкиКонтрагенты"),
        new(typeof(WarehouseNode), typeof(DiscountPolicy), "scope", "n:m", "Скидка может быть ограничена складом/магазином.", "ПолучателиСкидкиСклады"),
        new(typeof(ItemCategory), typeof(DiscountPolicy), "scope", "n:m", "Скидка может быть ограничена категориями.", "НоменклатураГруппыЦеновыеГруппы"),
        new(typeof(PriceGroup), typeof(DiscountPolicy), "scope", "n:m", "Скидка может применяться по ценовым группам.", "НоменклатураГруппыЦеновыеГруппы"),
        new(typeof(PriceType), typeof(PriceRegistrationLine), "prices", "1:n", "Строка установки цен ссылается на вид цены.", "ТЧ Запасы: ВидЦены"),
        new(typeof(NomenclatureItem), typeof(PriceRegistrationLine), "priced item", "1:n", "Цена регистрируется на товар.", "ТЧ Запасы: Номенклатура"),
        new(typeof(PriceRegistrationDocument), typeof(PriceRegistrationLine), "contains", "1:n", "Документ установки цен содержит строки.", "Document.УстановкаЦенНоменклатуры"),
        new(typeof(BusinessPartner), typeof(SalesOrder), "customer", "1:n", "Заказ покупателя оформляется на контрагента.", "Контрагент"),
        new(typeof(PartnerContract), typeof(SalesOrder), "contract", "1:n", "Заказ покупателя использует договор.", "Договор"),
        new(typeof(PriceType), typeof(SalesOrder), "price type", "1:n", "Заказ покупателя хранит вид цены.", "ВидЦен"),
        new(typeof(WarehouseNode), typeof(SalesOrder), "reserve warehouse", "1:n", "Заказ резервирует запасы в узле хранения.", "СтруктурнаяЕдиницаРезерв"),
        new(typeof(StorageBin), typeof(SalesOrder), "bin", "1:n", "Заказ может ссылаться на ячейку.", "Ячейка"),
        new(typeof(SalesOrder), typeof(CommercialDocumentLine), "contains", "1:n", "Заказ покупателя содержит товарные строки.", "ТЧ Запасы"),
        new(typeof(SalesOrder), typeof(PaymentScheduleLine), "payment plan", "1:n", "В заказе есть платежный календарь.", "ТЧ ПлатежныйКалендарь"),
        new(typeof(BusinessPartner), typeof(SalesInvoice), "customer", "1:n", "Счет выставляется покупателю.", "Контрагент"),
        new(typeof(PartnerContract), typeof(SalesInvoice), "contract", "1:n", "Счет связан с договором.", "Договор"),
        new(typeof(PriceType), typeof(SalesInvoice), "price type", "1:n", "Счет хранит вид цены.", "ВидЦен"),
        new(typeof(SalesInvoice), typeof(CommercialDocumentLine), "contains", "1:n", "Счет содержит строки номенклатуры.", "ТЧ Запасы"),
        new(typeof(SalesInvoice), typeof(PaymentScheduleLine), "payment plan", "1:n", "Счет может содержать платежный календарь.", "ТЧ ПлатежныйКалендарь"),
        new(typeof(BusinessPartner), typeof(SalesShipment), "customer", "1:n", "Расходная накладная оформляется на контрагента.", "Контрагент"),
        new(typeof(PriceType), typeof(SalesShipment), "price type", "1:n", "Отгрузка хранит вид цены.", "ВидЦен"),
        new(typeof(WarehouseNode), typeof(SalesShipment), "warehouse", "1:n", "Отгрузка происходит со склада/подразделения.", "Подразделение, СтруктурнаяЕдиница"),
        new(typeof(StorageBin), typeof(SalesShipment), "bin", "1:n", "Отгрузка может быть привязана к ячейке.", "Ячейка"),
        new(typeof(SalesOrder), typeof(SalesShipment), "fulfills", "1:n", "Отгрузка исполняет заказ покупателя.", "Заказ"),
        new(typeof(SalesShipment), typeof(CommercialDocumentLine), "contains", "1:n", "Отгрузка содержит строки товаров.", "ТЧ Запасы"),
        new(typeof(BusinessPartner), typeof(PurchaseOrder), "supplier", "1:n", "Заказ поставщику оформляется на контрагента.", "Контрагент"),
        new(typeof(PartnerContract), typeof(PurchaseOrder), "contract", "1:n", "Заказ поставщику использует договор.", "Договор"),
        new(typeof(PriceType), typeof(PurchaseOrder), "partner price type", "1:n", "Закупка может использовать вид цен контрагента.", "ВидЦенКонтрагента"),
        new(typeof(WarehouseNode), typeof(PurchaseOrder), "reserve warehouse", "1:n", "Заказ поставщику может резервировать узел хранения.", "СтруктурнаяЕдиницаРезерв"),
        new(typeof(SalesOrder), typeof(PurchaseOrder), "back-to-back", "1:n", "Закупка может быть завязана на заказ покупателя.", "ЗаказПокупателя"),
        new(typeof(PurchaseOrder), typeof(CommercialDocumentLine), "contains", "1:n", "Заказ поставщику содержит строки.", "ТЧ Запасы"),
        new(typeof(PurchaseOrder), typeof(PaymentScheduleLine), "payment plan", "1:n", "В заказе поставщику есть платежный календарь.", "ТЧ ПлатежныйКалендарь"),
        new(typeof(BusinessPartner), typeof(SupplierInvoice), "supplier", "1:n", "Счет поставщика принадлежит контрагенту.", "Контрагент"),
        new(typeof(PartnerContract), typeof(SupplierInvoice), "contract", "1:n", "Счет поставщика связан с договором.", "Договор"),
        new(typeof(PurchaseOrder), typeof(SupplierInvoice), "based on", "1:n", "Счет поставщика может опираться на заказ.", "ДокументОснование"),
        new(typeof(SupplierInvoice), typeof(CommercialDocumentLine), "contains", "1:n", "Счет поставщика содержит строки товаров.", "ТЧ Запасы"),
        new(typeof(SupplierInvoice), typeof(PaymentScheduleLine), "payment plan", "1:n", "Счет поставщика содержит график оплаты.", "ТЧ ПлатежныйКалендарь"),
        new(typeof(BusinessPartner), typeof(PurchaseReceipt), "supplier", "1:n", "Приходная накладная оформляется на поставщика.", "Контрагент"),
        new(typeof(PurchaseOrder), typeof(PurchaseReceipt), "receives", "1:n", "Приемка исполняет заказ поставщику.", "Заказ"),
        new(typeof(PriceType), typeof(PurchaseReceipt), "partner price type", "1:n", "Приходная накладная хранит вид цен контрагента.", "ВидЦенКонтрагента"),
        new(typeof(WarehouseNode), typeof(PurchaseReceipt), "warehouse", "1:n", "Приемка поступает в узел хранения.", "СтруктурнаяЕдиница, Подразделение"),
        new(typeof(StorageBin), typeof(PurchaseReceipt), "bin", "1:n", "Приемка может быть привязана к ячейке.", "Ячейка"),
        new(typeof(PurchaseReceipt), typeof(CommercialDocumentLine), "contains", "1:n", "Приходная накладная содержит товарные строки.", "ТЧ Запасы"),
        new(typeof(PurchaseReceipt), typeof(AdditionalChargeLine), "allocates", "1:n", "Приходная накладная содержит дополнительные расходы.", "ТЧ Расходы"),
        new(typeof(WarehouseNode), typeof(TransferOrder), "route", "1:n", "Заказ на перемещение задает отправителя и получателя.", "СтруктурнаяЕдиницаРезерв, СтруктурнаяЕдиницаПолучатель"),
        new(typeof(SalesOrder), typeof(TransferOrder), "supports", "1:n", "Перемещение может быть связано с заказом покупателя.", "ЗаказПокупателя"),
        new(typeof(TransferOrder), typeof(StockMovementLine), "contains", "1:n", "Заказ на перемещение содержит строки запасов.", "ТЧ Запасы"),
        new(typeof(TransferOrder), typeof(StockTransfer), "executes", "1:n", "Фактическое перемещение исполняет заказ на перемещение.", "Логическая связь по предметной области"),
        new(typeof(WarehouseNode), typeof(StockTransfer), "route", "1:n", "Перемещение переводит запас между узлами хранения.", "По доменной модели, probe документа поврежден"),
        new(typeof(StockTransfer), typeof(StockMovementLine), "contains", "1:n", "Перемещение должно содержать строки движения.", "По доменной модели"),
        new(typeof(SalesOrder), typeof(StockReservationDocument), "reserves for", "1:n", "Резервирование работает от заказа покупателя.", "ЗаказПокупателя"),
        new(typeof(StockReservationDocument), typeof(StockMovementLine), "contains", "1:n", "Резервирование хранит строки запасов.", "ТЧ Запасы"),
        new(typeof(WarehouseNode), typeof(InventoryCount), "counts", "1:n", "Инвентаризация проводится по узлу хранения.", "СтруктурнаяЕдиница"),
        new(typeof(StorageBin), typeof(InventoryCount), "bin", "1:n", "Инвентаризация может быть ограничена ячейкой.", "Ячейка"),
        new(typeof(InventoryCount), typeof(InventoryCountLine), "contains", "1:n", "Документ инвентаризации содержит строки.", "ТЧ Запасы"),
        new(typeof(InventoryCount), typeof(StockWriteOff), "adjusts", "1:n", "Списание может быть создано по инвентаризации.", "ДокументОснование"),
        new(typeof(WarehouseNode), typeof(StockWriteOff), "writes off from", "1:n", "Списание работает по складу/подразделению.", "СтруктурнаяЕдиница"),
        new(typeof(StorageBin), typeof(StockWriteOff), "bin", "1:n", "Списание может быть привязано к ячейке.", "Ячейка"),
        new(typeof(PriceType), typeof(StockWriteOff), "valuation", "1:n", "Списание может хранить вид цены.", "ВидЦен"),
        new(typeof(StockWriteOff), typeof(CommercialDocumentLine), "contains", "1:n", "Списание содержит строки товаров.", "ТЧ Запасы"),
        new(typeof(NomenclatureItem), typeof(CommercialDocumentLine), "item", "1:n", "Строка документа ссылается на товар.", "Номенклатура"),
        new(typeof(NomenclatureItem), typeof(StockMovementLine), "item", "1:n", "Строка движения ссылается на товар.", "Номенклатура"),
        new(typeof(NomenclatureItem), typeof(InventoryCountLine), "item", "1:n", "Строка инвентаризации ссылается на товар.", "Номенклатура"),
        new(typeof(NomenclatureItem), typeof(StockBalance), "stock", "1:n", "Остаток хранится по товару.", "Остатки запасов"),
        new(typeof(WarehouseNode), typeof(StockBalance), "stock", "1:n", "Остаток хранится по складу/узлу.", "Остатки запасов"),
        new(typeof(StorageBin), typeof(StockBalance), "stock", "1:n", "Остаток может храниться по ячейке.", "Остатки запасов")
    ];

    public static EntityMetadata? FindEntity(Type? clrType) =>
        clrType is null ? null : Entities.FirstOrDefault(entity => entity.ClrType == clrType);

    public static DomainArea? FindArea(string areaKey) =>
        Areas.FirstOrDefault(area => area.Key == areaKey);

    public static IReadOnlyList<EntityMetadata> FilterEntities(string? areaKey, string? searchText)
    {
        IEnumerable<EntityMetadata> query = Entities;

        if (!string.IsNullOrWhiteSpace(areaKey))
        {
            query = query.Where(entity => entity.AreaKey == areaKey);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            query = query.Where(entity =>
                entity.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                entity.ClrType.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                entity.OneCSource.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderBy(entity => FindArea(entity.AreaKey)?.Order ?? int.MaxValue)
            .ThenBy(entity => entity.DisplayName)
            .ToArray();
    }

    public static IReadOnlyList<RelationshipMetadata> GetRelationshipsFor(Type? clrType)
    {
        if (clrType is null)
        {
            return Relationships;
        }

        return Relationships
            .Where(relationship => relationship.SourceType == clrType || relationship.TargetType == clrType)
            .ToArray();
    }
}
