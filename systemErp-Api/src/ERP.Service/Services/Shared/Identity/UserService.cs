using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ERP.Service.Services.Shared;

public class UserService : IUserService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IPasswordHasher<User> _hasher;

    public UserService(ErpDbContext db, ICurrentUser current, IPasswordHasher<User> hasher)
    {
        _db = db; _current = current; _hasher = hasher;
    }

    public async Task<List<UserDto>> ListAsync(CancellationToken ct = default)
        => (await _db.Set<User>().AsNoTracking().OrderBy(u => u.Name).ToListAsync(ct)).Select(Mapper.Map<UserDto>).ToList();

    public async Task<UserDto> GetCurrentAsync(CancellationToken ct = default)
        => await GetAsync(_current.UserId ?? throw new UnauthorizedAppException(), ct);

    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<UserDto>(await _db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NotFoundException("المستخدم غير موجود"));

    public async Task<UserDto> CreateAsync(CreateUserRequestDto r, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Name)) errors.Add("الاسم مطلوب.");
        if (string.IsNullOrWhiteSpace(r.Email) || !r.Email.Contains('@')) errors.Add("البريد الإلكتروني غير صالح.");
        if (r.Password is null || r.Password.Length < 8) errors.Add("كلمة المرور 8 أحرف على الأقل.");
        if (r.Role == UserRole.Owner) errors.Add("لا يمكن إنشاء مالك ثانٍ للمنشأة.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var email = r.Email.Trim().ToLower();
        if (await _db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == email, ct))
            throw new ConflictException("هذا البريد الإلكتروني مسجّل مسبقاً.");

        var sub = await _db.Set<Subscription>().OrderByDescending(s => s.StartDate).FirstOrDefaultAsync(ct);
        if (sub != null && await _db.Set<User>().CountAsync(u => u.IsActive, ct) >= sub.MaxUsers)
            throw new ConflictException("تم بلوغ الحد الأقصى للمستخدمين في باقتك الحالية.");

        var user = new User
        {
            Name = r.Name.Trim(), Email = email, Phone = r.Phone, Role = r.Role,
            JobTitle = r.JobTitle, Department = r.Department, AvatarInitials = AuthService.Initials(r.Name),
        };
        user.PasswordHash = _hasher.HashPassword(user, r.Password!);
        _db.Add(user);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<UserDto>(user);
    }

    public async Task<UserDto> UpdateAsync(Guid id, UpdateUserRequestDto r, CancellationToken ct = default)
    {
        var user = await _db.Set<User>().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("المستخدم غير موجود");
        if (user.Role == UserRole.Owner && (r.Role != UserRole.Owner || !r.IsActive))
            throw new ConflictException("لا يمكن تغيير دور مالك المنشأة أو تعطيله.");
        if (r.Role == UserRole.Owner && user.Role != UserRole.Owner)
            throw new ConflictException("لا يمكن إسناد دور المالك لمستخدم آخر.");
        if (string.IsNullOrWhiteSpace(r.Name)) throw new ValidationFailedException("الاسم مطلوب.");

        user.Name = r.Name.Trim(); user.Phone = r.Phone; user.Role = r.Role;
        user.JobTitle = r.JobTitle; user.Department = r.Department; user.IsActive = r.IsActive;
        user.AvatarInitials = AuthService.Initials(user.Name);
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<UserDto>(user);
    }

    public async Task<UserDto> UpdateProfileAsync(UpdateProfileDto r, CancellationToken ct = default)
    {
        var id = _current.UserId ?? throw new UnauthorizedAppException();
        var user = await _db.Set<User>().FirstAsync(u => u.Id == id, ct);
        if (!string.IsNullOrWhiteSpace(r.Name)) { user.Name = r.Name.Trim(); user.AvatarInitials = AuthService.Initials(user.Name); }
        if (r.Phone != null) user.Phone = r.Phone;
        if (r.AvatarUrl != null) user.AvatarUrl = r.AvatarUrl;
        if (r.JobTitle != null) user.JobTitle = r.JobTitle;
        if (r.Department != null) user.Department = r.Department;
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<UserDto>(user);
    }

    public async Task DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        if (id == _current.UserId) throw new ConflictException("لا يمكنك تعطيل حسابك الحالي.");
        var user = await _db.Set<User>().FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("المستخدم غير موجود");
        if (user.Role == UserRole.Owner) throw new ConflictException("لا يمكن تعطيل مالك المنشأة.");
        user.IsActive = false;
        await _db.SaveChangesAsync(ct);
    }
}
