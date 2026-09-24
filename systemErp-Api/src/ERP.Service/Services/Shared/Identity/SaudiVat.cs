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

public static class SaudiVat
{
    /// <summary>الرقم الضريبي السعودي: 15 رقماً يبدأ وينتهي بالرقم 3.</summary>
    public static bool IsValid(string? vat)
        => vat is { Length: 15 } && vat[0] == '3' && vat[^1] == '3' && vat.All(char.IsDigit);
}
