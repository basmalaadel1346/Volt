using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Shared.Users;

// أي Controller في أي مودل يقدر يستخدمها عشان ياخد الـ UserId من التوكن بأمان
// (بدل ما ياخده من الـ Body/Query اللي ممكن حد يغيّره)
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var idValue = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? throw new InvalidOperationException("الـ Token مايحتويش على Claim الـ Sub (UserId)");

        return Guid.Parse(idValue);
    }

    public static string? GetRole(this ClaimsPrincipal user) => user.FindFirst(ClaimTypes.Role)?.Value;
}
