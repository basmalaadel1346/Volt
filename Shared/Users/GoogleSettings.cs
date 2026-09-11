namespace Shared.Users;

public class GoogleSettings
{
    public const string SectionName = "Google";

    /// <summary>الـ OAuth Client ID بتاع Google Cloud Console (اللي تطبيق الـ Flutter هيستخدمه).</summary>
    public string ClientId { get; set; } = default!;
}
