using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// اعتماد طريقة الدفع المختارة في المستند عند الحفظ: الرمز لطريقة فعّالة في جدول طرق الدفع، ونوع الدفع يُشتق من نوعها
/// (الرمز هو المرجع لا النوع المرسل)، والترحيل على حسابها المرتبط. الرمز الفارغ = مستند قديم يُرحَّل بالنوع.
/// </summary>
public static class PaymentMethodRules
{
    /// <summary>نوع الطريقة في الجدول ← نوع الدفع في المستند (الشيك يُرحَّل كتحويل بنكي).</summary>
    public static PaymentMethod MethodOf(string type) => type switch
    {
        "cash" => PaymentMethod.Cash,
        "card" => PaymentMethod.BankCard,
        "credit" => PaymentMethod.Credit,
        _ => PaymentMethod.BankTransfer,
    };

    /// <summary>طريقة الرمز المختار، أو null بلا رمز. يرمي إن لم تكن موجودة أو كانت موقوفة.</summary>
    public static async Task<PaymentMethodItem?> RequireAsync(ErpDbContext db, string? code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var trimmed = code.Trim();
        var method = await db.Set<PaymentMethodItem>().AsNoTracking().FirstOrDefaultAsync(m => m.Code == trimmed, ct);
        if (method == null || method.Status != "active")
            throw new ValidationFailedException(string.Format(Messages.PaymentMethodNotAvailable, trimmed));
        return method;
    }

    /// <summary>نوع دفع المستند بعد اعتماد رمزه. العربون لا يُحصَّل بطريقة (يُقفل حساب العربونات) فلا يُتحقق من رمزه.</summary>
    public static async Task<PaymentMethod> MethodForAsync(ErpDbContext db, PaymentMethod method, string? code, CancellationToken ct)
    {
        if (method == PaymentMethod.CustomerDeposit) return method;
        var chosen = await RequireAsync(db, code, ct);
        return chosen == null ? method : MethodOf(chosen.Type);
    }

    /// <summary>حساب الخزينة لطريقة مختارة (يجب ألا تكون آجلة)؛ بلا رمز = الحساب المرسل.</summary>
    public static async Task<string?> AccountForAsync(ErpDbContext db, string? code, string? fallback, CancellationToken ct)
    {
        var chosen = await RequireAsync(db, code, ct);
        if (chosen == null) return fallback;
        if (chosen.Type == "credit") throw new ValidationFailedException(string.Format(Messages.PaymentMethodNotImmediate, chosen.NameAr));
        return chosen.LinkedAccountCode;
    }
}
