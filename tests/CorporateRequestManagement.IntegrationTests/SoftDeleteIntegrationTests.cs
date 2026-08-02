using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class SoftDeleteIntegrationTests(SqlServerDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DepartmentSoftDelete_ShouldHideNormalQueryAndPreserveMetadata()
    {
        Guid id = Guid.NewGuid();
        Guid actor = Guid.NewGuid();
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            Department department = Department.Create(id, "Soft delete " + id.ToString("N"), null, Now);
            setup.Departments.Add(department);
            await setup.SaveChangesAsync();
            Assert.True(await setup.Departments.AnyAsync(item => item.Id == id));
            department.SoftDelete(Now.AddMinutes(1), actor);
            await setup.SaveChangesAsync();
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.False(await verification.Departments.AnyAsync(item => item.Id == id));
        Department deleted = await verification.Departments.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
        Assert.True(deleted.IsDeleted);
        Assert.Equal(actor, deleted.DeletedByUserId);
        Assert.Equal(Now.AddMinutes(1), deleted.DeletedDate);
    }

    [Fact]
    public async Task CorporateRequestSoftDelete_ShouldPreserveApprovalHistory()
    {
        Guid requestId;
        Guid approvalId = Guid.NewGuid();
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(setup, Guid.NewGuid().ToString("N"));
            CorporateRequest request = CorporateRequest.Create(Guid.NewGuid(), "Request", "Description", RequestType.Expense, 5000m, principals.Employee.Id, principals.Department.Id, Now);
            requestId = request.Id;
            request.Submit(principals.Employee.Id, Now.AddMinutes(1));
            request.ApproveByManager(approvalId, principals.Manager.Id, 1000m, Now.AddMinutes(2));
            setup.CorporateRequests.Add(request);
            await setup.SaveChangesAsync();
            request.SoftDelete(Now.AddMinutes(3), principals.Manager.Id);
            await setup.SaveChangesAsync();
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.False(await verification.CorporateRequests.AnyAsync(item => item.Id == requestId));
        CorporateRequest deleted = await verification.CorporateRequests.IgnoreQueryFilters().SingleAsync(item => item.Id == requestId);
        Assert.True(deleted.IsDeleted);
        Assert.True(await verification.RequestApprovals.AnyAsync(item => item.Id == approvalId && item.RequestId == requestId));
    }
}
