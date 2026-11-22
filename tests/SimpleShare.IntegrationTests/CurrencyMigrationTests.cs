using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using simpleShare_backend.Data;
namespace SimpleShare.IntegrationTests;

[Collection(nameof(ApiCollection))]
public class CurrencyMigrationTests(ApiFixture fixture)
{
    [Fact]
    public async Task BackfillPreservesExactLegacyMoneyDatesAndDeletedSettlements()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = fixture.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var name = "migration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(source.Database.GetConnectionString());
        await admin.OpenAsync(ct);
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin)) await create.ExecuteNonQueryAsync(ct);
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(source.Database.GetConnectionString()) { Database = name, Pooling = false };
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261113123720_AddBetaSignups", ct);
            var group = Guid.NewGuid(); var member = Guid.NewGuid(); var expense = Guid.NewGuid();
            var date = new DateTime(2026, 9, 13, 17, 23, 42, DateTimeKind.Utc);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Groups\" (\"Id\",\"Name\",\"SimplifyDebts\",\"CreatedAt\") VALUES ({group}, 'Legacy', false, {date})", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Members\" (\"Id\",\"DisplayName\",\"CreatedAt\") VALUES ({member}, 'Legacy member', {date})", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Expenses\" (\"Id\",\"GroupId\",\"Description\",\"TotalAmount\",\"ExpenseDate\",\"CreatedByMemberId\",\"CreatedAt\",\"DeletedAt\",\"IsSettleUp\") VALUES ({expense},{group},'Legacy',10.0051,{date},{member},{date},{date},true)", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ExpensePayments\" (\"ExpenseId\",\"PaidByMemberId\",\"Amount\",\"CreatedAt\") VALUES ({expense},{member},10.0051,{date})", ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ExpenseSplits\" (\"ExpenseId\",\"OwedByMemberId\",\"Amount\",\"CreatedAt\") VALUES ({expense},{member},10.0051,{date})", ct);
            await migrator.MigrateAsync(null, ct);
            var row = await db.Expenses.Include(x => x.Payments).Include(x => x.Splits).Include(x => x.Group).SingleAsync(ct);
            row.TotalAmount.ShouldBe(10.0051m); row.OriginalAmount.ShouldBe(10.0051m);
            row.ExpenseDate.ShouldBe(date); row.DeletedAt.ShouldBe(date); row.IsSettleUp.ShouldBe(true);
            row.OriginalCurrency.ShouldBe("BAM"); row.Group.Currency.ShouldBe("BAM"); row.ExchangeRate.ShouldBe(1m); row.ExchangeRateDate.ShouldBeNull();
            row.Payments.Single().Amount.ShouldBe(10.0051m); row.Payments.Single().OriginalAmount.ShouldBe(10.0051m);
            row.Splits.Single().Amount.ShouldBe(10.0051m); row.Splits.Single().OriginalAmount.ShouldBe(10.0051m);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
