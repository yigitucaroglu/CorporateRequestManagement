using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.Application.Features.CorporateRequests.Common;

public sealed record CorporateRequestListItemDto(
    Guid Id, string Title, RequestType RequestType, decimal? Amount, RequestStatus Status,
    DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate, DateTimeOffset? SubmittedDate, string RowVersion);

public sealed record CorporateRequestDetailDto(
    Guid Id, string Title, string Description, RequestType RequestType, decimal? Amount,
    RequestStatus Status, Guid DepartmentId, string? DepartmentName, DateTimeOffset CreatedDate,
    DateTimeOffset UpdatedDate, DateTimeOffset? SubmittedDate, DateTimeOffset? CompletedDate, string RowVersion);

public sealed record CorporateRequestListCriteria(
    int PageNumber, int PageSize, RequestStatus? Status, RequestType? RequestType,
    DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo, string? SearchTerm, string SortBy, SortDirection SortDirection);

public static class CorporateRequestSearch
{
    public const int MaxLength = 100;

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class CorporateRequestAuditActions
{
    public const string Created = "CorporateRequest.Created";
    public const string DraftUpdated = "CorporateRequest.DraftUpdated";
    public const string Submitted = "CorporateRequest.Submitted";
    public const string Cancelled = "CorporateRequest.Cancelled";
}

public static class CorporateRequestSortFields
{
    public const string CreatedDate = "createdDate";
    public const string UpdatedDate = "updatedDate";
    public const string Title = "title";
    public const string Amount = "amount";
    public const string Status = "status";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { CreatedDate, UpdatedDate, Title, Amount, Status };
}

public static class RowVersionCodec
{
    public const int ByteLength = 8;
    public static string Encode(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != ByteLength) throw new ArgumentException("RowVersion must contain 8 bytes.", nameof(value));
        return Convert.ToBase64String(value);
    }
    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length == ByteLength; }
        catch (FormatException) { return false; }
    }
}

internal static class CorporateRequestMappings
{
    public static CorporateRequestDetailDto ToDetail(this CorporateRequest request, string? departmentName = null) =>
        new(request.Id, request.Title, request.Description, request.RequestType, request.Amount, request.Status,
            request.DepartmentId, departmentName, request.CreatedDate, request.UpdatedDate, request.SubmittedDate,
            request.CompletedDate, RowVersionCodec.Encode(request.RowVersion));
}
