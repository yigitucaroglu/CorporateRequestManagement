using CorporateRequestManagement.Application.Common.Behaviors;
using CorporateRequestManagement.Application.Common.Exceptions;
using FluentValidation;
using FluentValidation.Results;

namespace CorporateRequestManagement.UnitTests.Application.Authentication;

public sealed class ValidationBehaviorTests
{
    [Fact]
    public async Task Handle_WhenNoValidator_ShouldCallNextOnceAndForwardToken()
    {
        int calls = 0; CancellationToken observed = default; using var source = new CancellationTokenSource();
        var behavior = new ValidationBehavior<TestRequest, string>([]);
        string result = await behavior.Handle(new(), token => { calls++; observed = token; return Task.FromResult("ok"); }, source.Token);
        Assert.Equal("ok", result); Assert.Equal(1, calls); Assert.Equal(source.Token, observed);
    }

    [Fact]
    public async Task Handle_WhenValidatorSucceeds_ShouldCallNext()
    {
        int calls = 0;
        var behavior = new ValidationBehavior<TestRequest, string>([new StubValidator([])]);
        Assert.Equal("ok", await behavior.Handle(new(), _ => { calls++; return Task.FromResult("ok"); }, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Handle_WhenValidatorsFail_ShouldGroupDeduplicateAndNotCallNext()
    {
        int calls = 0;
        var behavior = new ValidationBehavior<TestRequest, string>(
        [new StubValidator([new("Email", "required"), new("Email", "required")]), new StubValidator([new("Email", "invalid"), new("Password", "required")])]);
        ApplicationValidationException error = await Assert.ThrowsAsync<ApplicationValidationException>(() => behavior.Handle(new(), _ => { calls++; return Task.FromResult("ok"); }, default));
        Assert.Equal(0, calls); Assert.Equal("validation.failed", error.Code); Assert.Equal("İstek doğrulaması başarısız.", error.Message);
        Assert.Equal(["required", "invalid"], error.Errors["Email"]); Assert.Equal(["required"], error.Errors["Password"]);
    }

    [Fact]
    public async Task Handle_ShouldForwardCancellationTokenToValidator()
    {
        using var source = new CancellationTokenSource(); var validator = new StubValidator([]);
        await new ValidationBehavior<TestRequest, string>([validator]).Handle(new(), _ => Task.FromResult("ok"), source.Token);
        Assert.Equal(source.Token, validator.ObservedToken);
    }

    private sealed record TestRequest;
    private sealed class StubValidator(IReadOnlyCollection<ValidationFailure> failures) : AbstractValidator<TestRequest>
    {
        public CancellationToken ObservedToken { get; private set; }
        public override Task<ValidationResult> ValidateAsync(ValidationContext<TestRequest> context, CancellationToken cancellation = default)
        { ObservedToken = cancellation; return Task.FromResult(new ValidationResult(failures)); }
    }
}
