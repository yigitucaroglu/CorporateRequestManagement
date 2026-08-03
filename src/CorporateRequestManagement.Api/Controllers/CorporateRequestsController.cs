using CorporateRequestManagement.Api.Contracts.CorporateRequests;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.CorporateRequests.Cancel;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Application.Features.CorporateRequests.Create;
using CorporateRequestManagement.Application.Features.CorporateRequests.GetById;
using CorporateRequestManagement.Application.Features.CorporateRequests.GetMine;
using CorporateRequestManagement.Application.Features.CorporateRequests.Submit;
using CorporateRequestManagement.Application.Features.CorporateRequests.UpdateDraft;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Api.Controllers;

[ApiController, Authorize, Route("api/requests")]
public sealed class CorporateRequestsController(ISender sender) : ControllerBase
{
    [HttpPost, ProducesResponseType<CorporateRequestDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateCorporateRequestRequest request, CancellationToken cancellationToken)
    {
        CorporateRequestDetailDto result = await sender.Send(new CreateCorporateRequestCommand(request.Title, request.Description, request.RequestType, request.Amount), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet, ProducesResponseType<PagedResult<CorporateRequestListItemDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<CorporateRequestListItemDto>> GetMine([FromQuery] GetMyCorporateRequestsRequest request, CancellationToken cancellationToken) =>
        sender.Send(new GetMyCorporateRequestsQuery(request.PageNumber, request.PageSize, request.Status, request.RequestType,
            request.CreatedFrom, request.CreatedTo, request.SortBy, request.SortDirection), cancellationToken);

    [HttpGet("{id:guid}"), ProducesResponseType<CorporateRequestDetailDto>(StatusCodes.Status200OK)]
    public Task<CorporateRequestDetailDto> GetById(Guid id, CancellationToken cancellationToken) => sender.Send(new GetCorporateRequestByIdQuery(id), cancellationToken);

    [HttpPut("{id:guid}"), ProducesResponseType<CorporateRequestDetailDto>(StatusCodes.Status200OK)]
    public Task<CorporateRequestDetailDto> Update(Guid id, UpdateDraftCorporateRequestRequest request, CancellationToken cancellationToken) =>
        sender.Send(new UpdateDraftCorporateRequestCommand(id, request.Title, request.Description, request.RequestType, request.Amount, request.RowVersion), cancellationToken);

    [HttpPost("{id:guid}/submit"), ProducesResponseType<CorporateRequestDetailDto>(StatusCodes.Status200OK)]
    public Task<CorporateRequestDetailDto> Submit(Guid id, CorporateRequestMutationRequest request, CancellationToken cancellationToken) =>
        sender.Send(new SubmitCorporateRequestCommand(id, request.RowVersion), cancellationToken);

    [HttpPost("{id:guid}/cancel"), ProducesResponseType<CorporateRequestDetailDto>(StatusCodes.Status200OK)]
    public Task<CorporateRequestDetailDto> Cancel(Guid id, CorporateRequestMutationRequest request, CancellationToken cancellationToken) =>
        sender.Send(new CancelCorporateRequestCommand(id, request.RowVersion), cancellationToken);
}
