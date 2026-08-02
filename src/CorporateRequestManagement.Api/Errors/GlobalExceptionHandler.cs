using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Api.Errors;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, string, Exception?> LogUnhandled =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(1, "UnhandledApiException"), "Unhandled API exception. TraceId: {TraceId}");
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        (int status, string code, string title, string detail, object? errors) = exception switch
        {
            ApplicationValidationException e => (400, e.Code, "Doğrulama hatası", e.Message, e.Errors),
            AuthenticationFailedException e => (401, e.Code, "Kimlik doğrulama hatası", e.Message, null),
            ForbiddenAccessException e => (403, e.Code, "Erişim reddedildi", e.Message, null),
            NotFoundException e => (404, e.Code, "Bulunamadı", e.Message, null),
            ConflictException e => (409, e.Code, "Çakışma", e.Message, null),
            DomainRuleException e => (409, e.Code, "İş kuralı çakışması", e.Message, null),
            DbUpdateConcurrencyException => (409, "concurrency.conflict", "Çakışma", "Kayıt başka bir işlem tarafından değiştirildi.", null),
            _ => (500, "server.unexpected", "Sunucu hatası", "Beklenmeyen bir hata oluştu.", null)
        };
        if (status == 500) LogUnhandled(logger, context.TraceIdentifier, exception);
        await ProblemDetailsResponse.WriteAsync(context, status, code, title, detail, errors);
        return true;
    }
}
