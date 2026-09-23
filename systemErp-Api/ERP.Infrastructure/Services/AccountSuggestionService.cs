using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Infrastructure.Services;

public class AccountSuggestionService : IAccountSuggestionService
{
    public List<AccountSuggestionDto> GetInvoiceSuggestions(Invoice invoice)
    {
        var suggestions = new List<AccountSuggestionDto>();

        // Sales Invoice
        if (invoice.InvoiceType == InvoiceType.StandardTaxInvoice || invoice.InvoiceType == InvoiceType.SimplifiedTaxInvoice)
        {
            suggestions.Add(new AccountSuggestionDto("1201001", "مدينون", "Debit", "القيمة الإجمالية للفاتورة"));
            suggestions.Add(new AccountSuggestionDto("4101001", "مبيعات", "Credit", "قيمة البضاعة المباعة"));
            if (invoice.VatTotal > 0)
            {
                suggestions.Add(new AccountSuggestionDto("2201001", "ضريبة مخرجات", "Credit", "ضريبة القيمة المضافة"));
            }
        }
        // Purchase Invoice
        else if (invoice.InvoiceType == InvoiceType.PurchaseInvoice)
        {
            suggestions.Add(new AccountSuggestionDto("1205001", "المخزون", "Debit", "قيمة البضاعة المشتراة"));
            if (invoice.VatTotal > 0)
            {
                suggestions.Add(new AccountSuggestionDto("1206001", "ضريبة مدخلات", "Debit", "ضريبة القيمة المضافة للمشتريات"));
            }
            suggestions.Add(new AccountSuggestionDto("2101001", "دائنون", "Credit", "القيمة الإجمالية للمورد"));
        }

        return suggestions;
    }

    public List<AccountSuggestionDto> GetVoucherSuggestions(Voucher voucher)
    {
        var suggestions = new List<AccountSuggestionDto>();

        if (voucher.Type == VoucherType.Receipt)
        {
            suggestions.Add(new AccountSuggestionDto(voucher.TreasuryAccountCode, "الصندوق/البنك", "Debit", "المبلغ المقبوض"));
            suggestions.Add(new AccountSuggestionDto(voucher.PartyAccountCode, "حساب العميل/الطرف", "Credit", "تسوية حساب الطرف"));
        }
        else
        {
            suggestions.Add(new AccountSuggestionDto(voucher.PartyAccountCode, "حساب المورد/الطرف", "Debit", "تسوية حساب الطرف"));
            suggestions.Add(new AccountSuggestionDto(voucher.TreasuryAccountCode, "الصندوق/البنك", "Credit", "المبلغ المصروف"));
        }

        return suggestions;
    }
}
