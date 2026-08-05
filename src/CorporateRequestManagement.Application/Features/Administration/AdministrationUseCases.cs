using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Administration;

public sealed record GetAdminUsersQuery(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null, UserRole? Role = null,
    Guid? DepartmentId = null, bool? IsActive = null, string SortBy = "createdDate", SortDirection SortDirection = SortDirection.Descending) : IRequest<PagedResult<AdminUserDto>>;
public sealed record GetAdminUserQuery(Guid Id) : IRequest<AdminUserDto>;
public sealed record CreateAdminUserCommand(string FirstName, string LastName, string Email, string InitialPassword, string ConfirmInitialPassword, UserRole Role, Guid? DepartmentId, bool IsActive) : IRequest<AdminUserDto>;
public sealed record UpdateAdminUserCommand(Guid Id, string FirstName, string LastName, string Email, UserRole Role, Guid? DepartmentId, bool IsActive) : IRequest<AdminUserDto>;
public sealed record DeleteAdminUserCommand(Guid Id) : IRequest;
public sealed record ResetAdminUserPasswordCommand(Guid Id, string NewPassword, string ConfirmPassword) : IRequest;

public sealed record GetDepartmentsQuery(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null, bool? HasManager = null,
    string SortBy = "createdDate", SortDirection SortDirection = SortDirection.Descending) : IRequest<PagedResult<DepartmentDto>>;
public sealed record GetDepartmentQuery(Guid Id) : IRequest<DepartmentDto>;
public sealed record GetEligibleDepartmentManagersQuery(Guid DepartmentId) : IRequest<IReadOnlyList<EligibleManagerDto>>;
public sealed record CreateDepartmentCommand(string Name) : IRequest<DepartmentDto>;
public sealed record UpdateDepartmentCommand(Guid Id, string Name) : IRequest<DepartmentDto>;
public sealed record AssignDepartmentManagerCommand(Guid Id, Guid? ManagerId) : IRequest<DepartmentDto>;
public sealed record CreateAndAssignDepartmentManagerCommand(Guid DepartmentId, string FirstName, string LastName, string Email,
    string InitialPassword, string ConfirmPassword, bool IsActive) : IRequest<DepartmentDto>;
public sealed record DeleteDepartmentCommand(Guid Id) : IRequest;

internal sealed class GetAdminUsersValidator : AbstractValidator<GetAdminUsersQuery>
{
    private static readonly string[] SortFields = ["name", "email", "role", "createdDate"];
    public GetAdminUsersValidator() { RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize); RuleFor(x => x.SearchTerm).MaximumLength(100); RuleFor(x => x.SortBy).Must(x => SortFields.Contains(x, StringComparer.OrdinalIgnoreCase)); RuleFor(x => x.Role).Must(x => !x.HasValue || Enum.IsDefined(x.Value)); }
}
internal sealed class GetDepartmentsValidator : AbstractValidator<GetDepartmentsQuery>
{ public GetDepartmentsValidator() { RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize); RuleFor(x => x.SearchTerm).MaximumLength(100); RuleFor(x => x.SortBy).Must(x => x.Equals("name", StringComparison.OrdinalIgnoreCase) || x.Equals("createdDate", StringComparison.OrdinalIgnoreCase)); } }
internal sealed class CreateAdminUserValidator : AbstractValidator<CreateAdminUserCommand>
{ public CreateAdminUserValidator() { RuleFor(x => x.FirstName).NotEmpty().MaximumLength(User.NameMaxLength); RuleFor(x => x.LastName).NotEmpty().MaximumLength(User.NameMaxLength); RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength); RuleFor(x => x.Role).IsInEnum(); RuleFor(x => x.InitialPassword).NotEmpty().MaximumLength(128); RuleFor(x => x.ConfirmInitialPassword).NotEmpty().Equal(x => x.InitialPassword).WithMessage("Parolalar birbiriyle eşleşmiyor."); } }
internal sealed class UpdateAdminUserValidator : AbstractValidator<UpdateAdminUserCommand>
{ public UpdateAdminUserValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.FirstName).NotEmpty().MaximumLength(User.NameMaxLength); RuleFor(x => x.LastName).NotEmpty().MaximumLength(User.NameMaxLength); RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength); RuleFor(x => x.Role).IsInEnum(); } }
internal sealed class ResetAdminUserPasswordValidator : AbstractValidator<ResetAdminUserPasswordCommand>
{
    public ResetAdminUserPasswordValidator()
    {
        RuleFor(x => x.ConfirmPassword).NotEmpty().WithMessage("Parola tekrarı zorunludur.")
            .Equal(x => x.NewPassword).WithMessage("Parola tekrarı yeni parola ile eşleşmelidir.");
    }
}
internal sealed class CreateDepartmentValidator : AbstractValidator<CreateDepartmentCommand> { public CreateDepartmentValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(Department.NameMaxLength); }
internal sealed class UpdateDepartmentValidator : AbstractValidator<UpdateDepartmentCommand> { public UpdateDepartmentValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.Name).NotEmpty().MaximumLength(Department.NameMaxLength); } }
internal sealed class CreateAndAssignDepartmentManagerValidator : AbstractValidator<CreateAndAssignDepartmentManagerCommand>
{
    public CreateAndAssignDepartmentManagerValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(User.NameMaxLength);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(User.EmailMaxLength);
        RuleFor(x => x.InitialPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.ConfirmPassword).NotEmpty().WithMessage("Parola tekrarı zorunludur.")
            .Equal(x => x.InitialPassword).WithMessage("Parolalar birbiriyle eşleşmiyor.");
        RuleFor(x => x.IsActive).Equal(true).WithMessage("Departman yöneticisi aktif olmalıdır.");
    }
}

// Use-case orchestration lives in the focused handlers in AdministrationHandlers.cs.

internal static class PasswordRules
{
    public static void Ensure(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length is < 12 or > 128 || !value.Any(char.IsUpper) || !value.Any(char.IsLower) || !value.Any(char.IsDigit) || !value.Any(ch => !char.IsLetterOrDigit(ch))) throw new ApplicationValidationException("validation.failed", "İstek doğrulaması başarısız.", new Dictionary<string, string[]> { ["Password"] = ["Parola 12-128 karakter olmalı; büyük/küçük harf, rakam ve sembol içermelidir."] }); }
}
