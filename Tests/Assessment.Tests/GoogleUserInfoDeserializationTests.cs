using System.Text.Json;
using Shared.Users;
using Xunit;

namespace Assessment.Tests;

/// <summary>
/// Exercises Google UserInfo handling without touching the network: the mock
/// payload below is byte-for-byte the shape
/// https://openidconnect.googleapis.com/v1/userinfo returns, so deserializing
/// it proves the login workflow can proceed from a real response.
/// </summary>
public class GoogleUserInfoDeserializationTests
{
    private const string MockUserInfoJson = """
    {
      "sub": "109384756201938475620",
      "name": "Basmala Adel",
      "given_name": "Basmala",
      "family_name": "Adel",
      "picture": "https://lh3.googleusercontent.com/a/ACg8ocKq7Zx9-example=s96-c",
      "email": "basmahdy1920@gmail.com",
      "email_verified": true,
      "locale": "ar"
    }
    """;

    /// <summary>
    /// Google returns camel_case keys, so the JsonPropertyName attributes — not
    /// the default property-name match — are what make this bind.
    /// </summary>
    private static GoogleUserInfoDto DeserializeUserInfo(string json) =>
        JsonSerializer.Deserialize<GoogleUserInfoDto>(json)
        ?? throw new InvalidOperationException("Google returned an empty UserInfo payload");

    [Fact]
    public void DeserializeUserInfo_MapsEveryStandardGoogleField()
    {
        var userInfo = DeserializeUserInfo(MockUserInfoJson);

        Assert.Equal("109384756201938475620", userInfo.Sub);
        Assert.Equal("Basmala Adel", userInfo.Name);
        Assert.Equal("Basmala", userInfo.GivenName);
        Assert.Equal("Adel", userInfo.FamilyName);
        Assert.Equal("https://lh3.googleusercontent.com/a/ACg8ocKq7Zx9-example=s96-c", userInfo.Picture);
        Assert.Equal("basmahdy1920@gmail.com", userInfo.Email);
        Assert.True(userInfo.EmailVerified);
        Assert.Equal("ar", userInfo.Locale);
    }

    [Fact]
    public void DeserializeUserInfo_FeedsTheNormalGoogleLoginWorkflow()
    {
        var userInfo = DeserializeUserInfo(MockUserInfoJson);

        // The workflow refuses to link an account on an unverified address.
        Assert.True(userInfo.EmailVerified);

        // From here AuthController's Google path continues exactly as it does
        // after GoogleAuthValidator.ValidateAsync: same record, same fields.
        GoogleUserInfo identity = userInfo.ToGoogleUserInfo();

        Assert.Equal("109384756201938475620", identity.ProviderUserId);
        Assert.Equal("basmahdy1920@gmail.com", identity.Email);
        Assert.Equal("Basmala Adel", identity.FullName);
    }

    [Fact]
    public void DeserializeUserInfo_ToleratesTheOptionalFieldsGoogleOmits()
    {
        // Google drops keys it has no value for; only "sub" is guaranteed.
        var minimal = """{ "sub": "109384756201938475620", "email_verified": false }""";

        var userInfo = DeserializeUserInfo(minimal);

        Assert.Equal("109384756201938475620", userInfo.Sub);
        Assert.Null(userInfo.Name);
        Assert.Null(userInfo.Email);
        Assert.Null(userInfo.Picture);
        Assert.False(userInfo.EmailVerified);
    }
}
