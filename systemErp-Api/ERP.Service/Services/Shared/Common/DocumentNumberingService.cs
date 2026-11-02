using System.Text.RegularExpressions;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// إعداد ترقيم أنواع المستندات على صف مفتاح العدّاد العام نفسه (NumberSequence). الصرف يبقى في NumberSequenceService؛
/// هنا التحقق والحفظ والعرض فقط. الرقم التالي يُزاد ولا يُنقص، والتغيير يسري على المستندات الجديدة فقط.
/// </summary>
public class DocumentNumberingService : IDocumentNumberingService
{
    private static readonly Regex PrefixPattern = new("^([A-Za-z0-9\\-_/.]|\\{YYYY\\}|\\{YY\\})*$", RegexOptions.Compiled);
    private static readonly string[] Scopes = { "tenant", "warehouse" };

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IAuditService _audit;

    public DocumentNumberingService(ErpDbContext db, INumberSequenceService numbers, IAuditService audit)
    {
        _db = db; _numbers = numbers; _audit = audit;
    }

    public async Task<List<DocumentNumberingSettingDto>> ListAsync(Guid? warehouseId, CancellationToken ct = default)
    {
        var keys = DocumentNumberTypes.All.Select(t => t.Key).ToList();
        var rows = await _db.Set<NumberSequence>().AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, ct);
        var tenantScope = await TenantScopeAsync(ct);
        var list = new List<DocumentNumberingSettingDto>();
        foreach (var type in DocumentNumberTypes.All)
            list.Add(await ToDtoAsync(type, rows.GetValueOrDefault(type.Key), tenantScope, warehouseId, ct));
        return list;
    }

    public Task<DocumentNumberingSettingDto> UpdateAsync(string key, UpdateDocumentNumberingDto d, CancellationToken ct = default)
        => new TransactionRunner(_db).RunAsync(async token =>
        {
            var type = DocumentNumberTypes.Find(key) ?? throw new NotFoundException(Messages.NumberingTypeUnknown);
            var mode = (d.Mode ?? "auto").Trim().ToLowerInvariant();
            var scope = string.IsNullOrWhiteSpace(d.Scope) ? null : d.Scope.Trim().ToLowerInvariant();
            var prefix = (d.Prefix ?? string.Empty).Trim();
            if (mode is not ("auto" or "manual")) throw new ValidationFailedException(Messages.NumberingManualNotAllowed);
            if (mode == "manual" && !type.ManualAllowed) throw new ValidationFailedException(Messages.NumberingManualNotAllowed);
            if (scope != null && !Scopes.Contains(scope)) throw new ValidationFailedException(Messages.NumberingScopeTypeInvalid);
            if (prefix.Length > 20 || !PrefixPattern.IsMatch(prefix)) throw new ValidationFailedException(Messages.NumberingPrefixInvalid);
            if (d.Padding is < 1 or > 12) throw new ValidationFailedException(Messages.NumberingPaddingInvalid);
            // بلا سنة في البادئة يتكرر الرقم نفسه بين السنوات فيرفضه الفهرس الفريد
            if (d.ResetYearly && !NumberSequenceService.HasYearToken(prefix)) throw new ValidationFailedException(Messages.NumberingYearlyNeedsYear);

            var row = await _db.Set<NumberSequence>().FirstOrDefaultAsync(s => s.Key == key, token);
            if (row == null) { row = new NumberSequence { Key = key, LastValue = 0 }; _db.Add(row); }
            var before = $"{row.Mode}/{row.Scope ?? "-"}/{row.Prefix}/{row.Padding}/{(row.ResetYearly ? "yearly" : "continuous")}/{(row.IsActive ? "on" : "off")}";
            row.Mode = mode; row.Scope = scope; row.Prefix = prefix; row.Padding = d.Padding; row.ResetYearly = d.ResetYearly; row.IsActive = d.IsActive;
            row.Version = Guid.NewGuid();
            await _db.SaveChangesAsync(token);

            // الرقم التالي على العدّاد الذي ستُصرف منه المستندات الجديدة (المستودع المحدد والسنة المالية الحالية)
            if (d.NextNumber is long next)
            {
                var counter = await _numbers.CounterAsync(key, type.DefaultPrefix, d.WarehouseId, null, token);
                if (next < counter.NextNumber) throw new ValidationFailedException(string.Format(Messages.NumberingNextNumberDecrease, counter.NextNumber));
                if (next > counter.NextNumber)
                {
                    var seq = await _db.Set<NumberSequence>().FirstOrDefaultAsync(s => s.Key == counter.CounterKey, token);
                    if (seq == null) { seq = new NumberSequence { Key = counter.CounterKey, Prefix = counter.Prefix, Padding = counter.Padding }; _db.Add(seq); }
                    if (next - 1 < seq.LastValue) throw new ValidationFailedException(string.Format(Messages.NumberingNextNumberDecrease, seq.LastValue + 1));
                    seq.LastValue = next - 1;
                    seq.Version = Guid.NewGuid();
                    await _db.SaveChangesAsync(token);
                }
            }

            var after = $"{mode}/{scope ?? "-"}/{prefix}/{d.Padding}/{(d.ResetYearly ? "yearly" : "continuous")}/{(d.IsActive ? "on" : "off")}";
            await _audit.LogAsync("NUMBERING_UPDATED", nameof(NumberSequence), key,
                $"ترقيم {type.NameAr}: {before} ← {after}{(d.NextNumber.HasValue ? $"، الرقم التالي {d.NextNumber}" : "")}", token);
            return await ToDtoAsync(type, row, await TenantScopeAsync(token), d.WarehouseId, token);
        }, ct);

    public async Task<List<string>> ManualTypesAsync(CancellationToken ct = default)
    {
        var manualKeys = DocumentNumberTypes.All.Where(t => t.ManualAllowed).Select(t => t.Key).ToList();
        return await _db.Set<NumberSequence>().AsNoTracking()
            .Where(s => manualKeys.Contains(s.Key) && s.IsActive && s.Mode == "manual").Select(s => s.Key).ToListAsync(ct);
    }

    private async Task<string> TenantScopeAsync(CancellationToken ct)
        => await _db.Set<Tenant>().AsNoTracking().Select(t => t.DocumentNumberingScope).FirstOrDefaultAsync(ct) is { Length: > 0 } s ? s : "tenant";

    private async Task<DocumentNumberingSettingDto> ToDtoAsync(DocumentNumberType type, NumberSequence? row, string tenantScope, Guid? warehouseId, CancellationToken ct)
    {
        var counter = await _numbers.CounterAsync(type.Key, type.DefaultPrefix, warehouseId, null, ct);
        return new DocumentNumberingSettingDto
        {
            Key = type.Key, NameAr = type.NameAr, NameEn = type.NameEn, Module = type.Module, DefaultPrefix = type.DefaultPrefix,
            ManualAllowed = type.ManualAllowed,
            Mode = row?.Mode ?? "auto", Scope = row?.Scope, EffectiveScope = row?.Scope ?? tenantScope,
            Prefix = row?.Prefix ?? type.DefaultPrefix, Padding = row?.Padding is > 0 ? row.Padding : NumberSequenceService.DefaultPadding,
            ResetYearly = row?.ResetYearly ?? false, IsActive = row?.IsActive ?? true,
            NextNumber = counter.NextNumber, NextFormatted = counter.NextFormatted, FiscalYear = counter.FiscalYear,
        };
    }
}
