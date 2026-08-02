namespace CorporateRequestManagement.Application.Common.Exceptions;

public sealed class NotFoundException : ApplicationLayerException
{
    public NotFoundException(string code, string message)
        : base(code, message)
    {
    }
}
