using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class GridColumnBuilder
{
    public static void BuildColumns(DataGrid grid, Type rowType)
    {
        grid.Columns.Clear();

        foreach (var property in rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead)
            {
                continue;
            }

            var header = ResolveHeader(property);
            var column = new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(property.Name),
                Width = new DataGridLength(1, DataGridLengthUnitType.SizeToHeader),
                MinWidth = ResolveMinWidth(header)
            };

            grid.Columns.Add(column);
        }
    }

    private static double ResolveMinWidth(string header)
    {
        var normalized = TextMojibakeFixer.NormalizeText(header).ToLowerInvariant();

        if (normalized.Contains("дата") || normalized.Contains("время"))
        {
            return 120d;
        }

        if (normalized.Contains("код") || normalized.Contains("инн"))
        {
            return 118d;
        }

        if (normalized.Contains("сумм") || normalized.Contains("значен"))
        {
            return 132d;
        }

        if (normalized.Contains("статус") || normalized.Contains("режим") || normalized.Contains("результат"))
        {
            return 128d;
        }

        if (normalized.Contains("телефон"))
        {
            return 140d;
        }

        if (normalized.Contains("mail"))
        {
            return 190d;
        }

        if (normalized.Contains("клиент")
            || normalized.Contains("поставщик")
            || normalized.Contains("номенклатура")
            || normalized.Contains("сценар")
            || normalized.Contains("сообщен"))
        {
            return 220d;
        }

        return 150d;
    }

    private static string ResolveHeader(PropertyInfo property)
    {
        var displayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return TextMojibakeFixer.NormalizeText(displayName);
        }

        return TextMojibakeFixer.NormalizeText(Humanize(property.Name));
    }

    private static string Humanize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var chars = new List<char>(name.Length + 8);
        chars.Add(name[0]);
        for (var i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(name[i]);
        }

        return new string(chars.ToArray());
    }
}
