using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// Audit 08 §24: a refused READ_SMS left an empty Mailbox and no line. One line per permission per process: the read is
/// retried on every reload, and a line each time would roll the log.
/// </summary>
public class PermissionDenialsTests
{
    [Fact]
    public void EachPermissionIsSaidOnce()
    {
        var denials = new PermissionDenials();

        Assert.Equal("PERMISSION READ_SMS denied", denials.First("READ_SMS"));
        Assert.Null(denials.First("READ_SMS"));
        Assert.Equal("PERMISSION READ_CALL_LOG denied", denials.First("READ_CALL_LOG"));
    }
}
