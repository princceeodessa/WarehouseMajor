using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Infrastructure.Ai;
using WarehouseAutomatisaion.Infrastructure.Options;

// Sprint 5 Task 11: end-to-end smoke-tool для backend AI распознавания.
// Прогоняет полный pipeline: vision → catalog → matcher.
// Persistence (создание черновика приёмки) — отдельным флагом --save.
//
// Usage:
//   dotnet run --project tools/InvoiceVisionSmoke -- <path-to-image>
//   dotnet run --project tools/InvoiceVisionSmoke -- <path-to-image> --save
//
// Конфиг (appsettings.local.json) ищется в Desktop.Wpf или CWD.

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var imagePath = Path.GetFullPath(args[0]);
if (!File.Exists(imagePath))
{
    Console.Error.WriteLine($"❌ Файл не найден: {imagePath}");
    return 1;
}

var saveAsDraft = args.Any(a => a.Equals("--save", StringComparison.OrdinalIgnoreCase));

var configPath = FindConfigFile();
if (configPath is null)
{
    Console.Error.WriteLine("❌ appsettings.local.json не найден.");
    return 1;
}

Console.WriteLine($"Конфиг:    {configPath}");
Console.WriteLine($"Файл:      {imagePath}");
Console.WriteLine($"Сохранять: {(saveAsDraft ? "ДА — будет создан черновик в app_warehouse_documents" : "нет (--save для сохранения)")}");
Console.WriteLine();

var config = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: false)
    .Build();

// 1. AI options
var aiOptions = new AiProvidersOptions();
config.GetSection(AiProvidersOptions.SectionName).Bind(aiOptions);
if (string.IsNullOrWhiteSpace(aiOptions.OpenAi.ApiKey)
    || aiOptions.OpenAi.ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("❌ OpenAI API key не сконфигурирован.");
    return 1;
}

// 2. DB options (для catalog reader + опц. для receipt writer)
var dbOptions = ReadRemoteDatabaseOptions(config);
if (dbOptions is null)
{
    Console.Error.WriteLine("❌ Не удалось прочитать RemoteDatabase из конфига.");
    return 1;
}

// 3. Wire dependencies manually (без DI container)
using var loggerFactory = LoggerFactory.Create(b => b
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(opt =>
    {
        opt.TimestampFormat = "HH:mm:ss.fff ";
        opt.SingleLine = true;
    }));

var aiMonitor = new StaticOptionsMonitor<AiProvidersOptions>(aiOptions);
var visionService = new OpenAiInvoiceVisionService(
    aiMonitor,
    loggerFactory.CreateLogger<OpenAiInvoiceVisionService>());

var backplane = new DesktopMySqlBackplaneService(dbOptions);
var catalogReader = new MySqlNomenclatureCatalogReader(backplane);
var matcher = new InvoiceLineMatcher();
var orchestrator = new InvoiceRecognitionService(
    visionService,
    catalogReader,
    matcher,
    loggerFactory.CreateLogger<InvoiceRecognitionService>());

// 4. Load image
var bytes = await File.ReadAllBytesAsync(imagePath);
var contentType = ResolveContentType(imagePath);
var payload = new InvoiceImagePayload(bytes, contentType, Path.GetFileName(imagePath));

Console.WriteLine($"Провайдер:    OpenAI:{aiOptions.OpenAi.Model}");
Console.WriteLine($"Размер:       {bytes.Length / 1024.0:N1} KB ({contentType})");
Console.WriteLine();
Console.WriteLine("⏳ Pipeline: распознавание → каталог → matcher...");
Console.WriteLine();

try
{
    var result = await orchestrator.RecognizeAndMatchAsync(payload);
    PrintRecognition(result.Recognition);
    PrintMatches(result.Matches);

    if (saveAsDraft)
    {
        Console.WriteLine();
        Console.WriteLine("💾 Сохранение черновика приёмки...");
        var writer = new MySqlReceiptDraftWriter(backplane);
        var draft = BuildDraft(result, imagePath);
        var draftId = await writer.CreateDraftAsync(draft);
        Console.WriteLine($"✅ Создан черновик id={draftId} в app_warehouse_documents (document_kind='receipt').");
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"❌ Ошибка: {exception.GetType().Name}: {exception.Message}");
    if (exception.InnerException is not null)
    {
        Console.Error.WriteLine($"   Inner: {exception.InnerException.Message}");
    }
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/InvoiceVisionSmoke -- <path-to-image>");
    Console.WriteLine("  dotnet run --project tools/InvoiceVisionSmoke -- <path-to-image> --save");
    Console.WriteLine();
    Console.WriteLine("Полный pipeline: vision → catalog → matcher.");
    Console.WriteLine("С флагом --save: дополнительно создаёт черновик в app_warehouse_documents.");
}

static string? FindConfigFile()
{
    var candidates = new[]
    {
        Path.Combine(Environment.CurrentDirectory, "appsettings.local.json"),
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "WarehouseAutomatisaion.Desktop.Wpf", "appsettings.local.json")),
        Path.GetFullPath(Path.Combine(
            Environment.CurrentDirectory,
            "WarehouseAutomatisaion.Desktop.Wpf", "appsettings.local.json")),
    };

    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static string ResolveContentType(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };
}

static OperationalMySqlDesktopOptions? ReadRemoteDatabaseOptions(IConfiguration config)
{
    var section = config.GetSection("RemoteDatabase");
    if (!section.Exists())
    {
        return null;
    }

    var host = section["Host"];
    var database = section["Database"];
    var user = section["User"];
    var password = section["Password"];
    var portStr = section["Port"];
    var mysqlPath = section["MysqlExecutablePath"];

    if (string.IsNullOrWhiteSpace(host)
        || string.IsNullOrWhiteSpace(database)
        || string.IsNullOrWhiteSpace(user)
        || string.IsNullOrWhiteSpace(password))
    {
        return null;
    }

    return new OperationalMySqlDesktopOptions
    {
        Host = host!,
        Port = int.TryParse(portStr, out var port) ? port : 3306,
        DatabaseName = database!,
        User = user!,
        Password = password!,
        MysqlExecutablePath = mysqlPath ?? string.Empty
    };
}

static void PrintRecognition(InvoiceRecognitionResult result)
{
    Console.WriteLine();
    Console.WriteLine("=== Распознавание ===");
    Console.WriteLine($"Поставщик:    {result.SupplierName ?? "(не распознан)"}");
    Console.WriteLine($"ИНН:          {result.SupplierTaxId ?? "(не распознан)"}");
    Console.WriteLine($"Номер:        {result.InvoiceNumber ?? "(не распознан)"}");
    Console.WriteLine($"Дата:         {result.InvoiceDate?.ToString("dd.MM.yyyy") ?? "(не распознана)"}");
    Console.WriteLine($"Валюта:       {result.Currency ?? "?"}");
    Console.WriteLine($"Итого:        {result.TotalAmount?.ToString("N2") ?? "?"} (НДС: {result.TotalVat?.ToString("N2") ?? "?"})");
    Console.WriteLine($"Строк:        {result.Lines.Count}");
    Console.WriteLine($"Время AI:     {result.Duration.TotalMilliseconds:N0} ms");
}

static void PrintMatches(IReadOnlyList<MatchedInvoiceLine> matches)
{
    Console.WriteLine();
    Console.WriteLine("=== Matching с nomenclature_items ===");

    var summary = matches.GroupBy(m => m.Kind).ToDictionary(g => g.Key, g => g.Count());
    foreach (MatchKind kind in Enum.GetValues<MatchKind>())
    {
        var count = summary.GetValueOrDefault(kind, 0);
        Console.WriteLine($"  {kind,-13} {count,3}");
    }

    Console.WriteLine();
    Console.WriteLine($"  {"#",3}  {"Распознано",-45}  →  {"Найдено в каталоге",-45}  {"Conf",5}  {"Type",-12}");
    Console.WriteLine("  " + new string('-', 130));

    foreach (var match in matches)
    {
        var src = Truncate(match.Source.Name, 45);
        var matched = match.BestMatch is null
            ? "(нет совпадения — ручной выбор)"
            : Truncate($"{match.BestMatch.Code} · {match.BestMatch.Name}", 45);
        var confidence = match.Confidence > 0 ? match.Confidence.ToString("P0") : "—";
        Console.WriteLine($"  {match.Source.LineNumber,3}  {src,-45}  →  {matched,-45}  {confidence,5}  {match.Kind,-12}");
    }
}

static string Truncate(string value, int max)
{
    if (string.IsNullOrEmpty(value)) return string.Empty;
    return value.Length > max ? value[..(max - 1)] + "…" : value;
}

static WarehouseAutomatisaion.Application.Contracts.Receiving.ReceiptDraft BuildDraft(
    InvoiceRecognitionWithMatches result,
    string sourceFile)
{
    var lines = new List<WarehouseAutomatisaion.Application.Contracts.Receiving.ReceiptDraftLine>();
    foreach (var match in result.Matches)
    {
        var matchedId = match.BestMatch is not null && Guid.TryParse(match.BestMatch.Id, out var g) ? g : (Guid?)null;
        lines.Add(new WarehouseAutomatisaion.Application.Contracts.Receiving.ReceiptDraftLine(
            LineNumber: match.Source.LineNumber,
            MatchedItemId: matchedId,
            OriginalItemName: match.Source.Name,
            OriginalSku: match.Source.Sku,
            Unit: match.Source.Unit,
            Quantity: match.Source.Quantity,
            UnitPrice: match.Source.UnitPrice,
            Vat: match.Source.Vat,
            Subtotal: match.Source.Subtotal,
            Total: match.Source.Total));
    }

    return new WarehouseAutomatisaion.Application.Contracts.Receiving.ReceiptDraft(
        SupplierName: result.Recognition.SupplierName ?? "(не распознан)",
        SupplierTaxId: result.Recognition.SupplierTaxId,
        InvoiceNumber: result.Recognition.InvoiceNumber,
        InvoiceDate: result.Recognition.InvoiceDate,
        Currency: result.Recognition.Currency,
        TotalAmount: result.Recognition.TotalAmount,
        TotalVat: result.Recognition.TotalVat,
        Lines: lines,
        SourceLabel: $"ai:{result.Recognition.ProviderName}:{Path.GetFileName(sourceFile)}",
        CreatedByActor: Environment.UserName,
        CommentText: $"Распознано из {Path.GetFileName(sourceFile)} через {result.Recognition.ProviderName}, длительность {result.Recognition.Duration.TotalMilliseconds:N0} ms.");
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly T _value;
    public StaticOptionsMonitor(T value) => _value = value;
    public T CurrentValue => _value;
    public T Get(string? name) => _value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
