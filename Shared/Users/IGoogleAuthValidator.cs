namespace Shared.Users;

public record GoogleUserInfo(string ProviderUserId, string Email, string? FullName);

public interface IGoogleAuthValidator
{
    /// <summary>بيتحقق من صحة الـ Google ID Token وبيرجع بيانات صاحب الحساب، أو null لو غير صالح.</summary>
    Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken ct = default);
}
