using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أنواع التكاليف الإضافية للمشتريات العامة الأولية لكل منشأة: تُستكمل عند أول استخدام دون تكرار.
/// الإعدادات الضريبية قيم ابتدائية يعدّلها محاسب المنشأة.
/// </summary>
public static class PurchaseExpenseTypeSeed
{
    private const PurchaseCostAllocationMethod Val = PurchaseCostAllocationMethod.ByValue;
    private const PurchaseCostAllocationMethod Wt = PurchaseCostAllocationMethod.ByWeight;

    private static readonly (string Code, string Ar, string En, VatCategory Vat, PurchaseCostAllocationMethod Method, bool IsInputTax)[] Seeds =
    {
        ("FREIGHT", "شحن", "Freight", VatCategory.Standard, Val, false),
        ("INLAND_TRANSPORT", "نقل داخلي", "Inland Transport", VatCategory.Standard, Wt, false),
        ("INSURANCE", "تأمين الشحنة", "Shipment Insurance", VatCategory.Standard, Val, false),
        ("CUSTOMS_DUTY", "رسوم جمركية", "Customs Duty", VatCategory.OutOfScope, Val, false),
        ("IMPORT_VAT", "ضريبة استيراد", "Import VAT", VatCategory.Standard, Val, true),
        ("CUSTOMS_CLEARANCE", "تخليص جمركي", "Customs Clearance", VatCategory.Standard, Val, false),
        ("PORT_FEES", "رسوم موانئ", "Port Fees", VatCategory.Standard, Val, false),
    };

    public static IEnumerable<PurchaseExpenseType> Build() => Seeds.Select(s => new PurchaseExpenseType
    {
        Code = s.Code, NameAr = s.Ar, NameEn = s.En, VatCategory = s.Vat, DefaultAllocationMethod = s.Method,
        IsInputTax = s.IsInputTax, IsVatRecoverable = true, IsSystem = true, IsActive = true,
    });

    public static async Task EnsureAsync(ErpDbContext db, CancellationToken ct)
    {
        var existing = await db.Set<PurchaseExpenseType>().Select(t => t.Code).ToListAsync(ct);
        var missing = Build().Where(t => !existing.Contains(t.Code)).ToList();
        if (missing.Count == 0) return;
        db.AddRange(missing);
        await db.SaveChangesAsync(ct);
    }
}

public class PurchaseExpenseTypeService : IPurchaseExpenseTypeService
{
    private static readonly Regex CodePattern = new("^[A-Z0-9_]{2,40}$", RegexOptions.Compiled);
    private readonly ErpDbContext _db;
    public PurchaseExpenseTypeService(ErpDbContext db) => _db = db;

    public async Task<List<PurchaseExpenseTypeDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        await PurchaseExpenseTypeSeed.EnsureAsync(_db, ct);
        var q = _db.Set<PurchaseExpenseType>().AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        return (await q.OrderBy(t => t.Code).ToListAsync(ct)).Select(Mapper.Map<PurchaseExpenseTypeDto>).ToList();
    }

    public async Task<PurchaseExpenseTypeDto> CreateAsync(SavePurchaseExpenseTypeDto d, CancellationToken ct = default)
    {
        await PurchaseExpenseTypeSeed.EnsureAsync(_db, ct);
        var code = Validate(d);
        if (await _db.Set<PurchaseExpenseType>().AnyAsync(t => t.Code == code, ct)) throw new ConflictException(Messages.PurchaseExpenseTypeCodeExists);
        var type = new PurchaseExpenseType { Code = code };
        Apply(d, type);
        _db.Add(type);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<PurchaseExpenseTypeDto>(type);
    }

    public async Task<PurchaseExpenseTypeDto> UpdateAsync(Guid id, SavePurchaseExpenseTypeDto d, CancellationToken ct = default)
    {
        var type = await _db.Set<PurchaseExpenseType>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException(Messages.PurchaseExpenseTypeNotFound);
        var code = Validate(d);
        // رمز النوع المزروع ثابت (يُستكمل به الكتالوج)؛ وغيره يُغيَّر ما لم يتكرر
        if (type.IsSystem ? code != type.Code : code != type.Code && await _db.Set<PurchaseExpenseType>().AnyAsync(t => t.Code == code, ct))
            throw new ConflictException(Messages.PurchaseExpenseTypeCodeExists);
        type.Code = code;
        Apply(d, type);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<PurchaseExpenseTypeDto>(type);
    }

    private static string Validate(SavePurchaseExpenseTypeDto d)
    {
        var code = (d.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (!CodePattern.IsMatch(code) || string.IsNullOrWhiteSpace(d.NameAr)
            || !Enum.IsDefined(d.VatCategory) || !Enum.IsDefined(d.DefaultAllocationMethod)
            // ضريبة الاستيراد مبلغها كله ضريبة: فئتها خاضعة
            || (d.IsInputTax && d.VatCategory != VatCategory.Standard))
            throw new ValidationFailedException(Messages.PurchaseExpenseTypeValuesInvalid);
        return code;
    }

    private static void Apply(SavePurchaseExpenseTypeDto d, PurchaseExpenseType type)
    {
        type.NameAr = d.NameAr.Trim();
        type.NameEn = string.IsNullOrWhiteSpace(d.NameEn) ? type.NameAr : d.NameEn.Trim();
        type.VatCategory = d.VatCategory; type.IsVatRecoverable = d.IsVatRecoverable; type.IsInputTax = d.IsInputTax;
        type.DefaultAllocationMethod = d.DefaultAllocationMethod; type.IsActive = d.IsActive;
    }
}
