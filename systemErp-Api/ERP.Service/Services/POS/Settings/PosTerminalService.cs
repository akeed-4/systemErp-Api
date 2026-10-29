using System.Text.Json;
using ERP.Core.Contracts.POS;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.POS;
using ERP.Core.Models.POS;
using ERP.Core.Models.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.POS;

public class PosTerminalService : IPosTerminalService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    public PosTerminalService(ErpDbContext db, ICurrentUser user) { _db = db; _user = user; }

    public async Task<List<PosTerminalDto>> ListAsync(bool availableToCurrentUser, CancellationToken ct = default)
    {
        var q = _db.Set<PosTerminal>().AsNoTracking();
        if (availableToCurrentUser)
        {
            var me = _user.UserId ?? throw new UnauthorizedAppException();
            q = q.Where(t => t.IsActive);
            var rows = await q.OrderBy(t => t.Name).ToListAsync(ct);
            return rows.Where(t => AllowedUsers(t).Count == 0 || AllowedUsers(t).Contains(me)).Select(ToDto).ToList();
        }
        return (await q.OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<List<PosTerminalUserDto>> UsersAsync(CancellationToken ct = default)
        => await _db.Set<User>().AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Name)
            .Select(u => new PosTerminalUserDto { Id = u.Id, Name = u.Name }).ToListAsync(ct);

    public async Task<PosTerminalDto> SaveAsync(Guid? id, SavePosTerminalDto r, CancellationToken ct = default)
    {
        var code = CleanRequired(r.Code, 32, "رمز نقطة البيع مطلوب وبحد أقصى 32 حرفًا.").ToUpperInvariant();
        var name = CleanRequired(r.Name, 120, "اسم نقطة البيع مطلوب وبحد أقصى 120 حرفًا.");
        var device = CleanRequired(r.DeviceIdentifier, 160, "معرّف الجهاز الفيزيائي مطلوب.");
        if (!new[] { "computer", "pos_terminal", "tablet", "other" }.Contains(r.HardwareType)) throw new ValidationFailedException("نوع جهاز نقطة البيع غير معروف.");
        if (!new[] { "browser", "usb", "serial", "network", "bluetooth", "other" }.Contains(r.ConnectionType)) throw new ValidationFailedException("طريقة اتصال الجهاز غير معروفة.");
        if (!new[] { "browser", "usb", "serial", "network", "bluetooth", "other" }.Contains(r.PrinterConnectionType)) throw new ValidationFailedException("طريقة اتصال الطابعة غير معروفة.");
        if (r.MaxDiscountPercent is < 0 or > 100) throw new ValidationFailedException("الحد الأعلى للخصم يجب أن يكون بين 0 و100 بالمئة.");
        if (!r.AllowDiscount && r.MaxDiscountPercent.HasValue) throw new ValidationFailedException("لا يمكن تحديد حد خصم عند تعطيل الخصم على نقطة البيع.");

        var categories = (r.AllowedCategoryIds ?? new()).Distinct().ToList();
        var users = (r.AllowedUserIds ?? new()).Distinct().ToList();
        if (categories.Count > 0 && await _db.Set<ProductCategory>().CountAsync(c => categories.Contains(c.Id), ct) != categories.Count)
            throw new ValidationFailedException("تحتوي الفئات المحددة على فئة غير موجودة.");
        if (users.Count > 0 && await _db.Set<User>().CountAsync(u => users.Contains(u.Id) && u.IsActive, ct) != users.Count)
            throw new ValidationFailedException("يجب اختيار مستخدمين فعالين فقط.");
        if (r.WarehouseId.HasValue && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == r.WarehouseId, ct))
            throw new ValidationFailedException("المستودع المحدد غير موجود.");
        if (await _db.Set<PosTerminal>().AnyAsync(t => t.Id != id && (t.Code == code || t.DeviceIdentifier == device), ct))
            throw new ConflictException("رمز نقطة البيع أو معرّف الجهاز مستخدم مسبقًا.");

        PosTerminal terminal;
        if (id.HasValue)
            terminal = await _db.Set<PosTerminal>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("نقطة البيع غير موجودة.");
        else
        {
            terminal = new PosTerminal();
            _db.Add(terminal);
        }
        terminal.Code = code; terminal.Name = name; terminal.DeviceIdentifier = device;
        terminal.HardwareType = r.HardwareType; terminal.HardwareModel = CleanOptional(r.HardwareModel);
        terminal.HardwareSerialNumber = CleanOptional(r.HardwareSerialNumber); terminal.ConnectionType = r.ConnectionType;
        terminal.WarehouseId = r.WarehouseId; terminal.PrinterModel = CleanOptional(r.PrinterModel);
        terminal.PrinterConnectionType = r.PrinterConnectionType; terminal.PrinterAddress = CleanOptional(r.PrinterAddress);
        terminal.HasCashDrawer = r.HasCashDrawer; terminal.AllowReturns = r.AllowReturns; terminal.AllowDiscount = r.AllowDiscount;
        terminal.MaxDiscountPercent = r.AllowDiscount ? r.MaxDiscountPercent : null;
        terminal.AllowedCategoryIdsJson = JsonSerializer.Serialize(categories); terminal.AllowedUserIdsJson = JsonSerializer.Serialize(users);
        terminal.IsActive = r.IsActive;
        await _db.SaveChangesAsync(ct);
        return ToDto(terminal);
    }

    private static PosTerminalDto ToDto(PosTerminal t) => new()
    {
        Id = t.Id, Code = t.Code, Name = t.Name, DeviceIdentifier = t.DeviceIdentifier, HardwareType = t.HardwareType,
        HardwareModel = t.HardwareModel, HardwareSerialNumber = t.HardwareSerialNumber, ConnectionType = t.ConnectionType,
        WarehouseId = t.WarehouseId, PrinterModel = t.PrinterModel, PrinterConnectionType = t.PrinterConnectionType,
        PrinterAddress = t.PrinterAddress, HasCashDrawer = t.HasCashDrawer, AllowReturns = t.AllowReturns,
        AllowDiscount = t.AllowDiscount, MaxDiscountPercent = t.MaxDiscountPercent,
        AllowedCategoryIds = ReadIds(t.AllowedCategoryIdsJson), AllowedUserIds = ReadIds(t.AllowedUserIdsJson), IsActive = t.IsActive,
    };
    internal static List<Guid> ReadIds(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize<List<Guid>>(json) ?? new(); }
        catch { return new(); }
    }
    private static List<Guid> AllowedUsers(PosTerminal t) => ReadIds(t.AllowedUserIdsJson);
    private static string CleanRequired(string? value, int max, string error)
    {
        var cleaned = value?.Trim();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length > max) throw new ValidationFailedException(error);
        return cleaned;
    }
    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
