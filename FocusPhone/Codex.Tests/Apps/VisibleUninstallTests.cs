using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

// The system uninstaller, started for a result, answers with an activity result code: OK when the app went,
// CANCELED when the owner backed out, anything else when it refused. Each settles the uninstall's key action.
public class VisibleUninstallTests
{
    [Fact]
    public void OkMeansTheAppWasRemoved()
    {
        var verdict = VisibleUninstall.Read(VisibleUninstall.ResultOk);

        Assert.True(verdict.Succeeded);
        Assert.Null(verdict.Reason);
        Assert.False(verdict.ShowsProblem);
    }

    [Fact]
    public void CanceledIsAFailureWithAShortReasonButNoProblemNotice()
    {
        var verdict = VisibleUninstall.Read(VisibleUninstall.ResultCanceled);

        Assert.False(verdict.Succeeded);
        Assert.Equal("cancelled", verdict.Reason);
        Assert.False(verdict.ShowsProblem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-7)]
    public void AnyOtherCodeIsARefusal(int code)
    {
        var verdict = VisibleUninstall.Read(code);

        Assert.False(verdict.Succeeded);
        Assert.Equal("refused", verdict.Reason);
        Assert.True(verdict.ShowsProblem);
    }

    [Fact]
    public void NoUninstallerIsItsOwnReason()
    {
        var verdict = VisibleUninstall.Unavailable;

        Assert.False(verdict.Succeeded);
        Assert.Equal("no uninstaller", verdict.Reason);
        Assert.True(verdict.ShowsProblem);
    }

    [Fact]
    public void TheCodesAreAndroidsOwn()
    {
        Assert.Equal(-1, VisibleUninstall.ResultOk);
        Assert.Equal(0, VisibleUninstall.ResultCanceled);
    }
}
