using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class SalesManagerDisplayResolver
{
    private static readonly object SyncRoot = new();
    private static IReadOnlyDictionary<string, string>? _managerDisplayNames;

    public static string Resolve(string? manager)
    {
        var normalized = Clean(manager);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return GetManagerDisplayNames().TryGetValue(normalized, out var displayName)
            ? displayName
            : normalized;
    }

    private static IReadOnlyDictionary<string, string> GetManagerDisplayNames()
    {
        if (_managerDisplayNames is not null)
        {
            return _managerDisplayNames;
        }

        lock (SyncRoot)
        {
            if (_managerDisplayNames is not null)
            {
                return _managerDisplayNames;
            }

            _managerDisplayNames = LoadManagerDisplayNames();
            return _managerDisplayNames;
        }
    }

    private static IReadOnlyDictionary<string, string> LoadManagerDisplayNames()
    {
        try
        {
            var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
            if (backplane is null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in backplane.ListUserAccounts())
            {
                var userName = Clean(account.UserName);
                var displayName = Clean(account.DisplayName);
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = userName;
                }

                if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(displayName))
                {
                    map[userName] = displayName;
                }

                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    map.TryAdd(displayName, displayName);
                }
            }

            return map;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Clean(string? value)
    {
        var normalized = TextMojibakeFixer.NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.Trim();
    }
}
