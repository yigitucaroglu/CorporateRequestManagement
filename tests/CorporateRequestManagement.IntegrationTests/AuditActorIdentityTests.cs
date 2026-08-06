using System.Net;
using System.Net.Http.Json;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class AuditActorIdentityTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Audit_endpoints_should_resolve_deleted_and_system_actors_filter_in_sql_and_return_complete_distinct_lookup()
    {
        await using var host = new AdministrationApiTestHost(fixture);
        await host.StartAsync();
        using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var users = Enumerable.Range(0, 101).Select(index => User.Create(Guid.NewGuid(), $"Aktör{index:D3}", "Kullanıcı",
            $"positive-audit-actor-{index:D3}-{Guid.NewGuid():N}@company.com", "test-only-hash", UserRole.Employee,
            DevelopmentSeedData.DepartmentId, now.AddTicks(index))).ToList();
        users[0].SoftDelete(now.AddMinutes(1), DevelopmentSeedData.AdminUserId);
        List<Guid> auditIds = [];

        await using (ApplicationDbContext db = host.Db())
        {
            db.Users.AddRange(users);
            foreach (User user in users)
            {
                Guid auditId = Guid.NewGuid();
                auditIds.Add(auditId);
                db.AuditLogs.Add(AuditLog.Create(auditId, user.Id, "User", user.Id, "User.Updated", null, "{\"Role\":1}", now.AddSeconds(auditIds.Count)));
            }
            Guid duplicateId = Guid.NewGuid();
            Guid systemId = Guid.NewGuid();
            auditIds.AddRange([duplicateId, systemId]);
            db.AuditLogs.Add(AuditLog.Create(duplicateId, users[0].Id, "User", users[0].Id, "User.Updated", null, null, now.AddMinutes(3)));
            db.AuditLogs.Add(AuditLog.Create(systemId, null, "AuditLog", systemId, "System.Maintenance", null, null, now.AddMinutes(4)));
            await db.SaveChangesAsync();
        }

        try
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await host.Factory.CreateClient().GetAsync("/api/audit-logs/actors")).StatusCode);

            using HttpResponseMessage actorResponse = await admin.GetAsync("/api/audit-logs/actors");
            string actorPayload = await actorResponse.Content.ReadAsStringAsync();
            Assert.True(actorResponse.IsSuccessStatusCode, actorPayload);
            AuditActorResponse[] actors = (await actorResponse.Content.ReadFromJsonAsync<AuditActorResponse[]>())!;
            Assert.True(actors.Length >= 102); // 101 test actors plus the seeded admin login actor; other preserved historical actors are valid.
            Assert.Equal(actors.Length, actors.Select(actor => actor.Id).Distinct().Count());
            Assert.Single(actors, actor => actor.Id == users[0].Id);
            Assert.True(actors.Single(actor => actor.Id == users[0].Id).IsDeleted);
            Assert.Equal(users.Select(user => user.Id), actors.Where(actor => actor.Email.StartsWith("positive-audit-actor-", StringComparison.Ordinal)).Select(actor => actor.Id));

            AuditPageResponse filtered = (await admin.GetFromJsonAsync<AuditPageResponse>($"/api/audit-logs?userId={users[0].Id}&pageSize=100"))!;
            Assert.Equal(2, filtered.TotalCount);
            Assert.All(filtered.Items, item => { Assert.Equal(users[0].Id, item.ActorUserId); Assert.Equal("Aktör000 Kullanıcı", item.ActorFullName); Assert.True(item.ActorIsDeleted); Assert.Equal(UserRole.Employee, item.ActorRole); });

            AuditPageResponse system = (await admin.GetFromJsonAsync<AuditPageResponse>("/api/audit-logs?entityName=AuditLog"))!;
            AuditResponse systemItem = Assert.Single(system.Items, item => item.Id == auditIds[^1]);
            Assert.True(systemItem.IsSystemActor);
            Assert.Equal("Sistem", systemItem.ActorFullName);
            Assert.Null(systemItem.ActorUserId);
        }
        finally
        {
            await using ApplicationDbContext cleanup = host.Db();
            await cleanup.AuditLogs.Where(audit => auditIds.Contains(audit.Id)).ExecuteDeleteAsync();
        }
    }

    private sealed record AuditActorResponse(Guid Id, string FullName, string Email, UserRole Role, bool IsDeleted, bool IsSystemActor);
    private sealed record AuditPageResponse(IReadOnlyList<AuditResponse> Items, int TotalCount);
    private sealed record AuditResponse(Guid Id, Guid? ActorUserId, string ActorFullName, string? ActorEmail, UserRole? ActorRole, bool ActorIsDeleted, bool IsSystemActor);
}
