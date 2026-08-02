using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Domain.Entities;
using System.Reflection;
using System.Xml.Linq;

namespace CorporateRequestManagement.UnitTests.Application.Architecture;

public sealed class ApplicationArchitectureTests
{
    private static readonly Assembly ApplicationAssembly = typeof(PageRequest).Assembly;

    [Fact]
    public void ApplicationAssembly_WhenReferencesAreInspected_ShouldReferenceOnlyAllowedProjectLayer()
    {
        string[] references = ApplicationAssembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

        Assert.Contains(typeof(CorporateRequest).Assembly.GetName().Name, references);
        Assert.DoesNotContain("CorporateRequestManagement.Infrastructure", references);
        Assert.DoesNotContain("CorporateRequestManagement.Api", references);
        Assert.DoesNotContain("CorporateRequestManagement.Web", references);
    }

    [Fact]
    public void ApplicationAssembly_WhenFrameworkReferencesAreInspected_ShouldNotReferenceForbiddenFrameworks()
    {
        string[] references = ApplicationAssembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistenceInterfaces_WhenMethodsAreInspected_ShouldNotExposeQueryableOrEfTypes()
    {
        Type[] interfaces = PersistenceInterfaces();
        MethodInfo[] methods = interfaces.SelectMany(type => type.GetMethods()).ToArray();

        Assert.DoesNotContain(methods, method => ContainsForbiddenPersistenceType(method.ReturnType));
        Assert.DoesNotContain(
            methods.SelectMany(method => method.GetParameters()),
            parameter => ContainsForbiddenPersistenceType(parameter.ParameterType));
    }

    [Fact]
    public void ApplicationAssembly_WhenInterfacesAreInspected_ShouldNotContainGenericRepository()
    {
        Type[] interfaces = ApplicationAssembly.GetTypes().Where(type => type.IsInterface).ToArray();

        Assert.DoesNotContain(
            interfaces,
            type => type.Namespace == typeof(IUnitOfWork).Namespace
                && type.IsGenericTypeDefinition
                && type.Name.Contains("Repository", StringComparison.Ordinal));
    }

    [Fact]
    public void PersistenceInterfaces_WhenMethodsAreInspected_ShouldHonorCompleteAsyncContract()
    {
        MethodInfo[] methods = PersistenceInterfaces()
            .SelectMany(type => type.GetMethods())
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(
            methods,
            method =>
            {
                Assert.EndsWith("Async", method.Name, StringComparison.Ordinal);
                Assert.True(IsTaskType(method.ReturnType), $"{method.DeclaringType?.Name}.{method.Name} Task veya Task<T> döndürmelidir.");
                Assert.False(ContainsForbiddenPersistenceType(method.ReturnType));

                ParameterInfo[] parameters = method.GetParameters();
                ParameterInfo cancellationToken = Assert.Single(
                    parameters,
                    parameter => parameter.ParameterType == typeof(CancellationToken));

                Assert.Same(parameters[^1], cancellationToken);
                Assert.True(cancellationToken.HasDefaultValue);
                Assert.All(
                    parameters,
                    parameter =>
                    {
                        Assert.False(parameter.ParameterType.IsByRef);
                        Assert.False(parameter.IsOut);
                        Assert.False(parameter.IsIn);
                        Assert.False(ContainsForbiddenPersistenceType(parameter.ParameterType));
                    });
            });
    }

    [Fact]
    public void UserRepository_WhenContractIsInspected_ShouldExposeOnlyAuthoritativeUserOperations()
    {
        string[] methodNames = typeof(IUserRepository).GetMethods().Select(method => method.Name).Order().ToArray();

        Assert.Equal(["AddAsync", "EmailExistsAsync", "GetByEmailAsync", "GetByIdAsync"], methodNames);
        Assert.DoesNotContain("GetActiveManagerByDepartmentIdAsync", methodNames);
        Assert.Contains(typeof(IDepartmentRepository).GetMethods(), method => method.Name == "GetByIdAsync");
        Assert.DoesNotContain(typeof(ICurrentUserContext).GetProperties(), property => property.Name is "Role" or "DepartmentId");
    }

    [Fact]
    public void ApplicationProject_WhenProjectReferencesAreInspected_ShouldOnlyReferenceDomain()
    {
        string projectPath = ApplicationProjectPath();
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        XDocument project = XDocument.Load(projectPath);
        string[] includes = Elements(project, "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => NormalizeReferencedPath(projectDirectory, include!))
            .ToArray();
        string expectedDomainProject = Path.GetFullPath(
            Path.Combine(projectDirectory, "..", "CorporateRequestManagement.Domain", "CorporateRequestManagement.Domain.csproj"));

        Assert.Equal(expectedDomainProject, Assert.Single(includes), ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void ApplicationProject_WhenPackageAndFrameworkReferencesAreInspected_ShouldContainOnlyApprovedCqrsPackages()
    {
        XDocument project = XDocument.Load(ApplicationProjectPath());
        string[] packages = Elements(project, "PackageReference")
            .Select(element => $"{element.Attribute("Include")?.Value}:{element.Attribute("Version")?.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["FluentValidation.DependencyInjectionExtensions:12.1.1", "FluentValidation:12.1.1", "MediatR:12.5.0"],
            packages);
        Assert.Empty(Elements(project, "FrameworkReference"));
    }

    [Fact]
    public void ApplicationProject_WhenContentIsInspected_ShouldNotContainForbiddenDependencies()
    {
        string content = File.ReadAllText(ApplicationProjectPath());
        string[] forbiddenDependencies =
        [
            "EntityFrameworkCore",
            "AspNetCore",
            "Infrastructure",
            "CorporateRequestManagement.Api",
            "CorporateRequestManagement.Web"
        ];

        Assert.All(
            forbiddenDependencies,
            dependency => Assert.DoesNotContain(dependency, content, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnitOfWork_WhenContractIsInspected_ShouldOnlyExposeSaveChangesAsync()
    {
        MethodInfo method = Assert.Single(typeof(IUnitOfWork).GetMethods());

        Assert.Equal(nameof(IUnitOfWork.SaveChangesAsync), method.Name);
        Assert.Equal(typeof(Task<int>), method.ReturnType);
    }

    [Fact]
    public void TransactionExecutor_WhenContractIsInspected_ShouldOnlyExposeTwoExecuteAsyncOverloads()
    {
        MethodInfo[] methods = typeof(ITransactionExecutor).GetMethods();

        Assert.Equal(2, methods.Length);
        Assert.All(methods, method => Assert.Equal(nameof(ITransactionExecutor.ExecuteAsync), method.Name));
        Assert.Single(methods, method => method.IsGenericMethodDefinition);
        Assert.Single(methods, method => !method.IsGenericMethodDefinition);
    }

    [Fact]
    public void CurrentUserContext_WhenContractIsInspected_ShouldOnlyExposeIdentityProperties()
    {
        PropertyInfo[] properties = typeof(ICurrentUserContext).GetProperties();

        Assert.Equal(2, properties.Length);
        Assert.Contains(properties, property => property.Name == nameof(ICurrentUserContext.IsAuthenticated));
        Assert.Contains(properties, property => property.Name == nameof(ICurrentUserContext.UserId));
        Assert.Empty(typeof(ICurrentUserContext).GetMethods().Where(method => !method.IsSpecialName));
    }

    [Fact]
    public void ApplicationAssembly_WhenTypesAreInspected_ShouldNotContainApplicationDbContext()
    {
        Assert.DoesNotContain(ApplicationAssembly.GetTypes(), type => type.Name == "ApplicationDbContext");
    }

    private static Type[] PersistenceInterfaces()
    {
        return ApplicationAssembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(IUnitOfWork).Namespace)
            .ToArray();
    }

    private static bool ContainsForbiddenPersistenceType(Type type)
    {
        if (type.IsArray)
        {
            return ContainsForbiddenPersistenceType(type.GetElementType()!);
        }

        if (type.Name is "IQueryable" or "IQueryable`1" or "DbContext" or "DbSet`1" or "EntityEntry")
        {
            return true;
        }

        if (type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true)
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(ContainsForbiddenPersistenceType);
    }

    private static bool IsTaskType(Type type)
    {
        return type == typeof(Task)
            || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>);
    }

    private static string ApplicationProjectPath()
    {
        string repositoryRoot = RepositoryRoot();
        return Path.Combine(
            repositoryRoot,
            "src",
            "CorporateRequestManagement.Application",
            "CorporateRequestManagement.Application.csproj");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CorporateRequestManagement.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"CorporateRequestManagement.sln, test çıktı yolu '{AppContext.BaseDirectory}' üst klasörlerinde bulunamadı.");
    }

    private static IEnumerable<XElement> Elements(XDocument document, string localName)
    {
        return document.Descendants().Where(element => element.Name.LocalName == localName);
    }

    private static string NormalizeReferencedPath(string projectDirectory, string include)
    {
        string normalizedSeparators = include
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(projectDirectory, normalizedSeparators));
    }
}
