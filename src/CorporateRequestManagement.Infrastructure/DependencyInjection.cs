using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Repositories;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using CorporateRequestManagement.Infrastructure.Persistence.Transactions;
using CorporateRequestManagement.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text;
using CorporateRequestManagement.Application.Abstractions.Approvals;
using CorporateRequestManagement.Infrastructure.Approvals;

namespace CorporateRequestManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ICorporateRequestRepository, CorporateRequestRepository>();
        services.AddScoped<ICorporateRequestReadRepository, CorporateRequestReadRepository>();
        services.AddScoped<IApprovalReadRepository, ApprovalReadRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ITransactionExecutor, EfTransactionExecutor>();
        services.AddScoped<DevelopmentDataSeeder>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ApprovalOptions>().Configure(options => options.Threshold = 10_000m)
            .Validate(options => options.Threshold > 0, "Approval threshold sıfırdan büyük olmalıdır.").ValidateOnStart();
        services.AddSingleton<IApprovalThresholdProvider, ApprovalThresholdProvider>();
        return services;
    }

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection connection string yapılandırılmalıdır.");
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ICorporateRequestRepository, CorporateRequestRepository>();
        services.AddScoped<ICorporateRequestReadRepository, CorporateRequestReadRepository>();
        services.AddScoped<IApprovalReadRepository, ApprovalReadRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDepartmentRepository, DepartmentRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddScoped<ITransactionExecutor, EfTransactionExecutor>();
        services.AddScoped<DevelopmentDataSeeder>();
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IPasswordService, PasswordService>();
        services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
        services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IGuidGenerator, SystemGuidGenerator>();
        services.AddOptions<JwtOptions>().Configure(options =>
            {
                options.Issuer = configuration["Jwt:Issuer"] ?? string.Empty;
                options.Audience = configuration["Jwt:Audience"] ?? string.Empty;
                options.SigningKey = configuration["Jwt:SigningKey"] ?? string.Empty;
                options.AccessTokenMinutes = int.TryParse(configuration["Jwt:AccessTokenMinutes"], out int access) ? access : 15;
                options.RefreshTokenDays = int.TryParse(configuration["Jwt:RefreshTokenDays"], out int refresh) ? refresh : 7;
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Issuer zorunludur.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Audience zorunludur.")
            .Validate(options => Encoding.UTF8.GetByteCount(options.SigningKey) >= 32, "SigningKey en az 32 byte olmalıdır.")
            .Validate(options => options.AccessTokenMinutes is >= 1 and <= 60, "AccessTokenMinutes 1-60 aralığında olmalıdır.")
            .Validate(options => options.RefreshTokenDays is >= 1 and <= 30, "RefreshTokenDays 1-30 aralığında olmalıdır.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ApprovalOptions>().Configure(options =>
            {
                options.Threshold = decimal.TryParse(
                    configuration[$"{ApprovalOptions.SectionName}:Threshold"],
                    out decimal threshold)
                    ? threshold
                    : 10_000m;
            })
            .Validate(options => options.Threshold > 0, "Approval threshold sıfırdan büyük olmalıdır.").ValidateOnStart();
        services.AddSingleton<IApprovalThresholdProvider, ApprovalThresholdProvider>();

        return services;
    }
}
