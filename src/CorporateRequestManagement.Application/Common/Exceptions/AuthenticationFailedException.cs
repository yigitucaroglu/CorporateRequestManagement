namespace CorporateRequestManagement.Application.Common.Exceptions;

public sealed class AuthenticationFailedException : ApplicationLayerException
{
    public AuthenticationFailedException(string code, string message) : base(code, message) { }
}
