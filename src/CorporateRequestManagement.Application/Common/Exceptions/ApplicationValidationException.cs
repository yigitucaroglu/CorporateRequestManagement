namespace CorporateRequestManagement.Application.Common.Exceptions;

public sealed class ApplicationValidationException : ApplicationLayerException
{
    public ApplicationValidationException(string code, string message)
        : base(code, message)
    {
    }
}
