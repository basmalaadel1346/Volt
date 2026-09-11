namespace Shared.Users;

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(Guid userId, string role, string authProvider);
    string GenerateRefreshTokenValue();
}
