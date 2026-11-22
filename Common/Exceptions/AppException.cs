namespace simpleShare_backend.Common.Exceptions;

public class AppException : Exception
{
    public string Code { get; } = string.Empty;

    protected AppException(string code, string message) : base(message) => Code = code;
}

public sealed class UnauthorizedAppException(string code, string message) : AppException(code, message) { }

public sealed class NotFoundAppException(string code, string message) : AppException(code, message) { }

public sealed class ForbiddenAppException(string code, string message) : AppException(code, message) { }

public sealed class ConflictAppException(string code, string message) : AppException(code, message) { }

public sealed class ValidationAppException(
    string code,
    string message,
    IDictionary<string, string[]>? errors = null
) : AppException(code, message)
{
    public IDictionary<string, string[]>? Errors { get; } = errors;
}
public sealed class ExchangeRateUnavailableException() : AppException("exchange_rate_provider_unavailable", "Exchange rates are temporarily unavailable. Please retry.");
public sealed class ExchangeRateInvalidResponseException() : AppException("exchange_rate_response_invalid", "The exchange-rate provider returned invalid data. Please retry.");
