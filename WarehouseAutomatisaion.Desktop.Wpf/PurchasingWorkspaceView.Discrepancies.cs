using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView
{
    private int CountDiscrepancyDocuments()
    {
        return _workspace.PurchaseOrders.Count(HasDiscrepancy)
               + _workspace.SupplierInvoices.Count(HasDiscrepancy)
               + _workspace.PurchaseReceipts.Count(HasDiscrepancy);
    }

    private PurchasingGridRow[] BuildDiscrepancyRows()
    {
        return _workspace.PurchaseOrders.Cast<OperationalPurchasingDocumentRecord>()
            .Concat(_workspace.SupplierInvoices)
            .Concat(_workspace.PurchaseReceipts)
            .Where(item => HasDiscrepancy(item) || IsDocumentOverdue(item) || MissingInvoiceForDocument(item) || MissingReceiptForDocument(item) || UnpaidForDocument(item))
            .OrderByDescending(item => item.DocumentDate)
            .Select(BuildDiscrepancyRow)
            .ToArray();
    }

    private PurchasingGridRow BuildDiscrepancyRow(OperationalPurchasingDocumentRecord document)
    {
        var status = ResolveDiscrepancyStatus(document);
        return CreateRow(
            DiscrepanciesSection,
            document.Id,
            document,
            document.DocumentType,
            document.SupplierName,
            document.Warehouse,
            document.Number,
            document.SupplierName,
            document.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            EmptyAsDash(document.RelatedOrderNumber),
            document.Warehouse,
            FormatMoney(document.TotalAmount),
            TrimComment(document.Comment),
            EmptyAsDash(document.SourceLabel),
            status,
            ResolveResponsible(document.DocumentType, document.Id),
            status,
            document.DocumentDate,
            false,
            status.Equals("Просрочено", StringComparison.OrdinalIgnoreCase),
            MissingInvoiceForDocument(document),
            MissingReceiptForDocument(document),
            UnpaidForDocument(document),
            HasDiscrepancy(document),
            document.RelatedOrderId,
            document.TotalAmount,
            0m,
            document.TotalAmount,
            string.Join(" ", new[]
            {
                document.Number,
                document.SupplierName,
                document.Warehouse,
                document.Status,
                document.Comment,
                status
            }));
    }

    private string ResolveDiscrepancyStatus(OperationalPurchasingDocumentRecord document)
    {
        if (HasDiscrepancy(document))
        {
            return "Есть расхождения";
        }

        if (IsDocumentOverdue(document))
        {
            return "Просрочено";
        }

        if (MissingInvoiceForDocument(document))
        {
            return "Без счета";
        }

        if (MissingReceiptForDocument(document))
        {
            return "Без приемки";
        }

        if (UnpaidForDocument(document))
        {
            return "Частично оплачено";
        }

        return Ui(document.Status);
    }

    private void HandleCreateDiscrepancyClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        var document = ResolveDocumentForRow(row) ?? ResolveInvoiceForRow(row) ?? ResolveReceiptForRow(row) ?? ResolveOrderForRow(row);
        if (document is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Для выбранной записи нельзя завести расхождение.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var comment = PromptText("Расхождение", "Опишите найденное расхождение.", string.Empty, Array.Empty<string>());
        if (string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        var result = _workspace.AppendDocumentComment(document.DocumentType, document.Id, $"[Расхождение] {comment}", "Создание расхождения");
        ShowWorkflowResult(result);
    }
}
