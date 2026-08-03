using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Xml.Linq;

namespace CorporateRequestManagement.IntegrationTests;

public sealed class PersistenceArchitectureTests
{
    [Fact]
    public void ApplicationAssembly_DoesNotReferenceInfrastructureOrEntityFrameworkCore()
    {
        string[] references = typeof(IUnitOfWork).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

        Assert.DoesNotContain("CorporateRequestManagement.Infrastructure", references);
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void RepositoryImplementations_AreInternalAndSealed()
    {
        Type[] repositoryTypes = typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(type => type.Namespace == "CorporateRequestManagement.Infrastructure.Persistence.Repositories"
                && type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(7, repositoryTypes.Length);
        Assert.All(repositoryTypes, type =>
        {
            Assert.True(type.IsNotPublic);
            Assert.True(type.IsSealed);
        });
    }

    [Fact]
    public void Repositories_DoNotOwnSaveChangesOrTransactionMethods()
    {
        Type[] repositoryTypes = typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(type => type.Namespace == "CorporateRequestManagement.Infrastructure.Persistence.Repositories"
                && type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .ToArray();
        string[] prohibitedNames = ["SaveChanges", "SaveChangesAsync", "BeginTransaction", "BeginTransactionAsync"];

        Assert.All(repositoryTypes, type => Assert.DoesNotContain(
            type.GetMethods(),
            method => prohibitedNames.Contains(method.Name, StringComparer.Ordinal)));
    }

    [Fact]
    public void InfrastructureProject_ShouldReferenceOnlyApplicationAndDomainProjects()
    {
        XDocument project = LoadProject("src", "CorporateRequestManagement.Infrastructure", "CorporateRequestManagement.Infrastructure.csproj");
        string[] references = project.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension((string)element.Attribute("Include")!))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["CorporateRequestManagement.Application", "CorporateRequestManagement.Domain"], references);
    }

    [Theory]
    [InlineData("CorporateRequestManagement.Application")]
    [InlineData("CorporateRequestManagement.Domain")]
    public void InnerLayerProject_ShouldNotReferenceEntityFrameworkCore(string projectName)
    {
        XDocument project = LoadProject("src", projectName, projectName + ".csproj");
        Assert.DoesNotContain(project.Descendants("PackageReference"), element =>
            ((string?)element.Attribute("Include"))?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(project.Descendants("ProjectReference"), element =>
            ((string?)element.Attribute("Include"))?.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void InfrastructureAssembly_ShouldContainOneDbContextAndCompleteFocusedRepositories()
    {
        Type[] types = typeof(ApplicationDbContext).Assembly.GetTypes();
        Type[] contexts = types.Where(type => !type.IsAbstract && typeof(DbContext).IsAssignableFrom(type)).ToArray();
        Type[] interfaces =
        [
            typeof(ICorporateRequestRepository),
            typeof(ICorporateRequestReadRepository),
            typeof(IApprovalReadRepository),
            typeof(IUserRepository),
            typeof(IDepartmentRepository),
            typeof(IRefreshTokenRepository),
            typeof(IAuditLogRepository)
        ];

        Assert.Equal([typeof(ApplicationDbContext)], contexts);
        Assert.All(interfaces, contract =>
        {
            Type implementation = Assert.Single(types.Where(type => !type.IsAbstract && contract.IsAssignableFrom(type)));
            Assert.True(implementation.IsNotPublic);
            Assert.True(implementation.IsSealed);
        });
        Assert.DoesNotContain(types, type => type.Name.Contains("GenericRepository", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(types, type => type.Name.EndsWith("QueryService", StringComparison.Ordinal));
        Assert.DoesNotContain(types, type => type.Name.EndsWith("CurrentUserContext", StringComparison.Ordinal));
    }

    private static XDocument LoadProject(params string[] segments)
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return XDocument.Load(Path.Combine([root, .. segments]));
    }
}
