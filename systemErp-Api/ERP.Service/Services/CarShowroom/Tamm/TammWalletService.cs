using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// محفظة «تم»: حساب مدفوع مقدماً يُشحن من البنك وتُخصم منه رسوم العمليات الناجحة تلقائياً، ويُطابَق مع كشف تم.
/// الرصيد الدفتري هو رصيد حساب المحفظة المحدَّد في إعدادات المعرض.
/// </summary>
public class TammWalletService : ITammWalletService
{
    public const string TopUpSource = "car_tamm_wallet_topup";
    public const string AdjustmentSource = "car_tamm_wallet_adjustment";

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IAccountingPostingService _posting;
    private readonly IAuditService _audit;

    public TammWalletService(ErpDbContext db, ITransactionRunner tx, IAccountingPostingService posting, IAuditService audit)
    {
        _db = db; _tx = tx; _posting = posting; _audit = audit;
    }

    private async Task<decimal> BalanceAsync(string accountCode, CancellationToken ct)
        => await _db.Set<Account>().AsNoTracking().Where(a => a.Code == accountCode).Select(a => (decimal?)a.Balance).FirstOrDefaultAsync(ct) ?? 0;

    public async Task<TammWalletDto> GetAsync(PaginationParams p, CancellationToken ct = default)
    {
        var settings = await SalesCharges.SettingsAsync(_db, ct);
        var q = _db.Set<TammWalletTransaction>().AsNoTracking();
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        var last = await q.Where(t => t.Type == TammWalletTransactionType.Reconciliation).OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        return new TammWalletDto
        {
            AccountCode = settings.TammWalletAccount, Balance = await BalanceAsync(settings.TammWalletAccount, ct),
            LastReconciliation = last == null ? null : Mapper.Map<TammWalletTransactionDto>(last),
            Transactions = new PagedResultOfWalletTransactions { Items = items.Select(Mapper.Map<TammWalletTransactionDto>).ToList(), TotalCount = total },
        };
    }

    public Task<TammWalletTransactionDto> TopUpAsync(TammWalletTopUpDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (d.Amount <= 0) throw new ValidationFailedException(Messages.TammWalletAmountInvalid);
            var treasury = d.TreasuryAccountCode?.Trim();
            if (string.IsNullOrWhiteSpace(treasury)) throw new ValidationFailedException(Messages.TammWalletTreasuryRequired);
            var settings = await SalesCharges.SettingsAsync(_db, token);
            await ChargePosting.EnsureAccountsAsync(_db, settings, token);

            var transaction = new TammWalletTransaction
            {
                Date = d.Date ?? DateTime.UtcNow, Type = TammWalletTransactionType.TopUp, Amount = DocumentPricing.Round(d.Amount), TreasuryAccountCode = treasury,
                Reference = string.IsNullOrWhiteSpace(d.Reference) ? null : d.Reference.Trim(), Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(),
            };
            _db.Add(transaction);
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = transaction.Date, Description = "شحن محفظة تم" + (transaction.Reference == null ? string.Empty : $" - {transaction.Reference}"),
                SourceType = TopUpSource, SourceId = transaction.Id, SourceNumber = transaction.Reference,
                Lines = new List<PostingLine> { new(settings.TammWalletAccount, transaction.Amount, 0), new(treasury, 0, transaction.Amount) },
            }, token);
            transaction.JournalEntryId = posted.JournalEntryId;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAMM_WALLET_TOPUP", nameof(TammWalletTransaction), transaction.Id.ToString(), $"شحن محفظة تم بمبلغ {transaction.Amount:0.00}", token);
            return Mapper.Map<TammWalletTransactionDto>(transaction);
        }, ct);

    public Task<TammWalletTransactionDto> ReconcileAsync(TammWalletReconcileDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var settings = await SalesCharges.SettingsAsync(_db, token);
            await ChargePosting.EnsureAccountsAsync(_db, settings, token);
            var date = d.Date ?? DateTime.UtcNow;
            var book = await BalanceAsync(settings.TammWalletAccount, token);
            var difference = DocumentPricing.Round(d.StatementBalance - book);

            var reconciliation = new TammWalletTransaction
            {
                Date = date, Type = TammWalletTransactionType.Reconciliation, Amount = 0, StatementBalance = d.StatementBalance, BookBalance = book,
                Difference = difference, Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(),
            };
            _db.Add(reconciliation);

            // تسوية الفرق: يطابق الرصيدُ الدفتري كشفَ تم، والفرق على مصروف المعرض (زيادة الكشف تخفّض المصروف)
            if (d.AdjustDifference && difference != 0)
            {
                var amount = Math.Abs(difference);
                var adjustment = new TammWalletTransaction
                {
                    Date = date, Type = TammWalletTransactionType.Adjustment, Amount = difference, Notes = $"تسوية فرق مطابقة محفظة تم ({book:0.00} ← {d.StatementBalance:0.00})",
                };
                _db.Add(adjustment);
                var posted = await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = date, Description = adjustment.Notes!, SourceType = AdjustmentSource, SourceId = adjustment.Id,
                    Lines = difference > 0
                        ? new List<PostingLine> { new(settings.TammWalletAccount, amount, 0), new(settings.VehicleExpenseAccount, 0, amount) }
                        : new List<PostingLine> { new(settings.VehicleExpenseAccount, amount, 0), new(settings.TammWalletAccount, 0, amount) },
                }, token);
                adjustment.JournalEntryId = posted.JournalEntryId;
                reconciliation.JournalEntryId = posted.JournalEntryId;
            }
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAMM_WALLET_RECONCILED", nameof(TammWalletTransaction), reconciliation.Id.ToString(),
                $"مطابقة محفظة تم: الدفتري {book:0.00}، الكشف {d.StatementBalance:0.00}، الفرق {difference:0.00}" + (d.AdjustDifference && difference != 0 ? " (سُوّي)" : string.Empty), token);
            return Mapper.Map<TammWalletTransactionDto>(reconciliation);
        }, ct);
}
