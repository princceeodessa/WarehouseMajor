using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView
{
    private PurchasingGridRow BuildReceiptRow(OperationalPurchasingDocumentRecord receipt)
    {
        var invoice = GetInvoiceForOrder(receipt.RelatedOrderId);
        var paid = invoice is not null && IsInvoicePaid(invoice) ? invoice.TotalAmount : 0m;
        var balance = invoice is null ? 0m : Math.Max(invoice.TotalAmount - paid, 0m);
        // Release 1.0.119: порядок столбцов для «Приходных накладных» переставлен 1в1 со
        // скрином 1С УНФ — Дата | Номер | Поставщик/Покупатель | Сумма | Склад | Операция |
        // Состояние оригинала. Раньше первой шла Номер, а Дата стояла третьей.
        return CreateRow(
            ReceiptsSection,
            receipt.Id,
            receipt,
            receipt.DocumentType,
            receipt.SupplierName,
            receipt.Warehouse,
            receipt.DocumentDate.ToString("dd.MM.yyyy", RuCulture),  // Col1: Дата
            receipt.Number,                                          // Col2: Номер
            receipt.SupplierName,                                    // Col3: Поставщик / Покупатель
            FormatMoney(receipt.TotalAmount),                        // Col4: Сумма
            receipt.Warehouse,                                       // Col5: Склад
            string.IsNullOrWhiteSpace(receipt.DocumentType)          // Col6: Операция
                ? "Поступление от поставщика"
                : receipt.DocumentType,
            "<Неизвестно>",                                          // Col7: Состояние оригинала (плейсхолдер)
            string.Empty,                                            // Col8: пусто (в 1С здесь только иконка $)
            receipt.Status,
            string.Empty,                                            // Col9: пусто
            receipt.Status,
            receipt.DocumentDate,
            IsReceiptCompleted(receipt),
            IsReceiptOverdue(receipt),
            MissingInvoiceForDocument(receipt),
            false,
            invoice is not null && !IsInvoicePaid(invoice),
            HasDiscrepancy(receipt),
            receipt.RelatedOrderId,
            receipt.TotalAmount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                receipt.Number,
                receipt.SupplierName,
                receipt.Warehouse,
                receipt.Status,
                receipt.RelatedOrderNumber,
                receipt.Comment
            }));
    }

    private bool IsReceiptCompleted(OperationalPurchasingDocumentRecord receipt)
    {
        var status = Ui(receipt.Status);
        return status.Equals("Размещена", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Принята", StringComparison.OrdinalIgnoreCase)
               || status.Equals("Архив", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleCreateReceiptClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        CreateOrEditReceipt(row);
    }

    private void CreateOrEditReceipt(PurchasingGridRow row)
    {
        if (ResolveOrderForRow(row) is not { } order)
        {
            OpenDocumentEditor(PurchasingDocumentEditorMode.PurchaseReceipt, ResolveReceiptForRow(row), ResolveSelectedSupplierId());
            return;
        }

        var existing = GetReceiptForOrder(order.Id);
        var draft = existing ?? _workspace.CreateReceiptDraftFromOrder(order.Id);
        OpenDocumentEditor(PurchasingDocumentEditorMode.PurchaseReceipt, existing is null ? null : draft, order.SupplierId);
        if (existing is null)
        {
            // Persisted via OpenDocumentEditor.
        }
    }

    private void ReceiveReceipt(PurchasingGridRow row)
    {
        if (ResolveReceiptForRow(row) is not { } receipt)
        {
            return;
        }

        var result = _workspace.ReceivePurchaseReceipt(receipt.Id);
        ShowWorkflowResult(result);
    }

    private void PlaceReceipt(PurchasingGridRow row)
    {
        if (ResolveReceiptForRow(row) is not { } receipt)
        {
            return;
        }

        if (Ui(receipt.Status).Equals("Черновик", StringComparison.OrdinalIgnoreCase))
        {
            _workspace.ReceivePurchaseReceipt(receipt.Id);
        }

        var result = _workspace.PlacePurchaseReceipt(receipt.Id);
        ShowWorkflowResult(result);
    }
}
