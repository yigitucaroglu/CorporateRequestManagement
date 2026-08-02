namespace CorporateRequestManagement.Web.Configuration;
public sealed class BackendApiOptions
{
    public const string SectionName = "BackendApi";
    public string BaseUrl { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;
}
