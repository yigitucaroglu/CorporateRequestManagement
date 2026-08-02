using CorporateRequestManagement.Application.Common.Exceptions;

namespace CorporateRequestManagement.UnitTests.Application.Exceptions;

public sealed class ApplicationLayerExceptionTests
{
    [Fact]
    public void Constructor_WhenCodeIsBlank_ShouldRejectException()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationValidationException("  ", "Mesaj"));
    }

    [Fact]
    public void Constructor_WhenMessageIsBlank_ShouldRejectException()
    {
        Assert.Throws<ArgumentException>(() => new ApplicationValidationException("validation.failed", "  "));
    }

    [Fact]
    public void Constructor_WhenCodeContainsWhitespace_ShouldStoreTrimmedCode()
    {
        ApplicationValidationException exception = new("  validation.failed  ", "Mesaj");

        Assert.Equal("validation.failed", exception.Code);
    }

    [Fact]
    public void Constructor_WhenMessageIsValid_ShouldPreserveBaseExceptionMessage()
    {
        ApplicationValidationException exception = new("validation.failed", "Doğrulama başarısız.");

        Assert.Equal("validation.failed", exception.Code);
        Assert.Equal("Doğrulama başarısız.", exception.Message);
    }

    [Theory]
    [InlineData(typeof(ApplicationValidationException))]
    [InlineData(typeof(NotFoundException))]
    [InlineData(typeof(ForbiddenAccessException))]
    [InlineData(typeof(ConflictException))]
    public void DerivedException_WhenCreated_ShouldBeSealedAndPreserveContract(Type exceptionType)
    {
        ApplicationLayerException exception = (ApplicationLayerException)Activator.CreateInstance(
            exceptionType,
            "application.test",
            "Test mesajı")!;

        Assert.True(exceptionType.IsSealed);
        Assert.Equal("application.test", exception.Code);
        Assert.Equal("Test mesajı", exception.Message);
    }
}
