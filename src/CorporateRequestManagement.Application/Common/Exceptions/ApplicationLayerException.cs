namespace CorporateRequestManagement.Application.Common.Exceptions;

public abstract class ApplicationLayerException : Exception
{
    protected ApplicationLayerException(string code, string message)
        : base(ValidateMessage(message))
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code.Trim();
    }

    public string Code { get; }

    private static string ValidateMessage(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return message;
    }
}
