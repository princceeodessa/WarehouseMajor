using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 11: persistence для app_nomenclature_embeddings.
// Hot path — LoadAll: один SELECT, парсим LONGBLOB → float[] вручную.
// Embeddings storage = float32 little-endian (BitConverter.IsLittleEndian
// проверяется один раз, на всех Windows x64 машинах true).
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlEmbeddingCommandTimeoutSeconds = 60;

    public IReadOnlyDictionary<Guid, float[]> LoadEmbeddings(string provider)
    {
        EnsureDatabaseAndSchema();

        const string sql = """
            SELECT item_id, dimensions, embedding
            FROM app_nomenclature_embeddings
            WHERE provider = @provider;
            """;

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options, useDatabase: true, MysqlConnectTimeoutSeconds, MysqlEmbeddingCommandTimeoutSeconds);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = MysqlEmbeddingCommandTimeoutSeconds;
        cmd.Parameters.AddWithValue("@provider", provider);

        var result = new Dictionary<Guid, float[]>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var itemIdRaw = reader.GetValue(0);
            if (itemIdRaw is null || !Guid.TryParse(itemIdRaw.ToString(), out var itemId))
            {
                continue;
            }
            var dimensions = reader.GetInt32(1);
            var blob = reader.IsDBNull(2) ? Array.Empty<byte>() : (byte[])reader.GetValue(2);
            if (blob.Length != dimensions * sizeof(float))
            {
                continue; // повреждённая запись — пропускаем, refresher перепишет
            }
            var vec = new float[dimensions];
            Buffer.BlockCopy(blob, 0, vec, 0, blob.Length);
            result[itemId] = vec;
        }
        return result;
    }

    public int CountEmbeddings(string provider)
    {
        EnsureDatabaseAndSchema();

        const string sql = "SELECT COUNT(*) FROM app_nomenclature_embeddings WHERE provider = @provider;";

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options, useDatabase: true, MysqlConnectTimeoutSeconds, MysqlEmbeddingCommandTimeoutSeconds);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@provider", provider);
        var raw = cmd.ExecuteScalar();
        return Convert.ToInt32(raw ?? 0);
    }

    public IReadOnlyList<Guid> LoadMissingEmbeddingItemIds(string provider)
    {
        EnsureDatabaseAndSchema();

        // Все nomenclature_items.id у которых нет embedding для этого провайдера.
        // LEFT JOIN с NULL-фильтром — стандартный анти-сет.
        const string sql = """
            SELECT n.id
            FROM nomenclature_items n
            LEFT JOIN app_nomenclature_embeddings e
                ON e.item_id = n.id AND e.provider = @provider
            WHERE e.item_id IS NULL;
            """;

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options, useDatabase: true, MysqlConnectTimeoutSeconds, MysqlEmbeddingCommandTimeoutSeconds);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = MysqlEmbeddingCommandTimeoutSeconds;
        cmd.Parameters.AddWithValue("@provider", provider);

        var result = new List<Guid>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var raw = reader.GetValue(0);
            if (raw is not null && Guid.TryParse(raw.ToString(), out var id))
            {
                result.Add(id);
            }
        }
        return result;
    }

    public void BulkUpsertEmbeddings(IReadOnlyList<EmbeddingUpsert> embeddings)
    {
        if (embeddings.Count == 0)
        {
            return;
        }

        EnsureDatabaseAndSchema();

        const string sql = """
            INSERT INTO app_nomenclature_embeddings
                (item_id, provider, dimensions, source_text, embedding, created_at_utc, updated_at_utc)
            VALUES
                (@item_id, @provider, @dimensions, @source_text, @embedding, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
                dimensions = VALUES(dimensions),
                source_text = VALUES(source_text),
                embedding = VALUES(embedding),
                updated_at_utc = UTC_TIMESTAMP(6);
            """;

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options, useDatabase: true, MysqlConnectTimeoutSeconds, MysqlEmbeddingCommandTimeoutSeconds);
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var emb in embeddings)
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = sql;
                cmd.CommandTimeout = MysqlEmbeddingCommandTimeoutSeconds;
                cmd.Parameters.AddWithValue("@item_id", emb.ItemId.ToString());
                cmd.Parameters.AddWithValue("@provider", emb.Provider);
                cmd.Parameters.AddWithValue("@dimensions", emb.Dimensions);
                cmd.Parameters.AddWithValue("@source_text", emb.SourceText ?? string.Empty);

                var blob = new byte[emb.Embedding.Length * sizeof(float)];
                Buffer.BlockCopy(emb.Embedding, 0, blob, 0, blob.Length);
                cmd.Parameters.AddWithValue("@embedding", blob);

                cmd.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch (MySqlException) { }
            throw;
        }
    }
}
