using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CarAgentService : CrudService<CarAgent, CarAgentDto, CreateCarAgentDto, UpdateCarAgentDto>, ICarAgentService
{
    public CarAgentService(ErpDbContext db) : base(db) { }
    protected override string Label => "الوكيل";

    protected override IQueryable<CarAgent> ApplySearch(IQueryable<CarAgent> q, string t)
        => q.Where(a => a.NameAr.Contains(t) || a.NameEn.Contains(t) || a.CommercialRecord.Contains(t) || a.Brand.Contains(t));

    protected override async Task ValidateAsync(CreateCarAgentDto d, CarAgent? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add("اسم الوكيل بالعربية مطلوب.");
        if (!string.IsNullOrWhiteSpace(d.VatNumber) && !SaudiVat.IsValid(d.VatNumber)) errors.Add("الرقم الضريبي غير صالح.");
        if (!string.IsNullOrWhiteSpace(d.Email) && !d.Email.Contains('@')) errors.Add("البريد الإلكتروني غير صالح.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        if (d.BrandId.HasValue)
        {
            var brand = await Db.Set<CarBrand>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == d.BrandId, ct)
                ?? throw new ValidationFailedException("الماركة غير موجودة.");
            d.Brand = string.IsNullOrWhiteSpace(d.Brand) ? brand.NameAr : d.Brand;
        }
    }

    protected override async Task OnDeletingAsync(CarAgent e, CancellationToken ct)
    {
        if (await Db.Set<CarModel>().AnyAsync(m => m.AgentId == e.Id, ct)) throw new ConflictException("الوكيل مرتبط بموديلات.");
        if (await Db.Set<Vehicle>().AnyAsync(v => v.AgentId == e.Id, ct)) throw new ConflictException("الوكيل مرتبط بمركبات.");
    }
}
