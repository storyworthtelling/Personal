using Index.Domain.Connect;
using Xunit;

namespace Index.Tests.Connect;

public class ConnectBrowserPolicyTests
{
    [Fact]
    public void FocusPhoneRestrictedViewerIsDetected()
    {
        Assert.True(ConnectBrowserPolicy.IsRestrictedViewer(
            ConnectBrowserPolicy.FocusPhoneRestrictedBrowserPackage));
    }

    [Theory]
    [InlineData("com.android.chrome")]
    [InlineData("org.mozilla.firefox")]
    [InlineData("com.brave.browser")]
    [InlineData("com.microsoft.emmx")]
    public void RealBrowsersAreNotRestrictedViewers(string package)
    {
        Assert.False(ConnectBrowserPolicy.IsRestrictedViewer(package));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnresolvableDefaultIsNotTreatedAsRestricted(string? package)
    {
        // Only a positive match on the restricted viewer blocks linking. An
        // unresolvable default (package visibility withheld it) must never
        // falsely block a browser-capable phone from opening Plaid Link.
        Assert.False(ConnectBrowserPolicy.IsRestrictedViewer(package));
    }
}
