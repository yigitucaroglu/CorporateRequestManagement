using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Features.CorporateRequests.Cancel;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Application.Features.CorporateRequests.Create;
using CorporateRequestManagement.Application.Features.CorporateRequests.GetMine;
using CorporateRequestManagement.Application.Features.CorporateRequests.Submit;
using CorporateRequestManagement.Application.Features.CorporateRequests.UpdateDraft;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;

namespace CorporateRequestManagement.UnitTests.Application.CorporateRequests;

public sealed class CorporateRequestValidatorTests
{
    private static readonly string ValidVersion = Convert.ToBase64String(new byte[RowVersionCodec.ByteLength]);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task Create_WhenTitleMissing_ShouldFail(string? title)
    {
        var command = new CreateCorporateRequestCommand(title!, "Description", RequestType.Leave, null);
        Assert.False((await ((IValidator<CreateCorporateRequestCommand>)new CreateCorporateRequestCommandValidator()).ValidateAsync(command)).IsValid);
    }

    [Fact]
    public async Task Create_WhenFieldsAtBoundaries_ShouldPass()
    {
        var command = new CreateCorporateRequestCommand(new string('t', CorporateRequest.TitleMaxLength),
            new string('d', CorporateRequest.DescriptionMaxLength), RequestType.Expense, 9999999999999999.99m);
        Assert.True((await ((IValidator<CreateCorporateRequestCommand>)new CreateCorporateRequestCommandValidator()).ValidateAsync(command)).IsValid);
    }

    [Theory]
    [InlineData(RequestType.Expense)] [InlineData(RequestType.Equipment)]
    public async Task Create_WhenAmountRequiredButMissing_ShouldFail(RequestType type)
    {
        var command = new CreateCorporateRequestCommand("Title", "Description", type, null);
        Assert.False((await ((IValidator<CreateCorporateRequestCommand>)new CreateCorporateRequestCommandValidator()).ValidateAsync(command)).IsValid);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("not-base64")] [InlineData("AQID")]
    public async Task Update_WhenRowVersionInvalid_ShouldFail(string? value)
    {
        var command = new UpdateDraftCorporateRequestCommand(Guid.NewGuid(), "Title", "Description", RequestType.Leave, null, value!);
        Assert.False((await ((IValidator<UpdateDraftCorporateRequestCommand>)new UpdateDraftCorporateRequestCommandValidator()).ValidateAsync(command)).IsValid);
    }

    [Fact]
    public async Task Update_WhenRowVersionValid_ShouldPass()
    {
        var command = new UpdateDraftCorporateRequestCommand(Guid.NewGuid(), "Title", "Description", RequestType.Leave, null, ValidVersion);
        Assert.True((await ((IValidator<UpdateDraftCorporateRequestCommand>)new UpdateDraftCorporateRequestCommandValidator()).ValidateAsync(command)).IsValid);
    }

    [Fact]
    public async Task SubmitAndCancel_WhenRowVersionMalformed_ShouldFail()
    {
        Assert.False((await ((IValidator<SubmitCorporateRequestCommand>)new SubmitCorporateRequestCommandValidator()).ValidateAsync(new(Guid.NewGuid(), "bad"))).IsValid);
        Assert.False((await ((IValidator<CancelCorporateRequestCommand>)new CancelCorporateRequestCommandValidator()).ValidateAsync(new(Guid.NewGuid(), "bad"))).IsValid);
    }

    [Fact]
    public async Task List_WhenDefault_ShouldPass()
    {
        Assert.True((await ((IValidator<GetMyCorporateRequestsQuery>)new GetMyCorporateRequestsQueryValidator()).ValidateAsync(new())).IsValid);
    }

    [Theory]
    [InlineData(0, 20)] [InlineData(1, 0)] [InlineData(1, 101)]
    public async Task List_WhenPagingInvalid_ShouldFail(int page, int size)
    {
        Assert.False((await ((IValidator<GetMyCorporateRequestsQuery>)new GetMyCorporateRequestsQueryValidator()).ValidateAsync(new(PageNumber: page, PageSize: size))).IsValid);
    }

    [Fact]
    public async Task List_WhenSortOrDateRangeInvalid_ShouldFail()
    {
        DateTimeOffset now = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
        var query = new GetMyCorporateRequestsQuery(CreatedFrom: now, CreatedTo: now.AddDays(-1), SortBy: "unsupported", SortDirection: (SortDirection)999);
        Assert.False((await ((IValidator<GetMyCorporateRequestsQuery>)new GetMyCorporateRequestsQueryValidator()).ValidateAsync(query)).IsValid);
    }

    [Fact]
    public void RowVersionCodec_ShouldRoundTripAndRejectWrongLength()
    {
        byte[] value = [1, 2, 3, 4, 5, 6, 7, 8];
        string encoded = RowVersionCodec.Encode(value);
        Assert.True(RowVersionCodec.TryDecode(encoded, out byte[] decoded));
        Assert.Equal(value, decoded);
        Assert.False(RowVersionCodec.TryDecode(Convert.ToBase64String([1, 2]), out _));
    }
}
