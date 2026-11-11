using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Resend;
using simpleShare_backend.Common.Email;
using simpleShare_backend.Common.Endpoints;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Common.Security;
using simpleShare_backend.Data;
using simpleShare_backend.Infrastructure;
using simpleShare_backend.Interfaces;
using simpleShare_backend.Models.Identity;
using simpleShare_backend.Services;
using simpleShare_backend.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks();

// Backs the resend cooldown in EmailVerificationService.
builder.Services.AddMemoryCache();

builder
    .Services.AddOpenApi()
    .AddEndpointsApiExplorer()
    .AddSwaggerGen()
    .AddDbContext<ApplicationDbContext>(options =>
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default"));
    })
    .AddIdentity<User, IdentityRole<Guid>>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    // Email confirmation tokens come from here. Without it
    // GenerateEmailConfirmationTokenAsync throws NotSupportedException.
    .AddDefaultTokenProviders();

builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            options.DefaultChallengeScheme =
            options.DefaultForbidScheme =
            options.DefaultScheme =
            options.DefaultSignInScheme =
            options.DefaultSignOutScheme =
                JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JWT:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JWT:Audience"],
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(builder.Configuration["JWT:SigningKey"]!)
            ),
        };
    });

builder
    .Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var pd = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = "Invalid request body",
                Type = "https://httpstatuses.com/400",
            };
            pd.Extensions["code"] = "validation_error";
            pd.Extensions["traceId"] = context.HttpContext.TraceIdentifier;

            return new BadRequestObjectResult(pd) { ContentTypes = { "application/problem+json" } };
        };
    });
;
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
    );
});

#region Services scope

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthSessionService, AuthSessionService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRepositoryAsync, RepositoryAsync>();
builder.Services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IMemberService, MemberService>();
builder.Services.AddScoped<IGroupService, GroupService>();
builder.Services.AddScoped<IGroupMemberService, GroupMemberService>();
builder.Services.AddScoped<IUserConnectionService, UserConnectionService>();
builder.Services.AddScoped<IGroupMemberManagementService, GroupMemberManagementService>();
builder.Services.AddScoped<IGroupInvitationService, GroupInvitationService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IBalanceService, BalanceService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IActivityWriter, ActivityWriter>();

builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.AddScoped<IGoogleAuthService, GoogleAuthService>();
builder.Services.AddScoped<IExternalAccountService, ExternalAccountService>();

// The key never lives in appsettings: User Secrets locally, Resend__ApiKey in production.
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection(ResendOptions.SectionName));
builder.Services.AddResend(options =>
{
    options.ApiToken = builder.Configuration[$"{ResendOptions.SectionName}:ApiKey"] ?? string.Empty;
});
builder.Services.AddScoped<IEmailService, ResendEmailService>();

builder.Services.Configure<AppLinkOptions>(builder.Configuration.GetSection(AppLinkOptions.SectionName));
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();

// Identity's confirmation tokens are Data Protection payloads, so they only
// outlive a restart if the keys do. Unset means the in-memory default, which is
// what local runs and tests want; in production it points at a mounted volume.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder
        .Services.AddDataProtection()
        // Pinned rather than derived from the content root, which changes between
        // the image and a local run and would silently invalidate every key.
        .SetApplicationName("SimpleShare")
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

#endregion

var app = builder.Build();

// Apply pending migrations so the schema exists before seeding.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

await DbSeeder.SeedUsersAsync(app.Services);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapDevEmailEndpoints();
}

app.UseCors("AllowAll");

// Cloudflare sits in front and terminates TLS, so the origin sees plain HTTP.
// Without this, Request.IsHttps and every generated URL are wrong.
//
// KnownNetworks/KnownProxies have to be cleared: by default the middleware only
// trusts forwarded headers coming from loopback, so behind an external proxy it
// silently does nothing. The trade-off is that anything able to reach the origin
// directly can spoof these headers — acceptable here because they only affect
// the reported scheme and client IP, neither of which gates access.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
};
forwardedHeaders.KnownNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeaders);

app.UseHttpsRedirection();

app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapAppLinkEndpoints();
app.MapEmailVerificationEndpoints();

app.MapControllers();

app.Run();
