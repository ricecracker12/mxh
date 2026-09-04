namespace SocialApp.SharedKernel.Results;

/// <summary>
/// Kết quả thao tác không kèm dữ liệu. Dùng ở tầng Application để tránh ném exception cho luồng
/// nghiệp vụ thường gặp; controller ánh xạ sang HTTP.
/// </summary>
public readonly record struct Result(bool IsSuccess, Error? Error)
{
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Kết quả thao tác kèm dữ liệu <typeparamref name="T"/> khi thành công.</summary>
public readonly record struct Result<T>(bool IsSuccess, T? Value, Error? Error)
{
    public bool IsFailure => !IsSuccess;

    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(Error error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>Mô tả lỗi nghiệp vụ (mã + thông điệp + status HTTP gợi ý) — trung lập với tầng HTTP.</summary>
public readonly record struct Error(string Code, string Message, int Status);
