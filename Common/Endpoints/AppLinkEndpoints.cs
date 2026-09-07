using System.Net;
using System.Text.Json;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Services;

namespace simpleShare_backend.Common.Endpoints;

/// <summary>
/// Root-level routes that make invite links open the app instead of a browser,
/// plus the page a link falls back to when the app is not installed.
///
/// Minimal APIs rather than static files on purpose: apple-app-site-association
/// has no file extension, so FileExtensionContentTypeProvider refuses it and
/// UseStaticFiles would answer 404; and the identifiers have to come from
/// configuration rather than a checked-in file.
/// </summary>
public static class AppLinkEndpoints
{
    private const string PlayStoreUrl =
        "https://play.google.com/store/apps/details?id=com.choxster.simpleshare";

    public static void MapAppLinkEndpoints(this WebApplication app)
    {
        app.MapMethods(
                "/.well-known/assetlinks.json",
                ["GET", "HEAD"],
                (IConfiguration config) =>
                {
                    var statement = new[]
                    {
                        new
                        {
                            relation = new[] { "delegate_permission/common.handle_all_urls" },
                            target = new
                            {
                                @namespace = "android_app",
                                package_name = config["AppLinks:AndroidPackageName"],
                                sha256_cert_fingerprints = Fingerprints(config),
                            },
                        },
                    };

                    return Json(statement);
                }
            )
            .AllowAnonymous();

        app.MapMethods(
                "/.well-known/apple-app-site-association",
                ["GET", "HEAD"],
                (IConfiguration config) =>
                {
                    var association = new
                    {
                        applinks = new
                        {
                            details = new[]
                            {
                                new
                                {
                                    appIDs = AppleAppIds(config),
                                    components = new[]
                                    {
                                        new { _slash = "/join/*", comment = "group invite links" },
                                    },
                                },
                            },
                        },
                    };

                    // The component key is literally "/", which is not a valid C#
                    // identifier, so the anonymous type carries a placeholder that
                    // is swapped out here.
                    return Json(association, json => json.Replace("\"_slash\":", "\"/\":"));
                }
            )
            .AllowAnonymous();

        // Same URL the Android intent-filter claims. With the app installed
        // Android intercepts it; without it — or on a desktop — the invited
        // person lands here instead of on a bare 404 from the API.
        app.MapMethods(
                "/join/{token}",
                ["GET", "HEAD"],
                async (string token, IGroupInvitationService service) =>
                {
                    try
                    {
                        var preview = await service.GetPreviewAsync(token);
                        return Html(LandingPage(preview.GroupName, preview.MemberCount));
                    }
                    catch (NotFoundAppException)
                    {
                        return Html(ExpiredPage());
                    }
                }
            )
            .AllowAnonymous();
    }

    private static string[] Fingerprints(IConfiguration config) =>
        config.GetSection("AppLinks:AndroidSha256Fingerprints").Get<string[]>() ?? [];

    private static string[] AppleAppIds(IConfiguration config) =>
        config.GetSection("AppLinks:AppleAppIds").Get<string[]>() ?? [];

    private static IResult Json(object payload, Func<string, string>? transform = null)
    {
        var json = JsonSerializer.Serialize(payload);
        return Results.Content(transform is null ? json : transform(json), "application/json");
    }

    private static IResult Html(string html) => Results.Content(html, "text/html");

    /// <summary>
    /// Group names are user input, so every interpolated value is HTML-encoded.
    /// This is the only page in the project that puts user text into markup.
    /// </summary>
    private static string LandingPage(string groupName, int memberCount) =>
        Page(
            title: WebUtility.HtmlEncode(groupName),
            body: $"""
            <p class="lead">
              You've been invited to join <strong>{WebUtility.HtmlEncode(groupName)}</strong>
              on SimpleShare &mdash; {memberCount} {(memberCount == 1 ? "member" : "members")}.
            </p>
            <a class="cta" href="{PlayStoreUrl}">Get SimpleShare</a>
            <p class="hint">Already have the app? Open this link on your phone to join.</p>
            """
        );

    private static string ExpiredPage() =>
        Page(
            title: "Invite unavailable",
            body: """
            <p class="lead">This invite link is no longer valid.</p>
            <p class="hint">Ask whoever invited you to share a new one.</p>
            """
        );

    private static string Page(string title, string body) =>
        $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>{{title}} &middot; SimpleShare</title>
              <style>
                :root { color-scheme: light; }
                body {
                  margin: 0; min-height: 100vh; display: flex; align-items: center;
                  justify-content: center; padding: 24px; background: #f0eef9;
                  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                  color: #1a1640;
                }
                main { max-width: 420px; text-align: center; }
                h1 { margin: 0 0 12px; font-size: 24px; letter-spacing: -0.3px; }
                .lead { margin: 0 0 24px; font-size: 16px; line-height: 1.5; color: #6b6993; }
                .cta {
                  display: inline-block; padding: 14px 28px; border-radius: 9999px;
                  background: #534ab7; color: #fff; font-weight: 600; text-decoration: none;
                }
                .hint { margin: 20px 0 0; font-size: 13px; color: #6b6993; }
              </style>
            </head>
            <body>
              <main>
                <h1>{{title}}</h1>
                {{body}}
              </main>
            </body>
            </html>
            """;
}
