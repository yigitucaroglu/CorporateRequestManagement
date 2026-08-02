namespace CorporateRequestManagement.Application.Common.Exceptions;

public sealed class ConflictException : ApplicationLayerException
{
    public ConflictException(string code, string message)
        : base(code, message)
    {
    }
}
