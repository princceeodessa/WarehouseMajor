using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class CatalogWorkspace
{
    private CatalogWorkspace(
        BindingList<CatalogItemRecord> items,
        BindingList<CatalogPriceTypeRecord> priceTypes,
        BindingList<CatalogDiscountRecord> discounts,
        BindingList<CatalogPriceRegistrationRecord> priceRegistrations,
        IReadOnlyList<string> itemStatuses,
        IReadOnlyList<string> priceRegistrationStatuses,
        IReadOnlyList<string> discountStatuses,
        IReadOnlyList<string> currencies,
        IReadOnlyList<string> warehouses)
    {
        Items = items;
        PriceTypes = priceTypes;
        Discounts = discounts;
        PriceRegistrations = priceRegistrations;
        ItemStatuses = itemStatuses;
        PriceRegistrationStatuses = priceRegistrationStatuses;
        DiscountStatuses = discountStatuses;
        Currencies = currencies;
        Warehouses = warehouses;
    }

    public BindingList<CatalogItemRecord> Items { get; }

    public BindingList<CatalogPriceTypeRecord> PriceTypes { get; }

    public BindingList<CatalogDiscountRecord> Discounts { get; }

    public BindingList<CatalogPriceRegistrationRecord> PriceRegistrations { get; }

    public BindingList<CatalogOperationLogEntry> OperationLog { get; } = new();

    public IReadOnlyList<string> ItemStatuses { get; }

    public IReadOnlyList<string> PriceRegistrationStatuses { get; }

    public IReadOnlyList<string> DiscountStatuses { get; }

    public IReadOnlyList<string> Currencies { get; internal set; }

    public IReadOnlyList<string> Warehouses { get; internal set; }

    public string CurrentOperator { get; internal set; } = string.Empty;

    public event EventHandler? Changed;

    public static CatalogWorkspace Create(string currentOperator, CatalogWorkspaceSeed seed)
    {
        var workspace = new CatalogWorkspace(
            new BindingList<CatalogItemRecord>(),
            new BindingList<CatalogPriceTypeRecord>(),
            new BindingList<CatalogDiscountRecord>(),
            new BindingList<CatalogPriceRegistrationRecord>(),
            ["Активна", "На настройке", "Архив"],
            ["Черновик", "Подготовлен", "Проведен"],
            ["Активна", "Черновик", "Остановлена"],
            NormalizeLookup(seed.Currencies, "RUB"),
            NormalizeLookup(seed.Warehouses));

        workspace.CurrentOperator = string.IsNullOrWhiteSpace(currentOperator) ? Environment.UserName : currentOperator;

        foreach (var item in seed.Items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            workspace.Items.Add(EnsureMarking(item.Clone()));
        }

        var seededPriceTypes = seed.PriceTypes.Count > 0
            ? seed.PriceTypes
            : BuildFallbackPriceTypes();
        foreach (var priceType in EnsureSingleDefaultPriceType(seededPriceTypes))
        {
            workspace.PriceTypes.Add(priceType.Clone());
        }

        foreach (var discount in seed.Discounts.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            workspace.Discounts.Add(discount.Clone());
        }

        foreach (var document in seed.PriceRegistrations.OrderByDescending(item => item.DocumentDate))
        {
            workspace.PriceRegistrations.Add(document.Clone());
        }

        foreach (var logEntry in seed.OperationLog.OrderByDescending(item => item.LoggedAt))
        {
            workspace.OperationLog.Add(logEntry.Clone());
        }

        return workspace;
    }

    public static CatalogWorkspace CreateEmpty(
        string currentOperator,
        IReadOnlyList<string>? currencies = null,
        IReadOnlyList<string>? warehouses = null)
    {
        return Create(
            currentOperator,
            new CatalogWorkspaceSeed
            {
                Currencies = NormalizeLookup(currencies, "RUB"),
                Warehouses = NormalizeLookup(warehouses)
            });
    }

    public void ReplaceFrom(CatalogWorkspace source)
    {
        ReplaceBindingList(Items, source.Items, item => EnsureMarking(item.Clone()));
        ReplaceBindingList(PriceTypes, source.PriceTypes, item => item.Clone());
        ReplaceBindingList(Discounts, source.Discounts, item => item.Clone());
        ReplaceBindingList(PriceRegistrations, source.PriceRegistrations, item => item.Clone());
        ReplaceBindingList(OperationLog, source.OperationLog, item => item.Clone());
        CurrentOperator = source.CurrentOperator;
        Currencies = source.Currencies.ToArray();
        Warehouses = source.Warehouses.ToArray();
        OnChanged();
    }

    public CatalogItemRecord CreateItemDraft()
    {
        return EnsureMarking(new CatalogItemRecord
        {
            Id = Guid.NewGuid(),
            Code = $"ITM-{DateTime.Now:yyMMdd-HHmmss}",
            Status = ItemStatuses.First(),
            CurrencyCode = Currencies.FirstOrDefault() ?? "RUB",
            BarcodeFormat = "Code128",
            SourceLabel = "Локальный каталог"
        });
    }

    public CatalogPriceTypeRecord CreatePriceTypeDraft()
    {
        return new CatalogPriceTypeRecord
        {
            Id = Guid.NewGuid(),
            Code = $"PT-{DateTime.Now:yyMMdd-HHmmss}",
            CurrencyCode = Currencies.FirstOrDefault() ?? "RUB",
            Status = "Рабочий",
            IsDefault = PriceTypes.Count == 0
        };
    }

    public CatalogDiscountRecord CreateDiscountDraft()
    {
        return new CatalogDiscountRecord
        {
            Id = Guid.NewGuid(),
            Percent = 0m,
            Status = DiscountStatuses.First(),
            Period = $"{DateTime.Today:dd.MM.yyyy} - {DateTime.Today.AddMonths(1):dd.MM.yyyy}"
        };
    }

    public CatalogPriceRegistrationRecord CreatePriceRegistrationDraft()
    {
        return new CatalogPriceRegistrationRecord
        {
            Id = Guid.NewGuid(),
            Number = $"PRC-{DateTime.Now:yyMMdd-HHmmss}",
            DocumentDate = DateTime.Today,
            Status = PriceRegistrationStatuses.First(),
            PriceTypeName = GetDefaultPriceTypeName(),
            CurrencyCode = Currencies.FirstOrDefault() ?? "RUB",
            Comment = "Локальный документ изменения цен."
        };
    }

    public void UpsertItem(CatalogItemRecord item)
    {
        var copy = EnsureMarking(item.Clone());
        var index = FindIndex(Items, candidate => candidate.Id == copy.Id);
        if (index >= 0)
        {
            Items[index] = copy;
            WriteOperationLog("Номенклатура", copy.Id, copy.Code, "Изменение карточки", "Успешно", $"Обновлена карточка {copy.Name}.");
        }
        else
        {
            Items.Add(copy);
            WriteOperationLog("Номенклатура", copy.Id, copy.Code, "Создание карточки", "Успешно", $"Добавлена карточка {copy.Name}.");
        }

        OnChanged();
    }

    public void UpsertPriceType(CatalogPriceTypeRecord priceType)
    {
        var copy = priceType.Clone();
        var index = FindIndex(PriceTypes, candidate => candidate.Id == copy.Id);
        if (copy.IsDefault)
        {
            ClearDefaultPriceType(copy.Id);
        }
        else if (PriceTypes.Count == 0 || (PriceTypes.Count == 1 && index >= 0))
        {
            copy.IsDefault = true;
        }

        if (index >= 0)
        {
            PriceTypes[index] = copy;
            WriteOperationLog("Вид цены", copy.Id, copy.Code, "Изменение вида цены", "Успешно", $"Обновлен вид цены {copy.Name}.");
        }
        else
        {
            PriceTypes.Add(copy);
            WriteOperationLog("Вид цены", copy.Id, copy.Code, "Создание вида цены", "Успешно", $"Добавлен вид цены {copy.Name}.");
        }

        EnsureAtLeastOneDefaultPriceType();
        OnChanged();
    }

    public void UpsertDiscount(CatalogDiscountRecord discount)
    {
        var copy = discount.Clone();
        var index = FindIndex(Discounts, candidate => candidate.Id == copy.Id);
        if (index >= 0)
        {
            Discounts[index] = copy;
            WriteOperationLog("Скидка", copy.Id, copy.Name, "Изменение скидки", "Успешно", $"Обновлена скидка {copy.Name}.");
        }
        else
        {
            Discounts.Add(copy);
            WriteOperationLog("Скидка", copy.Id, copy.Name, "Создание скидки", "Успешно", $"Добавлена скидка {copy.Name}.");
        }

        OnChanged();
    }

    public void UpsertPriceRegistration(CatalogPriceRegistrationRecord document)
    {
        var copy = document.Clone();
        var index = FindIndex(PriceRegistrations, candidate => candidate.Id == copy.Id);
        if (index >= 0)
        {
            PriceRegistrations[index] = copy;
            WriteOperationLog("Установка цен", copy.Id, copy.Number, "Изменение документа цен", "Успешно", $"Документ {copy.Number} обновлен.");
        }
        else
        {
            PriceRegistrations.Add(copy);
            WriteOperationLog("Установка цен", copy.Id, copy.Number, "Создание документа цен", "Успешно", $"Создан документ {copy.Number}.");
        }

        if (string.Equals(copy.Status, "Проведен", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPriceRegistrationCore(copy.Id, false, out _);
        }

        OnChanged();
    }

    public bool ApplyPriceRegistration(Guid documentId, out string message)
    {
        var result = ApplyPriceRegistrationCore(documentId, true, out message);
        if (result)
        {
            OnChanged();
        }

        return result;
    }

    public IReadOnlyList<SalesCatalogItemOption> BuildSalesCatalogItems()
    {
        return Items
            .Where(item => !string.Equals(item.Status, "Архив", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(item => new SalesCatalogItemOption(
                item.Code,
                item.Name,
                string.IsNullOrWhiteSpace(item.Unit) ? "шт" : item.Unit,
                item.DefaultPrice))
            .ToArray();
    }

    public string GetDefaultPriceTypeName()
    {
        return PriceTypes.FirstOrDefault(item => item.IsDefault)?.Name
               ?? PriceTypes.FirstOrDefault()?.Name
               ?? "Розничная цена";
    }

    private bool ApplyPriceRegistrationCore(Guid documentId, bool appendLog, out string message)
    {
        var document = PriceRegistrations.FirstOrDefault(item => item.Id == documentId);
        if (document is null)
        {
            message = "Документ изменения цен не найден.";
            return false;
        }

        var priceType = PriceTypes.FirstOrDefault(item => item.Name.Equals(document.PriceTypeName, StringComparison.OrdinalIgnoreCase));
        var shouldUpdateDefaultPrice = priceType?.IsDefault ?? document.PriceTypeName.Equals(GetDefaultPriceTypeName(), StringComparison.OrdinalIgnoreCase);
        var updatedCount = 0;

        foreach (var line in document.Lines)
        {
            var item = Items.FirstOrDefault(candidate =>
                candidate.Code.Equals(line.ItemCode, StringComparison.OrdinalIgnoreCase)
                || candidate.Name.Equals(line.ItemName, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                continue;
            }

            var previousPrice = item.DefaultPrice;
            line.PreviousPrice = line.PreviousPrice <= 0m ? previousPrice : line.PreviousPrice;

            if (shouldUpdateDefaultPrice)
            {
                item.DefaultPrice = line.NewPrice;
                updatedCount++;
            }
        }

        document.Status = "Проведен";
        message = shouldUpdateDefaultPrice
            ? $"Проведен документ {document.Number}. Обновлено карточек: {updatedCount:N0}."
            : $"Проведен документ {document.Number}. Цены сохранены по виду \"{document.PriceTypeName}\" без замены базовой цены.";

        if (appendLog)
        {
            WriteOperationLog("Установка цен", document.Id, document.Number, "Проведение документа цен", "Успешно", message);
        }

        return true;
    }

    private void ClearDefaultPriceType(Guid keepId)
    {
        foreach (var candidate in PriceTypes.Where(item => item.Id != keepId))
        {
            candidate.IsDefault = false;
        }
    }

    private static CatalogItemRecord EnsureMarking(CatalogItemRecord item)
    {
        if (string.IsNullOrWhiteSpace(item.BarcodeFormat))
        {
            item.BarcodeFormat = "Code128";
        }

        if (ShouldGenerateBarcodeValue(item))
        {
            item.BarcodeValue = BuildDefaultBarcodeValue(item);
        }

        if (string.IsNullOrWhiteSpace(item.QrPayload))
        {
            item.QrPayload = BuildDefaultQrPayload(item);
        }

        return item;
    }

    private static string BuildDefaultBarcodeValue(CatalogItemRecord item)
    {
        var seed = string.Join("|", new[] { item.Code, item.Name, item.DefaultWarehouse, item.Supplier, item.Id == Guid.Empty ? null : item.Id.ToString("N", CultureInfo.InvariantCulture) }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = "ITEM";
        }
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
        var digits = string.Concat(hash.Select(value => (value % 1000).ToString("000", CultureInfo.InvariantCulture)));
        return digits[..13];
    }

    private static bool ShouldGenerateBarcodeValue(CatalogItemRecord item)
    {
        var barcode = NormalizeComparable(item.BarcodeValue);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return true;
        }

        var code = NormalizeComparable(item.Code);
        return !string.IsNullOrWhiteSpace(code) && string.Equals(barcode, code, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparable(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Trim().Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static string BuildDefaultQrPayload(CatalogItemRecord item)
    {
        var lines = new List<string>
        {
            $"Код: {item.Code}",
            $"Номенклатура: {item.Name}"
        };

        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            lines.Add($"Категория: {item.Category}");
        }

        if (item.DefaultPrice > 0m)
        {
            lines.Add($"Цена: {item.DefaultPrice:N2} {item.CurrencyCode}");
        }

        if (!string.IsNullOrWhiteSpace(item.DefaultWarehouse))
        {
            lines.Add($"Склад: {item.DefaultWarehouse}");
        }

        return string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private void EnsureAtLeastOneDefaultPriceType()
    {
        if (PriceTypes.Count == 0)
        {
            return;
        }

        if (PriceTypes.Any(item => item.IsDefault))
        {
            return;
        }

        PriceTypes[0].IsDefault = true;
    }

    private void WriteOperationLog(
        string entityType,
        Guid entityId,
        string entityNumber,
        string action,
        string result,
        string message)
    {
        OperationLog.Insert(0, new CatalogOperationLogEntry
        {
            Id = Guid.NewGuid(),
            LoggedAt = DateTime.Now,
            Actor = CurrentOperator,
            EntityType = entityType,
            EntityId = entityId,
            EntityNumber = entityNumber,
            Action = action,
            Result = result,
            Message = message
        });
    }

    private void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static int FindIndex<T>(IEnumerable<T> items, Func<T, bool> predicate)
    {
        var index = 0;
        foreach (var item in items)
        {
            if (predicate(item))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static IReadOnlyList<string> NormalizeLookup(IEnumerable<string>? values, params string[] fallback)
    {
        var result = values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return result is { Length: > 0 } ? result : fallback;
    }

    private static IReadOnlyList<CatalogPriceTypeRecord> EnsureSingleDefaultPriceType(IReadOnlyList<CatalogPriceTypeRecord> source)
    {
        if (source.Count == 0)
        {
            return BuildFallbackPriceTypes();
        }

        var list = source.Select(item => item.Clone()).ToList();
        var defaultCandidate = list.FirstOrDefault(item => item.IsDefault)
            ?? list.FirstOrDefault(item => item.Name.Contains("Рознич", StringComparison.OrdinalIgnoreCase))
            ?? list[0];

        foreach (var item in list)
        {
            item.IsDefault = item.Id == defaultCandidate.Id;
        }

        return list;
    }

    private static IReadOnlyList<CatalogPriceTypeRecord> BuildFallbackPriceTypes()
    {
        return
        [
            new CatalogPriceTypeRecord
            {
                Id = Guid.NewGuid(),
                Code = "PT-DEFAULT-RETAIL",
                Name = "Розничная цена",
                CurrencyCode = "RUB",
                Status = "Рабочий",
                IsDefault = true,
                RoundingRule = "Психологическое"
            },
            new CatalogPriceTypeRecord
            {
                Id = Guid.NewGuid(),
                Code = "PT-DEFAULT-PURCHASE",
                Name = "Закупочная",
                CurrencyCode = "RUB",
                Status = "Ручной",
                IsManualEntryOnly = true,
                RoundingRule = "Без округления"
            }
        ];
    }

    private static void ReplaceBindingList<T>(ICollection<T> target, IEnumerable<T> source, Func<T, T> clone)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(clone(item));
        }
    }
}

public sealed class CatalogWorkspaceSeed
{
    public IReadOnlyList<CatalogItemRecord> Items { get; init; } = Array.Empty<CatalogItemRecord>();

    public IReadOnlyList<CatalogPriceTypeRecord> PriceTypes { get; init; } = Array.Empty<CatalogPriceTypeRecord>();

    public IReadOnlyList<CatalogDiscountRecord> Discounts { get; init; } = Array.Empty<CatalogDiscountRecord>();

    public IReadOnlyList<CatalogPriceRegistrationRecord> PriceRegistrations { get; init; } = Array.Empty<CatalogPriceRegistrationRecord>();

    public IReadOnlyList<CatalogOperationLogEntry> OperationLog { get; init; } = Array.Empty<CatalogOperationLogEntry>();

    public IReadOnlyList<string> Currencies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warehouses { get; init; } = Array.Empty<string>();
}

public sealed class CatalogItemRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Supplier { get; set; } = string.Empty;

    public string DefaultWarehouse { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public decimal DefaultPrice { get; set; }

    public string BarcodeValue { get; set; } = string.Empty;

    public string BarcodeFormat { get; set; } = "Code128";

    public string QrPayload { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public CatalogItemRecord Clone()
    {
        return new CatalogItemRecord
        {
            Id = Id,
            Code = Code,
            Name = Name,
            Unit = Unit,
            Category = Category,
            Supplier = Supplier,
            DefaultWarehouse = DefaultWarehouse,
            Status = Status,
            CurrencyCode = CurrencyCode,
            DefaultPrice = DefaultPrice,
            BarcodeValue = BarcodeValue,
            BarcodeFormat = BarcodeFormat,
            QrPayload = QrPayload,
            Notes = Notes,
            SourceLabel = SourceLabel
        };
    }
}

public sealed class CatalogPriceTypeRecord
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public string BasePriceTypeName { get; set; } = string.Empty;

    public string RoundingRule { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public bool IsManualEntryOnly { get; set; }

    public bool UsesPsychologicalRounding { get; set; }

    public string Status { get; set; } = string.Empty;

    public CatalogPriceTypeRecord Clone()
    {
        return new CatalogPriceTypeRecord
        {
            Id = Id,
            Code = Code,
            Name = Name,
            CurrencyCode = CurrencyCode,
            BasePriceTypeName = BasePriceTypeName,
            RoundingRule = RoundingRule,
            IsDefault = IsDefault,
            IsManualEntryOnly = IsManualEntryOnly,
            UsesPsychologicalRounding = UsesPsychologicalRounding,
            Status = Status
        };
    }
}

public sealed class CatalogDiscountRecord
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Percent { get; set; }

    public string PriceTypeName { get; set; } = string.Empty;

    public string Period { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public CatalogDiscountRecord Clone()
    {
        return new CatalogDiscountRecord
        {
            Id = Id,
            Name = Name,
            Percent = Percent,
            PriceTypeName = PriceTypeName,
            Period = Period,
            Scope = Scope,
            Status = Status,
            Comment = Comment
        };
    }
}

public sealed class CatalogPriceRegistrationRecord
{
    public Guid Id { get; set; }

    public string Number { get; set; } = string.Empty;

    public DateTime DocumentDate { get; set; }

    public string PriceTypeName { get; set; } = string.Empty;

    public string CurrencyCode { get; set; } = "RUB";

    public string Status { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public List<CatalogPriceRegistrationLineRecord> Lines { get; set; } = [];

    public CatalogPriceRegistrationRecord Clone()
    {
        return new CatalogPriceRegistrationRecord
        {
            Id = Id,
            Number = Number,
            DocumentDate = DocumentDate,
            PriceTypeName = PriceTypeName,
            CurrencyCode = CurrencyCode,
            Status = Status,
            Comment = Comment,
            Lines = Lines.Select(item => item.Clone()).ToList()
        };
    }
}

public sealed class CatalogPriceRegistrationLineRecord
{
    public Guid Id { get; set; }

    public string ItemCode { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public decimal PreviousPrice { get; set; }

    public decimal NewPrice { get; set; }

    public CatalogPriceRegistrationLineRecord Clone()
    {
        return new CatalogPriceRegistrationLineRecord
        {
            Id = Id,
            ItemCode = ItemCode,
            ItemName = ItemName,
            Unit = Unit,
            PreviousPrice = PreviousPrice,
            NewPrice = NewPrice
        };
    }
}

public sealed class CatalogOperationLogEntry
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

    public CatalogOperationLogEntry Clone()
    {
        return new CatalogOperationLogEntry
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
