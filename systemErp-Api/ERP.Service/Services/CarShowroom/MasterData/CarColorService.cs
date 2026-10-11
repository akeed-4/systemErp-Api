using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class CarColorService : CrudService<CarColor, CarColorDto, CreateCarColorDto, UpdateCarColorDto>, ICarColorService
{
    public CarColorService(ErpDbContext db) : base(db) { }
    protected override string Label => Messages.LabelColor;

    protected override IQueryable<CarColor> ApplyFilters(IQueryable<CarColor> q, PaginationParams p)
        => p.Status switch { "exterior" => q.Where(c => c.IsExterior), "interior" => q.Where(c => !c.IsExterior), _ => q };

    protected override async Task ValidateAsync(CreateCarColorDto d, CarColor? existing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(d.NameAr)) throw new ValidationFailedException(Messages.ColorNameRequired);
        if (!string.IsNullOrWhiteSpace(d.Hex) && !System.Text.RegularExpressions.Regex.IsMatch(d.Hex, "^#[0-9a-fA-F]{6}$"))
            throw new ValidationFailedException(Messages.ColorValueFormat);
        if (await Db.Set<CarColor>().AnyAsync(c => c.NameAr == d.NameAr && c.IsExterior == d.IsExterior && (existing == null || c.Id != existing.Id), ct))
            throw new ConflictException(Messages.ColorAlreadyExists);
    }
}
