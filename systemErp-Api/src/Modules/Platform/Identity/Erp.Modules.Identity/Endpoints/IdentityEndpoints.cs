using System.Net.Mail;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Catalog.Contracts;
using Erp.Modules.Identity.Application;
using Erp.Modules.Identity.Domain;
using Erp.Modules.Identity.Persistence;
using Erp.Modules.Organization.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Messaging;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Endpoints;

internal static class IdentityEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // The six endpoints the frontend already calls keep their original (non-envelope) response shapes.
        var auth = app.MapGroup("/api/v1/auth").WithTags("Identity");
        auth.MapPost("login", async (LoginRequest request, AuthService service, CancellationToken ct) =>
            Results.Json(await service.LoginAsync(request, ct))).AllowAnonymous();
        auth.MapPost("refresh", async (RefreshRequest request, AuthService service, CancellationToken ct) =>
            Results.Json(await service.RefreshAsync(request, ct))).AllowAnonymous();
        auth.MapPost("forgot-password/request", async (ForgotPasswordRequest request, AuthService service, CancellationToken ct) =>
            Results.Json(await service.RequestPasswordResetAsync(request, ct))).AllowAnonymous();
        auth.MapPost("forgot-password/verify-otp", async (VerifyOtpRequest request, AuthService service, CancellationToken ct) =>
            Results.Json(await service.VerifyOtpAsync(request, ct))).AllowAnonymous();
        auth.MapPost("forgot-password/reset", async (ResetPasswordRequest request, AuthService service, CancellationToken ct) =>
            Results.Json(await service.ResetPasswordAsync(request, ct))).AllowAnonymous();
        auth.MapPost("register-company", async (CompanyRegistrationRequest request, AuthService service, CancellationToken ct) =>
            Results.Json(await service.RegisterCompanyAsync(request, ct))).AllowAnonymous();

        auth.MapPost("logout", LogoutAsync).RequireAuthorization();
        auth.MapGet("me", MeAsync).RequireAuthorization();
        auth.MapGet("users", ListAllUsersAsync).RequireAuthorization();

        app.MapPost("/api/v1/tenants/{id:guid}/switch", async (Guid id, AuthService service, ICurrentUser user, CancellationToken ct) =>
            Results.Json(await service.SwitchTenantAsync(id, user, ct))).WithTags("Identity").RequireAuthorization();

        var users = app.MapGroup("/api/v1/users").WithTags("Identity");
        users.MapGet(string.Empty, ListUsersAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.View);
        users.MapGet("{id:guid}", GetUserAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.View);
        users.MapPost(string.Empty, CreateUserAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.Create);
        users.MapPut("{id:guid}", UpdateUserAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.Edit);
        users.MapPost("{id:guid}/deactivate", DeactivateUserAsync).RequireScreen(ScreenIds.UserPermissions, ScreenAction.Delete);
        users.MapPut("me", UpdateMyProfileAsync).RequireAuthorization();
    }

    private static async Task<IResult> LogoutAsync(RefreshRequest? request, TenantAuthSession session, ICurrentUser user, CancellationToken ct)
    {
        await session.RevokeAsync(user.UserId!.Value, request?.RefreshToken, ct);
        return Results.Json(new SimpleResponse(true, "تم تسجيل الخروج"));
    }

    private static async Task<IResult> MeAsync(IdentityDbContext db, ICompanyProfileReader companies, ICurrentUser user, CancellationToken ct)
    {
        var entity = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == user.UserId, ct) ?? throw UserNotFound();
        return ErpResults.Ok(new { user = UserDto.From(entity), tenant = await companies.GetCurrentAsync(ct) });
    }

    /// <summary>GET /api/v1/auth/users: {success, data: User[]} of the current company (authenticated; the frontend falls back to local data before login).</summary>
    private static async Task<IResult> ListAllUsersAsync(IdentityDbContext db, CancellationToken ct)
    {
        var list = await db.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync(ct);
        return ErpResults.Ok(list.Select(UserDto.From).ToList());
    }

    private static async Task<IResult> ListUsersAsync([AsParameters] PaginationParams paging, IdentityDbContext db, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(u => u.Name.Contains(term) || (u.Email != null && u.Email.Contains(term)) || (u.Phone != null && u.Phone.Contains(term)));
        }

        var page = await query.OrderBy(u => u.Name).ToPagedResultAsync(paging, ct);
        return ErpResults.Ok(new PagedResult<UserDto>(page.Items.Select(UserDto.From).ToList(), page.TotalCount, page.PageNumber, page.PageSize));
    }

    private static async Task<IResult> GetUserAsync(Guid id, IdentityDbContext db, CancellationToken ct) =>
        ErpResults.Ok(UserDto.From(await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw UserNotFound()));

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        IdentityDbContext db,
        IPasswordHasher<User> hasher,
        IOutbox outbox,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || !MailAddress.TryCreate(request.Email?.Trim(), out _))
        {
            throw ErpException.Validation("Name and a valid email are required.", "الاسم وبريد إلكتروني صحيح مطلوبان.");
        }

        PasswordRules.Validate(request.Password, "password");
        var role = await ValidateRoleAsync(db, request.Role, currentUser, ct);

        var user = new User(Guid.CreateVersion7(), request.Name, request.Email!, request.Phone, role);
        await EnsureUniqueAsync(db, user, ct);
        user.UpdateProfile(user.Name, user.Phone, null, request.JobTitle, request.Department);
        user.SetPasswordHash(hasher.HashPassword(user, request.Password!));
        db.Users.Add(user);

        outbox.Enqueue(UserLoginIndexChanged.From(user, tenant.TenantId));
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/users/{user.Id}", UserDto.From(user), "تم إضافة المستخدم");
    }

    private static async Task<IResult> UpdateUserAsync(
        Guid id,
        UpdateUserRequest request,
        IdentityDbContext db,
        IOutbox outbox,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw UserNotFound();
        if (user.RoleCode == SystemRoles.Owner && currentUser.Role != SystemRoles.Owner)
        {
            throw ErpException.Forbidden("owner_protected", "Only an owner can change another owner.", "لا يمكن تعديل حساب المالك إلا من قبل مالك.");
        }

        if (!string.IsNullOrWhiteSpace(request.Role) && request.Role != user.RoleCode)
        {
            var role = await ValidateRoleAsync(db, request.Role, currentUser, ct);
            await EnsureNotLastOwnerAsync(db, user, ct);
            user.ChangeRole(role);
        }

        if (request.Email is not null)
        {
            if (!MailAddress.TryCreate(request.Email.Trim(), out _))
            {
                throw ErpException.Validation("The email is not valid.", "البريد الإلكتروني غير صحيح.");
            }

            user.ChangeEmail(request.Email);
        }

        user.UpdateProfile(
            string.IsNullOrWhiteSpace(request.Name) ? user.Name : request.Name,
            request.Phone ?? user.Phone,
            request.AvatarUrl ?? user.AvatarUrl,
            request.JobTitle ?? user.JobTitle,
            request.Department ?? user.Department);

        if (request.IsActive is { } active && active != user.IsActive)
        {
            if (!active)
            {
                await EnsureNotLastOwnerAsync(db, user, ct);
            }

            user.SetActive(active);
        }

        await EnsureUniqueAsync(db, user, ct);
        outbox.Enqueue(UserLoginIndexChanged.From(user, tenant.TenantId));
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(UserDto.From(user), "تم تحديث المستخدم");
    }

    private static async Task<IResult> DeactivateUserAsync(
        Guid id,
        IdentityDbContext db,
        TenantAuthSession session,
        IOutbox outbox,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw UserNotFound();
        if (user.Id == currentUser.UserId)
        {
            throw ErpException.Conflict("cannot_deactivate_self", "You cannot deactivate your own account.", "لا يمكنك تعطيل حسابك الشخصي.");
        }

        await EnsureNotLastOwnerAsync(db, user, ct);
        user.SetActive(false);
        outbox.Enqueue(UserLoginIndexChanged.From(user, tenant.TenantId));
        await unitOfWork.SaveChangesAsync(ct);
        await session.RevokeAsync(user.Id, refreshToken: null, ct);
        return ErpResults.Ok(UserDto.From(user), "تم تعطيل المستخدم");
    }

    private static async Task<IResult> UpdateMyProfileAsync(
        UpdateMyProfileRequest request,
        IdentityDbContext db,
        IOutbox outbox,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == currentUser.UserId, ct) ?? throw UserNotFound();
        var phoneChanged = request.Phone is not null && request.Phone != user.Phone;
        user.UpdateProfile(
            string.IsNullOrWhiteSpace(request.Name) ? user.Name : request.Name,
            request.Phone ?? user.Phone,
            request.AvatarUrl ?? user.AvatarUrl,
            request.JobTitle ?? user.JobTitle,
            request.Department ?? user.Department);
        await EnsureUniqueAsync(db, user, ct);
        if (phoneChanged)
        {
            outbox.Enqueue(UserLoginIndexChanged.From(user, tenant.TenantId));
        }

        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(UserDto.From(user), "تم تحديث بيانات الملف الشخصي بنجاح");
    }

    private static async Task<string> ValidateRoleAsync(IdentityDbContext db, string? role, ICurrentUser currentUser, CancellationToken ct)
    {
        var code = string.IsNullOrWhiteSpace(role) ? SystemRoles.SalesRep : role.Trim();
        if (!await db.Roles.AnyAsync(r => r.Code == code, ct))
        {
            throw ErpException.Validation($"Unknown role '{code}'.", "الدور الوظيفي غير معروف.");
        }

        if (code == SystemRoles.Owner && currentUser.Role != SystemRoles.Owner)
        {
            throw ErpException.Forbidden("owner_protected", "Only an owner can grant the owner role.", "لا يمكن منح دور المالك إلا من قبل مالك.");
        }

        return code;
    }

    private static async Task EnsureNotLastOwnerAsync(IdentityDbContext db, User user, CancellationToken ct)
    {
        if (user.RoleCode == SystemRoles.Owner
            && !await db.Users.AnyAsync(u => u.Id != user.Id && u.RoleCode == SystemRoles.Owner && u.IsActive, ct))
        {
            throw ErpException.Conflict("last_owner", "The company must keep at least one active owner.", "يجب أن يبقى مالك نشط واحد على الأقل للمنشأة.");
        }
    }

    private static async Task EnsureUniqueAsync(IdentityDbContext db, User user, CancellationToken ct)
    {
        if (user.NormalizedEmail is not null && await db.Users.AnyAsync(u => u.Id != user.Id && u.NormalizedEmail == user.NormalizedEmail, ct))
        {
            throw ErpException.Conflict("email_taken", "Another user already uses this email.", "البريد الإلكتروني مستخدم لمستخدم آخر.");
        }

        if (user.NormalizedPhone is not null && await db.Users.AnyAsync(u => u.Id != user.Id && u.NormalizedPhone == user.NormalizedPhone, ct))
        {
            throw ErpException.Conflict("phone_taken", "Another user already uses this phone.", "رقم الجوال مستخدم لمستخدم آخر.");
        }
    }

    private static ErpException UserNotFound() => ErpException.NotFound("User", "المستخدم");
}
