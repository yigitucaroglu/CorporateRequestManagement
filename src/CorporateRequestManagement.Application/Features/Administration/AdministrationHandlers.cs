using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Administration;

internal static class AdministrationHandlerRules
{
    public static Guid Actor(ICurrentUserContext current) => current.IsAuthenticated && current.UserId.HasValue
        ? current.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
    public static string Email(string value) => value.Trim().ToLowerInvariant();
    public static NotFoundException MissingUser() => new("user.not_found", "Kullanıcı bulunamadı.");
    public static NotFoundException MissingDepartment() => new("department.not_found", "Departman bulunamadı.");
    public static ConflictException DuplicateDepartment() => new("department.name_exists", "Bu departman adı zaten kullanılıyor.");
    public static async Task ValidateDepartmentAsync(UserRole role, Guid? id, IDepartmentRepository departments, CancellationToken ct)
    {
        if (role is UserRole.Employee or UserRole.Manager && id is null) throw Validation("DepartmentId", "Departman zorunludur.");
        if (id.HasValue && await departments.GetByIdAsync(id.Value, ct) is null) throw Validation("DepartmentId", "Geçerli aktif departman seçin.");
    }
    private static ApplicationValidationException Validation(string field, string message) => new("validation.failed", "İstek doğrulaması başarısız.", new Dictionary<string, string[]> { [field] = [message] });
}

internal sealed class GetAdminUsersHandler(IAdministrationQueryService queries) : IRequestHandler<GetAdminUsersQuery, PagedResult<AdminUserDto>>
{ public Task<PagedResult<AdminUserDto>> Handle(GetAdminUsersQuery q, CancellationToken ct) => queries.GetUsersAsync(new(q.PageNumber,q.PageSize,q.SearchTerm,q.Role,q.DepartmentId,q.IsActive,q.SortBy,q.SortDirection),ct); }

internal sealed class GetAdminUserHandler(IAdministrationQueryService queries) : IRequestHandler<GetAdminUserQuery, AdminUserDto>
{ public async Task<AdminUserDto> Handle(GetAdminUserQuery q,CancellationToken ct)=>await queries.GetUserAsync(q.Id,ct)??throw AdministrationHandlerRules.MissingUser(); }

internal sealed class CreateAdminUserHandler(IAdministrationQueryService queries, IUserRepository users, IDepartmentRepository departments,
    IPasswordService passwords, IAuditLogRepository audits, IUnitOfWork unit, ITransactionExecutor transactions,
    ICurrentUserContext current, IGuidGenerator guids, TimeProvider time) : IRequestHandler<CreateAdminUserCommand, AdminUserDto>
{
    public async Task<AdminUserDto> Handle(CreateAdminUserCommand c,CancellationToken ct)
    {
        await AdministrationHandlerRules.ValidateDepartmentAsync(c.Role,c.DepartmentId,departments,ct); string email=AdministrationHandlerRules.Email(c.Email);
        if(await users.EmailExistsAsync(email,null,ct)) throw new ConflictException("user.email_exists","Bu e-posta zaten kullanılıyor.");
        PasswordRules.Ensure(c.InitialPassword); DateTimeOffset now=time.GetUtcNow(); Guid id=guids.NewGuid(); Guid actor=AdministrationHandlerRules.Actor(current);
        User user=User.Create(id,c.FirstName,c.LastName,email,"temporary",c.Role,c.DepartmentId,now); user.ChangePasswordHash(passwords.HashPassword(user,c.InitialPassword));
        if(!c.IsActive) user.UpdateProfile(c.FirstName,c.LastName,email,c.Role,c.DepartmentId,false);
        await transactions.ExecuteAsync(async token=>{await users.AddAsync(user,token);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"User",id,"User.Created",null,null,now),token);await unit.SaveChangesAsync(token);},ct);
        return await queries.GetUserAsync(id,ct)??throw AdministrationHandlerRules.MissingUser();
    }
}

internal sealed class UpdateAdminUserHandler(IAdministrationQueryService queries,IUserRepository users,IDepartmentRepository departments,
    IUserTokenRevocationRepository tokens,IAuditLogRepository audits,IUnitOfWork unit,ITransactionExecutor transactions,
    ICurrentUserContext current,IGuidGenerator guids,TimeProvider time):IRequestHandler<UpdateAdminUserCommand,AdminUserDto>
{
    public async Task<AdminUserDto> Handle(UpdateAdminUserCommand c,CancellationToken ct)
    {
        Guid actor=AdministrationHandlerRules.Actor(current); User user=await users.GetByIdAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingUser();
        if(actor==user.Id&&(!c.IsActive||c.Role!=UserRole.Admin)) throw new ConflictException("user.self_protection","Kendi yönetici hesabınızı pasifleştiremez veya rolünü değiştiremezsiniz.");
        AdminUserDto before=await queries.GetUserAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingUser();
        if(before.Role==UserRole.Manager&&(before.Role!=c.Role||before.DepartmentId!=c.DepartmentId||!c.IsActive)&&await departments.IsActiveDepartmentManagerAsync(user.Id,ct)) throw new ConflictException("user.manager_dependency","Kullanıcı aktif bir departmanın yöneticisidir.");
        await AdministrationHandlerRules.ValidateDepartmentAsync(c.Role,c.DepartmentId,departments,ct); string email=AdministrationHandlerRules.Email(c.Email);
        if(await users.EmailExistsAsync(email,user.Id,ct)) throw new ConflictException("user.email_exists","Bu e-posta zaten kullanılıyor.");
        bool revoke=user.Role!=c.Role||user.IsActive!=c.IsActive; DateTimeOffset now=time.GetUtcNow();
        await transactions.ExecuteAsync(async token=>{user.UpdateProfile(c.FirstName,c.LastName,email,c.Role,c.DepartmentId,c.IsActive);if(revoke)await tokens.RevokeActiveForUserAsync(user.Id,now,token);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"User",user.Id,"User.Updated",null,null,now),token);await unit.SaveChangesAsync(token);},ct);
        return await queries.GetUserAsync(user.Id,ct)??throw AdministrationHandlerRules.MissingUser();
    }
}

internal sealed class DeleteAdminUserHandler(IUserRepository users,IDepartmentRepository departments,IUserTokenRevocationRepository tokens,
    IAuditLogRepository audits,IUnitOfWork unit,ITransactionExecutor transactions,ICurrentUserContext current,IGuidGenerator guids,TimeProvider time):IRequestHandler<DeleteAdminUserCommand>
{
    public async Task Handle(DeleteAdminUserCommand c,CancellationToken ct){Guid actor=AdministrationHandlerRules.Actor(current);if(actor==c.Id)throw new ConflictException("user.self_delete_forbidden","Kendi hesabınızı silemezsiniz.");User user=await users.GetByIdAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingUser();if(await departments.IsActiveDepartmentManagerAsync(user.Id,ct))throw new ConflictException("user.manager_dependency","Kullanıcı aktif bir departmanın yöneticisidir.");DateTimeOffset now=time.GetUtcNow();await transactions.ExecuteAsync(async token=>{user.SoftDelete(now,actor);await tokens.RevokeActiveForUserAsync(user.Id,now,token);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"User",user.Id,"User.SoftDeleted",null,null,now),token);await unit.SaveChangesAsync(token);},ct);}
}

internal sealed class ResetAdminUserPasswordHandler(IUserRepository users,IPasswordService passwords,IUserTokenRevocationRepository tokens,
    IAuditLogRepository audits,IUnitOfWork unit,ITransactionExecutor transactions,ICurrentUserContext current,IGuidGenerator guids,TimeProvider time):IRequestHandler<ResetAdminUserPasswordCommand>
{
    public async Task Handle(ResetAdminUserPasswordCommand c,CancellationToken ct){Guid actor=AdministrationHandlerRules.Actor(current);if(actor==c.Id)throw new ConflictException("user.self_reset_use_profile","Kendi parolanız için profil parola değiştirme akışını kullanın.");PasswordRules.Ensure(c.NewPassword);User user=await users.GetByIdAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingUser();if(!user.IsActive)throw AdministrationHandlerRules.MissingUser();DateTimeOffset now=time.GetUtcNow();await transactions.ExecuteAsync(async token=>{user.ChangePasswordHash(passwords.HashPassword(user,c.NewPassword));await tokens.RevokeActiveForUserAsync(user.Id,now,token);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"User",user.Id,"User.PasswordReset",null,null,now),token);await unit.SaveChangesAsync(token);},ct);}
}

internal sealed class GetDepartmentsHandler(IAdministrationQueryService queries):IRequestHandler<GetDepartmentsQuery,PagedResult<DepartmentDto>>
{public Task<PagedResult<DepartmentDto>> Handle(GetDepartmentsQuery q,CancellationToken ct)=>queries.GetDepartmentsAsync(new(q.PageNumber,q.PageSize,q.SearchTerm,q.HasManager,q.SortBy,q.SortDirection),ct);}
internal sealed class GetDepartmentHandler(IAdministrationQueryService queries):IRequestHandler<GetDepartmentQuery,DepartmentDto>
{public async Task<DepartmentDto> Handle(GetDepartmentQuery q,CancellationToken ct)=>await queries.GetDepartmentAsync(q.Id,ct)??throw AdministrationHandlerRules.MissingDepartment();}
internal sealed class GetEligibleDepartmentManagersHandler(IAdministrationQueryService queries, IDepartmentRepository departments):IRequestHandler<GetEligibleDepartmentManagersQuery,IReadOnlyList<EligibleManagerDto>>
{public async Task<IReadOnlyList<EligibleManagerDto>> Handle(GetEligibleDepartmentManagersQuery q,CancellationToken ct){_ = await departments.GetByIdAsync(q.DepartmentId,ct)??throw AdministrationHandlerRules.MissingDepartment();return await queries.GetEligibleManagersAsync(q.DepartmentId,ct);}}

internal sealed class CreateDepartmentHandler(IAdministrationQueryService queries,IDepartmentRepository departments,IAuditLogRepository audits,IUnitOfWork unit,ITransactionExecutor transactions,ICurrentUserContext current,IGuidGenerator guids,TimeProvider time):IRequestHandler<CreateDepartmentCommand,DepartmentDto>
{public async Task<DepartmentDto> Handle(CreateDepartmentCommand c,CancellationToken ct){string name=c.Name.Trim();if(await departments.NameExistsAsync(name,null,ct))throw AdministrationHandlerRules.DuplicateDepartment();DateTimeOffset now=time.GetUtcNow();Guid id=guids.NewGuid(),actor=AdministrationHandlerRules.Actor(current);Department d=Department.Create(id,name,null,now);await transactions.ExecuteAsync(async token=>{await departments.AddAsync(d,token);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"Department",id,"Department.Created",null,null,now),token);await unit.SaveChangesAsync(token);},ct);return await queries.GetDepartmentAsync(id,ct)??throw AdministrationHandlerRules.MissingDepartment();}}

internal sealed class UpdateDepartmentHandler(IAdministrationQueryService queries,IDepartmentRepository departments,IAuditLogRepository audits,IUnitOfWork unit,ITransactionExecutor transactions,ICurrentUserContext current,IGuidGenerator guids,TimeProvider time):IRequestHandler<UpdateDepartmentCommand,DepartmentDto>
{public async Task<DepartmentDto> Handle(UpdateDepartmentCommand c,CancellationToken ct){Department d=await departments.GetByIdAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingDepartment();string name=c.Name.Trim();if(await departments.NameExistsAsync(name,d.Id,ct))throw AdministrationHandlerRules.DuplicateDepartment();DateTimeOffset now=time.GetUtcNow();Guid actor=AdministrationHandlerRules.Actor(current);await transactions.ExecuteAsync(async token=>{d.Rename(name);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"Department",d.Id,"Department.Updated",null,null,now),token);await unit.SaveChangesAsync(token);},ct);return await queries.GetDepartmentAsync(d.Id,ct)??throw AdministrationHandlerRules.MissingDepartment();}}

internal sealed class AssignDepartmentManagerHandler(IAdministrationQueryService queries,IDepartmentRepository departments,IUserRepository users,IAuditLogRepository audits,IUnitOfWork unit,ITransactionExecutor transactions,ICurrentUserContext current,IGuidGenerator guids,TimeProvider time):IRequestHandler<AssignDepartmentManagerCommand,DepartmentDto>
{public async Task<DepartmentDto> Handle(AssignDepartmentManagerCommand c,CancellationToken ct){Department d=await departments.GetByIdAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingDepartment();if(c.ManagerId.HasValue){User manager=await users.GetByIdAsync(c.ManagerId.Value,ct)??throw Invalid();if(!manager.IsActive||manager.Role!=UserRole.Manager||manager.DepartmentId!=d.Id)throw Invalid();}DateTimeOffset now=time.GetUtcNow();Guid actor=AdministrationHandlerRules.Actor(current);await transactions.ExecuteAsync(async token=>{d.AssignManager(c.ManagerId);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"Department",d.Id,"Department.ManagerChanged",null,null,now),token);await unit.SaveChangesAsync(token);},ct);return await queries.GetDepartmentAsync(d.Id,ct)??throw AdministrationHandlerRules.MissingDepartment();}private static ConflictException Invalid()=>new("department.manager_invalid","Geçerli yönetici seçin.");}

internal sealed class CreateAndAssignDepartmentManagerHandler(IAdministrationQueryService queries, IDepartmentRepository departments,
    IUserRepository users, IPasswordService passwords, IAuditLogRepository audits, IUnitOfWork unit,
    ITransactionExecutor transactions, ICurrentUserContext current, IGuidGenerator guids, TimeProvider time)
    : IRequestHandler<CreateAndAssignDepartmentManagerCommand, DepartmentDto>
{
    public async Task<DepartmentDto> Handle(CreateAndAssignDepartmentManagerCommand command, CancellationToken cancellationToken)
    {
        Department department = await departments.GetByIdAsync(command.DepartmentId, cancellationToken)
            ?? throw AdministrationHandlerRules.MissingDepartment();
        if (department.ManagerId.HasValue)
            throw new ConflictException("department.manager_exists", "Bu departmanın zaten bir yöneticisi bulunuyor.");

        string email = AdministrationHandlerRules.Email(command.Email);
        if (await users.EmailExistsAsync(email, null, cancellationToken))
            throw new ConflictException("user.email_exists", "Bu e-posta adresiyle kayıtlı başka bir kullanıcı bulunuyor.");
        PasswordRules.Ensure(command.InitialPassword);

        Guid actor = AdministrationHandlerRules.Actor(current);
        Guid userId = guids.NewGuid();
        DateTimeOffset now = time.GetUtcNow();
        User manager = User.Create(userId, command.FirstName, command.LastName, email, "temporary", UserRole.Manager, department.Id, now);
        manager.ChangePasswordHash(passwords.HashPassword(manager, command.InitialPassword));
        await transactions.ExecuteAsync(async token =>
        {
            await users.AddAsync(manager, token);
            department.AssignManager(manager.Id);
            await audits.AddAsync(AuditLog.Create(guids.NewGuid(), actor, "User", manager.Id, "User.Created", null, null, now), token);
            await audits.AddAsync(AuditLog.Create(guids.NewGuid(), actor, "Department", department.Id, "Department.ManagerAssigned", null, null, now), token);
            await unit.SaveChangesAsync(token);
        }, cancellationToken);

        return await queries.GetDepartmentAsync(department.Id, cancellationToken)
            ?? throw AdministrationHandlerRules.MissingDepartment();
    }
}

internal sealed class DeleteDepartmentHandler(IAdministrationQueryService queries,IDepartmentRepository departments,IAuditLogRepository audits,IUnitOfWork unit,ITransactionExecutor transactions,ICurrentUserContext current,IGuidGenerator guids,TimeProvider time):IRequestHandler<DeleteDepartmentCommand>
{public async Task Handle(DeleteDepartmentCommand c,CancellationToken ct){Department d=await departments.GetByIdAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingDepartment();DepartmentDto dto=await queries.GetDepartmentAsync(c.Id,ct)??throw AdministrationHandlerRules.MissingDepartment();if(dto.ActiveUserCount>0||dto.ActiveRequestCount>0)throw new ConflictException("department.has_dependencies","Aktif kullanıcı veya talep bulunan departman silinemez.");DateTimeOffset now=time.GetUtcNow();Guid actor=AdministrationHandlerRules.Actor(current);await transactions.ExecuteAsync(async token=>{d.AssignManager(null);d.SoftDelete(now,actor);await audits.AddAsync(AuditLog.Create(guids.NewGuid(),actor,"Department",d.Id,"Department.SoftDeleted",null,null,now),token);await unit.SaveChangesAsync(token);},ct);}}
