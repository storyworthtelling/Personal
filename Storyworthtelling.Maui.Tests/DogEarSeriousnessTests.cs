using System.Text.RegularExpressions;
using Storyworthtelling.Maui.State;
using Storyworthtelling.Folds;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// D291 (Pulp P12, mockup 12) in the kit: the corner takes the colour of the MOST SERIOUS waiting
/// notice, not the newest, while the Waiting page still runs newest first. Same order as the native
/// <c>Storyworthtelling.Folds.NoticeSeriousness</c>, so the fold is one component in both dialects.
/// </summary>
public class DogEarSeriousnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 7, 30, 0, TimeSpan.Zero);

    private static DateTimeOffset Day(int day) => new(2026, 9, day, 12, 0, 0, TimeSpan.Zero);

    private static DogEarNotice Notice(string headline, DogEarHue hue, int day) =>
        new(headline, "detail", hue, At: Day(day));

    [Fact]
    public void TheCornerTakesAnOlderProblemOverANewerReference()
    {
        var state = new DogEarState();
        state.SetNotice("key", Notice("The availability key stopped working", DogEarHue.Problem, 1));
        state.SetNotice("landing", Notice("Lanternbox opened on its home page", DogEarHue.Reference, 5));

        var fold = state.Fold(Now)!;

        Assert.Equal(DogEarHue.Problem, fold.Hue);
        Assert.Equal("The availability key stopped working", fold.Headline);
    }

    [Fact]
    public void TheWaitingListStillRunsNewestFirst()
    {
        var state = new DogEarState();
        state.SetNotice("key", Notice("Older problem", DogEarHue.Problem, 1));
        state.SetNotice("landing", Notice("Newer reference", DogEarHue.Reference, 5));

        Assert.Equal(["Newer reference", "Older problem"], state.Notices.Select(n => n.Headline));
    }

    [Fact]
    public void ATieGoesToTheNewest()
    {
        var state = new DogEarState();
        state.SetNotice("a", Notice("Older caution", DogEarHue.Caution, 1));
        state.SetNotice("b", Notice("Newer caution", DogEarHue.Caution, 4));
        state.SetNotice("c", Notice("Newest progress", DogEarHue.Progress, 5));

        Assert.Equal("Newer caution", state.Fold(Now)!.Headline);
    }

    [Fact]
    public void AnUndoStillOutranksAProblemAndTheProblemComesBack()
    {
        var state = new DogEarState();
        state.SetNotice("key", Notice("Key rejected", DogEarHue.Problem, 1));
        state.SetNotice("trial", Notice("Trial", DogEarHue.Reference, 5));

        state.BeginUndo("Removed 1 title", () => { }, Now, TimeSpan.FromSeconds(6));

        var during = state.Fold(Now)!;
        Assert.True(during.ShowsBackArrow);
        Assert.Equal(DogEarHue.Action, during.Hue);

        var after = state.Fold(Now + TimeSpan.FromSeconds(7))!;
        Assert.False(after.ShowsBackArrow);
        Assert.Equal("Key rejected", after.Headline);
    }

    [Fact]
    public void EveryHueIsRankedExactlyOnce()
    {
        var hues = Enum.GetValues<DogEarHue>();
        Assert.NotEmpty(hues);
        Assert.Equal(hues.Order(), DogEarState.MostSeriousFirst.Order());
    }

    /// <summary>The kit's order is the native one. Both are in Storyworthtelling.Folds now, so they are compared directly.</summary>
    [Fact]
    public void TheOrderIsTheNativeOne()
    {
        Assert.Equal(
            NoticeSeriousness.MostSeriousFirst.Select(role => role.ToString()),
            DogEarState.MostSeriousFirst.Select(hue => hue.ToString()));
    }

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
