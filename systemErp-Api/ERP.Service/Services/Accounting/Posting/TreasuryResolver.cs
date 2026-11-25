using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ERP.Service.Services.Shared.Text;

namespace ERP.Service.Services.Accounting;

/// <summary>يحوّل طريقة الدفع إلى حساب خزينة/بنك: حساب طريقة الدفع المُعرَّفة إن وُجد وإلا الصندوق/البنك الافتراضي.</summary>
public static class TreasuryResolver
{
    /// <param name="depositsAccount">حساب عربونات العملاء من إعدادات المعرض؛ فارغ = الافتراضي.</param>
    /// <param name="code">رمز طريقة دفع مختارة من جدول طرق الدفع: حسابها المرتبط يُقدَّم على الاختيار بالنوع.</param>
    /// <param name="siteCash">صندوق الموقع: النقد بلا طريقة دفع مختارة صراحةً يُقيَّد عليه بدل الصندوق العام.</param>
    public static string Resolve(PaymentMethod method, IReadOnlyList<PaymentMethodItem> configured, string? depositsAccount = null, string? code = null,
        string? siteCash = null)
    {
        // استقطاع عقد: الرمز هو حساب الاستقطاع نفسه (محتجزات/غرامات/خصومات) يحدده النظام عند إنشاء فاتورة المستخلص
        if (method == PaymentMethod.ContractSettlement)
            return string.IsNullOrWhiteSpace(code) ? throw new ValidationFailedException(Messages.InvalidPaymentMethod) : code.Trim();
        if (method != PaymentMethod.CustomerDeposit && !string.IsNullOrWhiteSpace(code))
        {
            var chosen = configured.FirstOrDefault(m => m.Code == code && m.Status == "active" && !string.IsNullOrEmpty(m.LinkedAccountCode));
            // طريقة نقدية على الصندوق العام في موقع له صندوقه: صندوق الموقع. طريقة على صندوق/بنك آخر محدد تبقى كما اختيرت
            if (chosen != null && !(method == PaymentMethod.Cash && !string.IsNullOrWhiteSpace(siteCash)
                    && chosen.LinkedAccountCode == Resolve(PaymentMethod.Cash, configured)))
                return chosen.LinkedAccountCode;
        }
        if (method == PaymentMethod.Cash && !string.IsNullOrWhiteSpace(siteCash)) return siteCash;
        if (method == PaymentMethod.CustomerDeposit) return string.IsNullOrWhiteSpace(depositsAccount) ? DefaultAccounts.CustomerDeposits : depositsAccount;
        var type = method switch
        {
            PaymentMethod.Cash => "cash",
            PaymentMethod.BankCard => "card",
            PaymentMethod.BankTransfer => "bank",
            _ => "credit",
        };
        // الافتراضي للنوع: أقدم طريقة فعّالة منه (ثم بالرمز)، فطريقة صندوق تُنشأ لاحقاً (CashBoxPaymentMethods) لا تغيّر حساب النقد الافتراضي
        var match = configured.Where(m => m.Status == "active" && m.Type == type && !string.IsNullOrEmpty(m.LinkedAccountCode))
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Code, StringComparer.Ordinal).FirstOrDefault();
        if (match != null) return match.LinkedAccountCode;
        return method == PaymentMethod.Cash ? DefaultAccounts.Cash : DefaultAccounts.DefaultBank;
    }
}
