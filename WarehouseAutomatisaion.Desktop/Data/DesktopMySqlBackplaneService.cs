using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class DesktopMySqlBackplaneService
{
    private const string ErrorLogName = "desktop-mysql-backplane-error.log";
    private const int MysqlConnectTimeoutSeconds = 2;
    private const int MysqlDefaultCommandTimeoutSeconds = 15;
    private const int MysqlSnapshotCommandTimeoutSeconds = 60;
    private const int ConnectionBackoffSeconds = 45;

    private static readonly object DefaultInstanceSync = new();
    private static DesktopMySqlBackplaneService? s_defaultInstance;
    private static readonly object ConnectionStateSync = new();
    private static DateTime s_connectionBackoffUntilUtc;
    private static readonly object ErrorLogSync = new();
    private static DateTime s_lastErrorLogUtc;
    private static string s_lastErrorSignature = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly OperationalMySqlDesktopOptions _options;
    private bool _schemaEnsured;

    public DesktopMySqlBackplaneService(OperationalMySqlDesktopOptions options)
    {
        _options = options;
    }

    public bool IsConnectionHealthy => !IsConnectionBackoffActive();

    public static DesktopMySqlBackplaneService CreateDefault()
    {
        var options = DesktopRemoteDatabaseSettings.TryBuildOptions();
        if (options is null)
        {
            throw new InvalidOperationException("Remote MySQL is not configured for the desktop client.");
        }

        return new DesktopMySqlBackplaneService(options);
    }

    public static DesktopMySqlBackplaneService? TryCreateDefault()
    {
        lock (DefaultInstanceSync)
        {
            if (s_defaultInstance is not null)
            {
                return s_defaultInstance;
            }
        }

        if (IsConnectionBackoffActive())
        {
            return null;
        }

        var options = DesktopRemoteDatabaseSettings.TryBuildOptions();
        if (options is null)
        {
            return null;
        }

        try
        {
            var created = new DesktopMySqlBackplaneService(options);
            lock (DefaultInstanceSync)
            {
                s_defaultInstance ??= created;
                return s_defaultInstance;
            }
        }
        catch
        {
            return null;
        }
    }

    public void EnsureReady(string actorName)
    {
        EnsureDatabaseAndSchema();
        _ = EnsureUserProfile(actorName);
    }

    public DesktopAppUserProfile? TryEnsureUserProfile(string actorName)
    {
        try
        {
            return EnsureUserProfile(actorName);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public DesktopAppUserProfile EnsureUserProfile(string actorName)
    {
        EnsureDatabaseAndSchema();

        var normalizedUserName = NormalizeUserName(actorName);
        var userId = CreateDeterministicGuid($"desktop-user:{normalizedUserName}");

        var script = new StringBuilder();
        script.AppendLine("START TRANSACTION;");
        script.AppendLine(BuildRoleSeedSql());
        script.AppendLine($"""
            INSERT INTO app_users (
                id,
                user_name,
                display_name,
                is_active,
                created_at_utc,
                updated_at_utc,
                last_seen_at_utc
            )
            VALUES (
                {SqlUtf8TextExpression(userId.ToString())},
                {SqlUtf8TextExpression(normalizedUserName)},
                {SqlUtf8TextExpression(normalizedUserName)},
                1,
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            )
            ON DUPLICATE KEY UPDATE
                display_name = {SqlUtf8TextExpression(normalizedUserName)},
                is_active = 1,
                updated_at_utc = UTC_TIMESTAMP(6),
                last_seen_at_utc = UTC_TIMESTAMP(6);
            """);

        var roleCode = DesktopRoleCatalog.ResolveDefaultRoleCode(normalizedUserName);
        var roleId = CreateDeterministicGuid($"desktop-role:{roleCode}");
        script.AppendLine($"""
            DELETE FROM app_user_roles
            WHERE user_id = {SqlUtf8TextExpression(userId.ToString())};

            INSERT INTO app_user_roles (
                user_id,
                role_id,
                assigned_at_utc,
                assigned_by
            )
            VALUES (
                {SqlUtf8TextExpression(userId.ToString())},
                {SqlUtf8TextExpression(roleId.ToString())},
                UTC_TIMESTAMP(6),
                {SqlUtf8TextExpression(normalizedUserName)}
            );
            """);

        script.AppendLine("COMMIT;");
        ExecuteSqlNonQuery(script.ToString(), useDatabase: true);

        return LoadUserProfile(normalizedUserName);
    }

    public T? TryLoadModuleSnapshot<T>(string moduleCode)
    {
        var record = TryLoadModuleSnapshotRecord<T>(moduleCode);
        return record is null ? default : record.Snapshot;
    }

    public DesktopModuleSnapshotRecord<T>? TryLoadModuleSnapshotRecord<T>(string moduleCode)
    {
        try
        {
            EnsureDatabaseAndSchema();
            var sql = BuildSnapshotRecordSql(moduleCode, includePayload: true);
            var output = ExecuteSqlScalar(sql, useDatabase: true, commandTimeoutSeconds: MysqlSnapshotCommandTimeoutSeconds).Trim();
            if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var row = JsonSerializer.Deserialize<DesktopModuleSnapshotRow>(output, JsonOptions);
            if (row is null || string.IsNullOrWhiteSpace(row.PayloadJson))
            {
                return null;
            }

            var snapshot = JsonSerializer.Deserialize<T>(row.PayloadJson, JsonOptions);
            if (snapshot is null)
            {
                return null;
            }

            return new DesktopModuleSnapshotRecord<T>(snapshot, CreateSnapshotMetadata(moduleCode, row));
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public DesktopModuleSnapshotMetadata? TryLoadModuleSnapshotMetadata(string moduleCode)
    {
        try
        {
            EnsureDatabaseAndSchema();
            var sql = BuildSnapshotRecordSql(moduleCode, includePayload: false);
            var output = ExecuteSqlScalar(sql, useDatabase: true, commandTimeoutSeconds: MysqlSnapshotCommandTimeoutSeconds).Trim();
            if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var row = JsonSerializer.Deserialize<DesktopModuleSnapshotRow>(output, JsonOptions);
            return row is null ? null : CreateSnapshotMetadata(moduleCode, row);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public bool TrySaveModuleSnapshot<T>(
        string moduleCode,
        T snapshot,
        string actorName,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        try
        {
            SaveModuleSnapshot(moduleCode, snapshot, actorName, auditEvents);
            return true;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return false;
        }
    }

    public DesktopModuleSnapshotSaveResult TrySaveModuleSnapshot<T>(
        string moduleCode,
        T snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        try
        {
            var metadata = SaveModuleSnapshot(moduleCode, snapshot, actorName, expectedMetadata, auditEvents);
            return DesktopModuleSnapshotSaveResult.Saved(metadata);
        }
        catch (DesktopModuleSnapshotConflictException exception)
        {
            TryWriteErrorLog(exception);
            return DesktopModuleSnapshotSaveResult.Conflict(exception.ServerMetadata);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return DesktopModuleSnapshotSaveResult.Failed(exception.Message);
        }
    }

    public void SaveModuleSnapshot<T>(
        string moduleCode,
        T snapshot,
        string actorName,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(actorName);

        var normalizedModuleCode = NormalizeModuleCode(moduleCode);
        var normalizedActor = NormalizeUserName(actorName);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var payloadHash = ComputeSha256(json);

        var script = new StringBuilder();
        script.AppendLine("START TRANSACTION;");
        script.AppendLine($"""
            INSERT INTO app_module_snapshots (
                module_code,
                payload_json,
                payload_hash,
                version_no,
                updated_by,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                {SqlUtf8TextExpression(normalizedModuleCode)},
                {SqlJsonExpression(json)},
                {SqlUtf8TextExpression(payloadHash)},
                1,
                {SqlUtf8TextExpression(normalizedActor)},
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            )
            ON DUPLICATE KEY UPDATE
                payload_json = {SqlJsonExpression(json)},
                version_no = CASE
                    WHEN payload_hash <> {SqlUtf8TextExpression(payloadHash)} THEN version_no + 1
                    ELSE version_no
                END,
                payload_hash = {SqlUtf8TextExpression(payloadHash)},
                updated_by = {SqlUtf8TextExpression(normalizedActor)},
                updated_at_utc = UTC_TIMESTAMP(6);
            """);

        if (auditEvents is not null)
        {
            script.AppendLine($"""
                DELETE FROM app_audit_events
                WHERE module_code = {SqlUtf8TextExpression(normalizedModuleCode)};
                """);

            foreach (var eventSeed in auditEvents)
            {
                script.AppendLine(BuildAuditInsertSql(normalizedModuleCode, eventSeed));
            }
        }

        script.AppendLine("COMMIT;");
        ExecuteSqlNonQuery(script.ToString(), useDatabase: true, commandTimeoutSeconds: MysqlSnapshotCommandTimeoutSeconds);
    }

    public DesktopModuleSnapshotMetadata SaveModuleSnapshot<T>(
        string moduleCode,
        T snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(actorName);

        var normalizedModuleCode = NormalizeModuleCode(moduleCode);
        var normalizedActor = NormalizeUserName(actorName);
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var payloadHash = ComputeSha256(json);

        int affectedRows;
        if (expectedMetadata is null)
        {
            var insertSql = $"""
                INSERT IGNORE INTO app_module_snapshots (
                    module_code,
                    payload_json,
                    payload_hash,
                    version_no,
                    updated_by,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    {SqlUtf8TextExpression(normalizedModuleCode)},
                    {SqlJsonExpression(json)},
                    {SqlUtf8TextExpression(payloadHash)},
                    1,
                    {SqlUtf8TextExpression(normalizedActor)},
                    UTC_TIMESTAMP(6),
                    UTC_TIMESTAMP(6)
                );
                """;
            affectedRows = ExecuteSqlAffectedRows(insertSql, useDatabase: true, commandTimeoutSeconds: MysqlSnapshotCommandTimeoutSeconds);
        }
        else
        {
            var updateSql = $"""
                UPDATE app_module_snapshots
                SET
                    payload_json = {SqlJsonExpression(json)},
                    version_no = CASE
                        WHEN payload_hash <> {SqlUtf8TextExpression(payloadHash)} THEN version_no + 1
                        ELSE version_no
                    END,
                    payload_hash = {SqlUtf8TextExpression(payloadHash)},
                    updated_by = {SqlUtf8TextExpression(normalizedActor)},
                    updated_at_utc = UTC_TIMESTAMP(6)
                WHERE module_code = {SqlUtf8TextExpression(normalizedModuleCode)}
                  AND version_no = {expectedMetadata.VersionNo.ToString(CultureInfo.InvariantCulture)}
                  AND payload_hash = {SqlUtf8TextExpression(expectedMetadata.PayloadHash)};
                """;
            affectedRows = ExecuteSqlAffectedRows(updateSql, useDatabase: true, commandTimeoutSeconds: MysqlSnapshotCommandTimeoutSeconds);
        }

        if (affectedRows <= 0)
        {
            throw new DesktopModuleSnapshotConflictException(
                $"Module snapshot '{normalizedModuleCode}' was changed by another client.",
                TryLoadModuleSnapshotMetadata(normalizedModuleCode));
        }

        if (auditEvents is not null)
        {
            ReplaceAuditEvents(normalizedModuleCode, auditEvents);
        }

        return TryLoadModuleSnapshotMetadata(normalizedModuleCode)
               ?? new DesktopModuleSnapshotMetadata(normalizedModuleCode, 1, payloadHash, normalizedActor, DateTime.UtcNow);
    }

    public IReadOnlyList<DesktopAuditEventRecord> TryLoadAuditEvents(int limit = 2000)
    {
        try
        {
            EnsureDatabaseAndSchema();
            var safeLimit = Math.Clamp(limit, 1, 5000);
            var sql = $"""
                SELECT COALESCE(
                    CAST(
                        JSON_ARRAYAGG(
                            JSON_OBJECT(
                                'Id', data.id,
                                'ModuleCode', data.module_code,
                                'ModuleCaption', data.module_caption,
                                'LoggedAtUtc', DATE_FORMAT(data.logged_at_utc, '%Y-%m-%dT%H:%i:%s.%fZ'),
                                'Actor', data.actor_user_name,
                                'EntityType', data.entity_type,
                                'EntityId', data.entity_id,
                                'EntityNumber', data.entity_number,
                                'Action', data.action_text,
                                'Result', data.result_text,
                                'Message', data.message_text
                            )
                        ) AS CHAR CHARACTER SET utf8mb4
                    ),
                    '[]'
                )
                FROM (
                    SELECT
                        id,
                        module_code,
                        module_caption,
                        logged_at_utc,
                        actor_user_name,
                        entity_type,
                        entity_id,
                        entity_number,
                        action_text,
                        result_text,
                        message_text
                    FROM app_audit_events
                    ORDER BY logged_at_utc DESC, module_code, entity_number
                    LIMIT {safeLimit.ToString(CultureInfo.InvariantCulture)}
                ) AS data;
                """;

            return QueryJsonArray<DesktopAuditEventRow>(sql)
                .Select(row => new DesktopAuditEventRecord(
                    ParseGuid(row.Id, row.EntityNumber),
                    row.ModuleCode ?? string.Empty,
                    string.IsNullOrWhiteSpace(row.ModuleCaption) ? MapModuleCaption(row.ModuleCode) : row.ModuleCaption!,
                    ParseUtc(row.LoggedAtUtc),
                    row.Actor ?? string.Empty,
                    row.EntityType ?? string.Empty,
                    ParseGuid(row.EntityId, row.EntityNumber),
                    row.EntityNumber ?? string.Empty,
                    row.Action ?? string.Empty,
                    row.Result ?? string.Empty,
                    row.Message ?? string.Empty))
                .ToArray();
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<DesktopAuditEventRecord>();
        }
    }

    public IReadOnlyList<DesktopBackplaneSearchHit> TrySearch(string query, int limit = 40)
    {
        try
        {
            EnsureDatabaseAndSchema();

            var normalized = query.Trim();
            if (normalized.Length == 0)
            {
                return Array.Empty<DesktopBackplaneSearchHit>();
            }

            var likeValue = $"%{normalized}%";
            var safeLimit = Math.Clamp(limit, 1, 200);
            var sql = $"""\\n                SELECT COALESCE(\\n                    CAST(\\n                        JSON_ARRAYAGG(\\n                            JSON_OBJECT(\\n                                'Scope', data.scope_name,\\n                                'ModuleCode', data.module_code,\\n                                'Title', data.title_text,\\n                                'Subtitle', data.subtitle_text,\\n                                'Reference', data.reference_text\\n                            )\\n                        ) AS CHAR CHARACTER SET utf8mb4\\n                    ),\\n                    '[]'\\n                )\\n                FROM (\\n                    SELECT\\n                        'audit' AS scope_name,\\n                        module_code,\\n                        COALESCE(entity_number, entity_type, module_caption) AS title_text,\\n                        CONCAT(actor_user_name, ' / ', action_text, ' / ', result_text) AS subtitle_text,\\n                        COALESCE(message_text, '') AS reference_text\\n                    FROM app_audit_events\\n                    WHERE entity_number LIKE {SqlUtf8TextExpression(likeValue)}\\n                        OR entity_type LIKE {SqlUtf8TextExpression(likeValue)}\\n                        OR actor_user_name LIKE {SqlUtf8TextExpression(likeValue)}\\n                        OR action_text LIKE {SqlUtf8TextExpression(likeValue)}\\n                        OR message_text LIKE {SqlUtf8TextExpression(likeValue)}\\n\\n                    UNION ALL\\n\\n                    SELECT\\n                        'snapshot' AS scope_name,\\n                        module_code,\\n                        CONCAT('Модуль ', module_code) AS title_text,\\n                        CONCAT('Версия ', version_no) AS subtitle_text,\\n                        LEFT(CAST(payload_json AS CHAR CHARACTER SET utf8mb4), 512) AS reference_text\\n                    FROM app_module_snapshots\\n                    WHERE CAST(payload_json AS CHAR CHARACTER SET utf8mb4) LIKE {SqlUtf8TextExpression(likeValue)}\\n                ) AS data\\n                LIMIT {safeLimit.ToString(CultureInfo.InvariantCulture)};\\n                """;

            return QueryJsonArray<DesktopSearchHitRow>(sql)
                .Select(row => new DesktopBackplaneSearchHit(
                    row.Scope ?? string.Empty,
                    row.ModuleCode ?? string.Empty,
                    row.Title ?? string.Empty,
                    row.Subtitle ?? string.Empty,
                    row.Reference ?? string.Empty))
                .ToArray();
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<DesktopBackplaneSearchHit>();
        }
    }

    public DesktopModuleExportRecord? TryExportModuleSnapshot(string moduleCode, string actorName)
    {
        try
        {
            return ExportModuleSnapshot(moduleCode, actorName);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public DesktopModuleExportRecord ExportModuleSnapshot(string moduleCode, string actorName)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(actorName);

        var normalizedModuleCode = NormalizeModuleCode(moduleCode);
        var sql = $"""
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'ModuleCode', data.module_code,
                            'PayloadJson', data.payload_json_text,
                            'VersionNo', data.version_no
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    module_code,
                    CAST(payload_json AS CHAR CHARACTER SET utf8mb4) AS payload_json_text,
                    version_no
                FROM app_module_snapshots
                WHERE module_code = {SqlUtf8TextExpression(normalizedModuleCode)}
                LIMIT 1
            ) AS data;
            """;

        var snapshot = QueryJsonArray<DesktopModuleSnapshotExportRow>(sql).FirstOrDefault();
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.PayloadJson))
        {
            throw new InvalidOperationException($"Module snapshot `{normalizedModuleCode}` was not found.");
        }

        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        var exportDirectory = Path.Combine(root, "app_data", "saved_exports", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(exportDirectory);

        var versionNo = Math.Max(1, snapshot.VersionNo);
        var fileName = $"{normalizedModuleCode}-snapshot-v{versionNo}-{DateTime.Now:HHmmss}.json";
        var exportPath = Path.Combine(exportDirectory, fileName);
        File.WriteAllText(exportPath, snapshot.PayloadJson, new UTF8Encoding(false));

        var exportId = Guid.NewGuid();
        var insertSql = $"""
            INSERT INTO app_saved_exports (
                id,
                module_code,
                export_kind,
                file_name,
                storage_path,
                created_by,
                created_at_utc
            )
            VALUES (
                {SqlUtf8TextExpression(exportId.ToString())},
                {SqlUtf8TextExpression(normalizedModuleCode)},
                {SqlUtf8TextExpression("module-snapshot-json")},
                {SqlUtf8TextExpression(fileName)},
                {SqlUtf8TextExpression(exportPath)},
                {SqlUtf8TextExpression(NormalizeUserName(actorName))},
                UTC_TIMESTAMP(6)
            );
            """;
        ExecuteSqlNonQuery(insertSql, useDatabase: true);

        return new DesktopModuleExportRecord(
            exportId,
            normalizedModuleCode,
            fileName,
            exportPath,
            versionNo,
            DateTime.Now);
    }

    private DesktopAppUserProfile LoadUserProfile(string userName)
    {
        var sql = $"""
            SELECT COALESCE(
                CAST(
                    JSON_ARRAYAGG(
                        JSON_OBJECT(
                            'Id', data.id,
                            'UserName', data.user_name,
                            'DisplayName', data.display_name,
                            'IsActive', data.is_active,
                            'Roles', data.roles_json
                        )
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                '[]'
            )
            FROM (
                SELECT
                    u.id,
                    u.user_name,
                    u.display_name,
                    u.is_active,
                    JSON_ARRAYAGG(r.role_code) AS roles_json
                FROM app_users u
                LEFT JOIN app_user_roles ur ON ur.user_id = u.id
                LEFT JOIN app_roles r ON r.id = ur.role_id
                WHERE u.user_name = {SqlUtf8TextExpression(userName)}
                GROUP BY u.id, u.user_name, u.display_name, u.is_active
                LIMIT 1
            ) AS data;
            """;

        var row = QueryJsonArray<DesktopUserProfileRow>(sql).First();
        var roles = (row.Roles ?? Array.Empty<string>())
            .Select(DesktopRoleCatalog.NormalizeRoleCode)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (roles.Length == 0)
        {
            roles = [DesktopRoleCatalog.ResolveDefaultRoleCode(row.UserName ?? userName)];
        }

        return new DesktopAppUserProfile(
            ParseGuid(row.Id, row.UserName),
            row.UserName ?? string.Empty,
            row.DisplayName ?? row.UserName ?? string.Empty,
            row.IsActive != 0,
            roles);
    }

    private List<T> QueryJsonArray<T>(string sql)
    {
        var output = ExecuteSqlScalar(sql, useDatabase: true).Trim();
        if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<T>>(output, JsonOptions) ?? [];
    }

    private static string BuildSnapshotRecordSql(string moduleCode, bool includePayload)
    {
        var payloadProperty = includePayload
            ? $"""
                      'PayloadJson', CAST(payload_json AS CHAR CHARACTER SET utf8mb4),
                """
            : string.Empty;

        return $"""
            SELECT COALESCE(
                CAST(
                    JSON_OBJECT(
                        {payloadProperty}
                        'VersionNo', version_no,
                        'PayloadHash', payload_hash,
                        'UpdatedBy', updated_by,
                        'UpdatedAtUtc', DATE_FORMAT(updated_at_utc, '%Y-%m-%dT%H:%i:%s.%fZ')
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                'null'
            )
            FROM app_module_snapshots
            WHERE module_code = {SqlUtf8TextExpression(NormalizeModuleCode(moduleCode))}
            LIMIT 1;
            """;
    }

    private static DesktopModuleSnapshotMetadata CreateSnapshotMetadata(string moduleCode, DesktopModuleSnapshotRow row)
    {
        return new DesktopModuleSnapshotMetadata(
            NormalizeModuleCode(moduleCode),
            Math.Max(0, row.VersionNo),
            row.PayloadHash ?? string.Empty,
            row.UpdatedBy ?? string.Empty,
            ParseUtcValue(row.UpdatedAtUtc));
    }

    private void ReplaceAuditEvents(string moduleCode, IEnumerable<DesktopAuditEventSeed> auditEvents)
    {
        var script = new StringBuilder();
        script.AppendLine("START TRANSACTION;");
        script.AppendLine($"""
            DELETE FROM app_audit_events
            WHERE module_code = {SqlUtf8TextExpression(moduleCode)};
            """);

        foreach (var eventSeed in auditEvents)
        {
            script.AppendLine(BuildAuditInsertSql(moduleCode, eventSeed));
        }

        script.AppendLine("COMMIT;");
        ExecuteSqlNonQuery(script.ToString(), useDatabase: true, commandTimeoutSeconds: MysqlSnapshotCommandTimeoutSeconds);
    }

    private void EnsureDatabaseAndSchema()
    {
        if (_schemaEnsured)
        {
            return;
        }

        ValidateDatabaseName(_options.DatabaseName);

        var script = new StringBuilder();
        script.AppendLine("SET NAMES utf8mb4;");
        script.AppendLine($"CREATE DATABASE IF NOT EXISTS `{_options.DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");
        script.AppendLine($"USE `{_options.DatabaseName}`;");
        script.AppendLine(AppSchemaSql);
        ExecuteSqlNonQuery(script.ToString(), useDatabase: false);
        _schemaEnsured = true;
    }

    private string ExecuteSqlScalar(string sql, bool useDatabase, int commandTimeoutSeconds = MysqlDefaultCommandTimeoutSeconds)
    {
        ThrowIfConnectionBackoffActive();

        try
        {
            var output = DesktopMySqlCommandRunner.ExecuteScalar(
                _options,
                sql,
                useDatabase,
                MysqlConnectTimeoutSeconds,
                commandTimeoutSeconds);
            ResetConnectionBackoff();
            return output;
        }
        catch (Exception exception)
        {
            if (IsConnectionFailure(exception.Message))
            {
                EnterConnectionBackoff();
            }

            throw new InvalidOperationException($"Desktop MySQL backplane query failed.{Environment.NewLine}{exception.Message}", exception);
        }
    }

    private void ExecuteSqlNonQuery(string sql, bool useDatabase, int commandTimeoutSeconds = MysqlDefaultCommandTimeoutSeconds)
    {
        _ = ExecuteSqlAffectedRows(sql, useDatabase, commandTimeoutSeconds);
    }

    private int ExecuteSqlAffectedRows(string sql, bool useDatabase, int commandTimeoutSeconds = MysqlDefaultCommandTimeoutSeconds)
    {
        ThrowIfConnectionBackoffActive();

        try
        {
            var affectedRows = DesktopMySqlCommandRunner.ExecuteNonQuery(
                _options,
                sql,
                useDatabase,
                MysqlConnectTimeoutSeconds,
                commandTimeoutSeconds);
            ResetConnectionBackoff();
            return affectedRows;
        }
        catch (Exception exception)
        {
            if (IsConnectionFailure(exception.Message))
            {
                EnterConnectionBackoff();
            }

            throw new InvalidOperationException($"Desktop MySQL backplane command failed.{Environment.NewLine}{exception.Message}", exception);
        }
    }

    private string BuildAuditInsertSql(string moduleCode, DesktopAuditEventSeed eventSeed)
    {
        var eventId = eventSeed.Id == Guid.Empty
            ? CreateDeterministicGuid($"{moduleCode}:{eventSeed.EntityId}:{eventSeed.Action}:{eventSeed.LoggedAtUtc:O}")
            : eventSeed.Id;
        var loggedAtUtc = eventSeed.LoggedAtUtc.Kind == DateTimeKind.Utc
            ? eventSeed.LoggedAtUtc
            : eventSeed.LoggedAtUtc.ToUniversalTime();

        return $"""
            INSERT INTO app_audit_events (
                id,
                module_code,
                module_caption,
                logged_at_utc,
                actor_user_name,
                entity_type,
                entity_id,
                entity_number,
                action_text,
                result_text,
                message_text
            )
            VALUES (
                {SqlUtf8TextExpression(eventId.ToString())},
                {SqlUtf8TextExpression(moduleCode)},
                {SqlUtf8TextExpression(MapModuleCaption(moduleCode))},
                STR_TO_DATE({SqlUtf8TextExpression(loggedAtUtc.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture))}, '%Y-%m-%d %H:%i:%s.%f'),
                {SqlUtf8TextExpression(NormalizeUserName(eventSeed.Actor))},
                {SqlUtf8TextExpression(eventSeed.EntityType)},
                {SqlUtf8TextExpression(eventSeed.EntityId.ToString())},
                {SqlUtf8TextExpression(eventSeed.EntityNumber)},
                {SqlUtf8TextExpression(eventSeed.Action)},
                {SqlUtf8TextExpression(eventSeed.Result)},
                {SqlUtf8TextExpression(eventSeed.Message)}
            );
            """;
    }

    private static string BuildRoleSeedSql()
    {
        var builder = new StringBuilder();
        foreach (var role in DesktopRoleCatalog.All)
        {
            builder.AppendLine($"""
                INSERT IGNORE INTO app_roles (
                    id,
                    role_code,
                    display_name,
                    description_text,
                    created_at_utc
                )
                VALUES (
                    {SqlUtf8TextExpression(CreateDeterministicGuid($"desktop-role:{role.RoleCode}").ToString())},
                    {SqlUtf8TextExpression(role.RoleCode)},
                    {SqlUtf8TextExpression(role.DisplayName)},
                    {SqlUtf8TextExpression(role.Description)},
                    UTC_TIMESTAMP(6)
                );
                """);
        }

        return builder.ToString();
    }

    private static string NormalizeModuleCode(string moduleCode)
    {
        return string.IsNullOrWhiteSpace(moduleCode)
            ? "general"
            : moduleCode.Trim().ToLowerInvariant();
    }

    private static string NormalizeUserName(string actorName)
    {
        return string.IsNullOrWhiteSpace(actorName)
            ? Environment.UserName
            : actorName.Trim();
    }

    private static string MapModuleCaption(string? moduleCode)
    {
        return NormalizeModuleCode(moduleCode ?? string.Empty) switch
        {
            "sales" => "Продажи",
            "purchasing" => "Закупки",
            "warehouse" => "Склад",
            "catalog" => "Номенклатура",
            "audit" => "Аудит",
            _ => "Приложение"
        };
    }

    private static string SqlUtf8TextExpression(string? value)
    {
        return value is null
            ? "NULL"
            : $"CONVERT(0x{Convert.ToHexString(Encoding.UTF8.GetBytes(value))} USING utf8mb4)";
    }

    private static string SqlJsonExpression(string json)
    {
        return $"CAST(CONVERT(0x{Convert.ToHexString(Encoding.UTF8.GetBytes(json))} USING utf8mb4) AS JSON)";
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static DateTime ParseUtc(string? rawValue)
    {
        if (DateTime.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToLocalTime();
        }

        return DateTime.Now;
    }

    private static DateTime ParseUtcValue(string? rawValue)
    {
        if (DateTime.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        return DateTime.UtcNow;
    }

    private static Guid ParseGuid(string? rawValue, string? fallbackSeed)
    {
        return Guid.TryParse(rawValue, out var parsed)
            ? parsed
            : CreateDeterministicGuid(fallbackSeed ?? Guid.NewGuid().ToString("N"));
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> buffer = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(buffer);
        buffer[7] = (byte)((buffer[7] & 0x0F) | 0x40);
        buffer[8] = (byte)((buffer[8] & 0x3F) | 0x80);
        return new Guid(buffer);
    }

    private static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Desktop MySQL backplane database name is required.");
        }

        if (databaseName.Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
        {
            throw new InvalidOperationException("Desktop MySQL backplane database name can contain only letters, digits and underscore.");
        }
    }

    private static void TryWriteErrorLog(Exception exception)
    {
        if (!ShouldWriteError(exception))
        {
            return;
        }

        try
        {
            var root = WorkspacePathResolver.ResolveWorkspaceRoot();
            var path = Path.Combine(root, "app_data", ErrorLogName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exception.ToString(), new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static bool ShouldWriteError(Exception exception)
    {
        var signature = $"{exception.GetType().FullName}:{exception.Message}";
        lock (ErrorLogSync)
        {
            var now = DateTime.UtcNow;
            if (string.Equals(signature, s_lastErrorSignature, StringComparison.Ordinal)
                && (now - s_lastErrorLogUtc).TotalSeconds < 30)
            {
                return false;
            }

            s_lastErrorSignature = signature;
            s_lastErrorLogUtc = now;
            return true;
        }
    }

    private static bool IsConnectionFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.Contains("ERROR 2003", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Can't connect to MySQL server", StringComparison.OrdinalIgnoreCase)
               || message.Contains("Lost connection", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConnectionBackoffActive()
    {
        lock (ConnectionStateSync)
        {
            return DateTime.UtcNow < s_connectionBackoffUntilUtc;
        }
    }

    private static void ThrowIfConnectionBackoffActive()
    {
        lock (ConnectionStateSync)
        {
            if (DateTime.UtcNow < s_connectionBackoffUntilUtc)
            {
                throw new InvalidOperationException("Desktop MySQL backplane is temporarily unavailable after connection failures.");
            }
        }
    }

    private static void EnterConnectionBackoff()
    {
        lock (ConnectionStateSync)
        {
            s_connectionBackoffUntilUtc = DateTime.UtcNow.AddSeconds(ConnectionBackoffSeconds);
        }
    }

    private static void ResetConnectionBackoff()
    {
        lock (ConnectionStateSync)
        {
            s_connectionBackoffUntilUtc = DateTime.MinValue;
        }
    }

    private const string AppSchemaSql = """
        CREATE TABLE IF NOT EXISTS app_users (
            id CHAR(36) NOT NULL,
            user_name VARCHAR(128) NOT NULL,
            display_name VARCHAR(256) NOT NULL,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            last_seen_at_utc DATETIME(6) NULL,
            CONSTRAINT pk_app_users PRIMARY KEY (id),
            CONSTRAINT uq_app_users_user_name UNIQUE (user_name)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_roles (
            id CHAR(36) NOT NULL,
            role_code VARCHAR(64) NOT NULL,
            display_name VARCHAR(128) NOT NULL,
            description_text VARCHAR(512) NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_roles PRIMARY KEY (id),
            CONSTRAINT uq_app_roles_role_code UNIQUE (role_code)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_user_roles (
            user_id CHAR(36) NOT NULL,
            role_id CHAR(36) NOT NULL,
            assigned_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            assigned_by VARCHAR(128) NULL,
            CONSTRAINT pk_app_user_roles PRIMARY KEY (user_id, role_id),
            CONSTRAINT fk_app_user_roles_user FOREIGN KEY (user_id) REFERENCES app_users (id) ON DELETE CASCADE,
            CONSTRAINT fk_app_user_roles_role FOREIGN KEY (role_id) REFERENCES app_roles (id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_module_snapshots (
            module_code VARCHAR(64) NOT NULL,
            payload_json JSON NOT NULL,
            payload_hash CHAR(64) NOT NULL,
            version_no INT UNSIGNED NOT NULL DEFAULT 1,
            updated_by VARCHAR(128) NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_module_snapshots PRIMARY KEY (module_code)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_audit_events (
            id CHAR(36) NOT NULL,
            module_code VARCHAR(64) NOT NULL,
            module_caption VARCHAR(128) NOT NULL,
            logged_at_utc DATETIME(6) NOT NULL,
            actor_user_name VARCHAR(128) NOT NULL,
            entity_type VARCHAR(128) NOT NULL,
            entity_id CHAR(36) NOT NULL,
            entity_number VARCHAR(128) NULL,
            action_text VARCHAR(256) NOT NULL,
            result_text VARCHAR(128) NOT NULL,
            message_text TEXT NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_audit_events PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_document_attachments (
            id CHAR(36) NOT NULL,
            module_code VARCHAR(64) NOT NULL,
            entity_type VARCHAR(128) NOT NULL,
            entity_id CHAR(36) NOT NULL,
            entity_number VARCHAR(128) NULL,
            original_file_name VARCHAR(260) NOT NULL,
            storage_path VARCHAR(1024) NOT NULL,
            content_type VARCHAR(128) NULL,
            content_length BIGINT NOT NULL DEFAULT 0,
            checksum_sha256 CHAR(64) NULL,
            created_by VARCHAR(128) NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            is_deleted TINYINT(1) NOT NULL DEFAULT 0,
            CONSTRAINT pk_app_document_attachments PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_saved_exports (
            id CHAR(36) NOT NULL,
            module_code VARCHAR(64) NOT NULL,
            export_kind VARCHAR(64) NOT NULL,
            file_name VARCHAR(260) NOT NULL,
            storage_path VARCHAR(1024) NOT NULL,
            created_by VARCHAR(128) NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_saved_exports PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
        """;
}

public sealed record DesktopAppUserProfile(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    IReadOnlyList<string> Roles);

public sealed record DesktopAuditEventSeed(
    Guid Id,
    DateTime LoggedAtUtc,
    string Actor,
    string EntityType,
    Guid EntityId,
    string EntityNumber,
    string Action,
    string Result,
    string Message);

public sealed record DesktopAuditEventRecord(
    Guid Id,
    string ModuleCode,
    string ModuleCaption,
    DateTime LoggedAtLocal,
    string Actor,
    string EntityType,
    Guid EntityId,
    string EntityNumber,
    string Action,
    string Result,
    string Message);

public sealed record DesktopBackplaneSearchHit(
    string Scope,
    string ModuleCode,
    string Title,
    string Subtitle,
    string Reference);

public sealed record DesktopModuleExportRecord(
    Guid Id,
    string ModuleCode,
    string FileName,
    string StoragePath,
    int VersionNo,
    DateTime ExportedAtLocal);

public sealed record DesktopModuleSnapshotMetadata(
    string ModuleCode,
    int VersionNo,
    string PayloadHash,
    string UpdatedBy,
    DateTime UpdatedAtUtc);

public sealed record DesktopModuleSnapshotRecord<T>(
    T Snapshot,
    DesktopModuleSnapshotMetadata Metadata);

public sealed record DesktopModuleSnapshotSaveResult(
    DesktopModuleSnapshotSaveState State,
    DesktopModuleSnapshotMetadata? Metadata,
    string Message)
{
    public bool Succeeded => State == DesktopModuleSnapshotSaveState.Saved;

    public static DesktopModuleSnapshotSaveResult Saved(DesktopModuleSnapshotMetadata metadata)
    {
        return new DesktopModuleSnapshotSaveResult(DesktopModuleSnapshotSaveState.Saved, metadata, string.Empty);
    }

    public static DesktopModuleSnapshotSaveResult Conflict(DesktopModuleSnapshotMetadata? serverMetadata)
    {
        return new DesktopModuleSnapshotSaveResult(
            DesktopModuleSnapshotSaveState.Conflict,
            serverMetadata,
            "Данные на сервере изменены другим рабочим местом.");
    }

    public static DesktopModuleSnapshotSaveResult Failed(string message)
    {
        return new DesktopModuleSnapshotSaveResult(
            DesktopModuleSnapshotSaveState.Failed,
            null,
            string.IsNullOrWhiteSpace(message) ? "Не удалось сохранить данные на сервере." : message);
    }
}

public enum DesktopModuleSnapshotSaveState
{
    Saved,
    Conflict,
    Failed
}

internal sealed class DesktopModuleSnapshotConflictException : Exception
{
    public DesktopModuleSnapshotConflictException(string message, DesktopModuleSnapshotMetadata? serverMetadata)
        : base(message)
    {
        ServerMetadata = serverMetadata;
    }

    public DesktopModuleSnapshotMetadata? ServerMetadata { get; }
}

internal sealed class DesktopUserProfileRow
{
    public string? Id { get; set; }

    public string? UserName { get; set; }

    public string? DisplayName { get; set; }

    public int IsActive { get; set; }

    public string[]? Roles { get; set; }
}

internal sealed class DesktopAuditEventRow
{
    public string? Id { get; set; }

    public string? ModuleCode { get; set; }

    public string? ModuleCaption { get; set; }

    public string? LoggedAtUtc { get; set; }

    public string? Actor { get; set; }

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string? EntityNumber { get; set; }

    public string? Action { get; set; }

    public string? Result { get; set; }

    public string? Message { get; set; }
}

internal sealed class DesktopSearchHitRow
{
    public string? Scope { get; set; }

    public string? ModuleCode { get; set; }

    public string? Title { get; set; }

    public string? Subtitle { get; set; }

    public string? Reference { get; set; }
}

internal sealed class DesktopModuleSnapshotExportRow
{
    public string? ModuleCode { get; set; }

    public string? PayloadJson { get; set; }

    public int VersionNo { get; set; }
}

internal sealed class DesktopModuleSnapshotRow
{
    public string? PayloadJson { get; set; }

    public int VersionNo { get; set; }

    public string? PayloadHash { get; set; }

    public string? UpdatedBy { get; set; }

    public string? UpdatedAtUtc { get; set; }
}

internal static class DesktopRoleCatalog
{
    public const string AdminRoleCode = "admin";
    public const string ManagerRoleCode = "manager";

    public static IReadOnlyList<DesktopRoleDefinition> All { get; } =
    [
        new(AdminRoleCode, "Администратор", "Полный доступ к данным, настройкам и сервисным операциям приложения."),
        new(ManagerRoleCode, "Менеджер", "Работа с клиентами, заказами, закупками, складом и товарами без сервисных настроек.")
    ];

    public static IReadOnlyList<string> BootstrapRoleCodes { get; } = All
        .Select(item => item.RoleCode)
        .ToArray();

    public static string ResolveDefaultRoleCode(string userName)
    {
        var normalized = (userName ?? string.Empty).Trim();
        return IsBuiltInAdmin(normalized) ? AdminRoleCode : ManagerRoleCode;
    }

    public static string ResolvePrimaryRoleCode(IEnumerable<string> roleCodes, string userName)
    {
        var normalizedRoles = roleCodes
            .Select(NormalizeRoleCode)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedRoles.Any(item => string.Equals(item, AdminRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            return AdminRoleCode;
        }

        if (normalizedRoles.Any(item => string.Equals(item, ManagerRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            return ManagerRoleCode;
        }

        return ResolveDefaultRoleCode(userName);
    }

    public static string NormalizeRoleCode(string? roleCode)
    {
        var normalized = (roleCode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            AdminRoleCode => AdminRoleCode,
            ManagerRoleCode or "sales" or "purchasing" or "warehouse" or "catalog" or "audit" => ManagerRoleCode,
            _ => string.Empty
        };
    }

    public static string GetDisplayName(string roleCode)
    {
        var normalized = NormalizeRoleCode(roleCode);
        return All.FirstOrDefault(item => string.Equals(item.RoleCode, normalized, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? GetDisplayName(ManagerRoleCode);
    }

    private static bool IsBuiltInAdmin(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var normalized = userName.Trim();
        return normalized.Equals("priiiinnceee", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("admin", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("administrator", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("сисадмин", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record DesktopRoleDefinition(string RoleCode, string DisplayName, string Description);
