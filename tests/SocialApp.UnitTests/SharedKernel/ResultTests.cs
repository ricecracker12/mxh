using SocialApp.SharedKernel.Results;
using Xunit;

namespace SocialApp.UnitTests.SharedKernel;

/// <summary>Smoke test GĐ0 cho Result types — xác nhận khung unit test chạy được trên CI.</summary>
public sealed class ResultTests
{
    [Fact]
    public void Success_result_has_value_and_no_error()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_result_carries_error()
    {
        var error = new Error("post.not_found", "Không tìm thấy bài viết", 404);
        Result<int> result = error;

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }
}
