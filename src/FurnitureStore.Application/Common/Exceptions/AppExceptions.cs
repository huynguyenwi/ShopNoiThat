namespace FurnitureStore.Application.Common.Exceptions;

/// <summary>
/// Base type for expected, user-facing errors. The message is safe to show to end users.
/// The Web layer maps each subtype to an HTTP status code (see GlobalExceptionHandler).
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message) { }
}

/// <summary>Requested resource does not exist (HTTP 404).</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string entityName, object key)
        : base($"Không tìm thấy {entityName} ({key}).") { }
}

/// <summary>Input failed validation (HTTP 400).</summary>
public sealed class AppValidationException : AppException
{
    public AppValidationException(IEnumerable<string> errors)
        : base("Dữ liệu không hợp lệ.")
    {
        Errors = errors.ToList();
        FieldErrors = new Dictionary<string, string[]>();
    }

    public AppValidationException(string error) : this([error]) { }

    /// <summary>Errors keyed by property path (e.g. "Variants[0].Price") so forms can show them next to the field.</summary>
    public AppValidationException(IReadOnlyDictionary<string, string[]> fieldErrors)
        : base("Dữ liệu không hợp lệ.")
    {
        FieldErrors = fieldErrors;
        Errors = fieldErrors.SelectMany(e => e.Value).Distinct().ToList();
    }

    public AppValidationException(string field, string error) : this(new Dictionary<string, string[]> { [field] = [error] }) { }

    public IReadOnlyList<string> Errors { get; }

    public IReadOnlyDictionary<string, string[]> FieldErrors { get; }
}

/// <summary>A business rule was violated, e.g. out of stock, order can no longer be cancelled (HTTP 400).</summary>
public sealed class BusinessRuleException : AppException
{
    public BusinessRuleException(string message) : base(message) { }
}

/// <summary>
/// The data changed since it was read (optimistic concurrency) or a unique value already exists (HTTP 409).
/// </summary>
public sealed class ConflictException : AppException
{
    public ConflictException(string message = "Dữ liệu đã bị thay đổi bởi người khác. Vui lòng tải lại và thử lại.")
        : base(message) { }
}

/// <summary>Authenticated user is not allowed to perform the action (HTTP 403).</summary>
public sealed class ForbiddenAccessException : AppException
{
    public ForbiddenAccessException(string message = "Bạn không có quyền thực hiện thao tác này.") : base(message) { }
}
