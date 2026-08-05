using CorporateRequestManagement.Application.Common.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using CorporateRequestManagement.Application.Features.Administration;
using CorporateRequestManagement.Application.Features.Account;

namespace CorporateRequestManagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration =>
        {
            configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
            configuration.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}
