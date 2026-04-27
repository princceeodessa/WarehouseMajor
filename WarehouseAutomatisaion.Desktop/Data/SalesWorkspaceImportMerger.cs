using System.ComponentModel;
using System.Globalization;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public static class SalesWorkspaceImportMerger
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string[] LineSectionNames = ["Запасы", "Товары", "Услуги"];

    public static void Merge(SalesWorkspace workspace)
    {
        var snapshot = workspace.OneCImport;
        if (snapshot is null || !snapshot.HasAnyData)
        {
            return;
        }

        var context = new ImportMergeContext(workspace);
        MergeCustomers(workspace, snapshot.Customers, context);
        MergeCatalogItems(workspace, snapshot.Items, context);
        MergeSalesOrders(workspace, snapshot.SalesOrders, context);
        MergeSalesInvoices(workspace, snapshot.SalesInvoices, context);
        MergeSalesShipments(workspace, snapshot.SalesShipments, context);
        ApplyLookupLists(workspace, context);
    }

    private static void MergeCustomers(
        SalesWorkspace workspace,
        OneCEntityDataset dataset,
        ImportMergeContext context)
    {
        if (dataset.Records.Count == 0)
        {
            return;
        }

        foreach (var record in dataset.Records)
        {
            if (ShouldSkipCatalogRecord(record))
            {
                continue;
            }

            var code = FirstNonEmpty(record.Code, ExtractTaxId(record), CreateImportCode(record.Reference, "CUST"));
            var name = FirstNonEmpty(record.Title, SafeDisplay(record.FindField("НаименованиеПолное")), code);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var customer = context.FindCustomer(code, name);
            if (customer is null)
            {
                customer = new SalesCustomerRecord
                {
                    Id = Guid.NewGuid()
                };
                workspace.Customers.Add(customer);
            }

            var currency = ResolveCurrencyCode(
                record.FindField("ВалютаРасчетовПоУмолчанию"),
                customer.CurrencyCode,
                workspace.Currencies);
            var manager = ResolveManager(
                record.FindField("Ответственный"),
                customer.Manager,
                workspace.Managers);

            customer.Code = PreferImported(code, customer.Code, CreateImportCode(record.Reference, "CUST"));
            customer.Name = PreferImported(name, customer.Name, customer.Code);
            customer.ContractNumber = PreferImported(
                SafeDisplay(record.FindField("ОсновныеСведения")),
                customer.ContractNumber);
            customer.CurrencyCode = currency;
            customer.Manager = manager;
            customer.Status = MapCustomerStatus(record, customer.Status);
            customer.Phone = PreferImported(ResolveCustomerPhone(record), customer.Phone);
            customer.Email = PreferImported(ResolveCustomerEmail(record), customer.Email);
            customer.Notes = PreferImported(BuildCustomerNotes(record), customer.Notes);

            context.RegisterCustomer(record.Reference, customer);
            context.Managers.Add(manager);
            context.Currencies.Add(currency);
        }

        context.RebuildCustomerIndexes(workspace, dataset);
    }

    private static void MergeCatalogItems(
        SalesWorkspace workspace,
        OneCEntityDataset dataset,
        ImportMergeContext context)
    {
        if (dataset.Records.Count == 0)
        {
            return;
        }

        var items = workspace.CatalogItems.ToList();
        var indexesByCode = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => items.IndexOf(group.First()), StringComparer.OrdinalIgnoreCase);

        foreach (var record in dataset.Records)
        {
            if (ShouldSkipCatalogRecord(record))
            {
                continue;
            }

            var code = FirstNonEmpty(record.Code, CreateImportCode(record.Reference, "ITEM"));
            var name = FirstNonEmpty(record.Title, SafeDisplay(record.FindField("НаименованиеПолное")), code);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var existing = indexesByCode.TryGetValue(code, out var index)
                ? items[index]
                : items.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            var unit = ResolveItemUnit(record, existing?.Unit);
            var price = ResolveItemPrice(record, existing?.DefaultPrice);
            var item = new SalesCatalogItemOption(code, name, unit, price);

            if (existing is null)
            {
                items.Add(item);
                indexesByCode[code] = items.Count - 1;
            }
            else
            {
                var existingIndex = items.IndexOf(existing);
                items[existingIndex] = item;
                indexesByCode[code] = existingIndex;
            }

            context.RegisterItemReference(record.Reference, item);
        }

        workspace.CatalogItems = items
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        context.RebuildItemIndexes(workspace, dataset);
    }

    private static void MergeSalesOrders(
        SalesWorkspace workspace,
        OneCEntityDataset dataset,
        ImportMergeContext context)
    {
        if (dataset.Records.Count == 0)
        {
            return;
        }

        var ordersByNumber = workspace.Orders
            .Where(order => !string.IsNullOrWhiteSpace(order.Number))
            .ToDictionary(order => order.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var record in dataset.Records)
        {
            if (IsDeleted(record))
            {
                continue;
            }

            var number = FirstNonEmpty(record.Number, SafeDisplay(record.FindField("Номер")));
            if (string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var existing = ordersByNumber.GetValueOrDefault(number);
            var customer = context.ResolveCustomer(record.FindField("Контрагент"));
            var order = new SalesOrderRecord
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                Number = number,
                OrderDate = record.Date ?? ParseOneCDate(record.FindField("Дата")?.RawValue) ?? DateTime.Today,
                CustomerId = customer?.Id ?? existing?.CustomerId ?? Guid.Empty,
                CustomerCode = customer?.Code ?? existing?.CustomerCode ?? string.Empty,
                CustomerName = customer?.Name ?? existing?.CustomerName ?? SafeDisplay(record.FindField("Контрагент")),
                ContractNumber = ResolveContract(record, customer?.ContractNumber, existing?.ContractNumber),
                CurrencyCode = ResolveCurrencyCode(record.FindField("ВалютаДокумента"), customer?.CurrencyCode ?? existing?.CurrencyCode, workspace.Currencies),
                Warehouse = ResolveWarehouse(record, existing?.Warehouse, workspace.Warehouses, context),
                Status = MapOrderStatus(record, existing?.Status),
                Manager = ResolveManager(record.FindField("Ответственный"), existing?.Manager ?? customer?.Manager, workspace.Managers),
                Comment = PreferImported(SafeDisplay(record.FindField("Комментарий")), existing?.Comment),
                Lines = BuildDocumentLines(record, context, existing?.Lines)
            };

            if (existing is null)
            {
                workspace.Orders.Add(order);
                ordersByNumber[number] = order;
                existing = order;
            }
            else
            {
                existing.CopyFrom(order);
            }

            context.RegisterOrder(record.Reference, existing);
            context.Managers.Add(existing.Manager);
            context.Currencies.Add(existing.CurrencyCode);
            context.Warehouses.Add(existing.Warehouse);
        }
    }

    private static void MergeSalesInvoices(
        SalesWorkspace workspace,
        OneCEntityDataset dataset,
        ImportMergeContext context)
    {
        if (dataset.Records.Count == 0)
        {
            return;
        }

        var invoicesByNumber = workspace.Invoices
            .Where(invoice => !string.IsNullOrWhiteSpace(invoice.Number))
            .ToDictionary(invoice => invoice.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var record in dataset.Records)
        {
            if (IsDeleted(record))
            {
                continue;
            }

            var number = FirstNonEmpty(record.Number, SafeDisplay(record.FindField("Номер")));
            if (string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var existing = invoicesByNumber.GetValueOrDefault(number);
            var customer = context.ResolveCustomer(record.FindField("Контрагент"));
            var order = context.ResolveOrder(record.FindField("ДокументОснование"));
            var invoiceDate = record.Date ?? ParseOneCDate(record.FindField("Дата")?.RawValue) ?? DateTime.Today;
            var dueDate = ParseOneCDate(record.FindField("ОплатаДо")?.RawValue) ?? existing?.DueDate ?? invoiceDate.AddDays(3);
            var invoice = new SalesInvoiceRecord
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                Number = number,
                InvoiceDate = invoiceDate,
                DueDate = dueDate,
                SalesOrderId = order?.Id ?? existing?.SalesOrderId ?? Guid.Empty,
                SalesOrderNumber = order?.Number ?? existing?.SalesOrderNumber ?? SafeDisplay(record.FindField("ДокументОснование")),
                CustomerId = customer?.Id ?? existing?.CustomerId ?? Guid.Empty,
                CustomerCode = customer?.Code ?? existing?.CustomerCode ?? string.Empty,
                CustomerName = customer?.Name ?? existing?.CustomerName ?? SafeDisplay(record.FindField("Контрагент")),
                ContractNumber = ResolveContract(record, customer?.ContractNumber, existing?.ContractNumber),
                CurrencyCode = ResolveCurrencyCode(record.FindField("ВалютаДокумента"), customer?.CurrencyCode ?? existing?.CurrencyCode, workspace.Currencies),
                Status = MapInvoiceStatus(record, dueDate, existing?.Status),
                Manager = ResolveManager(record.FindField("Ответственный"), existing?.Manager ?? customer?.Manager, workspace.Managers),
                Comment = PreferImported(SafeDisplay(record.FindField("Комментарий")), existing?.Comment),
                Lines = BuildDocumentLines(record, context, existing?.Lines)
            };

            if (existing is null)
            {
                workspace.Invoices.Add(invoice);
                invoicesByNumber[number] = invoice;
            }
            else
            {
                existing.CopyFrom(invoice);
            }
        }
    }

    private static void MergeSalesShipments(
        SalesWorkspace workspace,
        OneCEntityDataset dataset,
        ImportMergeContext context)
    {
        if (dataset.Records.Count == 0)
        {
            return;
        }

        var shipmentsByNumber = workspace.Shipments
            .Where(shipment => !string.IsNullOrWhiteSpace(shipment.Number))
            .ToDictionary(shipment => shipment.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var record in dataset.Records)
        {
            if (IsDeleted(record))
            {
                continue;
            }

            var number = FirstNonEmpty(record.Number, SafeDisplay(record.FindField("Номер")));
            if (string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var existing = shipmentsByNumber.GetValueOrDefault(number);
            var customer = context.ResolveCustomer(record.FindField("Контрагент"));
            var order = context.ResolveOrder(record.FindField("Заказ")) ?? context.ResolveOrder(record.FindField("ДокументОснование"));
            var shipment = new SalesShipmentRecord
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                Number = number,
                ShipmentDate = record.Date ?? ParseOneCDate(record.FindField("Дата")?.RawValue) ?? DateTime.Today,
                SalesOrderId = order?.Id ?? existing?.SalesOrderId ?? Guid.Empty,
                SalesOrderNumber = order?.Number ?? existing?.SalesOrderNumber ?? SafeDisplay(record.FindField("Заказ")),
                CustomerId = customer?.Id ?? existing?.CustomerId ?? Guid.Empty,
                CustomerCode = customer?.Code ?? existing?.CustomerCode ?? string.Empty,
                CustomerName = customer?.Name ?? existing?.CustomerName ?? SafeDisplay(record.FindField("Контрагент")),
                ContractNumber = ResolveContract(record, customer?.ContractNumber, existing?.ContractNumber),
                CurrencyCode = ResolveCurrencyCode(record.FindField("ВалютаДокумента"), customer?.CurrencyCode ?? existing?.CurrencyCode, workspace.Currencies),
                Warehouse = ResolveWarehouse(record, existing?.Warehouse, workspace.Warehouses, context),
                Status = MapShipmentStatus(record, existing?.Status),
                Carrier = PreferImported(SafeDisplay(record.FindField("Перевозчик")), existing?.Carrier),
                Manager = ResolveManager(record.FindField("Ответственный"), existing?.Manager ?? customer?.Manager, workspace.Managers),
                Comment = PreferImported(SafeDisplay(record.FindField("Комментарий")), existing?.Comment),
                Lines = BuildDocumentLines(record, context, existing?.Lines)
            };

            if (existing is null)
            {
                workspace.Shipments.Add(shipment);
                shipmentsByNumber[number] = shipment;
            }
            else
            {
                existing.CopyFrom(shipment);
            }

            context.Warehouses.Add(shipment.Warehouse);
            context.Managers.Add(shipment.Manager);
            context.Currencies.Add(shipment.CurrencyCode);
        }
    }

    private static BindingList<SalesOrderLineRecord> BuildDocumentLines(
        OneCRecordSnapshot record,
        ImportMergeContext context,
        BindingList<SalesOrderLineRecord>? fallback)
    {
        var sections = record.TabularSections
            .Where(section => LineSectionNames.Contains(section.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (sections.Length == 0)
        {
            return CloneLines(fallback);
        }

        var lines = new List<SalesOrderLineRecord>();
        foreach (var section in sections)
        {
            foreach (var row in section.Rows)
            {
                var item = context.ResolveItem(row.FindField("Номенклатура"));
                var quantity = ParseDecimal(row.FindField("Количество")?.RawValue);
                if (quantity <= 0)
                {
                    continue;
                }

                var price = ParseDecimal(row.FindField("Цена")?.RawValue);
                lines.Add(new SalesOrderLineRecord
                {
                    Id = Guid.NewGuid(),
                    ItemCode = item?.Code ?? FirstNonEmpty(SafeDisplay(row.FindField("Номенклатура")), CreateImportCode(row.FindField("Номенклатура")?.RawValue, "ITEM")),
                    ItemName = FirstNonEmpty(SafeDisplay(row.FindField("Содержание")), item?.Name, SafeDisplay(row.FindField("Номенклатура"))),
                    Unit = ResolveLineUnit(row.FindField("ЕдиницаИзмерения"), item?.Unit),
                    Quantity = quantity,
                    Price = price > 0 ? price : Math.Max(item?.DefaultPrice ?? 0m, 0.01m)
                });
            }
        }

        return lines.Count == 0
            ? CloneLines(fallback)
            : new BindingList<SalesOrderLineRecord>(lines);
    }

    private static BindingList<SalesOrderLineRecord> CloneLines(BindingList<SalesOrderLineRecord>? lines)
    {
        return lines is null
            ? new BindingList<SalesOrderLineRecord>()
            : new BindingList<SalesOrderLineRecord>(lines.Select(line => line.Clone()).ToList());
    }

    private static void ApplyLookupLists(SalesWorkspace workspace, ImportMergeContext context)
    {
        workspace.Managers = BuildLookupList(context.Managers, workspace.Managers);
        workspace.Currencies = BuildLookupList(context.Currencies, workspace.Currencies);
        workspace.Warehouses = BuildLookupList(context.Warehouses, workspace.Warehouses);
    }

    private static IReadOnlyList<string> BuildLookupList(HashSet<string> importedValues, IReadOnlyList<string> existingValues)
    {
        var merged = existingValues
            .Concat(importedValues)
            .Select(value => value?.Trim() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        return merged.Length == 0 ? existingValues : merged;
    }

    private static string ResolveContract(OneCRecordSnapshot record, string? customerContract, string? existingValue)
    {
        return PreferImported(
            SafeDisplay(record.FindField("Договор")),
            customerContract,
            existingValue);
    }

    private static string ResolveCurrencyCode(
        OneCFieldValue? field,
        string? preferred,
        IReadOnlyList<string> fallbackValues)
    {
        var resolved = ExtractCurrencyCode(field?.DisplayValue);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return fallbackValues.FirstOrDefault() ?? "RUB";
    }

    private static string ResolveManager(
        OneCFieldValue? field,
        string? preferred,
        IReadOnlyList<string> fallbackValues)
    {
        var resolved = SafeDisplay(field);
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return fallbackValues.FirstOrDefault() ?? string.Empty;
    }

    private static string ResolveWarehouse(
        OneCRecordSnapshot record,
        string? preferred,
        IReadOnlyList<string> fallbackValues,
        ImportMergeContext context)
    {
        var resolved = FirstNonEmpty(
            SafeDisplay(record.FindField("Склад")),
            SafeDisplay(record.FindField("СтруктурнаяЕдиница")),
            SafeDisplay(record.FindField("Подразделение")));
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            context.Warehouses.Add(resolved);
            return resolved;
        }

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return fallbackValues.FirstOrDefault() ?? "Основной склад";
    }

    private static string ResolveItemUnit(OneCRecordSnapshot record, string? preferred)
    {
        return FirstNonEmpty(
            SafeDisplay(record.FindField("ЕдиницаДляОтчетов")),
            SafeDisplay(record.FindField("ЕдиницаИзмерения")),
            SafeDisplay(record.FindField("ЕдиницаДляЦенников")),
            preferred,
            "шт");
    }

    private static string ResolveLineUnit(OneCFieldValue? field, string? preferred)
    {
        return FirstNonEmpty(SafeDisplay(field), preferred, "шт");
    }

    private static decimal ResolveItemPrice(OneCRecordSnapshot record, decimal? preferred)
    {
        var price = ParseDecimal(record.FindField("ФиксированнаяСтоимость")?.RawValue);
        if (price > 0)
        {
            return price;
        }

        if (preferred is > 0)
        {
            return preferred.Value;
        }

        return 0.01m;
    }

    private static string ResolveCustomerPhone(OneCRecordSnapshot record)
    {
        return FirstNonEmpty(
            SafeDisplay(record.FindField("НомерТелефонаДляПоиска")),
            SafeSectionValue(record, "КонтактнаяИнформация", "НомерТелефона"),
            SafeSectionValue(record, "КонтактнаяИнформация", "Представление"));
    }

    private static string ResolveCustomerEmail(OneCRecordSnapshot record)
    {
        return FirstNonEmpty(
            SafeDisplay(record.FindField("АдресЭПДляПоиска")),
            SafeSectionValue(record, "КонтактнаяИнформация", "АдресЭП"));
    }

    private static string BuildCustomerNotes(OneCRecordSnapshot record)
    {
        var values = new List<string>();
        var comment = SafeDisplay(record.FindField("Комментарий"));
        if (!string.IsNullOrWhiteSpace(comment))
        {
            values.Add(comment);
        }

        var inn = SafeDisplay(record.FindField("ИНН"));
        var kpp = SafeDisplay(record.FindField("КПП"));
        var taxDetails = string.Join(" | ", new[]
        {
            string.IsNullOrWhiteSpace(inn) ? string.Empty : $"ИНН: {inn}",
            string.IsNullOrWhiteSpace(kpp) ? string.Empty : $"КПП: {kpp}"
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(taxDetails))
        {
            values.Add(taxDetails);
        }

        return string.Join(Environment.NewLine, values);
    }

    private static string MapCustomerStatus(OneCRecordSnapshot record, string? existingValue)
    {
        if (IsDeleted(record) || IsTrue(record, "Недействителен"))
        {
            return "Пауза";
        }

        if (IsTrue(record, "Покупатель") || record.Status.Contains("Покупатель", StringComparison.OrdinalIgnoreCase))
        {
            return "Активен";
        }

        if (IsTrue(record, "Поставщик"))
        {
            return string.IsNullOrWhiteSpace(existingValue) ? "На проверке" : existingValue;
        }

        return string.IsNullOrWhiteSpace(existingValue) ? "Активен" : existingValue;
    }

    private static string MapOrderStatus(OneCRecordSnapshot record, string? existingValue)
    {
        var status = FirstNonEmpty(record.Status, SafeDisplay(record.FindField("СостояниеЗаказа")));
        if (status.Contains("резерв", StringComparison.OrdinalIgnoreCase))
        {
            return "В резерве";
        }

        if (status.Contains("отгруз", StringComparison.OrdinalIgnoreCase)
            || status.Contains("готов", StringComparison.OrdinalIgnoreCase))
        {
            return "Готов к отгрузке";
        }

        if (status.Contains("подтверж", StringComparison.OrdinalIgnoreCase)
            || IsTrue(record, "Проведен"))
        {
            return "Подтвержден";
        }

        return string.IsNullOrWhiteSpace(existingValue) ? "План" : existingValue;
    }

    private static string MapInvoiceStatus(OneCRecordSnapshot record, DateTime dueDate, string? existingValue)
    {
        if (string.Equals(existingValue, "Оплачен", StringComparison.OrdinalIgnoreCase))
        {
            return "Оплачен";
        }

        if (!IsTrue(record, "Проведен"))
        {
            return "Черновик";
        }

        return dueDate.Date < DateTime.Today ? "Ожидает оплату" : "Выставлен";
    }

    private static string MapShipmentStatus(OneCRecordSnapshot record, string? existingValue)
    {
        if (IsTrue(record, "Проведен"))
        {
            return "Отгружена";
        }

        return string.IsNullOrWhiteSpace(existingValue) ? "Черновик" : existingValue;
    }

    private static bool ShouldSkipCatalogRecord(OneCRecordSnapshot record)
    {
        return IsDeleted(record) || IsTrue(record, "ЭтоГруппа");
    }

    private static bool IsDeleted(OneCRecordSnapshot record)
    {
        return IsTrue(record, "ПометкаУдаления");
    }

    private static bool IsTrue(OneCRecordSnapshot record, string fieldName)
    {
        return string.Equals(record.FindField(fieldName)?.RawValue, "Истина", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractTaxId(OneCRecordSnapshot record)
    {
        return SafeDisplay(record.FindField("ИНН"));
    }

    private static string ExtractCurrencyCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var token = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        return token.Length <= 5 ? token : string.Empty;
    }

    private static string SafeDisplay(OneCFieldValue? field)
    {
        return field is null ? string.Empty : SafeDisplay(field.DisplayValue);
    }

    private static string SafeDisplay(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.StartsWith("{", StringComparison.Ordinal) ? string.Empty : normalized;
    }

    private static string SafeSectionValue(OneCRecordSnapshot record, string sectionName, string fieldName)
    {
        return record.TabularSections
            .FirstOrDefault(section => section.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase))?
            .Rows
            .Select(row => SafeDisplay(row.FindField(fieldName)))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;
    }

    private static DateTime? ParseOneCDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[]
        {
            "dd.MM.yyyy",
            "dd.MM.yyyy H:mm:ss",
            "dd.MM.yyyy HH:mm:ss",
            "dd.MM.yyyy H:mm",
            "dd.MM.yyyy HH:mm"
        };

        return DateTime.TryParseExact(value, formats, RuCulture, DateTimeStyles.None, out var result)
            ? result
            : null;
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        if (decimal.TryParse(value, NumberStyles.Number, RuCulture, out var ruValue))
        {
            return ruValue;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue)
            ? invariantValue
            : 0m;
    }

    private static string CreateImportCode(string? reference, string prefix)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return prefix;
        }

        var chars = reference.Where(char.IsLetterOrDigit).ToArray();
        var suffix = chars.Length <= 12
            ? new string(chars)
            : new string(chars, chars.Length - 12, 12);
        return string.IsNullOrWhiteSpace(suffix) ? prefix : $"{prefix}-{suffix}";
    }

    private static string PreferImported(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return PreferImported(values);
    }

    private sealed class ImportMergeContext
    {
        private readonly Dictionary<string, SalesCustomerRecord> _customersByCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SalesCustomerRecord> _customersByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SalesCustomerRecord> _customersByReference = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SalesCatalogItemOption> _itemsByCode = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SalesCatalogItemOption> _itemsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SalesCatalogItemOption> _itemsByReference = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SalesOrderRecord> _ordersByNumber = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SalesOrderRecord> _ordersByReference = new(StringComparer.Ordinal);

        public ImportMergeContext(SalesWorkspace workspace)
        {
            Managers = new HashSet<string>(workspace.Managers.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
            Currencies = new HashSet<string>(workspace.Currencies.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
            Warehouses = new HashSet<string>(workspace.Warehouses.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
            RebuildCustomerIndexes(workspace, workspace.OneCImport?.Customers ?? OneCEntityDataset.Empty("Контрагенты"));
            RebuildItemIndexes(workspace, workspace.OneCImport?.Items ?? OneCEntityDataset.Empty("Номенклатура"));
        }

        public HashSet<string> Managers { get; }

        public HashSet<string> Currencies { get; }

        public HashSet<string> Warehouses { get; }

        public void RebuildCustomerIndexes(SalesWorkspace workspace, OneCEntityDataset dataset)
        {
            _customersByCode.Clear();
            _customersByName.Clear();
            _customersByReference.Clear();

            foreach (var customer in workspace.Customers)
            {
                if (!string.IsNullOrWhiteSpace(customer.Code))
                {
                    _customersByCode[customer.Code] = customer;
                }

                if (!string.IsNullOrWhiteSpace(customer.Name))
                {
                    _customersByName[customer.Name] = customer;
                }
            }

            foreach (var record in dataset.Records)
            {
                var customer = FindCustomer(record.Code, record.Title);
                if (customer is not null && !string.IsNullOrWhiteSpace(record.Reference))
                {
                    _customersByReference[record.Reference] = customer;
                }
            }
        }

        public void RebuildItemIndexes(SalesWorkspace workspace, OneCEntityDataset dataset)
        {
            _itemsByCode.Clear();
            _itemsByName.Clear();
            _itemsByReference.Clear();

            foreach (var item in workspace.CatalogItems)
            {
                if (!string.IsNullOrWhiteSpace(item.Code))
                {
                    _itemsByCode[item.Code] = item;
                }

                if (!string.IsNullOrWhiteSpace(item.Name))
                {
                    _itemsByName[item.Name] = item;
                }
            }

            foreach (var record in dataset.Records)
            {
                var item = FindItem(record.Code, record.Title);
                if (item is not null && !string.IsNullOrWhiteSpace(record.Reference))
                {
                    _itemsByReference[record.Reference] = item;
                }
            }
        }

        public SalesCustomerRecord? FindCustomer(string? code, string? name)
        {
            if (!string.IsNullOrWhiteSpace(code) && _customersByCode.TryGetValue(code, out var byCode))
            {
                return byCode;
            }

            if (!string.IsNullOrWhiteSpace(name) && _customersByName.TryGetValue(name, out var byName))
            {
                return byName;
            }

            return null;
        }

        public void RegisterCustomer(string? reference, SalesCustomerRecord customer)
        {
            if (!string.IsNullOrWhiteSpace(customer.Code))
            {
                _customersByCode[customer.Code] = customer;
            }

            if (!string.IsNullOrWhiteSpace(customer.Name))
            {
                _customersByName[customer.Name] = customer;
            }

            if (!string.IsNullOrWhiteSpace(reference))
            {
                _customersByReference[reference] = customer;
            }
        }

        public SalesCustomerRecord? ResolveCustomer(OneCFieldValue? field)
        {
            if (field is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(field.RawValue) && _customersByReference.TryGetValue(field.RawValue, out var byReference))
            {
                return byReference;
            }

            return ResolveByDisplay(field.DisplayValue, _customersByCode, _customersByName);
        }

        public SalesCatalogItemOption? FindItem(string? code, string? name)
        {
            if (!string.IsNullOrWhiteSpace(code) && _itemsByCode.TryGetValue(code, out var byCode))
            {
                return byCode;
            }

            if (!string.IsNullOrWhiteSpace(name) && _itemsByName.TryGetValue(name, out var byName))
            {
                return byName;
            }

            return null;
        }

        public void RegisterItemReference(string? reference, SalesCatalogItemOption item)
        {
            if (!string.IsNullOrWhiteSpace(item.Code))
            {
                _itemsByCode[item.Code] = item;
            }

            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                _itemsByName[item.Name] = item;
            }

            if (!string.IsNullOrWhiteSpace(reference))
            {
                _itemsByReference[reference] = item;
            }
        }

        public SalesCatalogItemOption? ResolveItem(OneCFieldValue? field)
        {
            if (field is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(field.RawValue) && _itemsByReference.TryGetValue(field.RawValue, out var byReference))
            {
                return byReference;
            }

            return ResolveByDisplay(field.DisplayValue, _itemsByCode, _itemsByName);
        }

        public void RegisterOrder(string? reference, SalesOrderRecord order)
        {
            if (!string.IsNullOrWhiteSpace(order.Number))
            {
                _ordersByNumber[order.Number] = order;
            }

            if (!string.IsNullOrWhiteSpace(reference))
            {
                _ordersByReference[reference] = order;
            }
        }

        public SalesOrderRecord? ResolveOrder(OneCFieldValue? field)
        {
            if (field is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(field.RawValue) && _ordersByReference.TryGetValue(field.RawValue, out var byReference))
            {
                return byReference;
            }

            return ResolveByDisplay(field.DisplayValue, _ordersByNumber, _ordersByNumber);
        }

        private static T? ResolveByDisplay<T>(
            string? displayValue,
            IReadOnlyDictionary<string, T> valuesByCode,
            IReadOnlyDictionary<string, T> valuesByName)
            where T : class
        {
            var display = SafeDisplay(displayValue);
            if (string.IsNullOrWhiteSpace(display))
            {
                return null;
            }

            var parts = display
                .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (valuesByCode.TryGetValue(part, out var byCode))
                {
                    return byCode;
                }

                if (valuesByName.TryGetValue(part, out var byName))
                {
                    return byName;
                }
            }

            return valuesByName.GetValueOrDefault(display);
        }
    }
}
