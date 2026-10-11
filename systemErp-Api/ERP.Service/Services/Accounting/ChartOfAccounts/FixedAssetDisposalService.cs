using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// استبعاد أصل ثابت ببيعه أو شطبه. القيد عبر المحرك المحاسبي: مدين مجمع الإهلاك والمتحصَّل، دائن حساب الأصل بتكلفته،
/// والفرق بين المتحصَّل والقيمة الدفترية ربح (دائن) أو خسارة (مدين) على مركز تكلفة الأصل. بعده يخرج الأصل من الإهلاك والتعديل.
/// </summary>
public class FixedAssetDisposalService : IFixedAssetDisposalService
{
    private const string SourceType = "fixed_asset_disposal";

    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public FixedAssetDisposalService(ErpDbContext db, IAccountingPostingService posting, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _posting = posting; _tx = tx; _audit = audit;
    }

    public Task<FixedAssetDto> DisposeAsync(Guid assetId, DisposeFixedAssetDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var asset = await _db.Set<FixedAsset>().FirstOrDefaultAsync(a => a.Id == assetId, token)
                ?? throw new NotFoundException(string.Format(Messages.EntityNotFound, Messages.LabelFixedAsset));
            if (asset.DisposedAt.HasValue) throw new ConflictException(string.Format(Messages.AssetAlreadyDisposed, asset.AssetCode));

            var date = (r.Date == default ? DateTime.UtcNow : r.Date).Date;
            var errors = new List<string>();
            if (date < asset.PurchaseDate.Date) errors.Add(Messages.DisposalBeforePurchase);
            if (r.Proceeds < 0) errors.Add(Messages.DisposalProceedsCannotBeNegative);
            if (r.Proceeds > 0 && string.IsNullOrWhiteSpace(r.TreasuryAccountCode)) errors.Add(Messages.DisposalTreasuryRequired);
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            // لا استبعاد بتاريخ يسبق فترة إهلاك مرحَّلة: الإهلاك حتى الاستبعاد يجب أن يكون مكتملاً قبله لا بعده
            var lastPeriod = await _db.Set<FixedAssetDepreciation>().AsNoTracking().Where(d => d.FixedAssetId == asset.Id).MaxAsync(d => (string?)d.Period, token);
            if (lastPeriod != null && string.CompareOrdinal(date.ToString("yyyy-MM", CultureInfo.InvariantCulture), lastPeriod) < 0)
                throw new ConflictException(string.Format(Messages.DisposalBeforePostedDepreciation, lastPeriod));

            var accountIds = new[] { asset.AssetAccountId, asset.AccumulatedDepreciationAccountId };
            var codes = await _db.Set<Account>().AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Code, token);
            if (!codes.TryGetValue(asset.AssetAccountId, out var assetAccount)) throw new ValidationFailedException(Messages.AssetAccountNotFound);
            if (!codes.TryGetValue(asset.AccumulatedDepreciationAccountId, out var accumulatedAccount)) throw new ValidationFailedException(Messages.AccumulatedDepreciationAccountNotFound);

            var bookValue = asset.CurrentBookValue;
            var accumulated = asset.PurchaseCost - bookValue;
            var gain = r.Proceeds - bookValue; // موجب = ربح استبعاد، سالب = خسارة
            await DefaultAccounts.EnsureAsync(_db, token, DefaultAccounts.AssetDisposalGain, DefaultAccounts.AssetDisposalLoss);

            var note = $"استبعاد الأصل {asset.AssetCode}";
            var lines = new List<PostingLine>();
            if (accumulated > 0) lines.Add(new(accumulatedAccount, accumulated, 0, note));
            if (r.Proceeds > 0) lines.Add(new(r.TreasuryAccountCode!.Trim(), r.Proceeds, 0, note));
            if (gain < 0) lines.Add(new(DefaultAccounts.AssetDisposalLoss, -gain, 0, note, asset.CostCenterId));
            lines.Add(new(assetAccount, 0, asset.PurchaseCost, note));
            if (gain > 0) lines.Add(new(DefaultAccounts.AssetDisposalGain, 0, gain, note, asset.CostCenterId));

            var journal = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = date, Description = $"{note} - {asset.NameAr}" + (string.IsNullOrWhiteSpace(r.Notes) ? "" : $" ({r.Notes.Trim()})"),
                SourceType = SourceType, SourceId = asset.Id, SourceNumber = asset.AssetCode, Lines = lines,
            }, token);

            asset.DisposedAt = date;
            asset.DisposalProceeds = r.Proceeds;
            asset.DisposalJournalEntryId = journal.JournalEntryId;
            asset.AccumulatedDepreciation = accumulated;
            asset.CurrentBookValue = 0; // خرج من الدفاتر
            await _db.SaveChangesAsync(token);

            await _audit.LogAsync("ASSET_DISPOSED", nameof(FixedAsset), asset.Id.ToString(),
                $"استبعاد الأصل {asset.AssetCode}: قيمة دفترية {bookValue:0.00}، متحصَّل {r.Proceeds:0.00}، {(gain >= 0 ? "ربح" : "خسارة")} {Math.Abs(gain):0.00}، القيد {journal.EntryNumber}", token);
            return Mapper.Map<FixedAssetDto>(asset);
        }, ct);
}
