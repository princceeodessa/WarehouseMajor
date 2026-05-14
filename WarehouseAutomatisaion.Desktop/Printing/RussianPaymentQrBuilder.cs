using System.Globalization;
using System.Text;
using QRCoder;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Printing;

public static class RussianPaymentQrBuilder
{
    public static string BuildPayload(
        OrganizationBankDetails details,
        decimal amount,
        string purpose)
    {
        var builder = new StringBuilder();
        builder.Append("ST00012");
        AppendField(builder, "Name", details.LegalName);
        AppendField(builder, "PersonalAcc", details.PaymentAccount);
        AppendField(builder, "BankName", details.BankName);
        AppendField(builder, "BIC", details.Bik);
        AppendField(builder, "CorrespAcc", details.CorrespondentAccount);
        if (!string.IsNullOrWhiteSpace(details.Inn))
        {
            AppendField(builder, "PayeeINN", details.Inn);
        }
        if (!string.IsNullOrWhiteSpace(details.Kpp))
        {
            AppendField(builder, "KPP", details.Kpp);
        }
        if (amount > 0)
        {
            var kopeks = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
            AppendField(builder, "Sum", kopeks.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(purpose))
        {
            AppendField(builder, "Purpose", purpose);
        }

        return builder.ToString();
    }

    public static string BuildDataUri(
        OrganizationBankDetails details,
        decimal amount,
        string purpose,
        int pixelsPerModule = 10)
    {
        var payload = BuildPayload(details, amount, purpose);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        var bytes = qrCode.GetGraphic(pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }

    private static void AppendField(StringBuilder builder, string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append('|');
        builder.Append(name);
        builder.Append('=');
        builder.Append(SanitizeFieldValue(value));
    }

    private static string SanitizeFieldValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (ch == '|' || ch == '\r' || ch == '\n' || ch == '\t')
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
