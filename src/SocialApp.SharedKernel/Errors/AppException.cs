using Microsoft.AspNetCore.Http;

namespace SocialApp.SharedKernel.Errors;

/// <summary>
/// Lỗi nghiệp vụ đã biết, ánh xạ thẳng sang RFC 7807 Problem Details với HTTP status tương ứng.
/// Ném loại này khi muốn trả lỗi có kiểm soát (400/403/404/409...) thay vì 500.
/// </summary>
public class AppException : Exception
{
    public int Status { get; }
    public string Title { get; }

    /// <summary>Lỗi theo trường (field -> danh sách thông điệp), đặt vào ProblemDetails.errors.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public AppException(int status, string title, string? detail = null,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(detail ?? title)
    {
        Status = status;
        Title = title;
        Errors = errors;
    }

    public static AppException NotFound(string detail) =>
        new(StatusCodes.Status404NotFound, "Không tìm thấy tài nguyên", detail);

    public static AppException Forbidden(string detail = "Bạn không có quyền thực hiện thao tác này") =>
        new(StatusCodes.Status403Forbidden, "Bị từ chối", detail);

    public static AppException Conflict(string detail) =>
        new(StatusCodes.Status409Conflict, "Xung đột dữ liệu", detail);

    public static AppException Validation(IReadOnlyDictionary<string, string[]> errors,
        string detail = "Dữ liệu đầu vào không hợp lệ") =>
        new(StatusCodes.Status400BadRequest, "Dữ liệu không hợp lệ", detail, errors);
}
