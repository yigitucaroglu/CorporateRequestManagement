using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.Api.Contracts.CorporateRequests;

public sealed record CreateCorporateRequestRequest(string Title, string Description, RequestType RequestType, decimal? Amount);
public sealed record UpdateDraftCorporateRequestRequest(string Title, string Description, RequestType RequestType, decimal? Amount, string RowVersion);
public sealed record CorporateRequestMutationRequest(string RowVersion);
public sealed record GetMyCorporateRequestsRequest(int PageNumber = PageRequest.DefaultPage, int PageSize = PageRequest.DefaultPageSize,
    RequestStatus? Status = null, RequestType? RequestType = null, DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null, string? SearchTerm = null, string SortBy = CorporateRequestSortFields.CreatedDate,
    SortDirection SortDirection = SortDirection.Descending);
