using System.Text.Json;
using CorporateRequestManagement.Web.Api;

namespace CorporateRequestManagement.Web.Models;

public sealed record AdminUsersPage(PagedContract<AdminUserContract> Result, ManagementListQuery Query, IReadOnlyList<DepartmentContract> Departments);
public sealed record AdminUserFormPage(AdminUserContract? User, IReadOnlyList<DepartmentContract> Departments,
    Guid? SelectedDepartmentId = null, Guid? ReturnToDepartment = null);
public sealed record AdminDepartmentsPage(PagedContract<DepartmentContract> Result, ManagementListQuery Query);
public sealed record AdminDepartmentDetailsPage(DepartmentContract Department, IReadOnlyList<EligibleManagerContract> EligibleManagers);
public sealed class CreateDepartmentManagerPage
{
    public required DepartmentContract Department { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}
public sealed record ScopedRequestsPage(PagedContract<ScopedRequestContract> Result, ManagementListQuery Query, IReadOnlyList<DepartmentContract> Departments, bool IsAdmin);
public sealed record AuditLogsPage(PagedContract<AuditLogContract> Result, ManagementListQuery Query, IReadOnlyList<AuditActorContract> Actors, bool ActorFilterAvailable);
public sealed record AuditDetailsPage(AuditLogContract Audit, IReadOnlyList<AuditChangeItem> OldValues, IReadOnlyList<AuditChangeItem> NewValues);
public sealed record AuditChangeItem(string Label, string Value);
public sealed record NotFoundPage(string Title, string Message, string ReturnText, string Controller, string Action);

public static class Phase7Display
{
    private static readonly Dictionary<string, string> EntityLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["User"] = "Kullanıcı", ["Department"] = "Departman", ["CorporateRequest"] = "Talep",
        ["RequestApproval"] = "Onay Süreci", ["RefreshToken"] = "Oturum Güvenliği", ["AuditLog"] = "İşlem Kaydı",
        ["Authentication"] = "Oturum Güvenliği"
    };

    private static readonly Dictionary<string, string> ActionLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Authentication.LoginSucceeded"] = "Başarılı giriş", ["Authentication.RefreshTokenRotated"] = "Oturum yenilendi",
        ["Authentication.RefreshTokenRevoked"] = "Oturum sonlandırıldı", ["User.Created"] = "Kullanıcı oluşturuldu",
        ["User.Updated"] = "Kullanıcı bilgileri güncellendi", ["User.PasswordReset"] = "Kullanıcı parolası sıfırlandı",
        ["User.PasswordChanged"] = "Parola değiştirildi", ["User.SoftDeleted"] = "Kullanıcı silindi",
        ["Department.Created"] = "Departman oluşturuldu", ["Department.Updated"] = "Departman güncellendi",
        ["Department.ManagerAssigned"] = "Departman yöneticisi atandı", ["Department.ManagerChanged"] = "Departman yöneticisi değiştirildi",
        ["Department.SoftDeleted"] = "Departman silindi", ["CorporateRequest.Created"] = "Talep oluşturuldu",
        ["CorporateRequest.DraftUpdated"] = "Talep güncellendi", ["CorporateRequest.Submitted"] = "Talep onaya gönderildi",
        ["CorporateRequest.ManagerApproved"] = "Yönetici talebi onayladı", ["CorporateRequest.ManagerRejected"] = "Yönetici talebi reddetti",
        ["CorporateRequest.AdminApproved"] = "Talep son onaydan geçti", ["CorporateRequest.AdminRejected"] = "Talep son onayda reddedildi",
        ["CorporateRequest.Cancelled"] = "Talep iptal edildi", ["CorporateRequest.SoftDeleted"] = "Talep silindi"
    };

    private static readonly Dictionary<string, string> PropertyLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FirstName"] = "Ad", ["LastName"] = "Soyad", ["Email"] = "E-posta", ["Role"] = "Rol",
        ["DepartmentName"] = "Departman", ["IsActive"] = "Hesap durumu", ["Name"] = "Ad",
        ["Title"] = "Başlık", ["Description"] = "Açıklama", ["Status"] = "Durum", ["Amount"] = "Tutar",
        ["RequestType"] = "Talep türü", ["ManagerName"] = "Yönetici"
    };

    public static string Entity(string? value) => value is not null && EntityLabels.TryGetValue(value, out string? label) ? label : "Sistem Kaydı";
    public static string Action(string? value) => value is not null && ActionLabels.TryGetValue(value, out string? label) ? label : "Sistem işlemi";
    public static string Initials(string firstName, string lastName) => $"{firstName.FirstOrDefault()}{lastName.FirstOrDefault()}".ToUpperInvariant();
    public static string Initials(string fullName)
    {
        string[] parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch { 0 => "S", 1 => parts[0][..1].ToUpperInvariant(), _ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant() };
    }
    public static string Active(bool active) => active ? "Aktif" : "Pasif";

    public static IReadOnlyList<AuditChangeItem> Changes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
            return document.RootElement.EnumerateObject()
                .Where(property => PropertyLabels.ContainsKey(property.Name))
                .Select(property => new AuditChangeItem(PropertyLabels[property.Name], SafeValue(property.Value)))
                .ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string SafeValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "Evet", JsonValueKind.False => "Hayır", JsonValueKind.Null => "Belirtilmedi",
        JsonValueKind.String => value.GetString() ?? "Belirtilmedi", JsonValueKind.Number => value.GetRawText(), _ => "Güncellendi"
    };
}
