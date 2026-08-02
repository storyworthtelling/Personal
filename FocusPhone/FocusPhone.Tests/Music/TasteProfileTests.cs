using FocusPhone.Domain.Music;
using Xunit;

namespace FocusPhone.Tests.Music;

public class TasteProfileTests
{
    [Fact]
    public void Empty_IsEmpty()
    {
        Assert.True(TasteProfile.Empty.IsEmpty);
    }

    [Fact]
    public void Populated_IsNotEmpty()
    {
        var profile = new TasteProfile("Leans jazz late at night.", ["bebop"], [], [], ["Jazz"], ["Bill Evans"], ["nights"], false, 12);
        Assert.False(profile.IsEmpty);
    }

    [Fact]
    public void BlankSummary_CountsAsEmptyEvenWithSessions()
    {
        var profile = TasteProfile.Empty with { SessionsAnalyzed = 5 };
        Assert.True(profile.IsEmpty);
    }

    [Fact]
    public void ZeroSessions_CountsAsEmptyEvenWithSummary()
    {
        var profile = TasteProfile.Empty with { Summary = "Something" };
        Assert.True(profile.IsEmpty);
    }
}
