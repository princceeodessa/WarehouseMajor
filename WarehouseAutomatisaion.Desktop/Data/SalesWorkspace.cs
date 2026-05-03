using System.ComponentModel;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class SalesWorkspace
{
    private SalesWorkspace(
        BindingList<SalesCustomerRecord> customers,
        BindingList<SalesOrderRecord> orders,
        BindingList<SalesInvoiceRecord> invoices,
        BindingList<SalesShipmentRecord> shipments,
        BindingList<SalesReturnRecord> returns,
        BindingList<SalesCashReceiptRecord> cashReceipts,
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        IReadOnlyList<string> customerStatuses,
        IReadOnlyList<string> orderStatuses,
        IReadOnlyList<string> invoiceStatuses,
        IReadOnlyList<string> shipmentStatuses,
        IReadOnlyList<string> managers,
        IReadOnlyList<string> currencies,
        IReadOnlyList<string> warehouses)
    {
        Customers = customers;
        Orders = orders;
        Invoices = invoices;
        Shipments = shipments;
        Returns = returns;
        CashReceipts = cashReceipts;
        CatalogItems = catalogItems;
        CustomerStatuses = customerStatuses;
        OrderStatuses = orderStatuses;
        InvoiceStatuses = invoiceStatuses;
        ShipmentStatuses = shipmentStatuses;
        Managers = managers;
        Currencies = currencies;
        Warehouses = warehouses;
    }

    public BindingList<SalesCustomerRecord> Customers { get; }

    public BindingList<SalesOrderRecord> Orders { get; }

    public BindingList<SalesInvoiceRecord> Invoices { get; }

    public BindingList<SalesShipmentRecord> Shipments { get; }

    public BindingList<SalesReturnRecord> Returns { get; }

    public BindingList<SalesCashReceiptRecord> CashReceipts { get; }

    public IReadOnlyList<SalesCatalogItemOption> CatalogItems { get; internal set; }

    public IReadOnlyList<string> CustomerStatuses { get; }

    public IReadOnlyList<string> OrderStatuses { get; }

    public IReadOnlyList<string> InvoiceStatuses { get; }

    public IReadOnlyList<string> ShipmentStatuses { get; }

    public IReadOnlyList<string> Managers { get; internal set; }

    public IReadOnlyList<string> Currencies { get; internal set; }

    public IReadOnlyList<string> Warehouses { get; internal set; }

    public OneCImportSnapshot? OneCImport { get; private set; }

    public DesktopOperationalSnapshot? OperationalSnapshot { get; private set; }

    public BindingList<SalesOperationLogEntry> OperationLog { get; } = new();

    public string CurrentOperator { get; internal set; } = string.Empty;

    public event EventHandler? Changed;

    public void AttachOneCImportSnapshot(OneCImportSnapshot? snapshot)
    {
        OneCImport = snapshot;
    }

    public void AttachOperationalSnapshot(DesktopOperationalSnapshot? snapshot)
    {
        OperationalSnapshot = snapshot;
    }

    public void NotifyExternalChange()
    {
        OnChanged();
    }

    public void ReplaceFrom(SalesWorkspace source)
    {
        ReplaceBindingList(Customers, source.Customers, item => item.Clone());
        ReplaceBindingList(Orders, source.Orders, item => item.Clone());
        ReplaceBindingList(Invoices, source.Invoices, item => item.Clone());
        ReplaceBindingList(Shipments, source.Shipments, item => item.Clone());
        ReplaceBindingList(Returns, source.Returns, item => item.Clone());
        ReplaceBindingList(CashReceipts, source.CashReceipts, item => item.Clone());

        CatalogItems = source.CatalogItems.Select(item => item with { }).ToArray();
        Managers = source.Managers.ToArray();
        Currencies = source.Currencies.ToArray();
        Warehouses = source.Warehouses.ToArray();
        OneCImport = source.OneCImport;
        OperationalSnapshot = source.OperationalSnapshot;
        CurrentOperator = source.CurrentOperator;
        ReplaceBindingList(OperationLog, source.OperationLog, item => item.Clone());

        OnChanged();
    }

    public static SalesWorkspace Create(string currentOperator)
    {
        var managers = new[]
        {
            "Ирина Киселева",
            "Антон Мельников",
            "Ольга Соколова",
            currentOperator
        };

        var customerStatuses = new[]
        {
            "Активен",
            "На проверке",
            "Пауза"
        };

        var orderStatuses = new[]
        {
            "План",
            "Подтвержден",
            "В резерве",
            "Готов к отгрузке",
            "Закрыт"
        };

        var invoiceStatuses = new[]
        {
            "Черновик",
            "Выставлен",
            "Ожидает оплату",
            "Оплачен"
        };

        var shipmentStatuses = new[]
        {
            "Черновик",
            "К сборке",
            "Готова к отгрузке",
            "Отгружена"
        };

        var currencies = new[]
        {
            "RUB",
            "USD"
        };

        var warehouses = new[]
        {
            "Главный склад",
            "Шоурум",
            "Монтажный склад"
        };

        var catalogItems = new[]
        {
            new SalesCatalogItemOption("ALTEZA-P50-BL", "ALTEZA профиль P-50 гардина черный мат", "м", 840m),
            new SalesCatalogItemOption("LUM-CLAMP-50", "Профиль LumFer Clamp Level 50", "м", 1_180m),
            new SalesCatalogItemOption("SCREEN-30", "Экран световой SCREEN 30 белый", "м", 2_450m),
            new SalesCatalogItemOption("GX53-BASE", "Платформа GX-53 белая", "шт", 380m),
            new SalesCatalogItemOption("KLEM-2X", "Клеммы 2-контактные", "шт", 22m)
        };

        var customers = new BindingList<SalesCustomerRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "C-001",
                Name = "ООО Атриум Дизайн",
                CounterpartyType = "Юридическое лицо",
                IsBuyer = true,
                IsSupplier = false,
                IsOther = false,
                ContractNumber = "AT-24/11",
                CurrencyCode = "RUB",
                Manager = "Ирина Киселева",
                Status = "Активен",
                Phone = "+7 (937) 333-10-20",
                Email = "atrium@major-flow.local",
                Inn = "6312001001",
                Kpp = "631201001",
                Ogrn = "1246300001010",
                LegalAddress = "443099, Самарская область, г. Самара, ул. Ленинградская, 12",
                ActualAddress = "443099, Самара, ул. Ленинградская, 12",
                Region = "Самарская область",
                City = "Самара",
                Source = "Рекомендация",
                Responsible = "Ирина Киселева",
                Tags = "дизайн, поэтапная отгрузка",
                BankAccount = "40702810900000001001",
                Notes = "Дизайн-студия. Часто просит отгрузку поэтапно.",
                Contacts = new BindingList<SalesCustomerContactRecord>
                {
                    new() { Name = "Елена Орлова", Role = "Закупки", Phone = "+7 (937) 333-10-20", Email = "atrium@major-flow.local", Comment = "Основной контакт" }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "C-002",
                Name = "Студия Линия Света",
                CounterpartyType = "Юридическое лицо",
                IsBuyer = true,
                IsSupplier = false,
                IsOther = false,
                ContractNumber = "LS-25/02",
                CurrencyCode = "RUB",
                Manager = "Антон Мельников",
                Status = "Активен",
                Phone = "+7 (917) 777-44-11",
                Email = "linea@major-flow.local",
                Inn = "6312001002",
                Kpp = "631201002",
                Ogrn = "1246300001020",
                LegalAddress = "443010, Самарская область, г. Самара, ул. Молодогвардейская, 45",
                ActualAddress = "443010, Самара, ул. Молодогвардейская, 45",
                Region = "Самарская область",
                City = "Самара",
                Source = "Сайт",
                Responsible = "Антон Мельников",
                Tags = "быстрый счет, монтаж",
                BankAccount = "40702810900000001002",
                Notes = "Быстрые сделки, любят счет в день заказа.",
                Contacts = new BindingList<SalesCustomerContactRecord>
                {
                    new() { Name = "Мария Белова", Role = "Руководитель проекта", Phone = "+7 (917) 777-44-11", Email = "linea@major-flow.local", Comment = "Согласует счета" }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "C-003",
                Name = "ИП Мир Потолков",
                CounterpartyType = "Индивидуальный предприниматель",
                IsBuyer = true,
                IsSupplier = true,
                IsOther = false,
                ContractNumber = "MP-25/01",
                CurrencyCode = "RUB",
                Manager = "Ирина Киселева",
                Status = "Активен",
                Phone = "+7 (927) 444-12-33",
                Email = "potolok@major-flow.local",
                Inn = "631200100303",
                Ogrn = "324630000103001",
                LegalAddress = "450000, Республика Башкортостан, г. Уфа, ул. Коммунистическая, 8",
                ActualAddress = "450000, Уфа, ул. Коммунистическая, 8",
                Region = "Республика Башкортостан",
                City = "Уфа",
                Source = "Повторные продажи",
                Responsible = "Ирина Киселева",
                Tags = "монтаж, постоянный клиент",
                BankAccount = "40802810900000001003",
                Notes = "Регулярные монтажные заказы.",
                Contacts = new BindingList<SalesCustomerContactRecord>
                {
                    new() { Name = "Алексей Потолков", Role = "ИП / владелец", Phone = "+7 (927) 444-12-33", Email = "potolok@major-flow.local", Comment = "Решает по оплате и отгрузке" },
                    new() { Name = "Сергей", Role = "Монтаж", Phone = "+7 (927) 444-12-34", Email = "", Comment = "Принимает товар на объекте" }
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "C-004",
                Name = "ООО Периметр Девелопмент",
                CounterpartyType = "Юридическое лицо",
                IsBuyer = true,
                IsSupplier = false,
                IsOther = false,
                ContractNumber = "PD-25/03",
                CurrencyCode = "RUB",
                Manager = "Антон Мельников",
                Status = "На проверке",
                Phone = "+7 (8452) 40-12-54",
                Email = "perimeter@major-flow.local",
                Inn = "6452001004",
                Kpp = "645201004",
                Ogrn = "1246400001040",
                LegalAddress = "410012, Саратовская область, г. Саратов, ул. Вольская, 31",
                ActualAddress = "410012, Саратов, ул. Вольская, 31",
                Region = "Саратовская область",
                City = "Саратов",
                Source = "Холодный звонок",
                Responsible = "Антон Мельников",
                Tags = "девелопмент, проверка лимита",
                BankAccount = "40702810900000001004",
                Notes = "Нужна предварительная верификация лимита.",
                Contacts = new BindingList<SalesCustomerContactRecord>
                {
                    new() { Name = "Дмитрий Романов", Role = "Снабжение", Phone = "+7 (8452) 40-12-54", Email = "perimeter@major-flow.local", Comment = "Ждет проверку лимита" }
                }
            }
        };

        var customerByCode = customers.ToDictionary(customer => customer.Code, StringComparer.OrdinalIgnoreCase);

        var orders = new BindingList<SalesOrderRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Number = "SO-260323-001",
                OrderDate = new DateTime(2026, 3, 21),
                CustomerId = customerByCode["C-001"].Id,
                CustomerCode = customerByCode["C-001"].Code,
                CustomerName = customerByCode["C-001"].Name,
                ContractNumber = customerByCode["C-001"].ContractNumber,
                CurrencyCode = customerByCode["C-001"].CurrencyCode,
                Warehouse = "Главный склад",
                Status = "В резерве",
                Manager = "Ирина Киселева",
                Comment = "Собрать в одну отгрузку к концу недели.",
                Lines = CloneLines(
                [
                    new() { Id = Guid.NewGuid(), ItemCode = "ALTEZA-P50-BL", ItemName = "ALTEZA профиль P-50 гардина черный мат", Unit = "м", Quantity = 120m, Price = 840m },
                    new() { Id = Guid.NewGuid(), ItemCode = "GX53-BASE", ItemName = "Платформа GX-53 белая", Unit = "шт", Quantity = 120m, Price = 380m }
                ])
            },
            new()
            {
                Id = Guid.NewGuid(),
                Number = "SO-260323-002",
                OrderDate = new DateTime(2026, 3, 22),
                CustomerId = customerByCode["C-002"].Id,
                CustomerCode = customerByCode["C-002"].Code,
                CustomerName = customerByCode["C-002"].Name,
                ContractNumber = customerByCode["C-002"].ContractNumber,
                CurrencyCode = customerByCode["C-002"].CurrencyCode,
                Warehouse = "Шоурум",
                Status = "Подтвержден",
                Manager = "Антон Мельников",
                Comment = "Нужна отгрузка первой части завтра утром.",
                Lines = CloneLines(
                [
                    new() { Id = Guid.NewGuid(), ItemCode = "SCREEN-30", ItemName = "Экран световой SCREEN 30 белый", Unit = "м", Quantity = 18m, Price = 2_450m },
                    new() { Id = Guid.NewGuid(), ItemCode = "LUM-CLAMP-50", ItemName = "Профиль LumFer Clamp Level 50", Unit = "м", Quantity = 20m, Price = 1_180m }
                ])
            },
            new()
            {
                Id = Guid.NewGuid(),
                Number = "SO-260323-003",
                OrderDate = new DateTime(2026, 3, 23),
                CustomerId = customerByCode["C-003"].Id,
                CustomerCode = customerByCode["C-003"].Code,
                CustomerName = customerByCode["C-003"].Name,
                ContractNumber = customerByCode["C-003"].ContractNumber,
                CurrencyCode = customerByCode["C-003"].CurrencyCode,
                Warehouse = "Монтажный склад",
                Status = "План",
                Manager = "Ирина Киселева",
                Comment = "Подготовить резерв после подтверждения аванса.",
                Lines = CloneLines(
                [
                    new() { Id = Guid.NewGuid(), ItemCode = "GX53-BASE", ItemName = "Платформа GX-53 белая", Unit = "шт", Quantity = 36m, Price = 380m },
                    new() { Id = Guid.NewGuid(), ItemCode = "KLEM-2X", ItemName = "Клеммы 2-контактные", Unit = "шт", Quantity = 480m, Price = 22m }
                ])
            }
        };

        var orderByNumber = orders.ToDictionary(order => order.Number, StringComparer.OrdinalIgnoreCase);

        var invoices = new BindingList<SalesInvoiceRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Number = "INV-260323-001",
                InvoiceDate = new DateTime(2026, 3, 22),
                DueDate = new DateTime(2026, 3, 25),
                SalesOrderId = orderByNumber["SO-260323-002"].Id,
                SalesOrderNumber = orderByNumber["SO-260323-002"].Number,
                CustomerId = orderByNumber["SO-260323-002"].CustomerId,
                CustomerCode = orderByNumber["SO-260323-002"].CustomerCode,
                CustomerName = orderByNumber["SO-260323-002"].CustomerName,
                ContractNumber = orderByNumber["SO-260323-002"].ContractNumber,
                CurrencyCode = orderByNumber["SO-260323-002"].CurrencyCode,
                Status = "Выставлен",
                Manager = orderByNumber["SO-260323-002"].Manager,
                Comment = "Выставлен в день подтверждения заказа.",
                Lines = CloneLines(orderByNumber["SO-260323-002"].Lines)
            }
        };

        var shipments = new BindingList<SalesShipmentRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Number = "SH-260323-001",
                ShipmentDate = new DateTime(2026, 3, 23),
                SalesOrderId = orderByNumber["SO-260323-002"].Id,
                SalesOrderNumber = orderByNumber["SO-260323-002"].Number,
                CustomerId = orderByNumber["SO-260323-002"].CustomerId,
                CustomerCode = orderByNumber["SO-260323-002"].CustomerCode,
                CustomerName = orderByNumber["SO-260323-002"].CustomerName,
                ContractNumber = orderByNumber["SO-260323-002"].ContractNumber,
                CurrencyCode = orderByNumber["SO-260323-002"].CurrencyCode,
                Warehouse = orderByNumber["SO-260323-002"].Warehouse,
                Status = "Черновик",
                Carrier = "Собственный транспорт",
                Manager = orderByNumber["SO-260323-002"].Manager,
                Comment = "Подготовлен черновик расходной накладной.",
                Lines = CloneLines(orderByNumber["SO-260323-002"].Lines)
            }
        };

        var returns = new BindingList<SalesReturnRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Number = "RET-260324-001",
                ReturnDate = new DateTime(2026, 3, 24),
                SalesOrderId = orderByNumber["SO-260323-002"].Id,
                SalesOrderNumber = orderByNumber["SO-260323-002"].Number,
                CustomerId = orderByNumber["SO-260323-002"].CustomerId,
                CustomerCode = orderByNumber["SO-260323-002"].CustomerCode,
                CustomerName = orderByNumber["SO-260323-002"].CustomerName,
                ContractNumber = orderByNumber["SO-260323-002"].ContractNumber,
                CurrencyCode = orderByNumber["SO-260323-002"].CurrencyCode,
                Warehouse = orderByNumber["SO-260323-002"].Warehouse,
                Status = "Принят",
                Manager = orderByNumber["SO-260323-002"].Manager,
                Reason = "Возврат части товара",
                Comment = "Клиент вернул часть позиции после сверки.",
                Lines = CloneLines(
                [
                    new() { Id = Guid.NewGuid(), ItemCode = "SCREEN-30", ItemName = "Экран световой SCREEN 30 белый", Unit = "м", Quantity = 2m, Price = 2_450m }
                ])
            }
        };

        var cashReceipts = new BindingList<SalesCashReceiptRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Number = "CASH-260324-001",
                ReceiptDate = new DateTime(2026, 3, 24),
                SalesOrderId = orderByNumber["SO-260323-002"].Id,
                SalesOrderNumber = orderByNumber["SO-260323-002"].Number,
                CustomerId = orderByNumber["SO-260323-002"].CustomerId,
                CustomerCode = orderByNumber["SO-260323-002"].CustomerCode,
                CustomerName = orderByNumber["SO-260323-002"].CustomerName,
                ContractNumber = orderByNumber["SO-260323-002"].ContractNumber,
                CurrencyCode = orderByNumber["SO-260323-002"].CurrencyCode,
                Amount = orderByNumber["SO-260323-002"].TotalAmount,
                Status = "Проведено",
                CashBox = "Основная касса",
                Manager = orderByNumber["SO-260323-002"].Manager,
                Comment = "Оплата по заказу покупателя."
            }
        };

        return new SalesWorkspace(
            customers,
            orders,
            invoices,
            shipments,
            returns,
            cashReceipts,
            catalogItems,
            customerStatuses,
            orderStatuses,
            invoiceStatuses,
            shipmentStatuses,
            managers,
            currencies,
            warehouses)
        {
            CurrentOperator = currentOperator
        };
    }

    public SalesCustomerRecord CreateCustomerDraft()
    {
        return new SalesCustomerRecord
        {
            Id = Guid.NewGuid(),
            Code = GetNextCustomerCode(),
            CurrencyCode = Currencies.First(),
            Manager = Managers.First(),
            Status = CustomerStatuses.First()
        };
    }

    public SalesOrderRecord CreateOrderDraft(Guid? customerId = null)
    {
        var customer = customerId is null
            ? Customers.FirstOrDefault()
            : Customers.FirstOrDefault(item => item.Id == customerId.Value);

        return new SalesOrderRecord
        {
            Id = Guid.NewGuid(),
            Number = GetNextOrderNumber(),
            OrderDate = DateTime.Today,
            CustomerId = customer?.Id ?? Guid.Empty,
            CustomerCode = customer?.Code ?? string.Empty,
            CustomerName = customer?.Name ?? string.Empty,
            ContractNumber = customer?.ContractNumber ?? string.Empty,
            CurrencyCode = customer?.CurrencyCode ?? Currencies.First(),
            Warehouse = Warehouses.First(),
            Status = OrderStatuses.First(),
            Manager = customer?.Manager ?? Managers.First(),
            Lines = new BindingList<SalesOrderLineRecord>()
        };
    }

    public SalesInvoiceRecord CreateInvoiceDraftFromOrder(Guid orderId)
    {
        var order = Orders.First(item => item.Id == orderId);

        return new SalesInvoiceRecord
        {
            Id = Guid.NewGuid(),
            Number = GetNextInvoiceNumber(),
            InvoiceDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(3),
            SalesOrderId = order.Id,
            SalesOrderNumber = order.Number,
            CustomerId = order.CustomerId,
            CustomerCode = order.CustomerCode,
            CustomerName = order.CustomerName,
            ContractNumber = order.ContractNumber,
            CurrencyCode = order.CurrencyCode,
            Status = InvoiceStatuses.First(),
            Manager = order.Manager,
            Comment = $"Основание: заказ {order.Number}",
            Lines = CloneLines(order.Lines)
        };
    }

    public SalesShipmentRecord CreateShipmentDraftFromOrder(Guid orderId)
    {
        var order = Orders.First(item => item.Id == orderId);

        return new SalesShipmentRecord
        {
            Id = Guid.NewGuid(),
            Number = GetNextShipmentNumber(),
            ShipmentDate = DateTime.Today,
            SalesOrderId = order.Id,
            SalesOrderNumber = order.Number,
            CustomerId = order.CustomerId,
            CustomerCode = order.CustomerCode,
            CustomerName = order.CustomerName,
            ContractNumber = order.ContractNumber,
            CurrencyCode = order.CurrencyCode,
            Warehouse = order.Warehouse,
            Status = ShipmentStatuses.First(),
            Carrier = "Собственный транспорт",
            Manager = order.Manager,
            Comment = $"Подготовлено из заказа {order.Number}",
            Lines = CloneLines(order.Lines)
        };
    }

    public SalesCashReceiptRecord CreateCashReceiptDraftFromOrder(Guid orderId, decimal? amount = null)
    {
        var order = Orders.First(item => item.Id == orderId);

        return new SalesCashReceiptRecord
        {
            Id = Guid.NewGuid(),
            Number = GetNextCashReceiptNumber(),
            ReceiptDate = DateTime.Today,
            SalesOrderId = order.Id,
            SalesOrderNumber = order.Number,
            CustomerId = order.CustomerId,
            CustomerCode = order.CustomerCode,
            CustomerName = order.CustomerName,
            ContractNumber = order.ContractNumber,
            CurrencyCode = order.CurrencyCode,
            Amount = amount ?? order.TotalAmount,
            Status = "Проведено",
            CashBox = "Основная касса",
            Manager = order.Manager,
            Comment = $"Оплата по заказу {order.Number}"
        };
    }

    public void AddCustomer(SalesCustomerRecord customer)
    {
        var copy = customer.Clone();
        Customers.Add(copy);
        WriteOperationLog("Покупатель", copy.Id, copy.Code, "Создание карточки", "Успех", $"Создан покупатель {copy.Name}.");
        OnChanged();
    }

    public void UpdateCustomer(SalesCustomerRecord customer)
    {
        var existing = Customers.First(item => item.Id == customer.Id);
        existing.CopyFrom(customer);
        SyncDocumentsForCustomer(existing);
        WriteOperationLog("Покупатель", existing.Id, existing.Code, "Изменение карточки", "Успех", $"Обновлена карточка {existing.Name}.");
        OnChanged();
    }

    public void AddOrder(SalesOrderRecord order)
    {
        var copy = order.Clone();
        if (string.IsNullOrWhiteSpace(copy.Status))
        {
            copy.Status = OrderStatuses.First();
        }

        ValidateOrderForPersist(copy);
        Orders.Add(copy);
        RefreshOrderLifecycle(copy.Id);
        WriteOperationLog("Заказ", copy.Id, copy.Number, "Создание заказа", "Успех", $"Создан заказ для {copy.CustomerName} на сумму {copy.TotalAmount:N2}.");
        OnChanged();
    }

    public void UpdateOrder(SalesOrderRecord order)
    {
        var existing = Orders.First(item => item.Id == order.Id);
        ValidateOrderForPersist(order);
        existing.CopyFrom(order);
        SyncDerivedDocumentsFromOrder(existing);
        RefreshOrderLifecycle(existing.Id);
        WriteOperationLog("Заказ", existing.Id, existing.Number, "Изменение заказа", "Успех", $"Обновлен заказ {existing.Number}.");
        OnChanged();
    }

    public void AddInvoice(SalesInvoiceRecord invoice)
    {
        var copy = invoice.Clone();
        if (string.IsNullOrWhiteSpace(copy.Status))
        {
            copy.Status = InvoiceStatuses.First();
        }

        ValidateInvoiceForPersist(copy);
        Invoices.Add(copy);
        RefreshOrderLifecycle(copy.SalesOrderId);
        WriteOperationLog("Счет", copy.Id, copy.Number, "Создание счета", "Успех", $"Создан счет {copy.Number} по заказу {copy.SalesOrderNumber}.");
        OnChanged();
    }

    public void UpdateInvoice(SalesInvoiceRecord invoice)
    {
        var existing = Invoices.First(item => item.Id == invoice.Id);
        ValidateInvoiceForPersist(invoice);
        existing.CopyFrom(invoice);
        RefreshOrderLifecycle(existing.SalesOrderId);
        WriteOperationLog("Счет", existing.Id, existing.Number, "Изменение счета", "Успех", $"Обновлен счет {existing.Number}.");
        OnChanged();
    }

    public void AddShipment(SalesShipmentRecord shipment)
    {
        var copy = shipment.Clone();
        if (string.IsNullOrWhiteSpace(copy.Status))
        {
            copy.Status = ShipmentStatuses.First();
        }

        ValidateShipmentForPersist(copy);
        Shipments.Add(copy);
        RefreshOrderLifecycle(copy.SalesOrderId);
        WriteOperationLog("Отгрузка", copy.Id, copy.Number, "Создание отгрузки", "Успех", $"Создана отгрузка {copy.Number} по заказу {copy.SalesOrderNumber}.");
        OnChanged();
    }

    public void UpdateShipment(SalesShipmentRecord shipment)
    {
        var existing = Shipments.First(item => item.Id == shipment.Id);
        ValidateShipmentForPersist(shipment);
        existing.CopyFrom(shipment);
        RefreshOrderLifecycle(existing.SalesOrderId);
        WriteOperationLog("Отгрузка", existing.Id, existing.Number, "Изменение отгрузки", "Успех", $"Обновлена отгрузка {existing.Number}.");
        OnChanged();
    }

    public void AddReturn(SalesReturnRecord returnDocument)
    {
        var copy = returnDocument.Clone();
        if (string.IsNullOrWhiteSpace(copy.Status))
        {
            copy.Status = "Черновик";
        }

        Returns.Add(copy);
        WriteOperationLog("Возврат", copy.Id, copy.Number, "Создание возврата", "Успех", $"Создан возврат {copy.Number} по заказу {copy.SalesOrderNumber}.");
        OnChanged();
    }

    public void UpdateReturn(SalesReturnRecord returnDocument)
    {
        var existing = Returns.First(item => item.Id == returnDocument.Id);
        existing.CopyFrom(returnDocument);
        WriteOperationLog("Возврат", existing.Id, existing.Number, "Изменение возврата", "Успех", $"Обновлен возврат {existing.Number}.");
        OnChanged();
    }

    public void AddCashReceipt(SalesCashReceiptRecord cashReceipt)
    {
        var copy = cashReceipt.Clone();
        if (string.IsNullOrWhiteSpace(copy.Status))
        {
            copy.Status = "Проведено";
        }

        CashReceipts.Add(copy);
        WriteOperationLog("Поступление в кассу", copy.Id, copy.Number, "Создание оплаты", "Успех", $"Создано поступление {copy.Number} по заказу {copy.SalesOrderNumber}.");
        OnChanged();
    }

    public void UpdateCashReceipt(SalesCashReceiptRecord cashReceipt)
    {
        var existing = CashReceipts.First(item => item.Id == cashReceipt.Id);
        existing.CopyFrom(cashReceipt);
        WriteOperationLog("Поступление в кассу", existing.Id, existing.Number, "Изменение оплаты", "Успех", $"Обновлено поступление {existing.Number}.");
        OnChanged();
    }

    public SalesOrderRecord DuplicateOrder(Guid orderId)
    {
        var source = Orders.First(item => item.Id == orderId);
        var copy = source.Clone();
        copy.Id = Guid.NewGuid();
        copy.Number = GetNextOrderNumber();
        copy.OrderDate = DateTime.Today;
        copy.Status = OrderStatuses.First();
        Orders.Add(copy);
        WriteOperationLog("Заказ", copy.Id, copy.Number, "Дублирование заказа", "Успех", $"Создан дубликат на основе {source.Number}.");
        OnChanged();
        return copy.Clone();
    }

    public SalesWorkflowActionResult ConfirmOrder(Guid orderId)
    {
        var order = Orders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return CreateWorkflowResult(false, "Заказ не найден.", "Не удалось подтвердить заказ.");
        }

        if (order.Status.Equals("Подтвержден", StringComparison.OrdinalIgnoreCase)
            || order.Status.Equals("В резерве", StringComparison.OrdinalIgnoreCase)
            || order.Status.Equals("Готов к отгрузке", StringComparison.OrdinalIgnoreCase)
            || order.Status.Equals("Отгружен", StringComparison.OrdinalIgnoreCase)
            || order.Status.Equals("Закрыт", StringComparison.OrdinalIgnoreCase))
        {
            return CreateWorkflowResult(true, $"Заказ {order.Number} уже подтвержден в рабочем контуре.", "Повторное подтверждение не требуется.");
        }

        order.Status = "Подтвержден";
        WriteOperationLog("Заказ", order.Id, order.Number, "Подтверждение заказа", "Успех", $"Заказ {order.Number} подтвержден.");
        OnChanged();
        return CreateWorkflowResult(true, $"Заказ {order.Number} подтвержден.", "Теперь по нему можно резервировать товар и выставлять счет.");
    }

    public SalesWorkflowActionResult ReserveOrder(Guid orderId)
    {
        var order = Orders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return CreateWorkflowResult(false, "Заказ не найден.", "Не удалось поставить заказ в резерв.");
        }

        var inventory = new SalesInventoryService(this);
        var check = inventory.AnalyzeOrder(order);
        if (!check.IsFullyCovered)
        {
            WriteOperationLog("Заказ", order.Id, order.Number, "Резервирование", "Ошибка", check.HintText);
            return CreateWorkflowResult(false, $"Не удалось зарезервировать заказ {order.Number}.", check.HintText);
        }

        order.Status = "В резерве";
        WriteOperationLog("Заказ", order.Id, order.Number, "Резервирование", "Успех", $"Заказ {order.Number} поставлен в резерв.");
        OnChanged();
        return CreateWorkflowResult(true, $"Заказ {order.Number} поставлен в резерв.", check.HintText);
    }

    public SalesWorkflowActionResult ReleaseOrderReserve(Guid orderId)
    {
        var order = Orders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return CreateWorkflowResult(false, "Заказ не найден.", "Не удалось снять резерв.");
        }

        if (!order.Status.Equals("В резерве", StringComparison.OrdinalIgnoreCase))
        {
            return CreateWorkflowResult(true, $"Заказ {order.Number} не находится в резерве.", "Снимать резерв не требуется.");
        }

        order.Status = "Подтвержден";
        WriteOperationLog("Заказ", order.Id, order.Number, "Снятие резерва", "Успех", $"Резерв по заказу {order.Number} снят.");
        OnChanged();
        return CreateWorkflowResult(true, $"Резерв по заказу {order.Number} снят.", "Заказ возвращен в подтвержденное состояние.");
    }

    public SalesWorkflowActionResult ConductExpenseAndCloseOrder(Guid orderId)
    {
        var order = Orders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return CreateWorkflowResult(false, "Заказ не найден.", "Не удалось провести расходную накладную.");
        }

        var shipment = Shipments
            .Where(item => item.SalesOrderId == order.Id)
            .OrderByDescending(item => item.ShipmentDate)
            .FirstOrDefault();
        var createdShipment = false;
        if (shipment is null)
        {
            shipment = CreateShipmentDraftFromOrder(order.Id);
            createdShipment = true;
        }

        var inventory = new SalesInventoryService(this);
        var check = inventory.AnalyzeShipment(shipment);
        if (!check.IsFullyCovered)
        {
            WriteOperationLog("Заказ", order.Id, order.Number, "Проведение расходной", "Ошибка", check.HintText);
            return CreateWorkflowResult(false, $"Не удалось провести расходную по заказу {order.Number}.", check.HintText);
        }

        if (createdShipment)
        {
            Shipments.Add(shipment);
            WriteOperationLog("Отгрузка", shipment.Id, shipment.Number, "Создание расходной", "Успех", $"Создана расходная накладная по заказу {order.Number}.");
        }

        shipment.Status = "Отгружена";
        order.Status = "Закрыт";
        WriteOperationLog("Отгрузка", shipment.Id, shipment.Number, "Проведение расходной", "Успех", $"Расходная {shipment.Number} проведена.");
        WriteOperationLog("Заказ", order.Id, order.Number, "Завершение заказа", "Успех", $"Заказ {order.Number} закрыт после проведения расходной.");
        OnChanged();

        var detail = createdShipment
            ? $"Создана и проведена расходная накладная {shipment.Number}. Заказ переведен в статус 'Закрыт'."
            : $"Проведена расходная накладная {shipment.Number}. Заказ переведен в статус 'Закрыт'.";
        return CreateWorkflowResult(true, $"Расходная по заказу {order.Number} проведена.", detail);
    }

    public SalesWorkflowActionResult RecordCashReceiptForOrder(Guid orderId)
    {
        var order = Orders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return CreateWorkflowResult(false, "Заказ не найден.", "Не удалось создать поступление в кассу.");
        }

        var receivedAmount = CashReceipts
            .Where(item => item.SalesOrderId == order.Id)
            .Sum(item => item.Amount);
        var remainingAmount = Math.Round(order.TotalAmount - receivedAmount, 2, MidpointRounding.AwayFromZero);
        if (remainingAmount <= 0m)
        {
            return CreateWorkflowResult(true, $"Заказ {order.Number} уже оплачен через кассу.", $"Связанные поступления: {CashReceipts.Count(item => item.SalesOrderId == order.Id):N0}.");
        }

        var receipt = CreateCashReceiptDraftFromOrder(order.Id, remainingAmount);
        CashReceipts.Add(receipt);

        foreach (var invoice in Invoices.Where(item => item.SalesOrderId == order.Id))
        {
            invoice.Status = receivedAmount + remainingAmount >= invoice.TotalAmount
                ? "Оплачен"
                : "Частично оплачен";
        }

        var hasShippedExpense = Shipments.Any(item =>
            item.SalesOrderId == order.Id
            && item.Status.Equals("Отгружена", StringComparison.OrdinalIgnoreCase));
        if (hasShippedExpense && receivedAmount + remainingAmount >= order.TotalAmount)
        {
            order.Status = "Закрыт";
        }
        else if (order.Status.Equals("План", StringComparison.OrdinalIgnoreCase))
        {
            order.Status = "Подтвержден";
        }

        WriteOperationLog("Поступление в кассу", receipt.Id, receipt.Number, "Поступление оплаты", "Успех", $"Оплата {receipt.Amount:N2} {receipt.CurrencyCode} по заказу {order.Number}.");
        OnChanged();
        return CreateWorkflowResult(true, $"Создано поступление в кассу {receipt.Number}.", $"Сумма: {receipt.Amount:N2} {receipt.CurrencyCode}. Связано с заказом {order.Number}.");
    }

    public SalesWorkflowActionResult MarkInvoiceIssued(Guid invoiceId)
    {
        var invoice = Invoices.FirstOrDefault(item => item.Id == invoiceId);
        if (invoice is null)
        {
            return CreateWorkflowResult(false, "Счет не найден.", "Не удалось выставить счет.");
        }

        invoice.Status = "Выставлен";
        RefreshOrderLifecycle(invoice.SalesOrderId);
        WriteOperationLog("Счет", invoice.Id, invoice.Number, "Выставление счета", "Успех", $"Счет {invoice.Number} выставлен.");
        OnChanged();
        return CreateWorkflowResult(true, $"Счет {invoice.Number} выставлен.", $"Счет связан с заказом {invoice.SalesOrderNumber}.");
    }

    public SalesWorkflowActionResult MarkInvoicePaid(Guid invoiceId)
    {
        var invoice = Invoices.FirstOrDefault(item => item.Id == invoiceId);
        if (invoice is null)
        {
            return CreateWorkflowResult(false, "Счет не найден.", "Не удалось отметить оплату.");
        }

        invoice.Status = "Оплачен";
        RefreshOrderLifecycle(invoice.SalesOrderId);
        WriteOperationLog("Счет", invoice.Id, invoice.Number, "Оплата счета", "Успех", $"Счет {invoice.Number} отмечен как оплаченный.");
        OnChanged();
        return CreateWorkflowResult(true, $"Счет {invoice.Number} оплачен.", "Документ закрыт по оплате.");
    }

    public SalesWorkflowActionResult PrepareShipment(Guid shipmentId)
    {
        var shipment = Shipments.FirstOrDefault(item => item.Id == shipmentId);
        if (shipment is null)
        {
            return CreateWorkflowResult(false, "Отгрузка не найдена.", "Не удалось перевести отгрузку к сборке.");
        }

        var inventory = new SalesInventoryService(this);
        var check = inventory.AnalyzeShipment(shipment);
        if (!check.IsFullyCovered)
        {
            WriteOperationLog("Отгрузка", shipment.Id, shipment.Number, "Подготовка к сборке", "Ошибка", check.HintText);
            return CreateWorkflowResult(false, $"Не удалось подготовить отгрузку {shipment.Number}.", check.HintText);
        }

        shipment.Status = "К сборке";
        RefreshOrderLifecycle(shipment.SalesOrderId);
        WriteOperationLog("Отгрузка", shipment.Id, shipment.Number, "Подготовка к сборке", "Успех", $"Отгрузка {shipment.Number} переведена в статус 'К сборке'.");
        OnChanged();
        return CreateWorkflowResult(true, $"Отгрузка {shipment.Number} переведена к сборке.", check.HintText);
    }

    public SalesWorkflowActionResult ShipShipment(Guid shipmentId)
    {
        var shipment = Shipments.FirstOrDefault(item => item.Id == shipmentId);
        if (shipment is null)
        {
            return CreateWorkflowResult(false, "Отгрузка не найдена.", "Не удалось провести отгрузку.");
        }

        var inventory = new SalesInventoryService(this);
        var check = inventory.AnalyzeShipment(shipment);
        if (!check.IsFullyCovered)
        {
            WriteOperationLog("Отгрузка", shipment.Id, shipment.Number, "Проведение отгрузки", "Ошибка", check.HintText);
            return CreateWorkflowResult(false, $"Не удалось провести отгрузку {shipment.Number}.", check.HintText);
        }

        shipment.Status = "Отгружена";
        RefreshOrderLifecycle(shipment.SalesOrderId);
        WriteOperationLog("Отгрузка", shipment.Id, shipment.Number, "Проведение отгрузки", "Успех", $"Отгрузка {shipment.Number} проведена.");
        OnChanged();
        return CreateWorkflowResult(true, $"Отгрузка {shipment.Number} проведена.", "Складское движение зафиксировано в локальном контуре.");
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SyncDocumentsForCustomer(SalesCustomerRecord customer)
    {
        foreach (var order in Orders.Where(item => item.CustomerId == customer.Id))
        {
            order.CustomerCode = customer.Code;
            order.CustomerName = customer.Name;
            order.ContractNumber = customer.ContractNumber;
            order.CurrencyCode = customer.CurrencyCode;
        }

        foreach (var invoice in Invoices.Where(item => item.CustomerId == customer.Id))
        {
            invoice.CustomerCode = customer.Code;
            invoice.CustomerName = customer.Name;
            invoice.ContractNumber = customer.ContractNumber;
            invoice.CurrencyCode = customer.CurrencyCode;
        }

        foreach (var shipment in Shipments.Where(item => item.CustomerId == customer.Id))
        {
            shipment.CustomerCode = customer.Code;
            shipment.CustomerName = customer.Name;
            shipment.ContractNumber = customer.ContractNumber;
            shipment.CurrencyCode = customer.CurrencyCode;
        }

        foreach (var returnDocument in Returns.Where(item => item.CustomerId == customer.Id))
        {
            returnDocument.CustomerCode = customer.Code;
            returnDocument.CustomerName = customer.Name;
            returnDocument.ContractNumber = customer.ContractNumber;
            returnDocument.CurrencyCode = customer.CurrencyCode;
        }

        foreach (var cashReceipt in CashReceipts.Where(item => item.CustomerId == customer.Id))
        {
            cashReceipt.CustomerCode = customer.Code;
            cashReceipt.CustomerName = customer.Name;
            cashReceipt.ContractNumber = customer.ContractNumber;
            cashReceipt.CurrencyCode = customer.CurrencyCode;
        }
    }

    private void SyncDerivedDocumentsFromOrder(SalesOrderRecord order)
    {
        foreach (var invoice in Invoices.Where(item => item.SalesOrderId == order.Id))
        {
            invoice.SalesOrderNumber = order.Number;
            invoice.CustomerId = order.CustomerId;
            invoice.CustomerCode = order.CustomerCode;
            invoice.CustomerName = order.CustomerName;
            invoice.ContractNumber = order.ContractNumber;
            invoice.CurrencyCode = order.CurrencyCode;
            invoice.Manager = order.Manager;
            invoice.Lines = CloneLines(order.Lines);
        }

        foreach (var shipment in Shipments.Where(item => item.SalesOrderId == order.Id))
        {
            shipment.SalesOrderNumber = order.Number;
            shipment.CustomerId = order.CustomerId;
            shipment.CustomerCode = order.CustomerCode;
            shipment.CustomerName = order.CustomerName;
            shipment.ContractNumber = order.ContractNumber;
            shipment.CurrencyCode = order.CurrencyCode;
            shipment.Warehouse = order.Warehouse;
            shipment.Manager = order.Manager;
            shipment.Lines = CloneLines(order.Lines);
        }

        foreach (var returnDocument in Returns.Where(item => item.SalesOrderId == order.Id))
        {
            returnDocument.SalesOrderNumber = order.Number;
            returnDocument.CustomerId = order.CustomerId;
            returnDocument.CustomerCode = order.CustomerCode;
            returnDocument.CustomerName = order.CustomerName;
            returnDocument.ContractNumber = order.ContractNumber;
            returnDocument.CurrencyCode = order.CurrencyCode;
            returnDocument.Warehouse = order.Warehouse;
            returnDocument.Manager = order.Manager;
        }

        foreach (var cashReceipt in CashReceipts.Where(item => item.SalesOrderId == order.Id))
        {
            cashReceipt.SalesOrderNumber = order.Number;
            cashReceipt.CustomerId = order.CustomerId;
            cashReceipt.CustomerCode = order.CustomerCode;
            cashReceipt.CustomerName = order.CustomerName;
            cashReceipt.ContractNumber = order.ContractNumber;
            cashReceipt.CurrencyCode = order.CurrencyCode;
            cashReceipt.Manager = order.Manager;
        }
    }

    private void PromoteOrderFromInvoice(SalesInvoiceRecord invoice)
    {
        var order = Orders.FirstOrDefault(item => item.Id == invoice.SalesOrderId);
        if (order is null || order.Status != "План")
        {
            return;
        }

        order.Status = "Подтвержден";
    }

    private void PromoteOrderFromShipment(SalesShipmentRecord shipment)
    {
        if (shipment.Status == "Черновик")
        {
            return;
        }

        var order = Orders.FirstOrDefault(item => item.Id == shipment.SalesOrderId);
        if (order is null)
        {
            return;
        }

        order.Status = "Готов к отгрузке";
    }

    private void RefreshOrderLifecycle(Guid orderId)
    {
        var order = Orders.FirstOrDefault(item => item.Id == orderId);
        if (order is null)
        {
            return;
        }

        if (order.Status.Equals("Закрыт", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relatedInvoices = Invoices.Where(item => item.SalesOrderId == order.Id).ToArray();
        var relatedShipments = Shipments.Where(item => item.SalesOrderId == order.Id).ToArray();

        if (relatedShipments.Any(item => item.Status.Equals("Отгружена", StringComparison.OrdinalIgnoreCase)))
        {
            order.Status = "Готов к отгрузке";
            return;
        }

        if (relatedShipments.Any(item =>
                item.Status.Equals("К сборке", StringComparison.OrdinalIgnoreCase)
                || item.Status.Equals("Готова к отгрузке", StringComparison.OrdinalIgnoreCase)))
        {
            order.Status = "Готов к отгрузке";
            return;
        }

        if (order.Status.Equals("В резерве", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (relatedInvoices.Any(item =>
                item.Status.Equals("Выставлен", StringComparison.OrdinalIgnoreCase)
                || item.Status.Equals("Ожидает оплату", StringComparison.OrdinalIgnoreCase)
                || item.Status.Equals("Оплачен", StringComparison.OrdinalIgnoreCase)))
        {
            order.Status = "Подтвержден";
            return;
        }

        if (order.Status.Equals("Готов к отгрузке", StringComparison.OrdinalIgnoreCase))
        {
            order.Status = "Подтвержден";
        }
    }

    private SalesWorkflowActionResult CreateWorkflowResult(bool succeeded, string message, string detail)
    {
        return new SalesWorkflowActionResult(succeeded, message, detail);
    }

    private void ValidateOrderForPersist(SalesOrderRecord order)
    {
        if (order.CustomerId == Guid.Empty || Customers.All(item => item.Id != order.CustomerId))
        {
            throw new InvalidOperationException("Нельзя сохранить заказ: клиент не найден в справочнике.");
        }

        if (string.IsNullOrWhiteSpace(order.CustomerName))
        {
            throw new InvalidOperationException("Нельзя сохранить заказ: не указано имя клиента.");
        }

        if (string.IsNullOrWhiteSpace(order.Warehouse))
        {
            throw new InvalidOperationException("Нельзя сохранить заказ: не указан склад.");
        }

        ValidateSalesLines(order.Lines, "заказ");
    }

    private void ValidateInvoiceForPersist(SalesInvoiceRecord invoice)
    {
        if (!HasOrderReference(invoice.SalesOrderId, invoice.SalesOrderNumber))
        {
            throw new InvalidOperationException("Нельзя сохранить счет: заказ-основание не найден.");
        }

        if (invoice.CustomerId == Guid.Empty || Customers.All(item => item.Id != invoice.CustomerId))
        {
            throw new InvalidOperationException("Нельзя сохранить счет: клиент не найден в справочнике.");
        }

        ValidateSalesLines(invoice.Lines, "счет");
    }

    private void ValidateShipmentForPersist(SalesShipmentRecord shipment)
    {
        if (!HasOrderReference(shipment.SalesOrderId, shipment.SalesOrderNumber))
        {
            throw new InvalidOperationException("Нельзя сохранить отгрузку: заказ-основание не найден.");
        }

        if (shipment.CustomerId == Guid.Empty || Customers.All(item => item.Id != shipment.CustomerId))
        {
            throw new InvalidOperationException("Нельзя сохранить отгрузку: клиент не найден в справочнике.");
        }

        if (string.IsNullOrWhiteSpace(shipment.Warehouse))
        {
            throw new InvalidOperationException("Нельзя сохранить отгрузку: не указан склад.");
        }

        ValidateSalesLines(shipment.Lines, "отгрузку");
    }

    private bool HasOrderReference(Guid orderId, string orderNumber)
    {
        return orderId != Guid.Empty && Orders.Any(item => item.Id == orderId)
            || !string.IsNullOrWhiteSpace(orderNumber)
            && Orders.Any(item => item.Number.Equals(orderNumber, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateSalesLines(IEnumerable<SalesOrderLineRecord> lines, string documentKind)
    {
        var rows = lines.ToArray();
        if (rows.Length == 0)
        {
            throw new InvalidOperationException($"Нельзя сохранить {documentKind}: добавьте хотя бы одну позицию.");
        }

        for (var index = 0; index < rows.Length; index++)
        {
            var line = rows[index];
            if (string.IsNullOrWhiteSpace(line.ItemCode) && string.IsNullOrWhiteSpace(line.ItemName))
            {
                throw new InvalidOperationException($"Нельзя сохранить {documentKind}: в позиции {index + 1} не указан товар.");
            }

            if (line.Quantity <= 0m)
            {
                throw new InvalidOperationException($"Нельзя сохранить {documentKind}: количество в позиции {index + 1} должно быть больше нуля.");
            }

            if (line.Price < 0m)
            {
                throw new InvalidOperationException($"Нельзя сохранить {documentKind}: цена в позиции {index + 1} не может быть отрицательной.");
            }
        }
    }

    private void WriteOperationLog(
        string entityType,
        Guid entityId,
        string entityNumber,
        string action,
        string result,
        string message)
    {
        OperationLog.Insert(0, new SalesOperationLogEntry
        {
            Id = Guid.NewGuid(),
            LoggedAt = DateTime.Now,
            Actor = EnsureCurrentOperator(),
            EntityType = entityType,
            EntityId = entityId,
            EntityNumber = entityNumber,
            Action = action,
            Result = result,
            Message = message
        });

        while (OperationLog.Count > 500)
        {
            OperationLog.RemoveAt(OperationLog.Count - 1);
        }
    }

    private string EnsureCurrentOperator()
    {
        if (!string.IsNullOrWhiteSpace(CurrentOperator))
        {
            return CurrentOperator;
        }

        CurrentOperator = Environment.UserName;
        return CurrentOperator;
    }

    private string GetNextCustomerCode()
    {
        var next = Customers
            .Select(customer => ParseNumericSuffix(customer.Code))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"C-{next:000}";
    }

    private string GetNextOrderNumber()
    {
        var next = Orders
            .Select(order => ParseNumericSuffix(order.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"SO-{DateTime.Today:yyMMdd}-{next:000}";
    }

    private string GetNextInvoiceNumber()
    {
        var next = Invoices
            .Select(invoice => ParseNumericSuffix(invoice.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"INV-{DateTime.Today:yyMMdd}-{next:000}";
    }

    private string GetNextShipmentNumber()
    {
        var next = Shipments
            .Select(shipment => ParseNumericSuffix(shipment.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"SH-{DateTime.Today:yyMMdd}-{next:000}";
    }

    private string GetNextCashReceiptNumber()
    {
        var next = CashReceipts
            .Select(receipt => ParseNumericSuffix(receipt.Number))
            .DefaultIfEmpty(0)
            .Max() + 1;

        return $"CASH-{DateTime.Today:yyMMdd}-{next:000}";
    }

    private static BindingList<SalesOrderLineRecord> CloneLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        return new BindingList<SalesOrderLineRecord>(lines.Select(line => line.Clone()).ToList());
    }

    private static void ReplaceBindingList<T>(
        BindingList<T> target,
        IEnumerable<T> source,
        Func<T, T> clone)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(clone(item));
        }
    }

    private static int ParseNumericSuffix(string value)
    {
        var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, out var number) ? number : 0;
    }
}

public sealed class SalesCustomerRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CounterpartyType { get; set; } = "Юридическое лицо";

    public bool IsBuyer { get; set; } = true;

    public bool IsSupplier { get; set; }

    public bool IsOther { get; set; }

    public string ContractNumber { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public string Manager { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Inn { get; set; } = string.Empty;

    public string Kpp { get; set; } = string.Empty;

    public string Ogrn { get; set; } = string.Empty;

    public string LegalAddress { get; set; } = string.Empty;

    public string ActualAddress { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public string Responsible { get; set; } = string.Empty;

    public string Tags { get; set; } = string.Empty;

    public string BankAccount { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public BindingList<SalesCustomerContactRecord> Contacts { get; set; } = new();

    public SalesCustomerRecord Clone()
    {
        return new SalesCustomerRecord
        {
            Id = Id,
            Code = Code,
            Name = Name,
            CounterpartyType = CounterpartyType,
            IsBuyer = IsBuyer,
            IsSupplier = IsSupplier,
            IsOther = IsOther,
            ContractNumber = ContractNumber,
            CurrencyCode = CurrencyCode,
            Manager = Manager,
            Status = Status,
            Phone = Phone,
            Email = Email,
            Inn = Inn,
            Kpp = Kpp,
            Ogrn = Ogrn,
            LegalAddress = LegalAddress,
            ActualAddress = ActualAddress,
            Region = Region,
            City = City,
            Source = Source,
            Responsible = Responsible,
            Tags = Tags,
            BankAccount = BankAccount,
            Notes = Notes,
            Contacts = CloneContacts(Contacts)
        };
    }

    public void CopyFrom(SalesCustomerRecord source)
    {
        Code = source.Code;
        Name = source.Name;
        CounterpartyType = source.CounterpartyType;
        IsBuyer = source.IsBuyer;
        IsSupplier = source.IsSupplier;
        IsOther = source.IsOther;
        ContractNumber = source.ContractNumber;
        CurrencyCode = source.CurrencyCode;
        Manager = source.Manager;
        Status = source.Status;
        Phone = source.Phone;
        Email = source.Email;
        Inn = source.Inn;
        Kpp = source.Kpp;
        Ogrn = source.Ogrn;
        LegalAddress = source.LegalAddress;
        ActualAddress = source.ActualAddress;
        Region = source.Region;
        City = source.City;
        Source = source.Source;
        Responsible = source.Responsible;
        Tags = source.Tags;
        BankAccount = source.BankAccount;
        Notes = source.Notes;
        Contacts = CloneContacts(source.Contacts);
    }

    private static BindingList<SalesCustomerContactRecord> CloneContacts(IEnumerable<SalesCustomerContactRecord>? contacts)
    {
        return new BindingList<SalesCustomerContactRecord>((contacts ?? Array.Empty<SalesCustomerContactRecord>())
            .Select(contact => contact.Clone())
            .ToList());
    }
}

public sealed class SalesCustomerContactRecord
{
    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public SalesCustomerContactRecord Clone()
    {
        return new SalesCustomerContactRecord
        {
            Name = Name,
            Role = Role,
            Phone = Phone,
            Email = Email,
            Comment = Comment
        };
    }
}

public sealed class SalesOrderRecord
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateTime OrderDate { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string ContractNumber { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public string Warehouse { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Manager { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public BindingList<SalesOrderLineRecord> Lines { get; set; } = new();

    public int PositionCount => Lines.Count;

    public decimal TotalAmount => Lines.Sum(line => line.Amount);

    public SalesOrderRecord Clone()
    {
        return new SalesOrderRecord
        {
            Id = Id,
            Number = Number,
            OrderDate = OrderDate,
            CustomerId = CustomerId,
            CustomerCode = CustomerCode,
            CustomerName = CustomerName,
            ContractNumber = ContractNumber,
            CurrencyCode = CurrencyCode,
            Warehouse = Warehouse,
            Status = Status,
            Manager = Manager,
            Comment = Comment,
            Lines = CloneLines(Lines)
        };
    }

    public void CopyFrom(SalesOrderRecord source)
    {
        Number = source.Number;
        OrderDate = source.OrderDate;
        CustomerId = source.CustomerId;
        CustomerCode = source.CustomerCode;
        CustomerName = source.CustomerName;
        ContractNumber = source.ContractNumber;
        CurrencyCode = source.CurrencyCode;
        Warehouse = source.Warehouse;
        Status = source.Status;
        Manager = source.Manager;
        Comment = source.Comment;
        Lines = CloneLines(source.Lines);
    }

    private static BindingList<SalesOrderLineRecord> CloneLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        return new BindingList<SalesOrderLineRecord>(lines.Select(line => line.Clone()).ToList());
    }
}

public sealed class SalesInvoiceRecord
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateTime InvoiceDate { get; set; }

    public DateTime DueDate { get; set; }

    public Guid SalesOrderId { get; set; }

    public string SalesOrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string ContractNumber { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public string Status { get; set; } = string.Empty;

    public string Manager { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public BindingList<SalesOrderLineRecord> Lines { get; set; } = new();

    public int PositionCount => Lines.Count;

    public decimal TotalAmount => Lines.Sum(line => line.Amount);

    public SalesInvoiceRecord Clone()
    {
        return new SalesInvoiceRecord
        {
            Id = Id,
            Number = Number,
            InvoiceDate = InvoiceDate,
            DueDate = DueDate,
            SalesOrderId = SalesOrderId,
            SalesOrderNumber = SalesOrderNumber,
            CustomerId = CustomerId,
            CustomerCode = CustomerCode,
            CustomerName = CustomerName,
            ContractNumber = ContractNumber,
            CurrencyCode = CurrencyCode,
            Status = Status,
            Manager = Manager,
            Comment = Comment,
            Lines = CloneLines(Lines)
        };
    }

    public void CopyFrom(SalesInvoiceRecord source)
    {
        Number = source.Number;
        InvoiceDate = source.InvoiceDate;
        DueDate = source.DueDate;
        SalesOrderId = source.SalesOrderId;
        SalesOrderNumber = source.SalesOrderNumber;
        CustomerId = source.CustomerId;
        CustomerCode = source.CustomerCode;
        CustomerName = source.CustomerName;
        ContractNumber = source.ContractNumber;
        CurrencyCode = source.CurrencyCode;
        Status = source.Status;
        Manager = source.Manager;
        Comment = source.Comment;
        Lines = CloneLines(source.Lines);
    }

    private static BindingList<SalesOrderLineRecord> CloneLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        return new BindingList<SalesOrderLineRecord>(lines.Select(line => line.Clone()).ToList());
    }
}

public sealed class SalesShipmentRecord
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateTime ShipmentDate { get; set; }

    public Guid SalesOrderId { get; set; }

    public string SalesOrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string ContractNumber { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public string Warehouse { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Carrier { get; set; } = string.Empty;

    public string Manager { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public BindingList<SalesOrderLineRecord> Lines { get; set; } = new();

    public int PositionCount => Lines.Count;

    public decimal TotalAmount => Lines.Sum(line => line.Amount);

    public SalesShipmentRecord Clone()
    {
        return new SalesShipmentRecord
        {
            Id = Id,
            Number = Number,
            ShipmentDate = ShipmentDate,
            SalesOrderId = SalesOrderId,
            SalesOrderNumber = SalesOrderNumber,
            CustomerId = CustomerId,
            CustomerCode = CustomerCode,
            CustomerName = CustomerName,
            ContractNumber = ContractNumber,
            CurrencyCode = CurrencyCode,
            Warehouse = Warehouse,
            Status = Status,
            Carrier = Carrier,
            Manager = Manager,
            Comment = Comment,
            Lines = CloneLines(Lines)
        };
    }

    public void CopyFrom(SalesShipmentRecord source)
    {
        Number = source.Number;
        ShipmentDate = source.ShipmentDate;
        SalesOrderId = source.SalesOrderId;
        SalesOrderNumber = source.SalesOrderNumber;
        CustomerId = source.CustomerId;
        CustomerCode = source.CustomerCode;
        CustomerName = source.CustomerName;
        ContractNumber = source.ContractNumber;
        CurrencyCode = source.CurrencyCode;
        Warehouse = source.Warehouse;
        Status = source.Status;
        Carrier = source.Carrier;
        Manager = source.Manager;
        Comment = source.Comment;
        Lines = CloneLines(source.Lines);
    }

    private static BindingList<SalesOrderLineRecord> CloneLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        return new BindingList<SalesOrderLineRecord>(lines.Select(line => line.Clone()).ToList());
    }
}

public sealed class SalesReturnRecord
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateTime ReturnDate { get; set; }

    public Guid SalesOrderId { get; set; }

    public string SalesOrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string ContractNumber { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public string Warehouse { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Manager { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public BindingList<SalesOrderLineRecord> Lines { get; set; } = new();

    public int PositionCount => Lines.Count;

    public decimal TotalAmount => Lines.Sum(line => line.Amount);

    public SalesReturnRecord Clone()
    {
        return new SalesReturnRecord
        {
            Id = Id,
            Number = Number,
            ReturnDate = ReturnDate,
            SalesOrderId = SalesOrderId,
            SalesOrderNumber = SalesOrderNumber,
            CustomerId = CustomerId,
            CustomerCode = CustomerCode,
            CustomerName = CustomerName,
            ContractNumber = ContractNumber,
            CurrencyCode = CurrencyCode,
            Warehouse = Warehouse,
            Status = Status,
            Manager = Manager,
            Reason = Reason,
            Comment = Comment,
            Lines = CloneLines(Lines)
        };
    }

    public void CopyFrom(SalesReturnRecord source)
    {
        Number = source.Number;
        ReturnDate = source.ReturnDate;
        SalesOrderId = source.SalesOrderId;
        SalesOrderNumber = source.SalesOrderNumber;
        CustomerId = source.CustomerId;
        CustomerCode = source.CustomerCode;
        CustomerName = source.CustomerName;
        ContractNumber = source.ContractNumber;
        CurrencyCode = source.CurrencyCode;
        Warehouse = source.Warehouse;
        Status = source.Status;
        Manager = source.Manager;
        Reason = source.Reason;
        Comment = source.Comment;
        Lines = CloneLines(source.Lines);
    }

    private static BindingList<SalesOrderLineRecord> CloneLines(IEnumerable<SalesOrderLineRecord> lines)
    {
        return new BindingList<SalesOrderLineRecord>(lines.Select(line => line.Clone()).ToList());
    }
}

public sealed class SalesCashReceiptRecord
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateTime ReceiptDate { get; set; }

    public Guid SalesOrderId { get; set; }

    public string SalesOrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string ContractNumber { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public decimal Amount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string CashBox { get; set; } = string.Empty;

    public string Manager { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public SalesCashReceiptRecord Clone()
    {
        return new SalesCashReceiptRecord
        {
            Id = Id,
            Number = Number,
            ReceiptDate = ReceiptDate,
            SalesOrderId = SalesOrderId,
            SalesOrderNumber = SalesOrderNumber,
            CustomerId = CustomerId,
            CustomerCode = CustomerCode,
            CustomerName = CustomerName,
            ContractNumber = ContractNumber,
            CurrencyCode = CurrencyCode,
            Amount = Amount,
            Status = Status,
            CashBox = CashBox,
            Manager = Manager,
            Comment = Comment
        };
    }

    public void CopyFrom(SalesCashReceiptRecord source)
    {
        Number = source.Number;
        ReceiptDate = source.ReceiptDate;
        SalesOrderId = source.SalesOrderId;
        SalesOrderNumber = source.SalesOrderNumber;
        CustomerId = source.CustomerId;
        CustomerCode = source.CustomerCode;
        CustomerName = source.CustomerName;
        ContractNumber = source.ContractNumber;
        CurrencyCode = source.CurrencyCode;
        Amount = source.Amount;
        Status = source.Status;
        CashBox = source.CashBox;
        Manager = source.Manager;
        Comment = source.Comment;
    }
}

public sealed class SalesOrderLineRecord
{
    public Guid Id { get; set; }

    public string ItemCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal Amount => Math.Round(Quantity * Price, 2, MidpointRounding.AwayFromZero);

    public SalesOrderLineRecord Clone()
    {
        return new SalesOrderLineRecord
        {
            Id = Id,
            ItemCode = ItemCode,
            ItemName = ItemName,
            Unit = Unit,
            Quantity = Quantity,
            Price = Price
        };
    }
}

public sealed class SalesOperationLogEntry
{
    public Guid Id { get; set; }

    public DateTime LoggedAt { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string EntityNumber { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public SalesOperationLogEntry Clone()
    {
        return new SalesOperationLogEntry
        {
            Id = Id,
            LoggedAt = LoggedAt,
            Actor = Actor,
            EntityType = EntityType,
            EntityId = EntityId,
            EntityNumber = EntityNumber,
            Action = Action,
            Result = Result,
            Message = Message
        };
    }
}

public sealed record SalesWorkflowActionResult(bool Succeeded, string Message, string Detail);

public sealed record SalesCatalogItemOption(string Code, string Name, string Unit, decimal DefaultPrice);
