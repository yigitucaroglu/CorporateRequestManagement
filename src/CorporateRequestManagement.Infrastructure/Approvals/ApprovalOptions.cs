using CorporateRequestManagement.Application.Abstractions.Approvals;
using Microsoft.Extensions.Options;

namespace CorporateRequestManagement.Infrastructure.Approvals;

public sealed class ApprovalOptions
{
    public const string SectionName = "Approval";
    public decimal Threshold { get; set; } = 10_000m;
}

internal sealed class ApprovalThresholdProvider(IOptions<ApprovalOptions> options) : IApprovalThresholdProvider
{
    public decimal Threshold => options.Value.Threshold;
}
