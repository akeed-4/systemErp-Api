using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// حدود بيان التسليم (وسند الاستلام) على اتفاقية: كل سطر من بنود الاتفاقية، وصافي المسلَّم لكل بند
/// (هذا البيان + بياناتها الأخرى المعتمدة بعد مرتجعاتها) لا يتجاوز كميته المتفق عليها، فيصح التسليم على دفعات
/// ولا يُسلَّم المتبقي مرتين. عقود جدول الكميات وسندات استلام المشاريع لها قواعدها (المستخلصات و<see cref="ProjectReceipts"/>).
/// </summary>
internal static class ContractDeliveries
{
    private const decimal Tolerance = 0.00005m;

    /// <summary>
    /// مفتاح بند الاتفاقية كما يرسله سطر البيان: الصنف المرتبط، وإلا معرّف البند نفسه (بند وصفي بلا صنف).
    /// </summary>
    private static Guid KeyOf(CommercialContractItem item) => item.ItemId ?? item.Id;

    /// <summary>
    /// قفل الاتفاقية حتى نهاية المعاملة: بيانان متزامنان على الاتفاقية نفسها لا يقرآن المتبقي نفسه فيتجاوزانه معاً.
    /// </summary>
    private static async Task LockAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Contract delivery lock requires an active transaction.");
        var resource = $"contract-delivery:{db.CurrentTenantId:N}:{contractId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DECLARE @r int; EXEC @r = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000; IF @r < 0 THROW 51000, 'contract delivery lock timeout', 1;",
            ct);
    }

    /// <summary>
    /// يرفض سطراً ليس من بنود الاتفاقية، وكمية تتجاوز المتبقي من البند (المتفق عليه − صافي المسلَّم في بياناتها الأخرى).
    /// يُستدعى عند الإنشاء والتعديل داخل المعاملة، بعد ربط البيان بالاتفاقية.
    /// </summary>
    public static async Task EnsureWithinContractAsync(ErpDbContext db, DeliveryNote note, CommercialContract contract, CancellationToken ct)
    {
        await LockAsync(db, contract.Id, ct);
        var agreed = await db.Set<CommercialContractItem>().AsNoTracking()
            .Where(i => i.ContractId == contract.Id && !i.IsGroup)
            .ToListAsync(ct);
        var lines = agreed.GroupBy(KeyOf).ToDictionary(g => g.Key, g => (Name: g.First().Description, Qty: g.Sum(i => i.Quantity), LineId: g.First().Id));

        var noteId = note.Id;
        var delivered = await DeliverySources.Active(db.Set<DeliveryNoteItem>().AsNoTracking())
            .Where(i => i.DeliveryNote.ContractId == contract.Id && i.DeliveryNoteId != noteId)
            .GroupBy(i => i.ItemId)
            // الفعّال: المؤكد تسليمه إن أُكّد وإلا كمية البيان، بعد مرتجع التسليم؛ الملغى لا يحجز
            .Select(g => new { ItemId = g.Key, Net = g.Sum(i => (i.ConfirmedQty ?? i.DeliveredQty) - i.ReturnedQty) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Net, ct);

        foreach (var g in note.Items.GroupBy(i => i.ItemId))
        {
            if (!lines.TryGetValue(g.Key, out var line))
                throw new ValidationFailedException(string.Format(Messages.ItemNotInContract, g.First().ItemName, contract.ContractNumber));
            foreach (var i in g) i.SourceLineId = line.LineId;
            var qty = g.Sum(i => i.DeliveredQty);
            var remaining = line.Qty - delivered.GetValueOrDefault(g.Key);
            if (qty > remaining + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.DeliveryExceedsContract,
                    line.Name, qty.ToString("0.####"), Math.Max(0, remaining).ToString("0.####"), line.Qty.ToString("0.####")));
        }
    }

    /// <summary>
    /// بعد إعادة كمية إلى سند غير متتبَّع على الاتفاقية (حذف مرتجع سند استلام): صافي سنداتها لكل بند لا يتجاوز المتفق عليه.
    /// يقفل الاتفاقية ثم يحفظ ويقرأ، فلا يتجاوز مع سند لاحق أُنشئ على المتبقي الذي أعاده المرتجع.
    /// </summary>
    public static async Task EnsureRoomAfterChangeAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        await LockAsync(db, contractId, ct);
        await db.SaveChangesAsync(ct);
        var agreed = await db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == contractId && !i.IsGroup)
            .Select(i => new { Key = i.ItemId ?? i.Id, i.Description, i.Quantity }).ToListAsync(ct);
        var delivered = await DeliverySources.Active(db.Set<DeliveryNoteItem>().AsNoTracking()).Where(i => i.DeliveryNote.ContractId == contractId)
            .GroupBy(i => i.ItemId).Select(g => new { g.Key, Qty = g.Sum(i => (i.ConfirmedQty ?? i.DeliveredQty) - i.ReturnedQty) }).ToListAsync(ct);
        foreach (var d in delivered)
        {
            var lines = agreed.Where(a => a.Key == d.Key).ToList();
            var qty = lines.Sum(a => a.Quantity);
            if (lines.Count > 0 && d.Qty > qty + Tolerance)
                throw new ConflictException(string.Format(Messages.DeliveryExceedsContract, lines[0].Description, d.Qty.ToString("0.####"),
                    qty.ToString("0.####"), qty.ToString("0.####")));
        }
    }
}
