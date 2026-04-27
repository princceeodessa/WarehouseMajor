using System.Reflection;

namespace WarehouseAutomatisaion.Desktop.Model;

public sealed record DomainArea(
    string Key,
    string DisplayName,
    Color AccentColor,
    int Order);

public sealed record EntityMetadata(
    Type ClrType,
    string DisplayName,
    string AreaKey,
    string Summary,
    string OneCSource,
    string Notes);

public sealed record RelationshipMetadata(
    Type SourceType,
    Type TargetType,
    string Label,
    string Cardinality,
    string Description,
    string Evidence);

public sealed record EntityPropertyRow(
    string Name,
    string Type,
    string Role,
    string DeclaredBy);

public static class TypeDisplayFormatter
{
    public static string Format(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType is not null)
        {
            return $"{Format(underlyingType)}?";
        }

        if (type.IsGenericType)
        {
            var genericArguments = type.GetGenericArguments().Select(Format);
            var genericName = type.Name.Split('`')[0];
            return $"{genericName}<{string.Join(", ", genericArguments)}>";
        }

        return type.Name switch
        {
            nameof(Guid) => "Guid",
            nameof(String) => "string",
            nameof(Int32) => "int",
            nameof(Int64) => "long",
            nameof(Boolean) => "bool",
            nameof(Decimal) => "decimal",
            nameof(Double) => "double",
            nameof(DateTime) => "DateTime",
            nameof(DateOnly) => "DateOnly",
            nameof(DateTimeOffset) => "DateTimeOffset",
            _ => type.Name
        };
    }

    public static IReadOnlyList<EntityPropertyRow> GetPropertyRows(Type type)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => property.DeclaringType == type ? 0 : 1)
            .ThenBy(property => property.Name)
            .Select(property => new EntityPropertyRow(
                property.Name,
                Format(property.PropertyType),
                ClassifyProperty(property),
                property.DeclaringType?.Name ?? type.Name))
            .ToArray();
    }

    private static string ClassifyProperty(PropertyInfo property)
    {
        if (property.Name == "Id")
        {
            return "Key";
        }

        if (IsCollection(property.PropertyType))
        {
            return "Collection";
        }

        if (property.Name.EndsWith("Id", StringComparison.Ordinal) ||
            property.Name.EndsWith("Ids", StringComparison.Ordinal))
        {
            return "Reference";
        }

        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (underlyingType.IsEnum)
        {
            return "State";
        }

        return underlyingType.Name switch
        {
            nameof(Guid) => "Reference",
            nameof(String) => "Text",
            nameof(Decimal) => "Number",
            nameof(Int32) => "Number",
            nameof(Int64) => "Number",
            nameof(Boolean) => "Flag",
            nameof(DateTime) => "Date",
            nameof(DateOnly) => "Date",
            nameof(DateTimeOffset) => "Date",
            _ => "Value"
        };
    }

    private static bool IsCollection(Type type)
    {
        if (type == typeof(string))
        {
            return false;
        }

        return type.IsGenericType &&
               typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }
}
