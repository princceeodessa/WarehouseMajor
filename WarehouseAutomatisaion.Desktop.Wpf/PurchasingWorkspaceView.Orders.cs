using System.Windows;
using System.Windows.Controls;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView
{
    private void HandleOrdersSubTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string sub)
        {
            return;
        }

        if (string.Equals(_activeOrderSubTab, sub, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeOrderSubTab = sub;
        _allRows = BuildRowsForSection(OrdersSection);
        ApplySectionButtons();
        ApplyFilters(keepSelection: false);
        UpdateEmptyStateCopy();
    }

    private void HandleConvertCustomerOrderClick(object sender, RoutedEventArgs e)
    {
        // Заглушка: будущая логика — превращать выбранный заказ покупателя в заказ поставщику.
        MessageBox.Show(
            Window.GetWindow(this),
            "Оформление заказа поставщику из заказа покупателя — в разработке.",
            "Закупки",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private PurchasingGridRow BuildOrderRow(OperationalPurchasingDocumentRecord order)
    {
        var invoices = GetInvoicesForOrder(order.Id);
        var receipts = GetReceiptsForOrder(order.Id);
        var plannedDate = ResolvePlannedDate(order);
        var paid = invoices.Where(IsInvoicePaid).Sum(item => item.TotalAmount);
        var balance = Math.Max(order.TotalAmount - paid, 0m);
        var responsible = ResolveResponsible(order.DocumentType, order.Id);

        // Release 1.0.109: порядок колонок 1в1 как в 1С УНФ «Заказы поставщикам».
        // col1=Дата, col2=Номер, col3=Состояние, col4=Поставщик, col5=Сумма,
        // col6=Дата поступления, col7=Договор, col8=Склад, status=Статус оригинала, col9=Ответственный.
        var row = CreateRow(
            OrdersSection,
            order.Id,
            order,
            order.DocumentType,
            order.SupplierName,
            order.Warehouse,
            order.DocumentDate.ToString("dd.MM.yyyy", RuCulture),
            order.Number,
            order.Status,
            order.SupplierName,
            FormatMoney(order.TotalAmount),
            plannedDate?.ToString("dd.MM.yyyy", RuCulture) ?? "-",
            string.IsNullOrWhiteSpace(order.Contract) ? "-" : order.Contract,
            order.Warehouse,
            order.Status,
            responsible,
            order.Status,
            order.DocumentDate,
            IsOrderClosed(order),
            IsOrderOverdue(order),
            !invoices.Any(),
            !receipts.Any(),
            invoices.Any(item => !IsInvoicePaid(item)),
            HasDiscrepancy(order) || invoices.Any(HasDiscrepancy) || receipts.Any(HasDiscrepancy),
            order.Id,
            order.TotalAmount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                order.Number,
                order.SupplierName,
                order.Warehouse,
                order.Status,
                order.Contract,
                order.Comment,
                string.Join(" ", order.Lines.Select(item => $"{item.ItemCode} {item.ItemName}"))
            }));

        // 1С УНФ-style индикаторы статусов:
        // dot1 — выполнено (есть приёмка) ⇒ зелёный заполненный; иначе кружок-контур.
        // dot2 — оплата: полностью оплачено ⇒ зелёный; частично/просрочка ⇒ красный.
        var hasReceipts = receipts.Any();
        var hasInvoices = invoices.Any();
        var allPaid = hasInvoices && invoices.All(IsInvoicePaid);
        if (hasReceipts)
        {
            row.StatusDot1Color = "#1FA45F";
            row.StatusDot1Fill = "#1FA45F";
        }
        else
        {
            row.StatusDot1Color = "#1FA45F";
            row.StatusDot1Fill = "Transparent";
        }

        if (allPaid)
        {
            row.StatusDot2Color = "#1FA45F";
            row.StatusDot2Fill = "#1FA45F";
        }
        else if (!hasInvoices || balance > 0m)
        {
            row.StatusDot2Color = "#D92D20";
            row.StatusDot2Fill = "Transparent";
        }

        return row;
    }

    private void CreateNewPurchase(Guid? supplierId = null)
    {
        var dialog = new PurchasingDocumentEditorWindow(
            _workspace,
            PurchasingDocumentEditorMode.PurchaseOrder,
            null,
            supplierId,
            ResolveStorageCellOptions())
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultDocument is null)
        {
            return;
        }

        _workspace.AddPurchaseOrder(dialog.ResultDocument);
    }
}
