using System.Net;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Services;
using simpleShare_backend.Services.Calculations;
using Xunit;
namespace SimpleShare.UnitTests;
public class CurrencyTests
{
    private static Guid Id(int n) => new(n, 0, 0, new byte[8]);
    [Fact] public void ForeignAllocationsPreserveExactMarginals()
    {
        var rows = new[] { (Id(1), 60m), (Id(2), 40m) };
        ExpenseMoneyNormalizer.ValidateOriginal(100m, rows, rows);
        var total = ExpenseMoneyNormalizer.Total(100m, 1.67m);
        var result = ExpenseMoneyNormalizer.Allocate(rows, 1.67m, total);
        Assert.Equal(167m, total); Assert.Equal(100.20m, result[Id(1)]); Assert.Equal(66.80m, result[Id(2)]);
    }
    [Fact] public void RemaindersAreDeterministicAndIndependentOfInputOrder()
    {
        var rows = new[] { (Id(1), 1m), (Id(2), 1m), (Id(3), 1m) };
        var total = ExpenseMoneyNormalizer.Total(3m, 1.005m);
        var a = ExpenseMoneyNormalizer.Allocate(rows, 1.005m, total);
        var b = ExpenseMoneyNormalizer.Allocate(rows.Reverse().ToArray(), 1.005m, total);
        Assert.Equal(3.02m, total); Assert.Equal(total, a.Values.Sum());
        foreach (var id in a.Keys) Assert.Equal(a[id], b[id]);
        Assert.Equal(1.01m, a[Id(1)]); Assert.Equal(1m, a[Id(3)]);
    }
    [Fact] public void TinyChildIsRejectedRatherThanInventingACent()
    {
        var ex = Assert.Throws<ValidationAppException>(() => ExpenseMoneyNormalizer.Allocate([(Id(1), .01m), (Id(2), .99m)], .4m, .4m));
        Assert.Equal("normalized_allocation_too_small", ex.Code);
    }
    [Fact] public void ConvertedTotalMayExceedOriginalLimit()
    {
        ExpenseMoneyNormalizer.ValidateOriginal(100000m, [(Id(1), 100000m)], [(Id(2), 100000m)]);
        Assert.Equal(200000m, ExpenseMoneyNormalizer.Total(100000m, 2m));
        Assert.Throws<ValidationAppException>(() => ExpenseMoneyNormalizer.ValidateOriginal(100000.01m, [(Id(1), 100000.01m)], [(Id(2), 100000.01m)]));
    }
    [Fact] public void PositiveInputMayNotNormalizeToZeroOrOverflow()
    {
        Assert.Throws<ValidationAppException>(() => ExpenseMoneyNormalizer.Total(.01m, .01m));
        Assert.Throws<ValidationAppException>(() => ExpenseMoneyNormalizer.Total(100000m, decimal.MaxValue));
    }
    [Fact] public void SubCentOriginalDiscrepancyIsNotAccepted()
    {
        Assert.Throws<ValidationAppException>(() => ExpenseMoneyNormalizer.ValidateOriginal(1m, [(Id(1), .9999m)], [(Id(2), 1m)]));
    }
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
    [Fact] public async Task HistoricalObservationRetainsPrecedingDateAndDirection()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"base":"USD","quote":"BAM","date":"2026-09-11","rate":1.67}""");
        var provider = new FrankfurterExchangeRateProvider(new HttpClient(handler) { BaseAddress = new("https://example.test/") });
        var observation = await provider.GetRateAsync("USD", "BAM", new(2026, 9, 13));
        Assert.Equal(new DateOnly(2026, 9, 11), observation.Date);
        Assert.Equal(1.67m, observation.Rate);
        Assert.Contains("date=2026-09-13", handler.Requested!.Query);
    }
    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"base\":\"BAM\",\"quote\":\"USD\",\"date\":\"2026-09-11\",\"rate\":1.67}")]
    [InlineData("{\"base\":\"USD\",\"quote\":\"BAM\",\"date\":\"2026-09-14\",\"rate\":1.67}")]
    [InlineData("{\"base\":\"USD\",\"quote\":\"BAM\",\"date\":\"2026-09-11\",\"rate\":0}")]
    public async Task InvalidProviderDataCannotProduceSnapshot(string body)
    {
        var provider = new FrankfurterExchangeRateProvider(new HttpClient(new Handler(HttpStatusCode.OK, body)) { BaseAddress = new("https://example.test/") });
        await Assert.ThrowsAsync<ExchangeRateInvalidResponseException>(() => provider.GetRateAsync("USD", "BAM", new(2026, 9, 13)));
    }
    [Theory] [InlineData(429)] [InlineData(503)]
    public async Task OutagesAreRetryable(int status)
    {
        var provider = new FrankfurterExchangeRateProvider(new HttpClient(new Handler((HttpStatusCode)status, "")) { BaseAddress = new("https://example.test/") });
        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(() => provider.GetRateAsync("USD", "BAM", new(2026, 9, 13)));
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        }
    }
    [Fact] public async Task TimeoutIsRetryableButClientCancellationPropagates()
    {
        using var http = new HttpClient(new WaitingHandler()) { BaseAddress = new("https://example.test/"), Timeout = TimeSpan.FromMilliseconds(20) };
        var provider = new FrankfurterExchangeRateProvider(http);
        await Assert.ThrowsAsync<ExchangeRateUnavailableException>(() => provider.GetRateAsync("USD", "BAM", new(2026, 9, 13)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRateAsync("USD", "BAM", new(2026, 9, 13), cancellation.Token));
    }
}
