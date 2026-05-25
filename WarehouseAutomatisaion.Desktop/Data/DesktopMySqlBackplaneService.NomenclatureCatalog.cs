using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 5: чтение каталога номенклатуры для AI matcher.
// Берём из таблицы nomenclature_items (legacy схема прода).
// Когда придёт OData (Sprint 2) — добавится альтернативный источник из 1С,
// результат конвертируется в тот же NomenclatureRef.
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlNomenclatureCommandTimeoutSeconds = 30;

    public IReadOnlyList<NomenclatureRef> LoadNomenclatureRefs()
    {
        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlNomenclatureCommandTimeoutSeconds);

            const string sql = """
                SELECT id, code, name
                FROM nomenclature_items
                ORDER BY name;
                """;

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = MysqlNomenclatureCommandTimeoutSeconds;

            var rows = new List<NomenclatureRef>(capacity: 9000);
            using var reader = command.ExecuteReader();

            var ordId = reader.GetOrdinal("id");
            var ordCode = reader.GetOrdinal("code");
            var ordName = reader.GetOrdinal("name");

            while (reader.Read())
            {
                rows.Add(new NomenclatureRef(
                    Id: reader.GetString(ordId),
                    Code: reader.IsDBNull(ordCode) ? string.Empty : reader.GetString(ordCode),
                    Name: reader.IsDBNull(ordName) ? string.Empty : reader.GetString(ordName)));
            }

            return rows;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<NomenclatureRef>();
        }
    }
}
