using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PurchasingWorkspaceView
{
    private PurchasingGridRow BuildSupplierRow(OperationalPurchasingSupplierRecord supplier)
    {
        var orders = _workspace.PurchaseOrders.Where(item => item.SupplierId == supplier.Id).ToArray();
        var invoices = _workspace.SupplierInvoices.Where(item => item.SupplierId == supplier.Id).ToArray();
        var receipts = _workspace.PurchaseReceipts.Where(item => item.SupplierId == supplier.Id).ToArray();
        var openOrders = orders.Count(item => !IsOrderClosed(item));
        var activeDocuments = invoices.Length + receipts.Length;
        var lastDate = orders.Select(item => item.DocumentDate)
            .Concat(invoices.Select(item => item.DocumentDate))
            .Concat(receipts.Select(item => item.DocumentDate))
            .DefaultIfEmpty(DateTime.Today)
            .Max();
        var paid = invoices.Where(IsInvoicePaid).Sum(item => item.TotalAmount);
        var amount = orders.Sum(item => item.TotalAmount);
        var balance = Math.Max(amount - paid, 0m);

        return CreateRow(
            SuppliersSection,
            supplier.Id,
            supplier,
            "Поставщик",
            supplier.Name,
            ResolveDominantWarehouse(orders, invoices, receipts),
            supplier.Code,
            supplier.Name,
            EmptyAsDash(supplier.TaxId),
            EmptyAsDash(supplier.Phone),
            EmptyAsDash(supplier.Email),
            EmptyAsDash(supplier.Contract),
            openOrders.ToString("N0", RuCulture),
            activeDocuments.ToString("N0", RuCulture),
            supplier.Status,
            EmptyAsDash(supplier.SourceLabel),
            supplier.Status,
            lastDate,
            Ui(supplier.Status).Equals("Пауза", StringComparison.OrdinalIgnoreCase),
            false,
            orders.Any(item => !GetInvoicesForOrder(item.Id).Any()),
            orders.Any(item => !GetReceiptsForOrder(item.Id).Any()),
            invoices.Any(item => !IsInvoicePaid(item)),
            orders.Any(HasDiscrepancy) || invoices.Any(HasDiscrepancy) || receipts.Any(HasDiscrepancy),
            orders.FirstOrDefault()?.Id ?? Guid.Empty,
            amount,
            paid,
            balance,
            string.Join(" ", new[]
            {
                supplier.Code,
                supplier.Name,
                supplier.TaxId,
                supplier.Phone,
                supplier.Email,
                supplier.Contract,
                supplier.SourceLabel
            }));
    }

    private void RefreshSupplierDetails(OperationalPurchasingSupplierRecord supplier)
    {
        var orders = _workspace.PurchaseOrders.Where(item => item.SupplierId == supplier.Id).OrderByDescending(item => item.DocumentDate).ToArray();
        var invoices = _workspace.SupplierInvoices.Where(item => item.SupplierId == supplier.Id).OrderByDescending(item => item.DocumentDate).ToArray();
        var receipts = _workspace.PurchaseReceipts.Where(item => item.SupplierId == supplier.Id).OrderByDescending(item => item.DocumentDate).ToArray();
        var latestOrder = orders.FirstOrDefault();
        var latestInvoice = invoices.FirstOrDefault();
        var latestReceipt = receipts.FirstOrDefault();
        var paid = invoices.Where(IsInvoicePaid).Sum(item => item.TotalAmount);
        var amount = orders.Sum(item => item.TotalAmount);
        var balance = Math.Max(amount - paid, 0m);
        var updatedAt = ResolveUpdatedAt("Поставщик", supplier.Id, latestOrder?.DocumentDate ?? DateTime.Today);

        DetailsTitleText.Text = Ui(supplier.Name);
        DetailsSubtitleText.Text = string.Join(" ? ", new[]
        {
            EmptyAsDash(supplier.TaxId),
            EmptyAsDash(supplier.Phone),
            EmptyAsDash(supplier.Email)
        }.Where(item => item != "-"));

        ApplyBadge(DetailsStatusBadge, DetailsStatusBadgeText, supplier.Status);
        DetailsSupplierText.Text = Ui(supplier.Name);
        DetailsWarehouseText.Text = ResolveDominantWarehouse(orders, invoices, receipts);
        DetailsCreatedText.Text = latestOrder?.DocumentDate.ToString("dd.MM.yyyy", RuCulture) ?? "-";
        DetailsPlannedText.Text = ResolvePlannedDate(latestOrder ?? new OperationalPurchasingDocumentRecord())?.ToString("dd.MM.yyyy", RuCulture) ?? "-";
        DetailsNumberText.Text = Ui(supplier.Code);
        DetailsResponsibleText.Text = ResolveResponsible("Поставщик", supplier.Id);
        DetailsSourceText.Text = EmptyAsDash(supplier.SourceLabel);
        DetailsContractText.Text = EmptyAsDash(supplier.Contract);
        DetailsAmountText.Text = FormatMoney(amount);
        DetailsPaidText.Text = FormatMoney(paid);
        DetailsBalanceText.Text = FormatMoney(balance);
        DetailsCommentText.Text = string.Join(Environment.NewLine, new[]
        {
            supplier.Phone,
            supplier.Email
        }.Where(item => !string.IsNullOrWhiteSpace(item)));
        DetailsMetaResponsibleText.Text = ResolveResponsible("Поставщик", supplier.Id);
        DetailsCreatedByText.Text = ResolveResponsible("Поставщик", supplier.Id);
        DetailsUpdatedText.Text = updatedAt.ToString("dd.MM.yyyy HH:mm", RuCulture);

        SetLinkedButton(LinkedOrderButton, latestOrder is null ? "Заказ: не создан" : $"Заказ: {latestOrder.Number}", latestOrder is null ? null : new LinkedTarget(OrdersSection, latestOrder.Id, latestOrder.Number));
        SetLinkedButton(LinkedInvoiceButton, latestInvoice is null ? "Счет: не создан" : $"Счет: {latestInvoice.Number}", latestInvoice is null ? null : new LinkedTarget(InvoicesSection, latestInvoice.Id, latestInvoice.Number));
        SetLinkedButton(LinkedReceiptButton, latestReceipt is null ? "Приемка: не создана" : $"Приемка: {latestReceipt.Number}", latestReceipt is null ? null : new LinkedTarget(ReceiptsSection, latestReceipt.Id, latestReceipt.Number));
        SetLinkedButton(LinkedPaymentButton, latestInvoice is null ? "Оплата: не создана" : $"Оплата: {latestInvoice.Number}", latestInvoice is null ? null : new LinkedTarget(PaymentsSection, latestInvoice.Id, latestInvoice.Number));

        RenderChain(latestOrder, latestInvoice, latestReceipt, latestInvoice);
        RenderDetailLines((latestOrder?.Lines ?? latestInvoice?.Lines ?? latestReceipt?.Lines)?.ToArray() ?? Array.Empty<OperationalPurchasingLineRecord>());
    }

    private Guid? ResolveSelectedSupplierId()
    {
        return GetCurrentRow()?.Payload switch
        {
            OperationalPurchasingSupplierRecord supplier => supplier.Id,
            OperationalPurchasingDocumentRecord document when document.SupplierId != Guid.Empty => document.SupplierId,
            _ => null
        };
    }

    private void HandleSendSupplierClick(object sender, RoutedEventArgs e)
    {
        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        SendSupplier(row);
    }

    private void SendSupplier(PurchasingGridRow row)
    {
        if (row.Payload is OperationalPurchasingSupplierRecord supplier)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Контакты поставщика:\n{supplier.Name}\n{supplier.Phone}\n{supplier.Email}",
                "Закупки",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (ResolveOrderForRow(row) is { } order && !row.IsDisabled)
        {
            var result = _workspace.PlacePurchaseOrder(order.Id);
            ShowWorkflowResult(result);
            return;
        }

        MessageBox.Show(Window.GetWindow(this), "Для выбранной записи отправка поставщику не требуется.", "Закупки", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenSupplierEditor(OperationalPurchasingSupplierRecord? supplier)
    {
        var dialog = new PurchasingSupplierEditorWindow(_workspace, supplier)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.ResultSupplier is null)
        {
            return;
        }

        if (supplier is null)
        {
            _workspace.AddSupplier(dialog.ResultSupplier);
        }
        else
        {
            _workspace.UpdateSupplier(dialog.ResultSupplier);
        }
    }

    private OperationalPurchasingSupplierRecord EnsureSupplier(string supplierName)
    {
        var existing = _workspace.Suppliers.FirstOrDefault(item => Ui(item.Name).Equals(Ui(supplierName), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var draft = _workspace.CreateSupplierDraft();
        draft.Name = Ui(supplierName);
        draft.Code = string.IsNullOrWhiteSpace(draft.Code) ? $"SUP-{DateTime.Now:yyMMddHHmmss}" : draft.Code;
        _workspace.AddSupplier(draft);
        return _workspace.Suppliers.First(item => item.Id == draft.Id);
    }
}
