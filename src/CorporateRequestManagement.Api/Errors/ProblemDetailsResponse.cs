using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Api.Errors;

internal static class ProblemDetailsResponse
{
    public static Task WriteAsync(HttpContext context, int status, string code, string title, string detail, object? errors = null)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = context.Request.Path, Type = $"https://httpstatuses.com/{status}" };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (errors is not null) problem.Extensions["errors"] = errors;
        return context.Response.WriteAsJsonAsync(problem);
    }
}
