using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView
{
    private PurchasingGridRow BuildInvoiceRow(OperationalPurchasingDocumentRecord invoice)
    {
        var paid = IsInvoicePaid(invoice) ? invoice.TotalAmount : 0m;
        var balance = Math.Max(invoice.TotalAmount - paid, 0m);
        return CreateRow(
            InvoicesSection,
            invoice.Id,
            invoice,
            invoice.DocumentType,
            invoice.SupplierName,
            invoice.Warehouse,
            invoice.Number,
            invoice.SupplierName,
            invoice.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            invoice.DueDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-",
            invoice.Warehouse,
            FormatMoney(invoice.TotalAmount),
            FormatMoney(paid),
            EmptyAsDash(invoice.RelatedOrderNumber),
            invoice.Status,
            EmptyAsDash(invoice.SourceLabel),
            invoice.Status,
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
                invoice.Status,
                invoice.RelatedOrderNumber,
                invoice.Comment
            }));
    }

    private void CreateOrEditInvoice(PurchasingGridRow row)
    {
        if (ResolveOrderForRow(row) is not { } order)
        {
            OpenDocumentEditor(PurchasingDocumentEditorMode.SupplierInvoice, ResolveInvoiceForRow(row), ResolveSelectedSupplierId());
            return;
        }

        var existing = GetInvoiceForOrder(order.Id);
        var draft = existing ?? _workspace.CreateSupplierInvoiceDraftFromOrder(order.Id);
        OpenDocumentEditor(PurchasingDocumentEditorMode.SupplierInvoice, existing is null ? null : draft, order.SupplierId);
        if (existing is null)
        {
            // The editor already persists through OpenDocumentEditor when ResultDocument is returned.
        }
    }

    private void MarkInvoiceReceived(PurchasingGridRow row)
    {
        var invoice = ResolveInvoiceForRow(row);
        if (invoice is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нет счета поставщика.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = _workspace.MarkSupplierInvoiceReceived(invoice.Id);
        ShowWorkflowResult(result);
    }

    private void MarkInvoicePayable(PurchasingGridRow row)
    {
        var invoice = ResolveInvoiceForRow(row);
        if (invoice is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нет счета поставщика.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Ui(invoice.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.MarkSupplierInvoiceReceived(invoice.Id);
        }

        var result = _workspace.MarkSupplierInvoicePayable(invoice.Id);
        ShowWorkflowResult(result);
    }
}
