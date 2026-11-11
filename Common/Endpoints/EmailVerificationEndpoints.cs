using System.Text.Json;
using simpleShare_backend.Services;

namespace simpleShare_backend.Common.Endpoints;

/// <summary>
/// The page the confirmation link opens when the app is not there to take it:
/// desktop, iOS, or Android before the App Link is verified. With the app
/// installed Android intercepts the same URL and the Angular route runs instead.
///
/// GET only renders - the address is confirmed by the POST the button sends.
/// Mail providers and link scanners follow links in email automatically, and a
/// GET that confirmed would hand them every account.
/// </summary>
public static class EmailVerificationEndpoints
{
    private const string ConfirmApiPath = "/api/Auth/confirm-email";

    public static void MapEmailVerificationEndpoints(this WebApplication app)
    {
        app.MapMethods(
                EmailVerificationService.VerificationPath,
                ["GET", "HEAD"],
                (string? userId, string? token) =>
                    Results.Content(
                        string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token)
                            ? BrokenLinkPage()
                            : ConfirmPage(userId, token),
                        "text/html"
                    )
            )
            .AllowAnonymous();
    }

    /// <summary>
    /// Both values go through the JSON serializer rather than straight into the
    /// script: they come from the query string, so they are user input even when
    /// the link is genuine.
    /// </summary>
    private static string ConfirmPage(string userId, string token) =>
        Page(
            title: "Confirm your email",
            body: $$"""
            <p class="lead" id="lead">One more step - confirm this address to finish setting up your SimpleShare account.</p>
            <button class="cta" id="confirm" type="button">Confirm my email</button>
            <p class="hint" id="hint">You can close this page once it is done.</p>
            <script>
              const payload = {
                userId: {{JsonSerializer.Serialize(userId)}},
                token: {{JsonSerializer.Serialize(token)}}
              };
              const button = document.getElementById('confirm');
              const lead = document.getElementById('lead');
              const hint = document.getElementById('hint');

              button.addEventListener('click', async () => {
                button.disabled = true;
                button.textContent = 'Confirming...';

                try {
                  const response = await fetch({{JsonSerializer.Serialize(ConfirmApiPath)}}, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                  });

                  if (response.ok) {
                    lead.textContent = 'Your email is confirmed. Open SimpleShare and sign in.';
                    hint.textContent = 'You can close this page.';
                    button.remove();
                    return;
                  }

                  const problem = await response.json().catch(() => null);
                  lead.textContent = problem?.detail ?? 'This link is not valid or has expired.';
                  hint.textContent = 'Register again in the app to get a new link.';
                  button.remove();
                } catch {
                  lead.textContent = 'Could not reach SimpleShare. Check your connection and try again.';
                  button.disabled = false;
                  button.textContent = 'Confirm my email';
                }
              });
            </script>
            """
        );

    private static string BrokenLinkPage() =>
        Page(
            title: "Link unavailable",
            body: """
            <p class="lead">This confirmation link is incomplete.</p>
            <p class="hint">Open the link straight from the email, or register again to get a new one.</p>
            """
        );

    /// <summary>
    /// Same shell as the invite landing page in <see cref="AppLinkEndpoints"/>.
    /// Nothing here interpolates user text into markup - the only user input on
    /// this page reaches the script as JSON.
    /// </summary>
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
                  display: inline-block; padding: 14px 28px; border-radius: 9999px; border: 0;
                  background: #534ab7; color: #fff; font-weight: 600; font-size: 16px;
                  cursor: pointer;
                }
                .cta[disabled] { opacity: 0.6; cursor: default; }
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
