using simpleShare_backend.Common.Currencies;
using simpleShare_backend.Common.Exceptions;
using Xunit;
namespace SimpleShare.UnitTests;
public class CurrencyCatalogTests
{
    [Theory] [InlineData("BAM")] [InlineData("EUR")] [InlineData("JPY")] [InlineData("KWD")]
    public void FiatInputUsesUniversalApplicationPrecision(string code) => Assert.Equal(code, CurrencyCatalog.Validate(code));
    [Theory] [InlineData("BTC")] [InlineData("XAU")] [InlineData("XDR")] [InlineData("CLF")] [InlineData("XXX")] [InlineData("HRK")]
    public void NonFiatAndArchivedUnitsAreRejected(string code) => Assert.Throws<ValidationAppException>(() => CurrencyCatalog.Validate(code));
}
