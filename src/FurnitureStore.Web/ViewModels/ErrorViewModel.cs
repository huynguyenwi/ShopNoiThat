namespace FurnitureStore.Web.ViewModels;

public sealed class ErrorViewModel
{
    public int StatusCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Icon { get; init; } = "bi-exclamation-circle";
    public string? RequestId { get; init; }

    /// <summary>Exception details; only populated in the Development environment.</summary>
    public string? Details { get; init; }

    public static ErrorViewModel For(int statusCode, string? message = null, string? requestId = null, string? details = null)
    {
        var (title, defaultMessage, icon) = statusCode switch
        {
            400 => ("Yêu cầu không hợp lệ", "Dữ liệu gửi lên không hợp lệ. Vui lòng kiểm tra và thử lại.", "bi-exclamation-diamond"),
            401 => ("Bạn chưa đăng nhập", "Vui lòng đăng nhập để tiếp tục.", "bi-person-lock"),
            403 => ("Không có quyền truy cập", "Tài khoản của bạn không được phép truy cập trang này.", "bi-shield-lock"),
            404 => ("Không tìm thấy trang", "Trang bạn tìm có thể đã bị xóa, đổi tên hoặc tạm thời không khả dụng.", "bi-signpost-split"),
            409 => ("Dữ liệu đã thay đổi", "Dữ liệu vừa được người khác cập nhật. Vui lòng tải lại trang và thử lại.", "bi-arrow-repeat"),
            413 => ("Tệp quá lớn", $"Ảnh gửi lên vượt dung lượng cho phép (tối đa {Application.Common.Settings.UploadLimits.MaxImageMb} MB mỗi ảnh). Vui lòng chọn ảnh khác.", "bi-file-earmark-x"),
            429 => ("Quá nhiều yêu cầu", "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.", "bi-hourglass-split"),
            _ when statusCode >= 500 => ("Đã có lỗi xảy ra", "Hệ thống gặp sự cố ngoài ý muốn. Chúng tôi đã ghi nhận và sẽ xử lý sớm.", "bi-tools"),
            _ => ("Có lỗi xảy ra", "Yêu cầu không thể hoàn tất.", "bi-exclamation-circle")
        };

        return new ErrorViewModel
        {
            StatusCode = statusCode,
            Title = title,
            Message = string.IsNullOrWhiteSpace(message) ? defaultMessage : message,
            Icon = icon,
            RequestId = requestId,
            Details = details
        };
    }
}
