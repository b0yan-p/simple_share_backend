namespace simpleShare_backend.Common.Security;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";

    /// <summary>Web ("server") OAuth client id - the audience mobile idTokens are issued for.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Only needed for the serverAuthCode flow, which is not implemented yet.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Extra accepted audiences (Android/iOS client ids). Empty means only ClientId.</summary>
    public string[] AllowedAudiences { get; set; } = [];

    /// <summary>Reserved for the serverAuthCode / web flow; registered in Google Cloud Console.</summary>
    public string RedirectUri { get; set; } = string.Empty;
}
