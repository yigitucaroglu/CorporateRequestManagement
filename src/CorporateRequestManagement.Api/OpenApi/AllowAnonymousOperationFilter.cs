using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CorporateRequestManagement.Api.OpenApi;

internal sealed class AllowAnonymousOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        bool allowsAnonymous = context.MethodInfo.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any()
            || context.MethodInfo.DeclaringType?.GetCustomAttributes(true).OfType<IAllowAnonymous>().Any() == true;

        if (allowsAnonymous)
            operation.Security = [];
    }
}
