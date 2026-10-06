using FurnitureStore.Application.Common.Models;

namespace FurnitureStore.Tests.Application;

public sealed class PagedResultTests
{
    [Theory]
    [InlineData(0, 12, 0)]
    [InlineData(1, 12, 1)]
    [InlineData(12, 12, 1)]
    [InlineData(13, 12, 2)]
    [InlineData(30, 12, 3)]
    public void TotalPages_IsRoundedUp(int totalCount, int pageSize, int expectedPages)
    {
        var result = new PagedResult<int>([], totalCount, 1, pageSize);

        Assert.Equal(expectedPages, result.TotalPages);
    }

    [Fact]
    public void HasPreviousAndHasNext_ReflectCurrentPage()
    {
        var middle = new PagedResult<int>([1, 2], 30, 2, 12);

        Assert.True(middle.HasPrevious);
        Assert.True(middle.HasNext);

        var last = new PagedResult<int>([1], 30, 3, 12);
        Assert.False(last.HasNext);
    }

    [Fact]
    public void Map_ProjectsItems_AndKeepsPaging()
    {
        var result = new PagedResult<int>([1, 2, 3], 3, 1, 12).Map(x => x * 10);

        Assert.Equal([10, 20, 30], result.Items);
        Assert.Equal(3, result.TotalCount);
    }
}
