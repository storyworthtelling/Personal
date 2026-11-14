using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Who the owner means when they say a face is someone else (orphan audit §26: FaceBook.Correct had no
/// caller, so a wrong suggestion could only be ignored, never fixed). The address book is the list; what
/// they type narrows it.
/// </summary>
public class FacePickerTests
{
    private static readonly IReadOnlyList<PickablePerson> People =
    [
        new("1", "Robin Ash"),
        new("2", "Sam Lee"),
        new("3", "Juniper Ashby"),
        new("4", "Lee Robinson"),
    ];

    [Fact]
    public void NothingTypedOffersEveryoneButTheWrongSuggestion()
    {
        var offered = FacePicker.Choose(People, "", wronglySuggested: "2");

        Assert.Equal(["1", "3", "4"], offered.Select(p => p.ContactId));
    }

    [Fact]
    public void AWordThatStartsTheNameComesBeforeOneInsideIt()
    {
        var offered = FacePicker.Choose(People, "rob", wronglySuggested: null);

        Assert.Equal(["Robin Ash", "Lee Robinson"], offered.Select(p => p.Name));
    }

    [Fact]
    public void MatchingIgnoresCaseAndSurroundingSpace()
    {
        Assert.Equal(["3"], FacePicker.Choose(People, "  JUNI ", null).Select(p => p.ContactId));
    }

    [Fact]
    public void TheListIsCappedSoItFitsOnTheCard()
    {
        var many = Enumerable.Range(0, 30).Select(i => new PickablePerson($"{i}", $"Person {i:00}")).ToList();

        Assert.Equal(FacePicker.MaxOffered, FacePicker.Choose(many, "person", null).Count);
    }
}
