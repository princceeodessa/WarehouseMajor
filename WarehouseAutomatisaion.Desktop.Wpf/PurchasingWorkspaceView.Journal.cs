using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView
{
    private PurchasingGridRow BuildJournalRow(PurchasingOperationLogEntry entry)
    {
        var status = Ui(entry.Result);
        return CreateRow(
            JournalSection,
            entry.Id,
            entry,
            Ui(entry.EntityType),
            ResolveSupplierNameForJournal(entry),
            ResolveWarehouseForJournal(entry),
            entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture),
            Ui(entry.EntityType),
            Ui(entry.EntityNumber),
            Ui(entry.Action),
            Ui(entry.Result),
            Ui(entry.Actor),
            TrimComment(entry.Message),
            ResolveJournalSection(entry),
            status,
            Ui(entry.Actor),
            status,
            entry.LoggedAt,
            true,
            false,
            false,
            false,
            false,
            false,
            Guid.Empty,
            0m,
            0m,
            0m,
            string.Join(" ", new[]
            {
                entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture),
                entry.Actor,
                entry.EntityType,
                entry.EntityNumber,
                entry.Action,
                entry.Result,
                entry.Message
            }));
    }

    private string ResolveJournalSection(PurchasingOperationLogEntry entry)
    {
        var entityType = Ui(entry.EntityType);
        if (entityType.Equals("Поставщик", StringComparison.OrdinalIgnoreCase))
        {
            return "Поставщики";
        }

        if (entityType.Equals("Счет поставщика", StringComparison.OrdinalIgnoreCase))
        {
            return "Счета";
        }

        if (entityType.Equals("Приемка", StringComparison.OrdinalIgnoreCase))
        {
            return "Приемки";
        }

        return "Заказы";
    }

    private string ResolveSupplierNameForJournal(PurchasingOperationLogEntry entry)
    {
        if (_workspace.PurchaseOrders.FirstOrDefault(item => item.Id == entry.EntityId) is { } order)
        {
            return order.SupplierName;
        }

        if (_workspace.SupplierInvoices.FirstOrDefault(item => item.Id == entry.EntityId) is { } invoice)
        {
            return invoice.SupplierName;
        }

        if (_workspace.PurchaseReceipts.FirstOrDefault(item => item.Id == entry.EntityId) is { } receipt)
        {
            return receipt.SupplierName;
        }

        if (_workspace.Suppliers.FirstOrDefault(item => item.Id == entry.EntityId) is { } supplier)
        {
            return supplier.Name;
        }

        return string.Empty;
    }

    private string ResolveWarehouseForJournal(PurchasingOperationLogEntry entry)
    {
        if (_workspace.PurchaseOrders.FirstOrDefault(item => item.Id == entry.EntityId) is { } order)
        {
            return order.Warehouse;
        }

        if (_workspace.SupplierInvoices.FirstOrDefault(item => item.Id == entry.EntityId) is { } invoice)
        {
            return invoice.Warehouse;
        }

        if (_workspace.PurchaseReceipts.FirstOrDefault(item => item.Id == entry.EntityId) is { } receipt)
        {
            return receipt.Warehouse;
        }

        return string.Empty;
    }

    private void RefreshJournalDetails(PurchasingOperationLogEntry entry)
    {
        var linkedOrder = _workspace.PurchaseOrders.FirstOrDefault(item => item.Id == entry.EntityId);
        var linkedInvoice = _workspace.SupplierInvoices.FirstOrDefault(item => item.Id == entry.EntityId);
        var linkedReceipt = _workspace.PurchaseReceipts.FirstOrDefault(item => item.Id == entry.EntityId);
        var linkedSupplier = _workspace.Suppliers.FirstOrDefault(item => item.Id == entry.EntityId);

        DetailsTitleText.Text = Ui(entry.Action);
        DetailsSubtitleText.Text = Ui(entry.Message);
        ApplyBadge(DetailsStatusBadge, DetailsStatusBadgeText, entry.Result);
        DetailsSupplierText.Text = linkedOrder?.SupplierName ?? linkedInvoice?.SupplierName ?? linkedReceipt?.SupplierName ?? linkedSupplier?.Name ?? "-";
        DetailsWarehouseText.Text = linkedOrder?.Warehouse ?? linkedInvoice?.Warehouse ?? linkedReceipt?.Warehouse ?? "-";
        DetailsCreatedText.Text = entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);
        DetailsPlannedText.Text = "-";
        DetailsNumberText.Text = Ui(entry.EntityNumber);
        DetailsResponsibleText.Text = Ui(entry.Actor);
        DetailsSourceText.Text = Ui(entry.EntityType);
        DetailsContractText.Text = linkedSupplier?.Contract ?? linkedOrder?.Contract ?? linkedInvoice?.Contract ?? linkedReceipt?.Contract ?? "-";
        DetailsAmountText.Text = linkedOrder is not null
            ? FormatMoney(linkedOrder.TotalAmount)
            : linkedInvoice is not null
                ? FormatMoney(linkedInvoice.TotalAmount)
                : linkedReceipt is not null
                    ? FormatMoney(linkedReceipt.TotalAmount)
                    : "0 ₽";
        DetailsPaidText.Text = linkedInvoice is not null && IsInvoicePaid(linkedInvoice) ? FormatMoney(linkedInvoice.TotalAmount) : "0 ₽";
        DetailsBalanceText.Text = linkedInvoice is not null && !IsInvoicePaid(linkedInvoice) ? FormatMoney(linkedInvoice.TotalAmount) : "0 ₽";
        DetailsCommentText.Text = Ui(entry.Message);
        DetailsMetaResponsibleText.Text = Ui(entry.Actor);
        DetailsCreatedByText.Text = Ui(entry.Actor);
        DetailsUpdatedText.Text = entry.LoggedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);

        var order = linkedOrder ?? (linkedInvoice is not null ? GetOrderById(linkedInvoice.RelatedOrderId) : linkedReceipt is not null ? GetOrderById(linkedReceipt.RelatedOrderId) : null);
        var invoice = linkedInvoice ?? (order is not null ? GetInvoiceForOrder(order.Id) : null);
        var receipt = linkedReceipt ?? (order is not null ? GetReceiptForOrder(order.Id) : null);
        RenderChain(order, invoice, receipt, invoice);
        SetLinkedButton(LinkedOrderButton, order is null ? "Заказ: не найден" : $"Заказ: {order.Number}", order is null ? null : new LinkedTarget(OrdersSection, order.Id, order.Number));
        SetLinkedButton(LinkedInvoiceButton, invoice is null ? "Счет: не создан" : $"Счет: {invoice.Number}", invoice is null ? null : new LinkedTarget(InvoicesSection, invoice.Id, invoice.Number));
        SetLinkedButton(LinkedReceiptButton, receipt is null ? "Приемка: не найдена" : $"Приемка: {receipt.Number}", receipt is null ? null : new LinkedTarget(ReceiptsSection, receipt.Id, receipt.Number));
        SetLinkedButton(LinkedPaymentButton, invoice is null ? "Оплата: не найдена" : $"Оплата: {invoice.Number}", invoice is null ? null : new LinkedTarget(PaymentsSection, invoice.Id, invoice.Number));
        RenderDetailLines((linkedOrder?.Lines ?? linkedInvoice?.Lines ?? linkedReceipt?.Lines)?.ToArray() ?? Array.Empty<OperationalPurchasingLineRecord>());
    }

    private LinkedTarget? ResolveLinkedTargetFromJournal(PurchasingOperationLogEntry entry)
    {
        if (_workspace.PurchaseOrders.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(OrdersSection, entry.EntityId, entry.EntityNumber);
        }

        if (_workspace.SupplierInvoices.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(InvoicesSection, entry.EntityId, entry.EntityNumber);
        }

        if (_workspace.PurchaseReceipts.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(ReceiptsSection, entry.EntityId, entry.EntityNumber);
        }

        if (_workspace.Suppliers.Any(item => item.Id == entry.EntityId))
        {
            return new LinkedTarget(SuppliersSection, entry.EntityId, entry.EntityNumber);
        }

        return null;
    }
}
