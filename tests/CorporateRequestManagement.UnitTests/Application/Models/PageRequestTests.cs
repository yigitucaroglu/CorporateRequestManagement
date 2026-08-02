using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Common.Models;

namespace CorporateRequestManagement.UnitTests.Application.Models;

public sealed class PageRequestTests
{
    [Fact]
    public void Create_WhenArgumentsAreOmitted_ShouldUseDefaults()
    {
        PageRequest request = PageRequest.Create();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal(0, request.Skip);
    }

    [Fact]
    public void Create_WhenArgumentsAreValid_ShouldStoreValuesAndCalculateSkip()
    {
        PageRequest request = PageRequest.Create(3, 25);

        Assert.Equal(3, request.Page);
        Assert.Equal(25, request.PageSize);
        Assert.Equal(50, request.Skip);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WhenPageIsLessThanOne_ShouldRejectRequest(int page)
    {
        ApplicationValidationException exception = Assert.Throws<ApplicationValidationException>(
            () => PageRequest.Create(page, 20));

        Assert.Equal("pagination.page_invalid", exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_WhenPageSizeIsOutsideAllowedRange_ShouldRejectRequest(int pageSize)
    {
        ApplicationValidationException exception = Assert.Throws<ApplicationValidationException>(
            () => PageRequest.Create(1, pageSize));

        Assert.Equal("pagination.page_size_invalid", exception.Code);
    }

    [Fact]
    public void Create_WhenPageSizeIsMaximum_ShouldAcceptRequest()
    {
        PageRequest request = PageRequest.Create(1, 100);

        Assert.Equal(100, request.PageSize);
    }

    [Fact]
    public void Create_WhenSkipCalculationOverflows_ShouldRejectWithStableValidationCode()
    {
        ApplicationValidationException exception = Assert.Throws<ApplicationValidationException>(
            () => PageRequest.Create(int.MaxValue, 100));

        Assert.Equal("pagination.skip_overflow", exception.Code);
    }
}
