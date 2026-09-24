using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class PaymentMethodService : CrudService<PaymentMethodItem, PaymentMethodItemDto, CreatePaymentMethodItemDto, UpdatePaymentMethodItemDto>, IPaymentMethodService
{
    private static readonly string[] Types = { "cash", "card", "bank", "cheque", "credit" };

    public PaymentMethodService(ErpDbContext db) : base(db) { }
    protected override string Label => "طريقة الدفع";

    protected override IQueryable<PaymentMethodItem> ApplySearch(IQueryable<PaymentMethodItem> q, string t)
        => q.Where(m => m.Code.Contains(t) || m.NameAr.Contains(t) || m.NameEn.Contains(t));

    protected override async Task ValidateAsync(CreatePaymentMethodItemDto d, PaymentMethodItem? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Code)) errors.Add("الكود مطلوب.");
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add("الاسم بالعربية مطلوب.");
        if (!Types.Contains(d.Type)) errors.Add("نوع طريقة الدفع: " + string.Join(" | ", Types));
        if (d.CommissionPercent is < 0 or > 100) errors.Add("نسبة العمولة بين 0 و100.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<PaymentMethodItem>().AnyAsync(m => m.Code == d.Code && (existing == null || m.Id != existing.Id), ct))
            throw new ConflictException("كود طريقة الدفع مستخدم مسبقاً.");
        if (string.IsNullOrWhiteSpace(d.LinkedAccountCode))
            throw new ValidationFailedException("حساب الخزينة/البنك المرتبط مطلوب.");
        if (!await Db.Set<Account>().AnyAsync(a => a.Code == d.LinkedAccountCode, ct))
            throw new ValidationFailedException("الحساب المرتبط غير موجود في شجرة الحسابات.");
    }

    protected override async Task OnCreatingAsync(PaymentMethodItem e, CreatePaymentMethodItemDto d, CancellationToken ct)
        => e.LinkedAccountName = await Db.Set<Account>().Where(a => a.Code == e.LinkedAccountCode).Select(a => a.NameAr).FirstAsync(ct);

    protected override async Task OnUpdatingAsync(PaymentMethodItem e, UpdatePaymentMethodItemDto d, CancellationToken ct)
        => e.LinkedAccountName = await Db.Set<Account>().Where(a => a.Code == e.LinkedAccountCode).Select(a => a.NameAr).FirstAsync(ct);
}
