using System.Globalization;

namespace ERP.Service.Services.Shared;

/// <summary>
/// قواعد الدفع المتعدد في المستندات (الفواتير والسندات): لكل سطر طريقة مختلفة (الطريقة المؤسَّسة برمزها، أو نوع الدفع
/// للسطر بلا رمز)، ومجموع المبالغ يساوي الإجمالي برسالة توضّح الزيادة أو النقص بمقدارها.
/// </summary>
public static class PaymentSplitRules
{
    private const decimal Tolerance = 0.005m;

    /// <summary>يرمي إن تكررت طريقة في أكثر من سطر.</summary>
    public static void EnsureDistinctMethods(IEnumerable<(PaymentMethod Method, string? Code)> splits)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (method, code) in splits)
        {
            var key = string.IsNullOrWhiteSpace(code) ? "type:" + method : code.Trim();
            if (!seen.Add(key))
                throw new ValidationFailedException(string.Format(Messages.SplitPaymentMethodRepeated, string.IsNullOrWhiteSpace(code) ? method.ToString() : code.Trim()));
        }
    }

    /// <summary>رسالة اختلاف مجموع الدفعات عن الإجمالي (زيادة أو نقص بمقدارهما)، أو null إن تساويا.</summary>
    public static string? TotalProblem(decimal paid, decimal total)
    {
        var diff = paid - total;
        if (Math.Abs(diff) <= Tolerance) return null;
        string F(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);
        return string.Format(diff > 0 ? Messages.SplitPaymentsExceedTotal : Messages.SplitPaymentsBelowTotal, F(paid), F(total), F(Math.Abs(diff)));
    }
}
