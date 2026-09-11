using System.Text.Json.Serialization;

namespace Shared.Users;

/// <summary>
/// Google's OpenID Connect UserInfo response
/// (https://openidconnect.googleapis.com/v1/userinfo).
///
/// Only the fields this project's Google-login workflow actually consumes are
/// modelled: <see cref="Sub"/>, <see cref="Email"/> and <see cref="Name"/> feed
/// <see cref="GoogleUserInfo"/>, and the rest are the standard claims that come
/// back in the same payload and are useful for profile display. Google omits
/// keys it has no value for, so everything except sub is nullable.
/// </summary>
public sealed class GoogleUserInfoDto
{
    /// <summary>Stable Google account id. The only field Google always returns.</summary>
    [JsonPropertyName("sub")]
    public string Sub { get; set; } = default!;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("given_name")]
    public string? GivenName { get; set; }

    [JsonPropertyName("family_name")]
    public string? FamilyName { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Whether Google has verified the address. Treat an unverified email as
    /// unusable for account linking.
    /// </summary>
    [JsonPropertyName("email_verified")]
    public bool EmailVerified { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    /// <summary>
    /// Projects onto the record the rest of the login workflow already takes,
    /// so a UserInfo payload and a validated ID token converge on one shape.
    /// </summary>
    public GoogleUserInfo ToGoogleUserInfo() =>
        new(Sub, Email ?? string.Empty, Name);
}
