using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using simpleShare_backend.Services.Interfaces;
using Resend;
using simpleShare_backend.Services;
using Testcontainers.PostgreSql;

namespace SimpleShare.IntegrationTests;

/// <summary>
/// Owns the Postgres container and the in-process API host shared by the whole
/// test assembly. Tests take this as a constructor parameter via
/// <see cref="ApiCollection"/> - never construct a factory or a container directly.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Exposed so negative-path tests can mint a deliberately invalid token
    // (expired, wrong issuer). Normal setup uses Identity and the real session service.
    public const string JwtIssuer = "https://test.local";
    public const string JwtAudience = "https://test.local";

    // HS256 needs >= 32 bytes of key material; the app throws at startup if
    // this is shorter.
    public const string JwtSigningKey = "integration-tests-signing-key-0123456789abcdef";

    public const string GoogleClientId = "integration-tests.apps.googleusercontent.com";

    // Same image and tag as production, so EF migrations, Npgsql behaviour and
    // `numeric` decimal semantics are the ones the deployment actually runs.
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public async ValueTask InitializeAsync() => await _db.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": keeps Swagger off, and keeps the app on the same
        // code path production takes. Never set ASPNETCORE_HTTPS_PORTS here -
        // UseHttpsRedirection() is a harmless no-op without it, and a 307 on
        // every request with it.
        builder.UseEnvironment("Testing");

        // Appended last, so these win over the committed appsettings.json -
        // which otherwise points at a localhost database and a dev signing key.
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _db.GetConnectionString(),
                ["JWT:Issuer"] = JwtIssuer,
                ["JWT:Audience"] = JwtAudience,
                ["JWT:SigningKey"] = JwtSigningKey,
                // Only reached by tokens the fake hands to the real service. Must be
                // non-empty: without an audience the real service refuses to run.
                ["Google:ClientId"] = GoogleClientId,
                ["Resend:ApiKey"] = string.Empty,
                ["AppLinks:PublicBaseUrl"] = "https://test.local",
                ["DataProtection:KeysPath"] = string.Empty,
            }));

        // No outbound email in tests. Google validation is isolated separately.
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IExchangeRateProvider>();
            services.AddSingleton<RecordingExchangeRateProvider>();
            services.AddSingleton<IExchangeRateProvider>(sp => sp.GetRequiredService<RecordingExchangeRateProvider>());
            services.RemoveAll<IEmailService>();
            services.RemoveAll<IResend>();
            services.AddSingleton<RecordingEmailService>();
            services.AddSingleton<IEmailService>(sp => sp.GetRequiredService<RecordingEmailService>());
            services.AddScoped<GoogleAuthService>();
            services.AddScoped<IGoogleAuthService, FakeGoogleAuthService>();
        });

        // The host is built lazily on the first CreateClient(), which is after
        // InitializeAsync has started the container - so the connection string
        // above is always resolvable by then. Program.cs then applies migrations
        // and runs DbSeeder before serving anything.
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}
