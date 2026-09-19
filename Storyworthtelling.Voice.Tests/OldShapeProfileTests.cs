using Storyworthtelling.Voice;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// The refit changes what is written to the owner's Drive, and there is already a file there.
///
/// <para>The bug this reintroduces is treating a profile in the old shape as <b>no profile</b>. It
/// is what happens by default, twice over: the old document has no <c>register</c>, so the record
/// deserialises with an empty one and reads as empty; and its <c>examples</c> are bare strings where
/// the new record wants objects, so the parse throws and the store's own catch returns
/// <see cref="VoiceProfile.Empty"/>. Either way every app in the suite quietly drops to neutral copy
/// and nothing anywhere says why — the voice simply stops sounding like the owner.</para>
///
/// <para>So "never learned" and "learned in a shape this version cannot read" are kept apart. The
/// first is the ordinary first-run state. The second is a <b>relearn</b>: the old profile is
/// discarded rather than migrated, because a paragraph of summary prose genuinely cannot be turned
/// into a register descriptor without asking a model, and a wrong guess at the owner's voice is
/// worse than a fresh start. The next ingestion rebuilds it.</para>
/// </summary>
public class OldShapeProfileTests
{
    /// <summary>The file as it actually sits on Drive today, written by the shipped app.</summary>
    private const string AsWrittenBeforeTheRefit = """
        {
          "Summary": "Wry, warm, economical. Prefers the concrete noun to the abstract one.",
          "Traits": [ "wry", "warm", "economical" ],
          "Examples": [ "the tide was out", "a sky the colour of an old coin" ],
          "Avoid": [ "corporate boilerplate", "exclamation marks" ],
          "SignatureMoves": [ "em-dash asides", "short declarative openers" ],
          "DocumentsAnalyzed": 4
        }
        """;

    [Fact]
    public async Task AProfileInTheOldShapeIsNotMistakenForNoProfile()
    {
        var loaded = await Load(AsWrittenBeforeTheRefit);

        Assert.True(loaded.NeedsRelearn,
            "The profile already on the owner's Drive read as 'nothing learned yet', so every app "
            + "silently fell back to neutral copy with nothing to explain why.");
    }

    /// <summary>
    /// It is still empty, though — there is no voice this version can write in. The two states are
    /// separate answers to separate questions: what to print now, and whether to relearn.
    /// </summary>
    [Fact]
    public async Task AnOldShapeProfileStillPrintsNeutralCopy()
    {
        var loaded = await Load(AsWrittenBeforeTheRefit);

        Assert.True(loaded.IsEmpty);
    }

    [Fact]
    public async Task AnAbsentFileIsNotARelearn()
    {
        var loaded = await Load(string.Empty);

        Assert.True(loaded.IsEmpty);
        Assert.False(loaded.NeedsRelearn,
            "A first run has nothing to relearn — it has never learned anything.");
    }

    /// <summary>A file nobody can parse is not a relearn either: there is nothing to say was lost.</summary>
    [Fact]
    public async Task AGarbledFileIsNotARelearn()
    {
        var loaded = await Load("{ this is not json");

        Assert.True(loaded.IsEmpty);
        Assert.False(loaded.NeedsRelearn);
    }

    [Fact]
    public async Task AProfileInTheNewShapeLoadsAndIsNotARelearn()
    {
        var project = Guid.NewGuid();
        var saved = VoiceProfile.Empty with
        {
            Register = "Wry, warm, economical",
            Traits = ["wry", "warm"],
            Examples = [new VoiceExample("the tide was out", project)],
            Avoid = ["hype"],
            Cadence = ["short declarative openers"],
            Punctuation = ["em-dash asides"],
            DocumentsAnalyzed = 4,
        };

        var file = new InMemoryDriveTextFile();
        var store = new DriveVoiceProfileStore(file);
        await store.SaveAsync(saved, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(loaded.NeedsRelearn);
        Assert.False(loaded.IsEmpty);
        Assert.Equal("Wry, warm, economical", loaded.Register);
        Assert.Equal(project, Assert.Single(loaded.Examples).ProjectId);
        Assert.Equal(["em-dash asides"], loaded.Punctuation);
    }

    private static async Task<VoiceProfile> Load(string content)
    {
        var store = new DriveVoiceProfileStore(new InMemoryDriveTextFile(content));
        return await store.LoadAsync(CancellationToken.None);
    }

    private sealed class InMemoryDriveTextFile(string content = "") : IDriveTextFile
    {
        private string stored = content;

        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(stored);

        public Task WriteAsync(string text, CancellationToken cancellationToken)
        {
            stored = text;
            return Task.CompletedTask;
        }
    }
}
