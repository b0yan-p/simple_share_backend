using simpleShare_backend.Common.Exceptions;
namespace simpleShare_backend.Services.Calculations;

public static class ExpenseMoneyNormalizer
{
    public const decimal MaximumStoredAmount = 999999999999999.9999m;
    public static void ValidateOriginal(decimal total, IReadOnlyList<(Guid Id, decimal Amount)> payments,
        IReadOnlyList<(Guid Id, decimal Amount)> splits)
    {
        if (total <= 0 || total > 100000 || decimal.Round(total, 2) != total)
            throw new ValidationAppException("original_amount_invalid", "Enter an amount from 0.01 to 100,000 with at most two decimal places.");
        foreach (var rows in new[] { payments, splits })
            if (rows.Count == 0 || rows.Select(x => x.Id).Distinct().Count() != rows.Count
                || rows.Any(x => x.Amount <= 0 || x.Amount > total || decimal.Round(x.Amount, 2) != x.Amount)
                || rows.Sum(x => x.Amount) != total)
                throw new ValidationAppException("original_allocations_invalid", "Positive, unique original allocations must add up exactly to the original total.");
    }
    public static decimal Total(decimal original, decimal rate)
    {
        try
        {
            var result = decimal.Round(checked(original * rate), 2, MidpointRounding.AwayFromZero);
            if (rate <= 0 || result <= 0 || result > MaximumStoredAmount) throw new OverflowException();
            return result;
        }
        catch (OverflowException) { throw new ValidationAppException("normalized_amount_invalid", "The converted amount is too small or too large."); }
    }
    public static Dictionary<Guid, decimal> Allocate(IReadOnlyList<(Guid Id, decimal Amount)> originals, decimal rate, decimal total)
    {
        var rows = originals.Select(x => (x.Id, Cents: checked(x.Amount * rate * 100m)))
            .Select(x => (x.Id, Floor: decimal.Floor(x.Cents), Remainder: x.Cents - decimal.Floor(x.Cents)))
            .OrderByDescending(x => x.Remainder).ThenBy(x => x.Id).ToArray();
        var remaining = total * 100m - rows.Sum(x => x.Floor);
        if (remaining < 0 || remaining > rows.Length) throw new ValidationAppException("normalized_amount_invalid", "Unable to allocate the converted total.");
        var result = rows.Select((x, i) => (x.Id, Amount: (x.Floor + (i < remaining ? 1 : 0)) / 100m)).ToDictionary(x => x.Id, x => x.Amount);
        if (result.Values.Any(x => x <= 0))
            throw new ValidationAppException("normalized_allocation_too_small", "A participant's converted allocation is less than 0.01. Adjust the original allocations.");
        if (result.Values.Sum() != total) throw new ValidationAppException("normalized_amount_invalid", "Converted allocations do not match the total.");
        return result;
    }
}
