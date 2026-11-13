using Storyworthtelling.Folds;
using Storyworthtelling.Maui.State;
using Xunit;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// Audit 11 §15: the MAUI heads' button handlers are <c>async void</c> with no try, so a database or navigation failure
/// crashed the app — and inside an undo window took the undo with it. <see cref="GuardedWork"/> awaits the work and
/// folds any failure into the dog-ear, the way Flyleaf and Plate do by hand.
/// </summary>
public class GuardedWorkTests
{
    [Fact]
    public async Task WorkThatSucceedsLeavesNoNoticeAndClearsAnOldOne()
    {
        var folds = new DogEarState();
        folds.SetNotice("remove-account", new DogEarNotice("Old", "old", DogEarHue.Problem));

        var ran = await GuardedWork.Run(folds, "remove-account", "Could not remove the account", () => Task.CompletedTask);

        Assert.True(ran);
        Assert.Empty(folds.Notices);
    }

    [Fact]
    public async Task AFailureBecomesANoticeInPlainWordsAndIsNotThrown()
    {
        var folds = new DogEarState();
        Exception? logged = null;

        var ran = await GuardedWork.Run(folds, "remove-account", "Could not remove the account",
            () => throw new IOException("write failed: ENOSPC (No space left on device)"), failure => logged = failure);

        Assert.False(ran);
        var notice = Assert.Single(folds.Notices);
        Assert.Equal("Could not remove the account", notice.Headline);
        Assert.Equal("The phone is out of storage space.", notice.Detail);
        Assert.Equal(DogEarHue.Problem, notice.Hue);
        Assert.IsType<IOException>(logged);
    }

    [Fact]
    public async Task CancellingIsNotAFailure()
    {
        var folds = new DogEarState();

        var ran = await GuardedWork.Run(folds, "k", "Could not", () => throw new OperationCanceledException());

        Assert.False(ran);
        Assert.Empty(folds.Notices);
    }
}
