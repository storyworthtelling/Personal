using System.Text.RegularExpressions;
using Constitution.Domain.Diagnostics;
using Constitution.Infrastructure.Household;
using Constitution.Tests.Recipes;
using Storyworthtelling.Household;

namespace Constitution.Tests.HouseholdHead;

public class HouseholdTrailTests
{
    private static readonly Regex Letters = new("[A-Za-z]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public async Task EveryLineIsVocabularyAndNoValue()
    {
        using var rig = new HeadRig();
        using var theirs = new HeadRig();
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.Keeper.Keep(RecipeData.Chili());
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.Drive.Failure = new IOException("offline " + RecipeData.Chili().Title);
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.Drive.Failure = null;
        rig.Drive.Content = theirs.SealedFile(RecipeData.Document([RecipeData.Chili()]), "some-other-passphrase");
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.Drive.Content = "this is not an envelope";
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.HeldPassphrase = string.Empty;
        await rig.Service.SyncAsync(CancellationToken.None);
        rig.HeldPassphrase = HeadRig.Passphrase;
        rig.HasAccount = false;
        await rig.Service.SyncAsync(CancellationToken.None);
        using var sharer = new HeadRig(role: HouseholdFileRole.SharedWith);
        await sharer.Service.SyncAsync(CancellationToken.None);

        var lines = rig.Log.Lines.Concat(sharer.Log.Lines).Distinct().ToList();

        Assert.True(lines.Count >= 7, "Only " + lines.Count + " distinct trail lines were written; the scenario should reach every ending.");
        foreach (var line in lines)
        {
            foreach (var word in Letters.Matches(line).Select(match => match.Value))
            {
                Assert.True(TrailLines.Vocabulary.Contains(word.ToLowerInvariant()), "'" + word + "' is not trail vocabulary, in: " + line);
            }

            Assert.DoesNotContain(RecipeData.Chili().Title, line, StringComparison.Ordinal);
            Assert.DoesNotContain(HeadRig.Passphrase, line, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(HouseholdUnreadableReason.NoPassphrase)]
    [InlineData(HouseholdUnreadableReason.WrongPassphrase)]
    [InlineData(HouseholdUnreadableReason.Damaged)]
    [InlineData(HouseholdUnreadableReason.NewerFormat)]
    public void EveryRefusalPhraseIsVocabulary(HouseholdUnreadableReason reason)
    {
        var phrase = HouseholdSentences.UnreadableTrail(reason);

        foreach (var word in Letters.Matches(phrase).Select(match => match.Value))
        {
            Assert.Contains(word, TrailLines.Vocabulary);
        }
    }
}
