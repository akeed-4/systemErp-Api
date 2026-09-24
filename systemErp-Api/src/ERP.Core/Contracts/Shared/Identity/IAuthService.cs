using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface IAuthService
{
    Task<AuthResultDto> LoginAsync(LoginRequestDto request, CancellationToken ct = default);
    Task<AuthResultDto> RegisterCompanyAsync(CompanyRegistrationRequestDto request, CancellationToken ct = default);
    Task<ForgotPasswordResultDto> RequestPasswordResetAsync(ForgotPasswordRequestDto request, CancellationToken ct = default);
    Task<VerifyOtpResultDto> VerifyResetOtpAsync(VerifyOtpRequestDto request, CancellationToken ct = default);
    Task<OperationResultDto> ResetPasswordAsync(ResetPasswordRequestDto request, CancellationToken ct = default);
}
