
using Microsoft.EntityFrameworkCore;
using Npgsql;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Domain;
using simpleShare_backend.Models.Enums;
using simpleShare_backend.Services;

public class BetaSignupService : IBetaSignupService
{
    private readonly ApplicationDbContext _dbContext;

    public BetaSignupService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task JoinBetaAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var trimmedEmail = email.Trim();
        var normalizedEmail = trimmedEmail.ToUpperInvariant();

        // Check whether the email is already registered.
        var alreadyExists = await _dbContext.BetaSignups
            .AnyAsync(
                x => x.NormalizedEmail == normalizedEmail,
                cancellationToken);

        if (alreadyExists)
            return;

        var signup = new BetaSignup
        {
            Email = trimmedEmail,
            NormalizedEmail = normalizedEmail,
            Status = BetaSignupStatus.Waitlisted,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.BetaSignups.Add(signup);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pgEx &&
            pgEx.SqlState == PostgresErrorCodes.UniqueViolation &&
            pgEx.ConstraintName == "IX_BetaSignups_NormalizedEmail")
        {
            // Concurrent registration of the same email.
            // The unique index prevents duplicate entries.
        }
    }
}
