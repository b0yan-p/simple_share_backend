using System.Text.Json;
using simpleShare_backend.Common.Exceptions;
namespace simpleShare_backend.Common.Currencies;

public static class CurrencyCatalog
{
    private static readonly HashSet<string> Codes = Load();
    private static HashSet<string> Load()
    {
        using var stream = typeof(CurrencyCatalog).Assembly.GetManifestResourceStream("simpleShare_backend.Common.Currencies.fiat-currencies.json")!;
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("currencies").EnumerateArray()
            .Select(x => x.GetProperty("code").GetString()!).ToHashSet(StringComparer.Ordinal);
    }
    public static string Validate(string? code)
    {
        var value = code?.Trim().ToUpperInvariant();
        if (value is null || !Codes.Contains(value))
            throw new ValidationAppException("currency_not_supported", "Select a supported fiat currency.");
        return value;
    }
}
