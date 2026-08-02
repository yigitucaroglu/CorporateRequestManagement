using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class PersistenceMappingTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public void Model_MapsAllAggregateTables()
    {
        using ApplicationDbContext context = fixture.CreateContext();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
        Type[] entityTypes = [typeof(User), typeof(Department), typeof(CorporateRequest), typeof(RequestApproval), typeof(RefreshToken), typeof(AuditLog)];
        string?[] tables = entityTypes
            .Select(type => context.Model.FindEntityType(type)?.GetTableName())
            .ToArray();

        Assert.Equal(["Users", "Departments", "CorporateRequests", "RequestApprovals", "RefreshTokens", "AuditLogs"], tables);
    }

    [Fact]
    public void Model_MapsRequestRowVersionAsSqlServerConcurrencyToken()
    {
        using ApplicationDbContext context = fixture.CreateContext();
        IProperty property = context.Model.FindEntityType(typeof(CorporateRequest))!.FindProperty("_rowVersion")!;

        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Equal("rowversion", property.GetColumnType());
    }

    [Fact]
    public void Model_DefinesSoftDeleteQueryFilters()
    {
        using ApplicationDbContext context = fixture.CreateContext();

        Assert.NotNull(context.Model.FindEntityType(typeof(User))!.GetQueryFilter());
        Assert.NotNull(context.Model.FindEntityType(typeof(Department))!.GetQueryFilter());
        Assert.NotNull(context.Model.FindEntityType(typeof(CorporateRequest))!.GetQueryFilter());
        Assert.Null(context.Model.FindEntityType(typeof(RequestApproval))!.GetQueryFilter());
        Assert.Null(context.Model.FindEntityType(typeof(RefreshToken))!.GetQueryFilter());
        Assert.Null(context.Model.FindEntityType(typeof(AuditLog))!.GetQueryFilter());
    }

    [Fact]
    public void Model_UsesNoActionForEveryForeignKey()
    {
        using ApplicationDbContext context = fixture.CreateContext();
        IEnumerable<IForeignKey> foreignKeys = context.Model.GetEntityTypes().SelectMany(type => type.GetForeignKeys());

        Assert.All(foreignKeys, foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
    }

    [Fact]
    public void Model_UsesFilteredUniqueIndexesForSoftDeletedNaturalKeys()
    {
        using ApplicationDbContext context = fixture.CreateContext();
        IIndex emailIndex = context.Model.FindEntityType(typeof(User))!.GetIndexes().Single(index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(User.Email));
        IIndex nameIndex = context.Model.FindEntityType(typeof(Department))!.GetIndexes().Single(index => index.Properties.Count == 1 && index.Properties[0].Name == nameof(Department.Name));

        Assert.True(emailIndex.IsUnique);
        Assert.Equal("[IsDeleted] = 0", emailIndex.GetFilter());
        Assert.True(nameIndex.IsUnique);
        Assert.Equal("[IsDeleted] = 0", nameIndex.GetFilter());
    }
}
