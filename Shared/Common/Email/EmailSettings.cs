namespace Shared.Common.Email;

public class EmailSettings
{
    public const string SectionName = "EmailSettings";

    public string SmtpHost { get; set; } = default!;
    public int SmtpPort { get; set; } = 587;
    public string SenderEmail { get; set; } = default!;
    public string SenderPassword { get; set; } = default!;
    public string SenderName { get; set; } = default!;
    public bool EnableSsl { get; set; } = true;
}
