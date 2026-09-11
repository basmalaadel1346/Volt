namespace Shared.Common.Email;

// عام تمامًا (مش خاص باليوزرز) - أي مودل بعدين (زي Content أو Progress)
// ممكن يستخدمه يبعت أي إيميل (تنبيه، تقرير أسبوعي للأهل...)
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}
