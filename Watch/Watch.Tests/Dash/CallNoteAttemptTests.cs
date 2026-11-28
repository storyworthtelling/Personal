using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// The note itself (W-5.1.5, A13): dictated on the wrist, kept by Post where it keeps call notes (B1, B2), always on a
/// call Post named. The watch reads no number of its own and sends none: B1's contract can carry one, and the wrist
/// never uses it. The note carries the moment it was written, so one told again after a lost answer is kept once.
/// </summary>
public class CallNoteAttemptTests
{
    private static readonly DashCall Robin = (DashCall)Messages["call"];

    [Fact]
    public void ANoteGoesOnTheCallPostNamed()
    {
        var note = CallNoteAttempt.For(Robin, " Robin can take Juniper on Thursday. ", T0.AddMinutes(1)).Message;

        Assert.Equal(CallId, note.Call);
        Assert.Equal("Robin can take Juniper on Thursday.", note.Text);
        Assert.Equal(T0.AddMinutes(1), note.At);
        Assert.Equal(Watch.Domain.WatchIdentity.DashCallNotePath, note.Path);
    }

    [Fact]
    public void ANoteNeverNamesANumber()
    {
        Assert.Null(CallNoteAttempt.For(Robin, "Call back about the dentist.", T0).Message.Number);
    }

    [Fact]
    public void NoCallIsNoNote()
    {
        // Post had no call that ended then: there is nothing for the note to go on.
        Assert.Throws<ArgumentException>(() => CallNoteAttempt.For((DashCall)Messages["no call"], "Hi", T0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ANoteNeedsWords(string? words)
    {
        Assert.Equal(SendCheck.Empty, CallNoteAttempt.Check(words));
        Assert.Throws<ArgumentException>(() => CallNoteAttempt.For(Robin, words!, T0));
    }

    [Fact]
    public void ANoteIsAtMostWhatOneMessageCarries()
    {
        Assert.Equal(SendCheck.Fits, CallNoteAttempt.Check(new string('e', DashLimits.SendText)));
        Assert.Equal(SendCheck.TooLong, CallNoteAttempt.Check(new string('e', DashLimits.SendText + 1)));
    }

    [Fact]
    public void KeptAgainIsTheSameNote()
    {
        // Keep after "Your phone isn’t in reach." goes again as the same note, so Post keeps it once if the first arrived.
        var first = CallNoteAttempt.For(Robin, "Robin can take Juniper on Thursday.", T0);

        Assert.Same(first, CallNoteAttempt.Keep(first, Robin, "Robin can take Juniper on Thursday. ", T0.AddMinutes(3)));
    }

    [Fact]
    public void NewWordsAreANewNote()
    {
        var first = CallNoteAttempt.For(Robin, "Robin can take Juniper on Thursday.", T0);

        var second = CallNoteAttempt.Keep(first, Robin, "Robin can take Juniper on Friday.", T0.AddMinutes(3));

        Assert.Equal(T0.AddMinutes(3), second.At);
        Assert.Equal("Robin can take Juniper on Friday.", second.Text);
        Assert.Equal(CallNoteAttempt.For(Robin, "Hi", T0), CallNoteAttempt.Keep(null, Robin, "Hi", T0));
    }
}
