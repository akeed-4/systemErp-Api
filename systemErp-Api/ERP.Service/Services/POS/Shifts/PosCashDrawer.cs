using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

/// <summary>درج الكاشير: النقد المتوقع، وقيد عجز/زيادة الصندوق عند الإغلاق ومزامنته مع أي تصحيح لاحق.</summary>
internal static class PosCashDrawer
{
    /// <summary>افتتاحي + مبيعات نقدية − مرتجعات نقدية + إيداعات − صرفيات.</summary>
    public static decimal ExpectedCash(PosShift s)
        => s.OpeningCash + s.TotalCashSales - s.TotalCashRefunds + s.TotalCashIn - s.TotalCashOut;

    /// <summary>حساب الصندوق المرتبط بطريقة الدفع النقدية (نفس حساب قيود البيع النقدي).</summary>
    public static async Task<string> CashAccountAsync(ErpDbContext db, CancellationToken ct)
        => TreasuryResolver.Resolve(PaymentMethod.Cash, await db.Set<PaymentMethodItem>().AsNoTracking().ToListAsync(ct));

    /// <summary>
    /// يعيد حساب فرق الصندوق لوردية مغلقة ويزامن قيده: عند تغيّر الفرق يُعكس القيد القديم ويُرحَّل قيد جديد
    /// (زيادة: مدين الصندوق / دائن 421 — عجز: مدين 522 / دائن الصندوق). الوردية المفتوحة لا قيد لها.
    /// </summary>
    public static async Task SyncVarianceAsync(ErpDbContext db, IAccountingPostingService posting, PosShift shift, CancellationToken ct)
    {
        if (shift.Status != PosShiftStatus.Closed || !shift.ClosingCashActual.HasValue) return;
        var variance = DocumentPricing.Round(shift.ClosingCashActual.Value - ExpectedCash(shift));
        var inSync = shift.CashVariance == variance && shift.VarianceJournalEntryId.HasValue == (variance != 0);
        shift.CashVariance = variance;
        if (inSync) return;

        if (shift.VarianceJournalEntryId is Guid old)
        {
            await posting.ReverseAsync(old, $"تصحيح فرق صندوق الوردية {shift.ShiftNumber}", ct);
            shift.VarianceJournalEntryId = null;
        }
        if (variance == 0) return;

        await DefaultAccounts.EnsureAsync(db, ct, DefaultAccounts.CashOverage, DefaultAccounts.CashShortage);
        var cash = await CashAccountAsync(db, ct);
        var amount = Math.Abs(variance);
        var lines = variance > 0
            ? new List<PostingLine> { new(cash, amount, 0), new(DefaultAccounts.CashOverage, 0, amount) }
            : new List<PostingLine> { new(DefaultAccounts.CashShortage, amount, 0), new(cash, 0, amount) };
        var posted = await posting.PostAsync(new GenericPostingRequest
        {
            Date = shift.ClosedAt ?? DateTime.UtcNow,
            Description = $"{(variance > 0 ? "زيادة" : "عجز")} نقدية الصندوق - وردية {shift.ShiftNumber}",
            SourceType = "pos_shift_variance", SourceId = shift.Id, SourceNumber = shift.ShiftNumber, Lines = lines,
        }, ct);
        shift.VarianceJournalEntryId = posted.JournalEntryId;
    }
}
