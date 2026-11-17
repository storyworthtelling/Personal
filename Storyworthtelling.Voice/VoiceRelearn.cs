namespace Storyworthtelling.Voice;

/// <summary>One book the voice is learned from: the project it belongs to and its text.</summary>
public sealed record VoiceSource(Guid ProjectId, string Text);

/// <summary>
/// Learns the voice again from the books that remain (audit W10.6). <see cref="VoiceProfile.Discard"/> can take a
/// book's anchors out but not what it taught the register and habits, and a profile read in an older shape has
/// nothing usable; both mark <see cref="VoiceProfile.NeedsRelearn"/>, and this is what acts on the mark: every
/// remaining book refined, in order, from nothing, each through its <see cref="VoiceSample"/>.
/// </summary>
public static class VoiceRelearn
{
    public static async Task<VoiceProfile> RebuildAsync(
        IVoiceClient client, IReadOnlyList<VoiceSource> books, CancellationToken cancellationToken)
    {
        var profile = VoiceProfile.Empty;
        foreach (var book in books)
        {
            profile = await client.RefineAsync(profile, VoiceSample.Of(book.Text), book.ProjectId, cancellationToken);
        }

        return profile with { NeedsRelearn = false };
    }
}
