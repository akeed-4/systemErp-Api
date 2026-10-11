using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الرصيد الافتتاحي لعميل/مورد/بنك قيدٌ في الدفاتر مقابل حساب الأرصدة الافتتاحية (حقوق ملكية)، لا رقماً يُجمع خارجها.
/// الموجب بطبيعة حساب الطرف: مدين للعميل والبنك، دائن للمورد؛ والسالب عكسه.
/// </summary>
internal static class OpeningBalances
{
    private const string SourceType = "opening_balance";

    /// <summary>
    /// يستبدل قيد الرصيد الافتتاحي للطرف بقيمته الحالية: يعكس القيد السابق إن وُجد ويرحّل الجديد.
    /// <paramref name="setEntry"/> يحفظ معرّف القيد على الطرف (null للرصيد الصفري).
    /// </summary>
    public static async Task ApplyAsync(IAccountingPostingService posting, ErpDbContext db, Guid? currentEntryId, Action<Guid?> setEntry,
        string accountCode, decimal amount, bool debitNature, string partyName, Guid partyId, CancellationToken ct)
    {
        if (currentEntryId.HasValue)
            await posting.ReverseAsync(currentEntryId.Value, $"تعديل الرصيد الافتتاحي - {partyName}", ct);

        // من هنا الرصيد الافتتاحي في الدفاتر: رصيد الطرف = رصيد حسابه، فلا يُجمع الافتتاحي عليه مرة ثانية أثناء الترحيل
        setEntry(Guid.Empty);
        if (amount == 0) { setEntry(null); return; }

        await DefaultAccounts.EnsureAsync(db, ct, DefaultAccounts.OpeningBalanceEquity);
        var value = Math.Abs(amount);
        var debitParty = debitNature == amount > 0;
        var posted = await posting.PostAsync(new GenericPostingRequest
        {
            Date = DateTime.UtcNow, Description = $"رصيد افتتاحي - {partyName}",
            SourceType = SourceType, SourceId = partyId, SourceNumber = accountCode,
            Lines = debitParty
                ? new() { new(accountCode, value, 0), new(DefaultAccounts.OpeningBalanceEquity, 0, value) }
                : new() { new(DefaultAccounts.OpeningBalanceEquity, value, 0), new(accountCode, 0, value) },
        }, ct);
        setEntry(posted.JournalEntryId);
    }

    /// <summary>رصيد الطرف المعروض: رصيد حسابه، مضافاً إليه الافتتاحي فقط للأطراف القديمة التي لم يُقيَّد افتتاحيها بعد.</summary>
    public static decimal Current(decimal accountBalance, decimal openingBalance, Guid? openingEntryId)
        => accountBalance + (openingEntryId.HasValue ? 0 : openingBalance);
}
