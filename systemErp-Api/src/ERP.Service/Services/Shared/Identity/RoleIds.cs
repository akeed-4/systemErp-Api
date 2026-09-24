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

public static class RoleIds
{
    /// <summary>owner | admin | general_manager | chief_accountant | sales_rep (تطابق UserRole في الواجهة).</summary>
    public static string From(UserRole role) => JsonNamingPolicy.SnakeCaseLower.ConvertName(role.ToString());
}
