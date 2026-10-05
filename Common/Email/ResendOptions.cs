namespace simpleShare_backend.Common.Email;

public sealed class ResendOptions
{
    public const string SectionName = "Resend";

    /// <summary>
    /// Never checked in: User Secrets locally ("Resend:ApiKey"), environment
    /// variable "Resend__ApiKey" in production.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Sender address; the domain has to be verified in Resend.</summary>
    public string FromEmail { get; set; } = string.Empty;

    /// <summary>Display name shown next to <see cref="FromEmail"/>.</summary>
    public string FromName { get; set; } = string.Empty;
}
