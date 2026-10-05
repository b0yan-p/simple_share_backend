namespace simpleShare_backend.Common.Email;

/// <summary>
/// Aliases of the templates published on the Resend dashboard. The subject and
/// the HTML live there, not here - the backend only names the template and
/// supplies its variables.
/// </summary>
public static class EmailTemplates
{
    public const string Verification = "verify-email";
}
