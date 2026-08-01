namespace CorporateRequestManagement.Domain.Exceptions;

public sealed class DomainRuleException : Exception
{
    public DomainRuleException(string code, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
    }

    public string Code { get; }
}
