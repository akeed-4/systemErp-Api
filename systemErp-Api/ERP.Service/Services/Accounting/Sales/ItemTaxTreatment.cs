using ERP.Core.DTOs.Accounting;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المعالجة الضريبية لبند الصنف في فاتورة المبيعات: منطق واحد في السرفر يُشتق فيه التصنيف والنسبة وسبب الإعفاء من تعريف الصنف،
/// ولا يُغيَّران إلا بتجاوز صريح (سبب + صلاحية) يُسجَّل في سجل التدقيق. القيم المعتمدة تُحفظ لقطةً على السطر وقت الإصدار،
/// فتعديل تعريف الصنف لاحقاً لا يغيّر فاتورة صدرت (ولا يغيّرها تعديلها: أسطرها المحفوظة هي الأصل لا تعريف الصنف الحالي).
/// </summary>
public static class ItemTaxTreatment
{
    /// <summary>التصنيف والنسبة وسبب الإعفاء؛ غير الخاضع للنسبة الأساسية بلا ضريبة.</summary>
    public sealed record Treatment(VatCategory Category, decimal Rate, string? ReasonCode)
    {
        public bool SameAs(Treatment other) => Category == other.Category && Rate == other.Rate
            && string.Equals(ReasonCode, other.ReasonCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>تجاوز جديد على سطر: يُسجَّل بعد الحفظ بقيمته الأصلية والجديدة وسببه.</summary>
    public sealed record Override(string ItemName, Treatment Original, Treatment Requested, string Reason)
    {
        public string Describe() => $"{ItemName}: {Label(Original)} ← {Label(Requested)} — السبب: {Reason}";
    }

    /// <summary>المعالجة الافتراضية من تعريف الصنف.</summary>
    public static Treatment Of(Product p) => p.VatCategory == VatCategory.Standard
        ? new Treatment(VatCategory.Standard, p.VatRate, null)
        : new Treatment(p.VatCategory, 0, p.VatExemptionReasonCode);

    /// <summary>المعالجة المحفوظة على سطر فاتورة صدرت (لقطتها).</summary>
    public static Treatment Of(InvoiceItemBase line) => new(line.VatCategory, line.VatRate, line.VatExemptionReasonCode);

    /// <summary>المعالجة التي يطلبها السطر المرسل (تصنيف فارغ = أساسي للخاضع وصفري للنسبة 0، كما يبنيه المحرك).</summary>
    public static Treatment Requested(InvoiceItemDto item)
        => new(item.VatCategory ?? (item.VatRate > 0 ? VatCategory.Standard : VatCategory.ZeroRated), item.VatRate,
            VatExemptionReasons.Find(item.VatExemptionReasonCode)?.Code);

    /// <summary>
    /// يطبّق معالجة الصنف على أسطر فاتورة مبيعات. <paramref name="previous"/> أسطر النسخة المحفوظة عند التعديل: الصنف الذي كان فيها
    /// يحتفظ بلقطته (لا يُعاد قراءة تعريفه)، وتجاوزه المحفوظ نفسه لا يتطلب الصلاحية من جديد.
    /// يعيد التجاوزات الجديدة لتُسجَّل؛ و<paramref name="canOverride"/> يُستدعى فقط حين يوجد تجاوز جديد.
    /// </summary>
    public static async Task<List<Override>> ApplyAsync(IList<InvoiceItemDto> items, IReadOnlyDictionary<Guid, Product> products,
        IReadOnlyList<InvoiceItemBase> previous, Func<Task<bool>> canOverride)
    {
        var overrides = new List<Override>();
        foreach (var item in items)
        {
            if (item.ItemId == Guid.Empty || !products.TryGetValue(item.ItemId, out var product)) continue; // بند خدمة أو صنف مفقود (يُرفض لاحقاً)
            var kept = previous.FirstOrDefault(l => l.ItemId == item.ItemId);
            var original = kept is { VatOverrideReason: null } ? Of(kept) : Of(product);
            var reason = string.IsNullOrWhiteSpace(item.VatOverrideReason) ? null : item.VatOverrideReason.Trim();
            item.VatAmountOverride = null; // ضريبة الصنف من نسبته دائماً (المبلغ الثابت لهامش ربح السيارات وحدها)

            var requested = reason == null ? original : Requested(item);
            if (reason == null || requested.SameAs(original))
            {
                Set(item, original, null);
                continue;
            }
            if (requested.Category == VatCategory.MarginScheme) throw new ValidationFailedException(Messages.MarginSchemeItemNotAllowed);
            var alreadyOverridden = kept?.VatOverrideReason != null && Of(kept).SameAs(requested);
            if (!alreadyOverridden)
            {
                if (!await canOverride()) throw new ForbiddenException(Messages.InvoiceTaxTreatmentOverrideForbidden);
                overrides.Add(new Override(string.IsNullOrWhiteSpace(item.ItemName) ? product.NameAr : item.ItemName, original, requested, reason));
            }
            Set(item, requested, reason);
        }
        return overrides;
    }

    private static void Set(InvoiceItemDto item, Treatment t, string? reason)
    {
        // صنف قديم «خاضع» بنسبة 0 (قبل تعريف التصنيفات): يبقى على سلوكه السابق، سطراً صفرياً يستنتجه المحرك
        item.VatCategory = t.Category == VatCategory.Standard && t.Rate == 0 ? null : t.Category;
        item.VatRate = t.Rate;
        item.VatExemptionReasonCode = t.ReasonCode;
        item.VatOverrideReason = reason;
    }

    public static string Label(Treatment t) => t.Category switch
    {
        VatCategory.Standard => $"خاضع {t.Rate:0.##}%",
        VatCategory.ZeroRated => $"صفري ({t.ReasonCode})",
        VatCategory.Exempt => $"معفى ({t.ReasonCode})",
        VatCategory.OutOfScope => $"خارج النطاق ({t.ReasonCode})",
        _ => "هامش الربح",
    };
}
