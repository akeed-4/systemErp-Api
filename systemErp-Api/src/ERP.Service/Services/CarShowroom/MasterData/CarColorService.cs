using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CarColorService : CrudService<CarColor, CarColorDto, CreateCarColorDto, UpdateCarColorDto>, ICarColorService
{
    public CarColorService(ErpDbContext db) : base(db) { }
    protected override string Label => "اللون";

    protected override IQueryable<CarColor> ApplyFilters(IQueryable<CarColor> q, PaginationParams p)
        => p.Status switch { "exterior" => q.Where(c => c.IsExterior), "interior" => q.Where(c => !c.IsExterior), _ => q };

    protected override async Task ValidateAsync(CreateCarColorDto d, CarColor? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.NameAr)) throw new ValidationFailedException("اسم اللون مطلوب.");
        if (!string.IsNullOrWhiteSpace(d.Hex) && !System.Text.RegularExpressions.Regex.IsMatch(d.Hex, "^#[0-9a-fA-F]{6}$"))
            throw new ValidationFailedException("قيمة اللون يجب أن تكون بصيغة #RRGGBB.");
        if (await Db.Set<CarColor>().AnyAsync(c => c.NameAr == d.NameAr && c.IsExterior == d.IsExterior && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException("اللون مسجّل مسبقاً.");
    }
}
