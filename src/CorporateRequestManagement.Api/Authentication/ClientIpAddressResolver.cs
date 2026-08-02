using CorporateRequestManagement.Domain.Entities;

namespace CorporateRequestManagement.Api.Authentication;

internal static class ClientIpAddressResolver
{
    public static string Resolve(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        string value = address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4().ToString() : address?.ToString() ?? "unknown";
        return value.Length <= RefreshToken.IpAddressMaxLength ? value : "unknown";
    }
}
