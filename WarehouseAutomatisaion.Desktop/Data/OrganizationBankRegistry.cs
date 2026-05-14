using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class OrganizationBankDetails
{
    public string LegalName { get; init; } = string.Empty;

    public string ShortName { get; init; } = string.Empty;

    public string Inn { get; init; } = string.Empty;

    public string Kpp { get; init; } = string.Empty;

    public string Ogrn { get; init; } = string.Empty;

    public string LegalAddress { get; init; } = string.Empty;

    public string BankName { get; init; } = string.Empty;

    public string Bik { get; init; } = string.Empty;

    public string CorrespondentAccount { get; init; } = string.Empty;

    public string PaymentAccount { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;
}

public static class OrganizationBankRegistry
{
    private static readonly Dictionary<string, OrganizationBankDetails> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ИП Закирова Ирина Викторовна"] = new OrganizationBankDetails
        {
            LegalName = "ИП Закирова Ирина Викторовна",
            ShortName = "ИП Закирова И.В.",
            Inn = "181500933018",
            Kpp = string.Empty,
            LegalAddress = "Респ. Удмуртская, г. Ижевск, ул. Льва Толстого, д. 24, кв. 168",
            BankName = "ФИЛИАЛ \"НИЖЕГОРОДСКИЙ\" АО \"АЛЬФА-БАНК\" г. Нижний Новгород",
            Bik = "042202824",
            CorrespondentAccount = "30101810200000000824",
            PaymentAccount = "40802810129690003186",
        },
        ["ИП"] = new OrganizationBankDetails
        {
            LegalName = "ИП Закирова Ирина Викторовна",
            ShortName = "ИП",
            Inn = "181500933018",
            LegalAddress = "Респ. Удмуртская, г. Ижевск, ул. Льва Толстого, д. 24, кв. 168",
            BankName = "ФИЛИАЛ \"НИЖЕГОРОДСКИЙ\" АО \"АЛЬФА-БАНК\" г. Нижний Новгород",
            Bik = "042202824",
            CorrespondentAccount = "30101810200000000824",
            PaymentAccount = "40802810129690003186",
        },
        ["ИП с НДС"] = new OrganizationBankDetails
        {
            LegalName = "ИП Закирова Ирина Викторовна",
            ShortName = "ИП с НДС",
            Inn = "181500933018",
            LegalAddress = "Респ. Удмуртская, г. Ижевск, ул. Льва Толстого, д. 24, кв. 168",
            BankName = "ФИЛИАЛ \"НИЖЕГОРОДСКИЙ\" АО \"АЛЬФА-БАНК\" г. Нижний Новгород",
            Bik = "042202824",
            CorrespondentAccount = "30101810200000000824",
            PaymentAccount = "40802810129690003186",
        },
    };

    public static OrganizationBankDetails? Resolve(string? organizationName)
    {
        var key = TextMojibakeFixer.NormalizeText(organizationName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return Registry.TryGetValue(key, out var details) ? details : null;
    }

    public static OrganizationBankDetails ResolveOrDefault(string? organizationName)
    {
        return Resolve(organizationName) ?? new OrganizationBankDetails
        {
            ShortName = TextMojibakeFixer.NormalizeText(organizationName ?? string.Empty),
            LegalName = TextMojibakeFixer.NormalizeText(organizationName ?? string.Empty),
        };
    }
}
