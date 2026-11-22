using Microsoft.Extensions.DependencyInjection;
using simpleShare_backend.Data;
using simpleShare_backend.Models.Domain;
namespace SimpleShare.IntegrationTests;
public static class LegacyExpenseFixtureExtensions
{
    // Legacy four-decimal financial data cannot be submitted through the new two-decimal input contract.
    public static async Task InsertLegacyExpenseAsync(this ApiFixture fixture, Guid group, string description, decimal total,
        Guid payer, (Guid Id, decimal Amount)[] splits, CancellationToken ct)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var expense = new Expense { Id = Guid.NewGuid(), GroupId = group, Description = description, TotalAmount = total,
            OriginalAmount = total, OriginalCurrency = "BAM", ExchangeRate = 1m, ExchangeRateDate = null,
            ExpenseDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, CreatedByMemberId = payer };
        expense.Payments.Add(new ExpensePayment { ExpenseId = expense.Id, PaidByMemberId = payer, Amount = total, OriginalAmount = total, CreatedAt = DateTime.UtcNow });
        foreach (var row in splits) expense.Splits.Add(new ExpenseSplit { ExpenseId = expense.Id, OwedByMemberId = row.Id, Amount = row.Amount, OriginalAmount = row.Amount, CreatedAt = DateTime.UtcNow });
        db.Expenses.Add(expense); await db.SaveChangesAsync(ct);
    }
}
