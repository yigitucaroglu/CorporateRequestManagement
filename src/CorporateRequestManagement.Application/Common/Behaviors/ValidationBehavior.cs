using CorporateRequestManagement.Application.Common.Exceptions;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Common.Behaviors;

internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any()) return await next(cancellationToken);
        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var errors = results.SelectMany(r => r.Errors).Where(e => e is not null)
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray());
        if (errors.Count > 0)
            throw new ApplicationValidationException("validation.failed", "İstek doğrulaması başarısız.", errors);
        return await next(cancellationToken);
    }
}
