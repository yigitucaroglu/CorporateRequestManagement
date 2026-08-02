namespace CorporateRequestManagement.Infrastructure.Persistence.Seeding;

public static class DevelopmentSeedData
{
    public const string DepartmentName = "Operasyon";
    public static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid AdminUserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid ManagerUserId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid EmployeeUserId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public const string AdminEmail = "admin@crm.local";
    public const string ManagerEmail = "manager@crm.local";
    public const string EmployeeEmail = "employee@crm.local";
    public const string DemoPassword = "CrmDemo!2026";
}
