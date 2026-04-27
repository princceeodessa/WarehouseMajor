namespace WarehouseAutomatisaion.Infrastructure.Persistence.Sql;

public static class SqlAssetCatalog
{
    public const string MySqlOperationalSchemaFileName = "mysql-operational-schema.sql";

    public static string GetMySqlOperationalSchemaPath(string? baseDirectory = null)
    {
        var rootDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;

        return Path.GetFullPath(
            Path.Combine(rootDirectory, "Persistence", "Sql", MySqlOperationalSchemaFileName));
    }
}
