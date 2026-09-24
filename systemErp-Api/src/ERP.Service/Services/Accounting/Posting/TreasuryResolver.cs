using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ERP.Service.Services.Shared.Text;

namespace ERP.Service.Services.Accounting;

/// <summary>يحوّل طريقة الدفع إلى حساب خزينة/بنك: حساب طريقة الدفع المُعرَّفة إن وُجد وإلا الصندوق/البنك الافتراضي.</summary>
public static class TreasuryResolver
{
    public static string Resolve(PaymentMethod method, IReadOnlyList<PaymentMethodItem> configured)
    {
        var type = method switch
        {
            PaymentMethod.Cash => "cash",
            PaymentMethod.BankCard => "card",
            PaymentMethod.BankTransfer => "bank",
            _ => "credit",
        };
        var match = configured.FirstOrDefault(m => m.Status == "active" && m.Type == type && !string.IsNullOrEmpty(m.LinkedAccountCode));
        if (match != null) return match.LinkedAccountCode;
        return method == PaymentMethod.Cash ? DefaultAccounts.Cash : DefaultAccounts.DefaultBank;
    }
}
