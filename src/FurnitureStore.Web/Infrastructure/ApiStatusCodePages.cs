using FurnitureStore.Application.Common.Models;
using Microsoft.AspNetCore.Diagnostics;

namespace FurnitureStore.Web.Infrastructure;

public static class ApiStatusCodePages
{
    /// <summary>
    /// Status-code-pages handler for API requests: writes a JSON ApiResponse for empty 4xx/5xx responses
    /// (unknown route, 401/403 from authorization, 405, 429 from rate limiting...).
    /// </summary>
    public static Task WriteResponseAsync(StatusCodeContext context)
    {
        var response = context.HttpContext.Response;
        return response.WriteAsJsonAsync(ApiResponse.Fail(MessageFor(response.StatusCode)), context.HttpContext.RequestAborted);
    }

    public static string MessageFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Yêu cầu không hợp lệ.",
        StatusCodes.Status401Unauthorized => "Vui lòng đăng nhập để tiếp tục.",
        StatusCodes.Status403Forbidden => "Bạn không có quyền truy cập tài nguyên này.",
        StatusCodes.Status404NotFound => "Không tìm thấy tài nguyên.",
        StatusCodes.Status405MethodNotAllowed => "Phương thức không được hỗ trợ.",
        StatusCodes.Status409Conflict => "Dữ liệu đã bị thay đổi. Vui lòng tải lại và thử lại.",
        StatusCodes.Status413PayloadTooLarge => "Dữ liệu gửi lên quá lớn.",
        StatusCodes.Status415UnsupportedMediaType => "Định dạng dữ liệu không được hỗ trợ.",
        StatusCodes.Status429TooManyRequests => "Bạn gửi quá nhiều yêu cầu. Vui lòng thử lại sau ít phút.",
        >= StatusCodes.Status500InternalServerError => "Đã có lỗi xảy ra. Vui lòng thử lại sau.",
        _ => "Yêu cầu không thành công."
    };
}
