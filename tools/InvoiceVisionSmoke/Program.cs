using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Infrastructure.Ai;
using WarehouseAutomatisaion.Infrastructure.Options;

// Sprint 5 Task 11: standalone smoke-tool для проверки AI распознавания
// накладных. Не зависит от MySQL/WPF — только Application + Infrastructure.Ai.
//
// Usage:
//   dotnet run --project tools/InvoiceVisionSmoke -- path/to/invoice.jpg
//
// Берёт ApiKey из WarehouseAutomatisaion.Desktop.Wpf/appsettings.local.json
// (или из текущей рабочей директории, если положить туда).

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

var configPath = FindConfigFile();
if (configPath is null)
{
    Console.Error.WriteLine("❌ appsettings.local.json не найден.");
    Console.Error.WriteLine("   Положи его в текущую папку или убедись что есть в WarehouseAutomatisaion.Desktop.Wpf/.");
    return 1;
}

Console.WriteLine($"Конфиг:  {configPath}");
Console.WriteLine($"Файл:    {imagePath}");
Console.WriteLine();

var config = new ConfigurationBuilder()
    .AddJsonFile(configPath, optional: false)
    .Build();

var aiOptions = new AiProvidersOptions();
config.GetSection(AiProvidersOptions.SectionName).Bind(aiOptions);

if (string.IsNullOrWhiteSpace(aiOptions.OpenAi.ApiKey)
    || aiOptions.OpenAi.ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("❌ OpenAI API key не сконфигурирован.");
    Console.Error.WriteLine("   Проверь AiProviders:OpenAi:ApiKey в appsettings.local.json.");
    return 1;
}

using var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(opt => opt.TimestampFormat = "HH:mm:ss.fff "));
var logger = loggerFactory.CreateLogger<OpenAiInvoiceVisionService>();

var monitor = new StaticOptionsMonitor<AiProvidersOptions>(aiOptions);
var service = new OpenAiInvoiceVisionService(monitor, logger);

var bytes = await File.ReadAllBytesAsync(imagePath);
var contentType = ResolveContentType(imagePath);
var payload = new InvoiceImagePayload(bytes, contentType, Path.GetFileName(imagePath));

Console.WriteLine($"Провайдер:    {service.ProviderName}");
Console.WriteLine($"Размер файла: {bytes.Length / 1024.0:N1} KB ({contentType})");
Console.WriteLine();
Console.WriteLine("⏳ Распознавание...");
Console.WriteLine();

try
{
    var result = await service.RecognizeAsync(payload);
    PrintResult(result);
    return 0;
}
catch (InvoiceVisionException exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"❌ Ошибка распознавания [{exception.Kind}]: {exception.Message}");
    if (exception.InnerException is not null)
    {
        Console.Error.WriteLine($"   Inner: {exception.InnerException.Message}");
    }
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"❌ Неожиданная ошибка: {exception}");
    return 3;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/InvoiceVisionSmoke -- <path-to-image>");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run --project tools/InvoiceVisionSmoke -- C:\\Users\\me\\Desktop\\torg12.jpg");
    Console.WriteLine("  dotnet run --project tools/InvoiceVisionSmoke -- test-data/upd-sample.png");
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

static void PrintResult(InvoiceRecognitionResult result)
{
    Console.WriteLine("=== Результат распознавания ===");
    Console.WriteLine();
    Console.WriteLine($"Поставщик:    {result.SupplierName ?? "(не распознан)"}");
    Console.WriteLine($"ИНН:          {result.SupplierTaxId ?? "(не распознан)"}");
    Console.WriteLine($"Номер:        {result.InvoiceNumber ?? "(не распознан)"}");
    Console.WriteLine($"Дата:         {result.InvoiceDate?.ToString("dd.MM.yyyy") ?? "(не распознана)"}");
    Console.WriteLine($"Валюта:       {result.Currency ?? "?"}");
    Console.WriteLine($"Итого:        {result.TotalAmount?.ToString("N2") ?? "?"} (НДС: {result.TotalVat?.ToString("N2") ?? "?"})");
    Console.WriteLine($"Строк:        {result.Lines.Count}");
    Console.WriteLine($"Время:        {result.Duration.TotalMilliseconds:N0} ms");
    Console.WriteLine();

    if (result.Lines.Count > 0)
    {
        Console.WriteLine("Строки:");
        Console.WriteLine($"  {"#",3}  {"SKU",-15}  {"Название",-45}  {"Кол-во",10}  {"Ед",-5}  {"Цена",12}  {"Сумма",14}");
        Console.WriteLine("  " + new string('-', 110));
        foreach (var line in result.Lines)
        {
            var name = line.Name.Length > 45 ? line.Name[..42] + "..." : line.Name;
            Console.WriteLine($"  {line.LineNumber,3}  {line.Sku ?? "—",-15}  {name,-45}  {line.Quantity,10:N3}  {line.Unit ?? "—",-5}  {line.UnitPrice?.ToString("N2") ?? "—",12}  {line.Total?.ToString("N2") ?? "—",14}");
        }
        Console.WriteLine();
    }

    Console.WriteLine("=== Raw JSON (для отладки) ===");
    Console.WriteLine(result.RawResponseJson);
}

internal sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly T _value;

    public StaticOptionsMonitor(T value) => _value = value;

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
