namespace CorporateRequestManagement.Application.Common.Exceptions;

public sealed class ApplicationValidationException : ApplicationLayerException
{
    private readonly IReadOnlyDictionary<string, string[]> errors;

    public ApplicationValidationException(string code, string message)
        : base(code, message)
    {
        errors = new Dictionary<string, string[]>();
    }

    public ApplicationValidationException(string code, string message, IReadOnlyDictionary<string, string[]>? errors)
        : base(code, message)
    {
        this.errors = (errors ?? new Dictionary<string, string[]>())
            .ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string[]> Errors => new System.Collections.ObjectModel.ReadOnlyDictionary<string, string[]>(
        errors.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal));
}
