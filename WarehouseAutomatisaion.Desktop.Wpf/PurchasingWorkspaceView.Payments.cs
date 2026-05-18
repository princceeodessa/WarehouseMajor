using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView
{
    private PurchasingGridRow BuildPaymentRow(OperationalPurchasingDocumentRecord invoice)
    {
        var paymentStatus = IsInvoicePaid(invoice)
            ? "Проведена"
            : Ui(invoice.Status) switch
            {
                "К оплате" => "Ожидает оплаты",
                "Получен" => "Ожидает оплаты",
                _ => "Не оплачена"
            };
        var paid = IsInvoicePaid(invoice) ? invoice.TotalAmount : 0m;
        var balance = Math.Max(invoice.TotalAmount - paid, 0m);
        return CreateRow(
            PaymentsSection,
            invoice.Id,
            invoice,
            "Оплата",
            invoice.SupplierName,
            invoice.Warehouse,
            $"PAY ? {invoice.Number}",
            invoice.SupplierName,
            invoice.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            invoice.DueDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-",
            invoice.Warehouse,
            FormatMoney(invoice.TotalAmount),
            FormatMoney(paid),
            FormatMoney(balance),
            paymentStatus,
            EmptyAsDash(invoice.RelatedOrderNumber),
            paymentStatus,
            invoice.DueDate ?? invoice.DocumentDate,
            IsInvoicePaid(invoice),
            IsInvoiceOverdue(invoice),
            false,
            MissingReceiptForDocument(invoice),
            !IsInvoicePaid(invoice),
            HasDiscrepancy(invoice),
            invoice.RelatedOrderId,
            invoice.TotalAmount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                invoice.Number,
                invoice.SupplierName,
                invoice.Warehouse,
                paymentStatus,
                invoice.RelatedOrderNumber
            }));
    }

    private void HandlePaySelectedClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        PayInvoice(row);
    }

    private void PayInvoice(PurchasingGridRow row)
    {
        var invoice = ResolveInvoiceForRow(row);
        if (invoice is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нет счета к оплате.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Ui(invoice.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.MarkSupplierInvoiceReceived(invoice.Id);
        }

        if (Ui(invoice.Status).Equals("Получен", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.MarkSupplierInvoicePayable(invoice.Id);
        }

        var result = _workspace.MarkSupplierInvoicePaid(invoice.Id);
        ShowWorkflowResult(result);
    }
}
