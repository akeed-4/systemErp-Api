using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosSettingsService : IPosSettingsService
{
    private static readonly string[] PaperSizes = { "80mm", "58mm", "A4" };
    private readonly ErpDbContext _db;
    public PosSettingsService(ErpDbContext db) => _db = db;

    private async Task<PosInvoiceSettings> LoadAsync(CancellationToken ct)
    {
        var s = await _db.Set<PosInvoiceSettings>().FirstOrDefaultAsync(ct);
        if (s == null) { s = new PosInvoiceSettings(); _db.Add(s); await _db.SaveChangesAsync(ct); }
        return s;
    }

    public async Task<PosInvoiceSettingsDto> GetAsync(CancellationToken ct = default) => Mapper.Map<PosInvoiceSettingsDto>(await LoadAsync(ct));

    public async Task<PosInvoiceSettingsDto> UpdateAsync(UpdatePosInvoiceSettingsDto r, CancellationToken ct = default)
    {
        if (r.DefaultInvoiceType is not ("simplified" or "standard")) throw new ValidationFailedException("نوع الفاتورة: simplified | standard.");
        if (!PaperSizes.Contains(r.PaperSize)) throw new ValidationFailedException("مقاس الورق: " + string.Join(" | ", PaperSizes));
        var s = await LoadAsync(ct);
        Mapper.Apply(r, s);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<PosInvoiceSettingsDto>(s);
    }
}
