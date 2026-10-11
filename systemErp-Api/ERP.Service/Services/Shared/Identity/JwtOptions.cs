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

public class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "erp-api";
    public string Audience { get; set; } = "erp-web";
    /// <summary>مفتاح التوقيع (32 حرفاً على الأقل). يُضبط عبر user-secrets أو متغير بيئة ولا يوضع في المستودع.</summary>
    public string Key { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 480;
}
