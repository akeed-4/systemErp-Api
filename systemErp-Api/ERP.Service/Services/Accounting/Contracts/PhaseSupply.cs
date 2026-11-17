namespace ERP.Service.Services.Accounting;

/// <summary>
/// حساب طلب الشراء حسب مراحل التنفيذ (منطق صرف بلا قاعدة بيانات). لكل مرحلة وصنف:
/// <list type="bullet">
/// <item>المطلوب = الاحتياجات المعتمدة المسندة للمرحلة (بوحدة الصنف).</item>
/// <item>طُلب للمرحلة = طلبات الشراء غير المرفوضة المرتبطة بها، بكل حالاتها: معلّق، ومعتمد لم يُستلم (مشتريات لم تُسلَّم)، ومحوّل لم
/// يُستلم (أمر شراء معلّق)، ومستلم. فكل كمية في الدورة تُطرح مرة واحدة.</item>
/// <item>مخزون المشروع المشترك = ما سُلِّم للموقع من المستودع العام ولم يأتِ من طلبات المشروع + طلبات المشروع القديمة بلا مرحلة. يُوزَّع
/// على المراحل بترتيبها (الأسبق أولاً) بقدر ما ينقص كل مرحلة بعد طلباتها، فلا يُحسب لمرحلتين.</item>
/// <item>المتبقي للطلب (الحد الملزم) = المطلوب − طُلب للمرحلة − حصتها من مخزون المشروع.</item>
/// <item>صافي الشراء (المقترح) = المتبقي − حصة المرحلة من رصيد مستودع المشروع الحر (غير المحجوز؛ يُصرف بدل الشراء).</item>
/// </list>
/// طلب مرحلة لا يُنقص حد مرحلة أخرى إلا عبر توزيع المخزون المشترك، لذا يحرس السرفر الصنف على كل المراحل عند الحفظ (رمز التزامن).
/// </summary>
internal static class PhaseSupply
{
    private const int Decimals = 4;
    public const string Upcoming = "upcoming", Current = "current", Completed = "completed";

    /// <summary>أرقام صنف في مرحلة.</summary>
    public sealed class Cell
    {
        public decimal Required { get; set; }
        /// <summary>التكلفة التقديرية للمطلوب (لتكلفة الوحدة).</summary>
        public decimal RequiredCost { get; set; }
        public decimal Requested { get; set; }
        /// <summary>في طلبات المرحلة غير المحوّلة ولم يُستلم (ومنه المعتمد = مشتريات لم تُسلَّم).</summary>
        public decimal Pending { get; set; }
        public decimal Approved { get; set; }
        /// <summary>في فواتير شراء مسودة من طلبات المرحلة (أمر شراء معلّق).</summary>
        public decimal Ordered { get; set; }
        public decimal Received { get; set; }
        public decimal ProjectInventory { get; private set; }
        public decimal Remaining { get; private set; }
        public decimal FreeStock { get; private set; }
        public decimal NetToPurchase { get; private set; }

        internal void Settle(decimal inventory, decimal stock)
        {
            var need = Math.Max(0, Required - Requested);
            ProjectInventory = Round(Math.Min(inventory, need));
            Remaining = Round(need - ProjectInventory);
            FreeStock = Round(Math.Min(stock, Remaining));
            NetToPurchase = Round(Remaining - FreeStock);
        }
    }

    /// <summary>
    /// يوزّع مخزون المشروع المشترك ثم رصيد المستودع الحر لكل صنف على المراحل بترتيبها، ويحسب المتبقي وصافي الشراء لكل خلية.
    /// </summary>
    /// <param name="orderedPhases">معرّفات المراحل بترتيب التنفيذ.</param>
    /// <param name="cells">المرحلة ← الصنف ← الخلية.</param>
    public static void Allocate(IReadOnlyList<Guid> orderedPhases, IReadOnlyDictionary<Guid, Dictionary<Guid, Cell>> cells,
        IReadOnlyDictionary<Guid, decimal> projectInventory, IReadOnlyDictionary<Guid, decimal> freeStock)
    {
        foreach (var item in cells.Values.SelectMany(c => c.Keys).Distinct())
        {
            var inventory = Math.Max(0, projectInventory.GetValueOrDefault(item));
            var stock = Math.Max(0, freeStock.GetValueOrDefault(item));
            foreach (var phase in orderedPhases)
            {
                if (!cells.TryGetValue(phase, out var byItem) || !byItem.TryGetValue(item, out var cell)) continue;
                cell.Settle(inventory, stock);
                inventory -= cell.ProjectInventory;
                stock -= cell.FreeStock;
            }
        }
    }

    /// <summary>
    /// المرحلة المستقبلية: لم يحن تاريخ بدايتها، وليست من أوائل المراحل (أبكر تاريخ بداية في المشروع؛ تُجهَّز موادها قبل البدء).
    /// تتقدم المراحل بالتاريخ وحده، فلا يُعدَّل سجل مرحلة سابقة.
    /// </summary>
    public static bool IsFuture(DateTime plannedStart, DateTime earliestStart, DateTime today)
        => plannedStart.Date > today.Date && plannedStart.Date > earliestStart.Date;

    public static string State(DateTime plannedStart, DateTime plannedEnd, DateTime today)
        => today.Date < plannedStart.Date ? Upcoming : today.Date > plannedEnd.Date ? Completed : Current;

    private static decimal Round(decimal v) => Math.Round(v, Decimals);
}
