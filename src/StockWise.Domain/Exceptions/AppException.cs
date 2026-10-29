namespace StockWise.Domain.Exceptions;

public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException(message);
    }
}

public class AppException : Exception
{
    public int StatusCode { get; }
    public string? ErrorCode { get; }
    public IDictionary<string, string[]>? Errors { get; }

    public AppException(string message, int statusCode = 400, string? errorCode = null,
        IDictionary<string, string[]>? errors = null) : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Errors = errors;
    }

    public static AppException NotFound(string what) =>
        new($"{what} was not found.", 404, "not_found");

    public static AppException Forbidden(string message = "You do not have permission to perform this action.") =>
        new(message, 403, "forbidden");

    public static AppException Conflict(string message) =>
        new(message, 409, "conflict");

    public static AppException Validation(string message, IDictionary<string, string[]>? errors = null) =>
        new(message, 422, "validation_failed", errors);

    public static AppException Unauthorized(string message = "Authentication is required.") =>
        new(message, 401, "unauthorized");

    public static AppException BadRequest(string message) =>
        new(message, 400, "bad_request");
}
