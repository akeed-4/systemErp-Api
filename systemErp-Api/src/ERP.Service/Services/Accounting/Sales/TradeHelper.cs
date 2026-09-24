using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

internal static class TradeHelper
{
    public static readonly string[] QuotationStatuses = { "draft", "sent", "accepted", "rejected", "converted_to_invoice", "converted_to_order" };
    public static readonly string[] OrderStatuses = { "draft", "confirmed", "partially_fulfilled", "completed", "cancelled" };
    public static readonly string[] RequisitionStatuses = { "draft", "pending_approval", "approved", "rejected", "converted_to_po", "converted_to_invoice" };

    public static void RequireParty(string partyName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(partyName)) errors.Add("اسم الطرف مطلوب.");
    }

    public static InvoiceType InvoiceTypeFor(string? partyVat) => string.IsNullOrWhiteSpace(partyVat) ? InvoiceType.Simplified : InvoiceType.TaxInvoice;
}
