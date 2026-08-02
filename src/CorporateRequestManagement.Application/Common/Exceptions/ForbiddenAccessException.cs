namespace CorporateRequestManagement.Application.Common.Exceptions;

public sealed class ForbiddenAccessException : ApplicationLayerException
{
    public ForbiddenAccessException(string code, string message)
        : base(code, message)
    {
    }
}
