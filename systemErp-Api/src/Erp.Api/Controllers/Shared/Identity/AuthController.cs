using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>نقاط الهوية كما تستدعيها الواجهة تماماً: /api/v1/auth/*.</summary>
[Route("api/v1/auth")]
public class AuthController : ErpControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserService _users;

    public AuthController(IAuthService auth, IUserService users)
    {
        _auth = auth; _users = users;
    }

    // الاستجابة مسطّحة { success, message, token, user, tenant } كما يتوقعها auth.service.ts
    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken ct)
        => Ok(await _auth.LoginAsync(request, ct));

    [AllowAnonymous, HttpPost("register-company")]
    public async Task<IActionResult> RegisterCompany([FromBody] CompanyRegistrationRequestDto request, CancellationToken ct)
        => Ok(await _auth.RegisterCompanyAsync(request, ct));

    [AllowAnonymous, HttpPost("forgot-password/request")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request, CancellationToken ct)
        => Ok(await _auth.RequestPasswordResetAsync(request, ct));

    [AllowAnonymous, HttpPost("forgot-password/verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequestDto request, CancellationToken ct)
        => Ok(await _auth.VerifyResetOtpAsync(request, ct));

    [AllowAnonymous, HttpPost("forgot-password/reset")]
    public async Task<IActionResult> Reset([FromBody] ResetPasswordRequestDto request, CancellationToken ct)
        => Ok(await _auth.ResetPasswordAsync(request, ct));

    /// <summary>{ success, data: [users] } - يحمّلها الواجهة عند البدء لدمج مستخدمي المنشأة.</summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken ct) => Success(await _users.ListAsync(ct));

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) => Success(await _users.GetCurrentAsync(ct));

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto request, CancellationToken ct)
        => Success(await _users.UpdateProfileAsync(request, ct), "تم تحديث بيانات الملف الشخصي بنجاح");
}
