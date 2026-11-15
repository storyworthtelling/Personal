namespace Storyworthtelling.Diagnostics.Tests;

// Log loop: the tap watch named an editable text field by its Text, so whatever had been typed into it (a
// recipe, a search, in principle a password) became the control's name in a tap line; and a tap that only
// placed the cursor changed nothing on screen, so it was judged dead after the window. A text field is named
// by its hint, then its id, else "text field", and a tap on one is never a dead candidate.
public class TapWatchTextEntryTests
{
    private long now;
    private readonly List<TapVerdict> reported = [];

    [Theory]
    [InlineData("Recipe title", "recipe_title", "Recipe title #recipe_title")]
    [InlineData("Recipe title", null, "Recipe title")]
    [InlineData(null, "recipe_title", "#recipe_title")]
    [InlineData("", null, "text field")]
    public void ATextFieldIsNamedByItsHintNeverByWhatWasTyped(string? hint, string? id, string expected)
    {
        Assert.Equal(expected, TapWatchCore.TextEntryName(hint, id));
    }

    [Fact]
    public void ATextFieldInAListIsStillNotNamedByItsHint()
    {
        Assert.Equal("list row #note", TapWatchCore.TextEntryName("Invented hint", "note", inList: true));
    }

    [Fact]
    public void ATapThatOnlyMovesTheCursorIsNeverDead()
    {
        var core = new TapWatchCore(() => now, reported.Add);
        core.ScreenShown("Recipe");
        var name = TapWatchCore.TextEntryName("Recipe title", "recipe_title");

        core.Touched(name, new TapPoint(40, 40), enabled: true, isControl: true, textEntry: true);
        now = 5_000;
        core.Tick();

        Assert.DoesNotContain(reported, verdict => verdict.Kind == TapVerdictKind.Dead);

        // The tap is still in the context a later verdict carries, under the hint, not the typed words.
        core.Touched("Save", new TapPoint(200, 200), enabled: true, isControl: true, textEntry: false);
        now = 10_000;
        core.Tick();
        var dead = Assert.Single(reported);
        Assert.Equal("Save", dead.Control);
        Assert.Contains(dead.Recent, tap => tap.Control == "Recipe title #recipe_title");
    }

    [Fact]
    public void AnOrdinaryControlStillGoesDead()
    {
        var core = new TapWatchCore(() => now, reported.Add);
        core.ScreenShown("Recipe");

        core.Touched("Save", new TapPoint(40, 40), enabled: true, isControl: true, textEntry: false);
        now = 5_000;
        core.Tick();

        Assert.Equal(TapVerdictKind.Dead, Assert.Single(reported).Kind);
    }
}
