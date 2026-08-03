namespace CorporateRequestManagement.Application.Abstractions.Approvals;

public interface IApprovalThresholdProvider
{
    decimal Threshold { get; }
}
