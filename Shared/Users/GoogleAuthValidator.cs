using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using Shared.Users;

namespace Shared.Users;

public class GoogleAuthValidator : IGoogleAuthValidator
{
    private readonly GoogleSettings _settings;

    public GoogleAuthValidator(IOptions<GoogleSettings> options) => _settings = options.Value;

    public async Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { _settings.ClientId }
            });

            return new GoogleUserInfo(payload.Subject, payload.Email, payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
