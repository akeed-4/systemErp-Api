using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class NumberSequenceService : INumberSequenceService
{
    private readonly ErpDbContext _db;
    public NumberSequenceService(ErpDbContext db) => _db = db;

    public async Task<string> NextAsync(string key, string defaultPrefix, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var seq = await _db.Set<NumberSequence>().FirstOrDefaultAsync(s => s.Key == key, ct);
            var isNew = seq == null;
            if (seq == null)
            {
                seq = new NumberSequence { Key = key, Prefix = defaultPrefix, LastValue = 0 };
                _db.Add(seq);
            }
            seq.LastValue++;
            seq.Version = Guid.NewGuid();
            try
            {
                await _db.SaveChangesAsync(ct);
                return $"{seq.Prefix}{seq.LastValue.ToString().PadLeft(seq.Padding, '0')}";
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(seq).State = EntityState.Detached; // تزامن: أعد المحاولة بقيمة محدَّثة
            }
            catch (DbUpdateException) when (isNew)
            {
                _db.Entry(seq).State = EntityState.Detached; // سباق إنشاء العدّاد لأول مرة
            }
        }
        throw new ConflictException("تعذّر توليد رقم المستند، أعد المحاولة.");
    }
}
