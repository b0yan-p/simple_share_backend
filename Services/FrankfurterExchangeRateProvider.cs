using System.Globalization;
using System.Net;
using System.Text.Json;
using simpleShare_backend.Common.Exceptions;
using simpleShare_backend.Services.Interfaces;
namespace simpleShare_backend.Services;

public sealed class FrankfurterExchangeRateProvider(HttpClient http) : IExchangeRateProvider
{
    public async Task<ExchangeRateObservation> GetRateAsync(string originalCurrency, string groupCurrency, DateOnly onOrBefore, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync($"v2/rate/{Uri.EscapeDataString(originalCurrency)}/{Uri.EscapeDataString(groupCurrency)}?date={onOrBefore.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", cancellationToken);
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
                throw new ValidationAppException("exchange_rate_unavailable", "No historical rate is available for this currency pair and date.");
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests) throw new ExchangeRateUnavailableException();
                throw new ExchangeRateInvalidResponseException();
            }
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var row = json.RootElement;
            if (row.GetProperty("base").GetString() != originalCurrency || row.GetProperty("quote").GetString() != groupCurrency
                || !DateOnly.TryParseExact(row.GetProperty("date").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date == default || date > onOrBefore)
                throw new ExchangeRateInvalidResponseException();
            var rate = decimal.Round(row.GetProperty("rate").GetDecimal(), 12, MidpointRounding.AwayFromZero);
            if (rate <= 0 || rate >= 10000000000000000m) throw new ExchangeRateInvalidResponseException();
            return new(rate, date);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ExchangeRateUnavailableException(); }
        catch (IOException) { throw new ExchangeRateUnavailableException(); }
        catch (HttpRequestException) { throw new ExchangeRateUnavailableException(); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        { throw new ExchangeRateInvalidResponseException(); }
    }
}
