using Constitution.Domain.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// C-9.2.2 — no faith content anywhere a seat can speak (brief, Design rules). A word-list guard over
/// every prompt, fact and fallback the builders can produce across a sweep of invented inputs, and
/// proof the guard can fail: a planted word, assembled at run time so this file does not itself
/// contain it, must be caught, or a clean pass would mean nothing.
/// </summary>
public class NoFaithContentTests
{
    [Fact]
    public async Task NoFaithContent()
    {
        var requests = SeatSweep.Requests();
        var texts = await SeatSweep.TextsAsync(requests);

        var found = texts.SelectMany(FaithWords.Find).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(found.Count == 0, "Faith words reached a prompt, a fact or a fallback: " + string.Join(", ", found));
    }

    [Fact]
    public async Task TheSweepIsBigEnoughToMeanSomething()
    {
        var requests = SeatSweep.Requests();
        var texts = await SeatSweep.TextsAsync(requests);

        Assert.True(requests.Count > 150, $"only {requests.Count} requests swept");
        Assert.All(Enum.GetValues<VoiceSeat>(), seat => Assert.Contains(requests, request => request.Seat == seat));
        Assert.True(texts.Count > requests.Count * 3);
    }

    [Fact]
    public async Task TheGuardFiresOnAPlantedWord()
    {
        // Built at run time so the source never holds the word being planted.
        var planted = string.Concat("sab", "bath");
        var requests = SeatSweep.Requests(extraDish: $"{planted} stew");
        var texts = await SeatSweep.TextsAsync(requests);

        var found = texts.SelectMany(FaithWords.Find).ToList();
        Assert.Contains(planted, found, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ThePlantedWordReachesThePromptNotJustTheFacts()
    {
        var planted = string.Concat("pra", "yer");
        var request = SeatFacts.RestDay(new RestDayFacts(DinnerPlan.At($"{planted} bread")));
        var texts = await SeatSweep.TextsAsync([request]);

        // The last texts are what the model is sent; the guard sees them there too.
        Assert.Contains(planted, FaithWords.Find(texts[^(VoicedOccasions.All.Count + 1)]), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("lentil soup tonight")]
    [InlineData("Strength at 5:30. Lentil soup tonight.")]
    [InlineData("Soup weather is here. Friday is somebody else's kitchen.")]
    [InlineData("Sunday walk, then a rest day")]
    public void TheGuardLeavesPlainWordsAlone(string text)
    {
        Assert.Empty(FaithWords.Find(text));
    }

    [Theory]
    [InlineData("Church supper", "church")]
    [InlineData("A quiet PRAYER", "prayer")]
    [InlineData("Say grace, then pray.", "pray")]
    [InlineData("Lent begins", "lent")]
    public void TheGuardFindsWholeWordsWhateverTheCase(string text, string word)
    {
        Assert.Contains(word, FaithWords.Find(text), StringComparer.OrdinalIgnoreCase);
    }
}
