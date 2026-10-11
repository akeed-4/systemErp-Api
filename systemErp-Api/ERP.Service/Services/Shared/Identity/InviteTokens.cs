using System.Security.Cryptography;
using System.Text;

namespace ERP.Service.Services.Shared;

/// <summary>رموز روابط الدعوة: عشوائية (256 بت) تُسلَّم مرة واحدة، ولا يُخزَّن منها إلا تجزئتها.</summary>
public static class InviteTokens
{
    public const int ValidDays = 7;

    public static (string Token, string Hash) New()
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
