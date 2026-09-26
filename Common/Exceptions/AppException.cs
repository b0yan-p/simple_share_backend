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