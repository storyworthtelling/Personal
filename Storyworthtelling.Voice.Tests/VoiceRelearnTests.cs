using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// Learning the voice again from the books that remain (audit W10.6): what a discard marks, and what a missed refine
/// in an older shape marks, is acted on rather than only flagged.
/// </summary>
public class VoiceRelearnTests
{
    private sealed class CountingClient : IVoiceClient
    {
        public List<(string Text, Guid Project)> Refined { get; } = [];

        public Task<VoiceProfile> RefineAsync(VoiceProfile existing, string documentText, Guid projectId, CancellationToken cancellationToken)
        {
            Refined.Add((documentText, projectId));
            return Task.FromResult(existing with
            {
                Register = "learned",
                Examples = [.. existing.Examples, new VoiceExample("from " + projectId.ToString("N")[..4], projectId)],
                DocumentsAnalyzed = existing.DocumentsAnalyzed + 1,
                NeedsRelearn = false,
            });
        }

        public Task<string> ComposeAsync(VoiceProfile profile, MessageRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
    }

    [Fact]
    public async Task EveryRemainingBookIsReadAgainFromNothingInOrder()
    {
        var client = new CountingClient();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var profile = await VoiceRelearn.RebuildAsync(client, [new(first, "one book"), new(second, "another book")], CancellationToken.None);

        Assert.Equal([first, second], client.Refined.Select(refine => refine.Project));
        Assert.Equal(2, profile.DocumentsAnalyzed);
        Assert.False(profile.NeedsRelearn);
        Assert.Equal(["one book", "another book"], client.Refined.Select(refine => refine.Text));
    }

    [Fact]
    public async Task ABookIsReadThroughItsSample()
    {
        var client = new CountingClient();
        var book = string.Join(" ", Enumerable.Repeat("word", 20000));

        await VoiceRelearn.RebuildAsync(client, [new(Guid.NewGuid(), book)], CancellationToken.None);

        Assert.Equal(VoiceSample.Of(book), client.Refined.Single().Text);
    }

    [Fact]
    public async Task NoBooksLeftIsNoVoice()
    {
        var profile = await VoiceRelearn.RebuildAsync(new CountingClient(), [], CancellationToken.None);

        Assert.True(profile.IsEmpty);
    }
}
