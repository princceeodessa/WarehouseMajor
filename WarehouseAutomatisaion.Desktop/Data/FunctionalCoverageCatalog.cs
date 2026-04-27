using System.ComponentModel;
using System.Drawing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class FunctionalCoverageSnapshot
{
    private FunctionalCoverageSnapshot(
        IReadOnlyList<CoverageSummaryCard> summaryCards,
        IReadOnlyList<FunctionalModuleDefinition> modules)
    {
        SummaryCards = summaryCards;
        Modules = modules;
    }

    public IReadOnlyList<CoverageSummaryCard> SummaryCards { get; }

    public IReadOnlyList<FunctionalModuleDefinition> Modules { get; }

    public static FunctionalCoverageSnapshot Create()
    {
        return new FunctionalCoverageSnapshot(
            [
                new CoverageSummaryCard("Критические контуры", "5", "Продажи, закупки, склад, номенклатура и платформа.", Color.FromArgb(79, 174, 92)),
                new CoverageSummaryCard("Ключевые сценарии", "24", "То, без чего сотрудники не смогут отказаться от 1С.", Color.FromArgb(78, 160, 190)),
                new CoverageSummaryCard("Документные цепочки", "8", "Основания, статусы, проведение, резерв и приемка.", Color.FromArgb(201, 134, 64)),
                new CoverageSummaryCard("Обязательные сервисы", "6", "Права, поиск, печать, аудит, вложения и фильтры.", Color.FromArgb(123, 104, 163))
            ],
            [
                new FunctionalModuleDefinition(
                    "sales",
                    "Продажи",
                    "Менеджер по продажам должен полностью пройти путь от клиента до отгрузки без возврата в 1С.",
                    [
                        new FunctionalScenarioRow("Критично", "Покупатели", "Карточка клиента, договор, контакт, менеджер, валюта, статус.", "BusinessPartner, PartnerContract, PartnerContact", "Заложено в модели и в desktop-прототипе", "Сделать нормальную карточку клиента с поиском и фильтрами."),
                        new FunctionalScenarioRow("Критично", "Заказ покупателя", "Создание, редактирование, подтверждение, резерв, суммы, строки товара.", "SalesOrder, CommercialDocumentLine, PaymentScheduleLine", "Заложено в модели, в UI пока как таблица", "Реализовать форму документа и жизненный цикл статусов."),
                        new FunctionalScenarioRow("Критично", "Счет на оплату", "Выставление счета из заказа и вручную, срок оплаты, платежный календарь.", "SalesInvoice, PaymentScheduleLine", "Заложено в модели, в UI пока как таблица", "Сделать мастер создания счета из заказа."),
                        new FunctionalScenarioRow("Критично", "Расходная накладная", "Отгрузка с учетом остатков, склада, ячейки и перевозчика.", "SalesShipment, StockBalance, WarehouseNode, StorageBin", "Заложено в модели, в UI пока как таблица", "Добавить подбор остатков и сборку отгрузки."),
                        new FunctionalScenarioRow("Важно", "Возвраты и корректировки", "Корректировка реализации и возврат от клиента.", "Базовые документы продаж и обратные связи", "Пока не моделировано отдельно", "После базовой цепочки продаж добавить обратные документы.")
                    ]),
                new FunctionalModuleDefinition(
                    "purchasing",
                    "Закупки",
                    "Закупщик должен закрывать дефицит, принимать счета и оформлять приемку без 1С.",
                    [
                        new FunctionalScenarioRow("Критично", "Поставщики", "Карточка поставщика, договор, контакт, закупщик, валюта.", "BusinessPartner, PartnerContract, PartnerContact", "Заложено в модели и в desktop-прототипе", "Сделать единый справочник контрагентов с ролями."),
                        new FunctionalScenarioRow("Критично", "Заказ поставщику", "Заказ по дефициту и вручную, связь с потребностью и заказом клиента.", "PurchaseOrder, CommercialDocumentLine, PaymentScheduleLine", "Заложено в модели, в UI пока как таблица", "Сделать форму заказа поставщику и генерацию из дефицита."),
                        new FunctionalScenarioRow("Критично", "Счет поставщика", "Регистрация входящего счета и контроль сроков оплаты.", "SupplierInvoice, PaymentScheduleLine", "Заложено в модели, в UI пока как таблица", "Добавить сценарий регистрации и согласования счета."),
                        new FunctionalScenarioRow("Критично", "Приходная накладная", "Приемка товара, распределение по складу и дополнительные расходы.", "PurchaseReceipt, AdditionalChargeLine, WarehouseNode, StorageBin", "Заложено в модели, в UI пока как таблица", "Сделать форму приемки с распределением дополнительных расходов."),
                        new FunctionalScenarioRow("Важно", "Возвраты поставщику", "Обратный документ по приемке или счету поставщика.", "Базовые документы закупки и обратные связи", "Пока не моделировано отдельно", "Добавить после завершения основного цикла закупки.")
                    ]),
                new FunctionalModuleDefinition(
                    "warehouse",
                    "Склад",
                    "Складской сотрудник должен видеть остатки, резерв, перемещения и инвентаризацию в одном простом контуре.",
                    [
                        new FunctionalScenarioRow("Критично", "Склады и ячейки", "Иерархия складов, магазинов, зон и ячеек хранения.", "WarehouseNode, StorageBin", "Заложено в модели", "Сделать справочник мест хранения и адресное хранение."),
                        new FunctionalScenarioRow("Критично", "Остатки и доступность", "Текущий остаток, резерв, доступно к продаже, контроль дефицита.", "StockBalance", "Заложено в модели и в desktop-прототипе", "Подключить SQL-хранилище остатков и live-обновление."),
                        new FunctionalScenarioRow("Критично", "Заказ на перемещение", "План перемещения между складами и зонами.", "TransferOrder, StockMovementLine", "Заложено в модели, в UI пока как таблица", "Сделать документ планового перемещения."),
                        new FunctionalScenarioRow("Критично", "Фактическое перемещение", "Исполнение перемещения с источником, приемником и подтверждением.", "StockTransfer, StockMovementLine", "Заложено в модели", "Сделать рабочий экран сборки и подтверждения перемещения."),
                        new FunctionalScenarioRow("Критично", "Резервирование", "Перенос товара в резерв под заказ покупателя.", "StockReservationDocument, SalesOrder, StockMovementLine", "Заложено в модели и в desktop-прототипе", "Привязать резерв к заказу и доступности."),
                        new FunctionalScenarioRow("Критично", "Инвентаризация", "Пересчет факта против учета по складу и ячейке.", "InventoryCount, InventoryCountLine", "Заложено в модели и в desktop-прототипе", "Сделать документ пересчета и фиксации расхождений."),
                        new FunctionalScenarioRow("Критично", "Списание", "Списание боя, порчи, недостачи и технических потерь.", "StockWriteOff, CommercialDocumentLine", "Заложено в модели и в desktop-прототипе", "Сделать документ списания с основаниями и ценой."),
                        new FunctionalScenarioRow("Важно", "Штрихкоды и этикетки", "Поиск товара, маркировка ячеек, печать этикеток.", "NomenclatureItem, StorageBin", "Пока не моделировано отдельно", "Добавить после запуска базового склада.")
                    ]),
                new FunctionalModuleDefinition(
                    "catalog",
                    "Номенклатура и цены",
                    "Справочник товаров и ценообразование должны стать отдельным чистым контуром вместо разрозненных экранов 1С.",
                    [
                        new FunctionalScenarioRow("Критично", "Номенклатура", "Карточка товара, единица измерения, категория, поставщик, склад по умолчанию.", "NomenclatureItem, UnitOfMeasure, ItemCategory, BusinessPartner", "Заложено в модели и в desktop-прототипе", "Сделать полную карточку товара и массовый поиск."),
                        new FunctionalScenarioRow("Критично", "Виды цен", "Розница, опт, монтаж, закупка и базовые цены.", "PriceType, PriceTypeRoundingRule", "Заложено в модели и в desktop-прототипе", "Сделать настройку ценовых правил и округления."),
                        new FunctionalScenarioRow("Критично", "Установка цен", "Документ изменения цен по товару и виду цены.", "PriceRegistrationDocument, PriceRegistrationLine", "Заложено в модели", "Сделать форму документа и историю изменений."),
                        new FunctionalScenarioRow("Важно", "Скидки", "Автоматические скидки по партнерам, складам, категориям и ценовым группам.", "DiscountPolicy", "Заложено в модели и в desktop-прототипе", "Сделать мастер скидки и правила применения."),
                        new FunctionalScenarioRow("Важно", "Прайс-листы", "Быстрая выгрузка прайс-листа по видам цен и сегментам.", "PriceType, NomenclatureItem, DiscountPolicy", "Пока не реализовано", "После основного каталога добавить генератор прайс-листов.")
                    ]),
                new FunctionalModuleDefinition(
                    "platform",
                    "Платформенный контур",
                    "Это вещи, без которых сотрудники будут постоянно возвращаться в 1С даже при наличии документов.",
                    [
                        new FunctionalScenarioRow("Критично", "Цепочки документов", "Основание, родительский документ, переходы заказ -> счет -> отгрузка и заказ -> закупка -> приемка.", "BaseDocumentId и междокументные связи", "Частично заложено в модели", "Зафиксировать универсальный механизм document links."),
                        new FunctionalScenarioRow("Критично", "Статусы и проведение", "Черновик, подтвержден, проведен, отменен, завершен.", "LifecycleStatus, DocumentPostingState", "Заложено в модели", "Вынести в единый движок статусов документов."),
                        new FunctionalScenarioRow("Критично", "Права и рабочие места", "Разные экраны и разрешения для продаж, закупок, склада и админов.", "Пользователи, роли, UI-workspaces", "Пока не реализовано", "Спроектировать роли и матрицу доступа."),
                        new FunctionalScenarioRow("Важно", "Поиск, фильтры, очереди", "Быстрые списки, поиск по номеру, контрагенту, товару, сроку и статусу.", "Все реестры документов и справочников", "Есть только демо-списки", "Сделать общий list-view движок с фильтрами."),
                        new FunctionalScenarioRow("Важно", "Печатные формы и экспорт", "Счет, накладная, заказ, инвентаризация, выгрузки для бухгалтерии и склада.", "Документы продаж, закупок и склада", "Пока не реализовано", "Добавить layer генерации печатных форм."),
                        new FunctionalScenarioRow("Важно", "Аудит и вложения", "История изменений, сканы документов, комментарии и причины операций.", "Документы и журнал событий", "Пока не реализовано", "После запуска основных документов добавить audit trail и вложения.")
                    ])
            ]);
    }
}

public sealed record CoverageSummaryCard(string Title, string Value, string Hint, Color AccentColor);

public sealed record FunctionalModuleDefinition(
    string Key,
    string Title,
    string Goal,
    IReadOnlyList<FunctionalScenarioRow> Scenarios);

public sealed record FunctionalScenarioRow(
    [property: DisplayName("Приоритет")] string Priority,
    [property: DisplayName("Сценарий")] string Scenario,
    [property: DisplayName("Что переносим")] string Scope,
    [property: DisplayName("База и связи")] string RelatedObjects,
    [property: DisplayName("Статус")] string Status,
    [property: DisplayName("Следующий шаг")] string NextStep);
