using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class PartyAgingService : IPartyAgingService
{
    private readonly ErpDbContext _db;
    public PartyAgingService(ErpDbContext db) => _db = db;

    private sealed record Party(Guid Id, string Name, string AccountCode);

    private async Task<List<Party>> PartiesAsync(bool receivable, CancellationToken ct)
        => receivable
            ? await _db.Set<Customer>().AsNoTracking().Select(c => new Party(c.Id, c.NameAr, c.AccountCode)).ToListAsync(ct)
            : await _db.Set<Supplier>().AsNoTracking().Select(s => new Party(s.Id, s.NameAr, s.AccountCode)).ToListAsync(ct);

    public async Task<AgingReportDto> GetAsync(bool receivable, DateTime? asOf, CancellationToken ct = default)
    {
        var date = (asOf ?? DateTime.UtcNow).Date;
        var end = date.AddDays(1);
        var kind = receivable ? InvoiceKind.Sales : InvoiceKind.Purchase;
        var parties = await PartiesAsync(receivable, ct);

        var invoices = await _db.Set<Invoice>().AsNoTracking()
            .Where(i => i.Kind == kind && i.Status == "posted" && i.PartyId != null && i.IssueDate < end)
            .Select(i => new { i.Id, PartyId = i.PartyId!.Value, i.IssueDate, i.ExchangeRate }).ToListAsync(ct);
        var balances = await InvoiceBalances.ForAsync(_db, invoices.Select(i => i.Id).ToList(), null, ct);

        // سندات الطرف غير الموزّعة على فواتير: دفعات مقدمة تُطرح من صافي ما عليه
        var voucherType = receivable ? VoucherType.Receipt : VoucherType.Payment;
        var accountCodes = parties.Select(p => p.AccountCode).ToList();
        var vouchers = await _db.Set<Voucher>().AsNoTracking()
            .Where(v => v.Type == voucherType && v.Date < end && accountCodes.Contains(v.PartyAccountCode))
            .Select(v => new { v.PartyAccountCode, Free = v.Amount - v.VatAmount - v.Allocations.Sum(a => (decimal?)a.Amount) ?? v.Amount - v.VatAmount })
            .ToListAsync(ct);
        var unallocated = vouchers.GroupBy(v => v.PartyAccountCode).ToDictionary(g => g.Key, g => g.Sum(v => v.Free));

        var rows = new List<AgingRowDto>();
        foreach (var party in parties.OrderBy(p => p.Name))
        {
            var row = new AgingRowDto { PartyId = party.Id, PartyName = party.Name, AccountCode = party.AccountCode };
            foreach (var invoice in invoices.Where(i => i.PartyId == party.Id))
            {
                if (!balances.TryGetValue(invoice.Id, out var balance) || balance.AmountDue <= 0) continue;
                var due = DocumentPricing.Round(balance.AmountDue * (invoice.ExchangeRate <= 0 ? 1 : invoice.ExchangeRate));
                var age = (date - invoice.IssueDate.Date).Days;
                if (age <= 30) row.Days0To30 += due;
                else if (age <= 60) row.Days31To60 += due;
                else if (age <= 90) row.Days61To90 += due;
                else row.Over90 += due;
                row.OpenInvoices++;
            }
            row.TotalDue = row.Days0To30 + row.Days31To60 + row.Days61To90 + row.Over90;
            row.Unallocated = DocumentPricing.Round(unallocated.GetValueOrDefault(party.AccountCode));
            row.Net = row.TotalDue - row.Unallocated;
            if (row.TotalDue != 0 || row.Unallocated != 0) rows.Add(row);
        }

        return new AgingReportDto
        {
            AsOf = date, Type = receivable ? "receivable" : "payable", Rows = rows,
            Totals = new AgingRowDto
            {
                Days0To30 = rows.Sum(r => r.Days0To30), Days31To60 = rows.Sum(r => r.Days31To60), Days61To90 = rows.Sum(r => r.Days61To90),
                Over90 = rows.Sum(r => r.Over90), TotalDue = rows.Sum(r => r.TotalDue), Unallocated = rows.Sum(r => r.Unallocated),
                Net = rows.Sum(r => r.Net), OpenInvoices = rows.Sum(r => r.OpenInvoices),
            },
        };
    }

    public async Task<List<OpenInvoiceDto>> OpenInvoicesAsync(string partyAccountCode, CancellationToken ct = default)
    {
        var code = partyAccountCode?.Trim() ?? string.Empty;
        var customerId = await _db.Set<Customer>().AsNoTracking().Where(c => c.AccountCode == code).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        var supplierId = customerId.HasValue ? null
            : await _db.Set<Supplier>().AsNoTracking().Where(s => s.AccountCode == code).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
        if (customerId == null && supplierId == null) return new();

        var kind = customerId.HasValue ? InvoiceKind.Sales : InvoiceKind.Purchase;
        var partyId = customerId ?? supplierId;
        var invoices = await _db.Set<Invoice>().AsNoTracking()
            .Where(i => i.Kind == kind && i.Status == "posted" && i.PartyId == partyId)
            .OrderBy(i => i.IssueDate).ThenBy(i => i.InvoiceNumber)
            .Select(i => new OpenInvoiceDto { Id = i.Id, InvoiceNumber = i.InvoiceNumber, IssueDate = i.IssueDate, GrandTotal = i.GrandTotal, CurrencyCode = i.CurrencyCode })
            .ToListAsync(ct);
        var balances = await InvoiceBalances.ForAsync(_db, invoices.Select(i => i.Id).ToList(), null, ct);
        foreach (var invoice in invoices)
            if (balances.TryGetValue(invoice.Id, out var balance)) { invoice.CreditAmount = balance.CreditAmount; invoice.AmountDue = balance.AmountDue; }
        return invoices.Where(i => i.AmountDue > 0).ToList();
    }
}
