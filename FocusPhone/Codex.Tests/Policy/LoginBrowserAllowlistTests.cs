using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

public class LoginBrowserAllowlistTests
{
    // Index connects a bank by opening Plaid's Hosted Link in the login browser. In Admin mode that
    // browser runs under URLBlocklist "*", so the Hosted Link page itself is refused unless its host is
    // on the allowlist. The first hop is therefore the one that has to be reachable, before any bank page.
    [Theory]
    [InlineData("hosted.plaid.com")]
    [InlineData("cdn.plaid.com")]
    public void PlaidHostedLink_IsReachableFromTheLoginBrowser(string host)
    {
        var allowed = PolicyContext.LoginBrowserUrlAllowlist;

        Assert.Contains(allowed, entry => host == entry || host.EndsWith($".{entry}", StringComparison.Ordinal));
    }
}
