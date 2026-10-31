using Constitution.Domain.ReadBack;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// When the read-back believes pen ink comes through the cloud's PDF (review finding S4). One read's word was enough, and
/// after it every blank page was a skip: a model that said "seen" once, wrongly, turned every later unsynced page into a
/// recorded skip. Now it takes two separate documents read on two separate mornings.
/// </summary>
public class InkProofTests
{
    private static readonly DateOnly Monday = new(2026, 11, 2);
    private static readonly DateOnly Tuesday = new(2026, 11, 3);

    private static InkSighting Of(string document, DateOnly morning) => new(document, morning);

    [Fact]
    public void NothingSeenIsNoProof()
    {
        Assert.False(InkProof.None.IsEstablished);
        Assert.Null(InkProof.None.EstablishedOn);
        Assert.Empty(InkProof.None.Sightings);
    }

    [Fact]
    public void OneSightingIsNotProof()
    {
        var proof = InkProof.None.Record(Of("Workout A", Monday));

        Assert.False(proof.IsEstablished);
        Assert.Single(proof.Sightings);
    }

    [Fact]
    public void TheSameDocumentTwiceIsNotTwoDocuments()
    {
        var proof = InkProof.None.Record(Of("Workout A", Monday)).Record(Of("Workout A", Tuesday));

        Assert.False(proof.IsEstablished);
    }

    [Fact]
    public void TwoDocumentsOnOneMorningAreNotTwoMornings()
    {
        var proof = InkProof.None.Record(Of("Workout A", Monday)).Record(Of("Workout B", Monday));

        Assert.False(proof.IsEstablished);
        Assert.Equal(2, proof.Sightings.Count);
    }

    [Fact]
    public void TwoDocumentsOnTwoMorningsAreProof()
    {
        var proof = InkProof.None.Record(Of("Workout A", Monday)).Record(Of("Workout B", Tuesday));

        Assert.True(proof.IsEstablished);
        Assert.Equal(Tuesday, proof.EstablishedOn);
    }

    [Fact]
    public void ThreeSightingsNeedOnlyOnePairThatDiffersInBoth()
    {
        // A and B were read the same morning, B and C on different mornings with different documents: C completes the pair with A.
        var proof = InkProof.None.Record(Of("Workout A", Monday)).Record(Of("Workout B", Monday)).Record(Of("Workout C", Tuesday));

        Assert.True(proof.IsEstablished);
        Assert.Equal(Tuesday, proof.EstablishedOn);
    }

    [Fact]
    public void OnceEstablishedItStaysAndTheDateDoesNotMove()
    {
        var proof = InkProof.None.Record(Of("Workout A", Monday)).Record(Of("Workout B", Tuesday));

        var later = proof.Record(Of("Workout C", Tuesday.AddDays(5)));

        Assert.True(later.IsEstablished);
        Assert.Equal(Tuesday, later.EstablishedOn);
        Assert.Same(proof, later);
    }

    [Fact]
    public void ASightingRecordedTwiceChangesNothing()
    {
        var once = InkProof.None.Record(Of("Workout A", Monday));

        var twice = once.Record(Of("Workout A", Monday));

        Assert.Same(once, twice);
    }

    [Fact]
    public void AProofFromBeforeDatesWereKeptIsEstablishedWithNoDate()
    {
        Assert.True(InkProof.EstablishedEarlier.IsEstablished);
        Assert.Null(InkProof.EstablishedEarlier.EstablishedOn);
        Assert.Same(InkProof.EstablishedEarlier, InkProof.EstablishedEarlier.Record(Of("Workout A", Monday)));
    }

    [Fact]
    public void SightingsKeptWhileUnprovenAreBounded()
    {
        var proof = InkProof.None;
        for (var index = 0; index < 100; index++)
        {
            proof = proof.Record(Of("Workout " + index, Monday));
        }

        Assert.False(proof.IsEstablished);
        Assert.Equal(InkProof.MostSightings, proof.Sightings.Count);
    }

    [Fact]
    public void ABlankDocumentNameIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new InkSighting(" ", Monday));
    }
}
