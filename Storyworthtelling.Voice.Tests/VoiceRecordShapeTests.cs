using System.Reflection;
using Storyworthtelling.Voice;

namespace Storyworthtelling.Voice.Tests;

/// <summary>
/// G11 — the refitted voice record: <c>Register</c>, <c>Cadence</c> and <c>Punctuation</c> exist,
/// the examples carry the project they were learned from, and there is no <c>Summary</c>.
///
/// <para>The bug this reintroduces is putting <c>Summary</c> back. It is the obvious field — a
/// paragraph describing the voice — and it is what made the profile impossible to reason about: a
/// paragraph cannot be checked, cannot be diffed between drafts, and cannot be partly discarded. The
/// three named fields can. <c>SignatureMoves</c> split into two for the same reason: "em-dash asides"
/// and "short declarative openers" were filed together and are different instructions.</para>
///
/// <para>The project id on an example is what makes <b>discard-and-relearn</b> possible at all.
/// Without it a voice learned from four books can only be thrown away whole, so one bad ingestion
/// costs every anchor the profile ever gathered.</para>
/// </summary>
public class VoiceRecordShapeTests
{
    private static IReadOnlyList<string> Fields =>
        [.. typeof(VoiceProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)];

    [Theory]
    [InlineData("Register")]
    [InlineData("Cadence")]
    [InlineData("Punctuation")]
    [InlineData("Traits")]
    [InlineData("Examples")]
    [InlineData("Avoid")]
    public void TheProfileCarriesTheNamedField(string field)
    {
        Assert.Contains(field, Fields);
    }

    [Theory]
    [InlineData("Summary")]
    [InlineData("SignatureMoves")]
    public void TheProfileCarriesNoRetiredField(string field)
    {
        Assert.DoesNotContain(field, Fields);
    }

    /// <summary>
    /// An example knows which book taught it. A bare string cannot be discarded selectively, so the
    /// relearn below would have to throw the whole voice away.
    /// </summary>
    [Fact]
    public void AnExampleCarriesTheProjectItWasLearnedFrom()
    {
        var examples = typeof(VoiceProfile).GetProperty("Examples")!.PropertyType;
        var element = examples.GetGenericArguments().Single();

        Assert.Equal(typeof(VoiceExample), element);
        Assert.Contains("ProjectId",
            element.GetProperties().Select(property => property.Name));
    }

    /// <summary>Nothing learned yet is the neutral-copy state every consumer already honours.</summary>
    [Fact]
    public void AProfileWithNoRegisterIsEmpty()
    {
        Assert.True(VoiceProfile.Empty.IsEmpty);
        Assert.True((VoiceProfile.Empty with { DocumentsAnalyzed = 3 }).IsEmpty);
    }

    [Fact]
    public void AProfileWithARegisterAndADocumentIsNotEmpty()
    {
        var learned = VoiceProfile.Empty with { Register = "Wry, plain-spoken", DocumentsAnalyzed = 1 };

        Assert.False(learned.IsEmpty);
    }

    /// <summary>
    /// Discard-and-relearn: one book's contribution comes out and the rest of the voice stands.
    /// </summary>
    [Fact]
    public void DiscardingAProjectTakesOnlyItsOwnExamples()
    {
        var kept = Guid.NewGuid();
        var dropped = Guid.NewGuid();
        var profile = VoiceProfile.Empty with
        {
            Register = "Wry",
            DocumentsAnalyzed = 2,
            Examples =
            [
                new VoiceExample("the tide was out", kept),
                new VoiceExample("a sky the colour of an old coin", dropped),
                new VoiceExample("nobody spoke to her", kept),
            ],
        };

        var after = profile.Discard(dropped);

        Assert.Equal(2, after.Examples.Count);
        Assert.All(after.Examples, example => Assert.Equal(kept, example.ProjectId));
        Assert.Equal(1, after.DocumentsAnalyzed);
        Assert.Equal("Wry", after.Register);
    }

    /// <summary>
    /// Discarding the only book that taught the voice leaves nothing to write in — and says so,
    /// rather than leaving a register with no anchors behind it.
    /// </summary>
    [Fact]
    public void DiscardingTheLastProjectEmptiesTheProfile()
    {
        var only = Guid.NewGuid();
        var profile = VoiceProfile.Empty with
        {
            Register = "Wry",
            DocumentsAnalyzed = 1,
            Examples = [new VoiceExample("the tide was out", only)],
        };

        Assert.True(profile.Discard(only).IsEmpty);
    }

    [Fact]
    public void DiscardingAProjectThatTaughtNothingChangesNothing()
    {
        var taught = Guid.NewGuid();
        var profile = VoiceProfile.Empty with
        {
            Register = "Wry",
            DocumentsAnalyzed = 1,
            Examples = [new VoiceExample("the tide was out", taught)],
        };

        Assert.Equal(profile, profile.Discard(Guid.NewGuid()));
    }
}
