namespace simpleShare_backend.Common.Endpoints;

/// <summary>
/// The public origin this API is reached at. Links mailed out have to be built
/// from it rather than from the incoming request, because the only host Android
/// verifies as an App Link is the one in AndroidManifest - a link built from a
/// dev tunnel or an internal hostname opens a browser instead of the app.
///
/// Only the part <see cref="AppLinkEndpoints"/> cannot read itself: the
/// well-known documents still take their identifiers straight from
/// IConfiguration, since those are arrays consumed once at request time.
/// </summary>
public sealed class AppLinkOptions
{
    public const string SectionName = "AppLinks";

    /// <summary>Origin without a trailing slash, e.g. "https://api.shareorb.app".</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
