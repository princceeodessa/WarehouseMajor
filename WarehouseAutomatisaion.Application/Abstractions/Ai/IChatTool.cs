namespace WarehouseAutomatisaion.Application.Abstractions.Ai;

// Sprint 12: один инструмент которым ассистент может пользоваться.
// Tool-метаданные (имя/описание/схема) → Claude через tool definitions.
// Tool-логика (ExecuteAsync) → внутри Major: SQL запросы, recommender, etc.
public interface IChatTool
{
    /// <summary>Имя инструмента — латиница, snake_case. Например "query_stock".
    /// Это то что ассистент ставит в tool_use.name.</summary>
    string Name { get; }

    /// <summary>Краткое описание для ассистента — что инструмент делает и КОГДА его вызывать.
    /// Хорошее описание = модель правильно использует tool. Это самое важное в tool use.</summary>
    string Description { get; }

    /// <summary>JSON schema аргументов в формате Anthropic input_schema.
    /// Пример:
    /// {
    ///   "type": "object",
    ///   "properties": { "search_term": { "type": "string", "description": "..." } },
    ///   "required": ["search_term"]
    /// }
    /// </summary>
    string InputSchemaJson { get; }

    /// <summary>Выполнить инструмент с аргументами от ассистента.
    /// Возвращает строку (обычно JSON или markdown) которая попадёт обратно ассистенту
    /// как tool_result. Длина — желательно до ~2000 символов чтобы не раздувать context.</summary>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);
}
