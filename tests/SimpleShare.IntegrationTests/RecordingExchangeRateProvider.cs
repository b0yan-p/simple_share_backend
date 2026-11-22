using System.Collections.Concurrent;
using simpleShare_backend.Services.Interfaces;
namespace SimpleShare.IntegrationTests;
public sealed class RecordingExchangeRateProvider : IExchangeRateProvider
{
    public Exception? NextFailure { get; set; }
    public ConcurrentQueue<(string Original, string Group, DateOnly Date)> Requests { get; } = new();
    public Task<ExchangeRateObservation> GetRateAsync(string originalCurrency, string groupCurrency, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (NextFailure is { } failure) { NextFailure = null; throw failure; }
        Requests.Enqueue((originalCurrency, groupCurrency, date));
        var effective = date.DayOfWeek == DayOfWeek.Sunday ? date.AddDays(-2) : date.DayOfWeek == DayOfWeek.Saturday ? date.AddDays(-1) : date;
        return Task.FromResult(new ExchangeRateObservation(originalCurrency == "USD" ? 1.67m : 1.95583m, effective));
    }
}
