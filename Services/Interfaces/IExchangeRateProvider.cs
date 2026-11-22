namespace simpleShare_backend.Services.Interfaces;
public sealed record ExchangeRateObservation(decimal Rate, DateOnly Date);
public interface IExchangeRateProvider
{
    Task<ExchangeRateObservation> GetRateAsync(string originalCurrency, string groupCurrency, DateOnly onOrBefore, CancellationToken cancellationToken = default);
}
