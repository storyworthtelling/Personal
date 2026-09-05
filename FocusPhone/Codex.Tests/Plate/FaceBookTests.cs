using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Knowing who is in a picture, on the phone.
///
/// <para>It has to be local. Claude will not name a person in an image — it refuses, as a
/// matter of policy — and a Darkroom picture has not been uploaded anywhere to be asked
/// about in the first place. So the faces are matched here, against people the owner already
/// knows: the Rolodex is the phone's own address book, and a contact with a photo is a
/// labelled face that already exists.</para>
///
/// <para><b>It suggests; the owner decides.</b> Every match is a question — "is this
/// Jordan?" — and a confirmation teaches it. That is what makes a forgiving threshold the
/// right one: a wrong suggestion costs a tap, and a missed one costs a name nobody offered.</para>
/// </summary>
public class FaceBookTests
{
    private static float[] Near(params float[] values) => values;

    private static FaceBook WithAshley() =>
        FaceBook.Empty.Learn("c1", "Jordan", Near(1, 0, 0));

    [Fact]
    public void AnEmptyBookKnowsNobody()
    {
        Assert.Equal(FaceMatch.Unknown, FaceBook.Empty.Nearest(Near(1, 0, 0)));
        Assert.Empty(FaceBook.Empty.People);
    }

    [Fact]
    public void TheSameFaceComesBackWithTheName()
    {
        var match = WithAshley().Nearest(Near(1, 0, 0));

        Assert.Equal("c1", match.ContactId);
        Assert.Equal("Jordan", match.Name);
        Assert.True(match.IsSomeone);
    }

    [Fact]
    public void ADifferentFaceIsNobodyRatherThanTheNearestName()
    {
        // The failure that matters. Handing back "closest person in the book" for a stranger
        // is how a picture of someone else gets filed under a friend's name.
        var match = WithAshley().Nearest(Near(0, 1, 0));

        Assert.False(match.IsSomeone);
        Assert.Equal(FaceMatch.Unknown, match);
    }

    [Fact]
    public void TheNearerOfTwoPeopleWins()
    {
        var book = FaceBook.Empty
            .Learn("c1", "Jordan", Near(1, 0, 0))
            .Learn("c2", "Ruth", Near(0.9f, 0.4f, 0));

        Assert.Equal("Jordan", book.Nearest(Near(1, 0.02f, 0)).Name);
        Assert.Equal("Ruth", book.Nearest(Near(0.88f, 0.45f, 0)).Name);
    }

    [Fact]
    public void ConfirmingAFaceTeachesIt()
    {
        // "Even if it takes learning to get there." A face at a new angle that the owner
        // confirms becomes another way of recognising the same person. This one sits well
        // past FaceBook.SameFace from the face already stored, so it genuinely is not
        // recognised until the owner says so.
        var fromTheSide = Near(0.2f, 0.98f, 0);
        var book = WithAshley();

        Assert.False(book.Nearest(fromTheSide).IsSomeone);

        var taught = book.Learn("c1", "Jordan", fromTheSide);
        Assert.Equal("Jordan", taught.Nearest(fromTheSide).Name);
        // And it still knows the face it started with.
        Assert.Equal("Jordan", taught.Nearest(Near(1, 0, 0)).Name);
    }

    [Fact]
    public void LearningTheSamePersonTwiceDoesNotSplitThem()
    {
        var book = WithAshley().Learn("c1", "Jordan", Near(0.2f, 0.98f, 0));

        Assert.Single(book.People);
        Assert.Equal(2, book.People.Single().Faces.Count);
    }

    [Fact]
    public void ANameThatChangedInTheRolodexFollowsThrough()
    {
        // People change their names, and the address book is the source of truth for them.
        var book = WithAshley().Learn("c1", "Jordan Okonjo", Near(0.2f, 0.98f, 0));

        Assert.Equal("Jordan Okonjo", book.People.Single().Name);
    }

    [Fact]
    public void ACorrectionIsPermanent()
    {
        // Post's sender promotions work the same way: telling it once is telling it forever.
        var face = Near(1, 0, 0);
        var book = WithAshley().Correct("c2", "Ruth", face);

        Assert.Equal("Ruth", book.Nearest(face).Name);
    }

    [Fact]
    public void ACorrectionTakesTheFaceOffThePersonItWasWronglyOn()
    {
        // Leaving it behind would mean the same face still pulls the old name back the next
        // time, and the owner would have to correct it again and again.
        var face = Near(1, 0, 0);
        var book = WithAshley()
            .Learn("c1", "Jordan", Near(0.99f, 0.01f, 0))
            .Correct("c2", "Ruth", face);

        var jordan = book.People.Single(person => person.ContactId == "c1");
        Assert.DoesNotContain(jordan.Faces, stored => stored.SequenceEqual(face));
    }

    [Fact]
    public void ForgettingSomeoneRemovesThemEntirely()
    {
        // The address book is the owner's, and so is this. Removing a person has to actually
        // remove them rather than leave their face behind under no name.
        var book = WithAshley().Forget("c1");

        Assert.Empty(book.People);
        Assert.False(book.Nearest(Near(1, 0, 0)).IsSomeone);
    }

    [Theory]
    [InlineData(new float[0])]
    [InlineData(new float[] { 0, 0, 0 })]
    public void AFaceWithNothingInItIsNeverAMatch(float[] empty)
    {
        // A detector that found nothing, or a crop that came out black. Either would
        // otherwise be "distance zero from everything".
        Assert.False(WithAshley().Nearest(empty).IsSomeone);
    }

    [Fact]
    public void AFaceOfTheWrongLengthIsNotComparedAtAll()
    {
        // A model swap changes the vector length. Comparing across it would produce
        // confident nonsense rather than an obvious failure.
        Assert.False(WithAshley().Nearest(Near(1, 0, 0, 0, 0)).IsSomeone);
    }

    [Fact]
    public void LearningIgnoresAFaceItCouldNotUse()
    {
        Assert.Empty(FaceBook.Empty.Learn("c1", "Jordan", []).People);
    }

    [Fact]
    public void OnlySoManyFacesAreKeptPerPerson()
    {
        // Every stored face is compared on every picture, and the phone does this while the
        // owner waits. The newest are kept, because a face changes over time.
        var book = FaceBook.Empty;
        for (var i = 0; i < FaceBook.FacesPerPerson + 5; i++)
        {
            book = book.Learn("c1", "Jordan", Near(1, i * 0.01f, 0));
        }

        Assert.Equal(FaceBook.FacesPerPerson, book.People.Single().Faces.Count);
    }
}
