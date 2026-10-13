namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// توزيع مصروف واحد على عدة سيارات. دالة صِرفة بلا قاعدة بيانات: تأخذ المبلغ وأوزان السيارات وتُرجع الحصص
/// مقرّبة لهللتين، ومجموعها يساوي المبلغ تماماً؛ فرق التقريب يُضاف لآخر سيارة.
/// </summary>
public static class ChargeAllocator
{
    /// <summary>حصة كل سيارة بنسبة وزنها (القيمة، الحجم، الوزن...).</summary>
    public static decimal[] Allocate(decimal total, IReadOnlyList<decimal> weights)
    {
        if (total < 0) throw new ArgumentOutOfRangeException(nameof(total));
        if (weights.Count == 0) return Array.Empty<decimal>();
        if (weights.Any(w => w < 0)) throw new ArgumentException("الأوزان لا تكون سالبة.", nameof(weights));
        var sum = weights.Sum();
        if (sum <= 0) throw new ArgumentException("مجموع الأوزان يجب أن يكون موجباً.", nameof(weights));

        var shares = new decimal[weights.Count];
        var allocated = 0m;
        for (var i = 0; i < weights.Count - 1; i++)
        {
            // لا تتجاوز الحصة المتبقي، حتى لا يدفع التقريبُ آخرَ سيارة إلى حصة سالبة
            shares[i] = Math.Min(Math.Round(total * weights[i] / sum, 2, MidpointRounding.AwayFromZero), total - allocated);
            allocated += shares[i];
        }
        shares[^1] = total - allocated;
        return shares;
    }

    public static decimal[] Equal(decimal total, int count)
        => Allocate(total, Enumerable.Repeat(1m, count).ToArray());
}
