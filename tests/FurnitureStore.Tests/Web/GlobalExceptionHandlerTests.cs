using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Web.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace FurnitureStore.Tests.Web;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public void NotFoundException_MapsTo404_WithItsMessage()
    {
        var (status, message, _) = GlobalExceptionHandler.Map(new NotFoundException("Sản phẩm", 42));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Contains("42", message);
    }

    [Fact]
    public void ValidationException_MapsTo400_WithAllErrors()
    {
        var (status, _, errors) = GlobalExceptionHandler.Map(new AppValidationException(["Tên bắt buộc", "Giá phải lớn hơn 0"]));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void BusinessRuleException_MapsTo400()
    {
        var (status, message, _) = GlobalExceptionHandler.Map(new BusinessRuleException("Sản phẩm đã hết hàng."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Equal("Sản phẩm đã hết hàng.", message);
    }

    [Fact]
    public void ForbiddenAccessException_MapsTo403()
    {
        var (status, _, _) = GlobalExceptionHandler.Map(new ForbiddenAccessException());

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public void UnexpectedException_MapsTo500_WithoutLeakingInternalMessage()
    {
        var (status, message, _) = GlobalExceptionHandler.Map(new InvalidOperationException("Connection string password=secret"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal(GlobalExceptionHandler.GenericErrorMessage, message);
        Assert.DoesNotContain("secret", message);
    }
}
