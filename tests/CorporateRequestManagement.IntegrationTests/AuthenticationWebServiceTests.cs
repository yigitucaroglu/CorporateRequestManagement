extern alias webapp;

using AuthenticationContract = webapp::CorporateRequestManagement.Web.Authentication.AuthenticationContract;
using RefreshCoordinator = webapp::CorporateRequestManagement.Web.Authentication.RefreshCoordinator;
using WebUserSession = webapp::CorporateRequestManagement.Web.Authentication.WebUserSession;

namespace CorporateRequestManagement.WebTests;

public sealed class AuthenticationWebServiceTests
{
    [Fact]
    public void Token_models_do_not_reveal_token_material_in_string_representation()
    {
        var session = new WebUserSession { AccessToken = "access-secret", RefreshToken = "refresh-secret" };
        var contract = new AuthenticationContract { AccessToken = "access-secret", RefreshToken = "refresh-secret" };

        Assert.Equal(nameof(WebUserSession), session.ToString());
        Assert.Equal(nameof(AuthenticationContract), contract.ToString());
        Assert.DoesNotContain("secret", session.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", contract.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_refresh_operations_for_same_session_are_serialized_and_lock_is_removed()
    {
        var coordinator = new RefreshCoordinator();
        int active = 0;
        int maximumActive = 0;

        Task<int>[] operations = Enumerable.Range(0, 12).Select(_ => coordinator.ExecuteAsync("opaque-session-id", async () =>
        {
            int current = Interlocked.Increment(ref active);
            maximumActive = Math.Max(maximumActive, current);
            await Task.Yield();
            Interlocked.Decrement(ref active);
            return current;
        })).ToArray();

        await Task.WhenAll(operations);

        Assert.Equal(1, maximumActive);
        Assert.Equal(0, coordinator.ActiveLockCount);
    }
}
