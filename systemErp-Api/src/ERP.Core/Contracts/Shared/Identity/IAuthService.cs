using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IAuthService
{
    Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
    Task<AuthResultDto> RegisterCompanyAsync(CompanyRegistrationRequestDto request, CancellationToken ct = default);
    Task<ForgotPasswordResultDto> RequestPasswordResetAsync(ForgotPasswordRequestDto request, CancellationToken ct = default);
    Task<VerifyOtpResultDto> VerifyResetOtpAsync(VerifyOtpRequestDto request, CancellationToken ct = default);
    Task<OperationResultDto> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default);
    /// <summary>بيانات صاحب رابط الدعوة؛ رابط منتهٍ أو مستعمل يُرفض.</summary>
    Task<InviteInfoDto> GetInviteAsync(string token, CancellationToken ct = default);
    /// <summary>يعيّن كلمة المرور من رابط الدعوة ويُدخل المستخدم مباشرة.</summary>
    Task<AuthResultDto> AcceptInviteAsync(AcceptInviteRequestDto request, CancellationToken ct = default);
}
