namespace FurnitureStore.Application.Common.Models;

/// <summary>
/// Standard JSON envelope returned by every API endpoint:
/// { "success": bool, "message": string, "data": T|null, "errors": string[] }.
/// </summary>
public sealed record ApiResponse<T>(bool Success, string Message, T? Data, IReadOnlyList<string> Errors);

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string message = "Thành công") =>
        new(true, message, data, []);

    public static ApiResponse<object?> Ok(string message = "Thành công") =>
        new(true, message, null, []);

    public static ApiResponse<object?> Fail(string message, IEnumerable<string>? errors = null) =>
        new(false, message, null, errors?.ToList() ?? []);
}
