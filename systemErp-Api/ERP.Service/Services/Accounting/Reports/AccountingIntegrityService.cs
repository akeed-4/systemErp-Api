using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.CarShowroom;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// سلامة الدفاتر. سطور القيود هي المصدر: رصيد الحساب المخزَّن نسخة سريعة منها يجب أن تساويها دائماً،
/// وقيمة المخزون في سجلّه التفصيلي يجب أن تساوي أرصدة حسابات المخزون.
/// </summary>
public class AccountingIntegrityService : IAccountingIntegrityService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public AccountingIntegrityService(ErpDbContext db, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _tx = tx; _audit = audit;
    }

    public async Task<BalanceIntegrityDto> CheckBalancesAsync(CancellationToken ct = default)
    {
        var accounts = await _db.Set<Account>().AsNoTracking().ToListAsync(ct);
        return Compare(accounts, await LedgerNetDebitsAsync(ct));
    }

    public Task<BalanceIntegrityDto> RebuildBalancesAsync(CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var accounts = await _db.Set<Account>().ToListAsync(token);
            var result = Compare(accounts, await LedgerNetDebitsAsync(token));
            if (result.DriftedCount == 0) return result;

            var drifted = result.Rows.Select(r => r.AccountCode).ToHashSet();
            var fixedAccounts = accounts.Where(a => drifted.Contains(a.Code)).ToList();
            var byCode = result.Rows.ToDictionary(r => r.AccountCode);
            foreach (var a in fixedAccounts) a.Balance = byCode[a.Code].LedgerBalance;
            await AccountingPostingService.SyncLinkedEntityBalancesAsync(_db, fixedAccounts, token);
            await _db.SaveChangesAsync(token);

            result.Rebuilt = true;
            await _audit.LogAsync("ACCOUNT_BALANCES_REBUILT", nameof(Account), string.Empty,
                $"إعادة احتساب أرصدة {result.DriftedCount} حساب من القيود (مجموع الفروق {result.TotalAbsoluteDifference:0.##})", token);
            return result;
        }, ct);

    /// <summary>صافي المدين (مدين - دائن) لكل حساب من سطور كل القيود المرحّلة؛ المعكوس وعكسه معاً أثرهما صفر.</summary>
    private Task<Dictionary<string, decimal>> LedgerNetDebitsAsync(CancellationToken ct)
        => (from l in _db.Set<JournalEntryLine>().AsNoTracking()
            join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
            where e.Status != JournalEntryStatus.Draft
            group l by l.AccountCode into g
            select new { Code = g.Key, Net = g.Sum(x => x.Debit - x.Credit) })
            .ToDictionaryAsync(x => x.Code, x => x.Net, ct);

    private static decimal NaturalBalance(Account a, Dictionary<string, decimal> netDebits)
    {
        var net = netDebits.GetValueOrDefault(a.Code);
        return a.IsDebitNature ? net : -net;
    }

    private static BalanceIntegrityDto Compare(List<Account> accounts, Dictionary<string, decimal> netDebits)
    {
        var rows = accounts
            .Select(a => new { a, Ledger = NaturalBalance(a, netDebits) })
            .Where(x => x.a.Balance != x.Ledger)
            .OrderBy(x => x.a.Code)
            .Select(x => new BalanceDriftRowDto
            {
                AccountCode = x.a.Code, AccountNameAr = x.a.NameAr, AccountNameEn = x.a.NameEn,
                StoredBalance = x.a.Balance, LedgerBalance = x.Ledger, Difference = x.a.Balance - x.Ledger,
            })
            .ToList();
        return new BalanceIntegrityDto
        {
            AccountsChecked = accounts.Count, DriftedCount = rows.Count,
            TotalAbsoluteDifference = rows.Sum(r => Math.Abs(r.Difference)), Rows = rows,
        };
    }

    public async Task<InventoryReconciliationDto> GetInventoryReconciliationAsync(CancellationToken ct = default)
    {
        var accounts = await _db.Set<Account>().AsNoTracking().ToListAsync(ct);
        var netDebits = await LedgerNetDebitsAsync(ct);
        var settings = await SalesCharges.SettingsAsync(_db, ct);
        var vehicleAccounts = Subtree(accounts, settings.VehicleInventoryAccount);

        // حسابات مخزون البضاعة: الحساب الافتراضي وفروعه، وأي حساب مخزون خاص اختير على فاتورة (عدا حسابات السيارات)
        var custom = await _db.Set<InvoiceBase>().AsNoTracking()
            .Where(i => i.InventoryAccountCode != null && i.InventoryAccountCode != "")
            .Select(i => i.InventoryAccountCode!).Distinct().ToListAsync(ct);
        var itemAccounts = Subtree(accounts, DefaultAccounts.Inventory)
            .Union(custom.SelectMany(c => Subtree(accounts, c)))
            .Except(vehicleAccounts)
            .ToList();

        var rows = new List<InventoryReconciliationRowDto>
        {
            Row("items", "مخزون البضاعة", "Merchandise inventory",
                await _db.Set<Product>().AsNoTracking().SumAsync(p => p.CurrentStock * p.AverageCost, ct), itemAccounts, accounts, netDebits),
        };

        // السيارات في المخزون (المتاحة والمحجوزة) بتكلفتها الكاملة، مقابل حساب مخزون السيارات
        var vehiclesValue = await _db.Set<Vehicle>().AsNoTracking()
            .Where(v => v.Status == VehicleStatus.Available || v.Status == VehicleStatus.Reserved)
            .SumAsync(v => v.TotalCost, ct);
        var vehicleRow = Row("vehicles", "مخزون السيارات", "Vehicle inventory", vehiclesValue, vehicleAccounts, accounts, netDebits);
        if (vehicleRow.SubledgerValue != 0 || vehicleRow.LedgerValue != 0) rows.Add(vehicleRow);

        return new InventoryReconciliationDto { AsOf = SaudiTime.Now, Rows = rows, IsReconciled = rows.All(r => r.IsReconciled) };
    }

    private static InventoryReconciliationRowDto Row(string key, string ar, string en, decimal subledger, List<string> codes,
        List<Account> accounts, Dictionary<string, decimal> netDebits)
    {
        var byCode = accounts.ToDictionary(a => a.Code);
        var leaves = codes.Where(c => byCode.ContainsKey(c)).ToList();
        var ledger = leaves.Sum(c => NaturalBalance(byCode[c], netDebits));
        var sub = Math.Round(subledger, 2, MidpointRounding.AwayFromZero);
        var diff = sub - ledger;
        return new InventoryReconciliationRowDto
        {
            Key = key, LabelAr = ar, LabelEn = en, SubledgerValue = sub, LedgerValue = ledger, Difference = diff,
            // فرق دون هللة واحدة ناتج عن تقريب متوسط التكلفة لكل صنف، لا عن قيد ناقص
            IsReconciled = Math.Abs(diff) < 0.01m, AccountCodes = leaves.OrderBy(c => c).ToList(),
        };
    }

    /// <summary>الحساب وكل فروعه (الترحيل على الطرفية فقط، فجمعها كلها لا يكرر شيئاً).</summary>
    private static List<string> Subtree(List<Account> accounts, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return new();
        var result = new List<string> { root };
        for (var i = 0; i < result.Count; i++)
            result.AddRange(accounts.Where(a => a.ParentCode == result[i]).Select(a => a.Code));
        return result;
    }
}
