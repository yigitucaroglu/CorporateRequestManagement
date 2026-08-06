using CorporateRequestManagement.Application.Features.Account;
using CorporateRequestManagement.Application.Features.Administration;
using FluentValidation;

namespace CorporateRequestManagement.UnitTests.Application.Administration;

public sealed class AdministrationValidationTests
{
    [Fact]
    public void AdministrationHandlers_WhenInspected_ShouldExposeDistinctUseCasesWithoutOperationsWrappers()
    {
        Type[] applicationTypes = typeof(GetAdminUsersQuery).Assembly.GetTypes();
        Type[] administrationTypes = applicationTypes
            .Where(type => type.Namespace?.Contains(".Administration", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.DoesNotContain(administrationTypes, type => type.Name.EndsWith("Operations", StringComparison.Ordinal));

        Type[] handlers = applicationTypes
            .Where(type => type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToArray();

        foreach (Type handler in handlers)
        {
            Type[] dependencies = handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .ToArray();

            Assert.DoesNotContain(dependencies, type => type.Name.EndsWith("Operations", StringComparison.Ordinal));
            Assert.DoesNotContain(dependencies, type => IsOrDerivesFrom(type, "Microsoft.EntityFrameworkCore.DbContext"));
            Assert.DoesNotContain(dependencies, type => IsOrDerivesFrom(type, "Microsoft.AspNetCore.Http.HttpContext"));
        }
    }

    private static bool IsOrDerivesFrom(Type type, string fullName)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
            if (string.Equals(current.FullName, fullName, StringComparison.Ordinal))
                return true;
        return false;
    }

    [Fact]
    public async Task Reset_password_confirmation_mismatch_should_fail_validation()
    {
        var validator = new ResetAdminUserPasswordValidator();
        var result = await validator.ValidateAsync(new ResetAdminUserPasswordCommand(Guid.NewGuid(), "StrongPass!2026", "DifferentPass!2026"));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Change_password_confirmation_mismatch_should_fail_validation()
    {
        var validator = new ChangePasswordValidator();
        var result = await validator.ValidateAsync(new ChangePasswordCommand("CurrentPass!2026", "StrongPass!2026", "DifferentPass!2026"));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    [InlineData(20, 10)]
    public async Task Manager_amount_filter_invalid_boundaries_should_fail_validation(decimal min, decimal max)
    {
        var validator = new ManagerRequestScopeValidator();
        var query = new GetManagerDepartmentRequestsQuery(MinAmount: min, MaxAmount: max);
        Assert.False((await validator.ValidateAsync(query)).IsValid);
    }

    [Fact]
    public async Task Audit_date_order_and_paging_should_fail_validation()
    {
        var validator = new AuditLogScopeValidator();
        var query = new GetAuditLogsQuery(0, 101, CreatedFrom: DateTimeOffset.UtcNow, CreatedTo: DateTimeOffset.UtcNow.AddDays(-1));
        Assert.False((await validator.ValidateAsync(query)).IsValid);
    }
}
