using Foreword.Domain.Calendar;
using Foreword.Domain.Install;
using Foreword.Domain.Models;
using Xunit;

namespace Foreword.Tests.Install;

/// <summary>
/// Whose phone this is.
///
/// <para>Foreword has only ever run on one, and the answer was baked in as a default:
/// <c>PersonColumn.Matt</c>, in four places, none of them a setting. On a second install every one
/// of those is wrong in the same quiet way — a hand-written note with nobody named on it goes into
/// somebody else's column, nothing errors, and the event is simply on the wrong person's day.</para>
///
/// <para>So it is proposed from the account that is signed in, because that is a fact the app
/// already has, and an explicit choice always wins over the proposal.</para>
/// </summary>
public class OwnerColumnTests
{
    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [PersonColumn.Matt] = ["storyworthtelling@gmail.com", "matt@work.example"],
            [PersonColumn.Ashley] = ["ashley@example.com"],
        };

    [Fact]
    public void TheSignedInAccountProposesWhoseInstallThisIs()
    {
        Assert.Equal(
            PersonColumn.Ashley,
            OwnerColumn.Propose("ashley@example.com", Addresses));
    }

    /// <summary>
    /// Any of a person's addresses, not only the first. The work address is the one somebody is
    /// more likely to have signed in with on a phone they use for work.
    /// </summary>
    [Fact]
    public void AnyOfTheirAddressesProposesThem()
    {
        Assert.Equal(PersonColumn.Matt, OwnerColumn.Propose("matt@work.example", Addresses));
    }

    [Fact]
    public void AddressesAreMatchedWithoutCaseOrSurroundingSpace()
    {
        Assert.Equal(
            PersonColumn.Ashley,
            OwnerColumn.Propose("  Ashley@Example.COM ", Addresses));
    }

    /// <summary>
    /// An account nobody has claimed proposes nothing. It is not a failure — it is a phone signed
    /// in to an address that has not been entered on the calendar screen yet, and the honest answer
    /// is to ask rather than to pick somebody.
    /// </summary>
    [Theory]
    [InlineData("someone.else@example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnclaimedAccountProposesNothing(string? email)
    {
        Assert.Null(OwnerColumn.Propose(email, Addresses));
    }

    /// <summary>
    /// Only the people who have a mailbox can be proposed. The girls' columns are filled by the
    /// calendar an event came from, never by an address — proposing one of them from a signed-in
    /// account would mean somebody had typed a child's address into a field that cannot work.
    /// </summary>
    [Fact]
    public void AChildIsNeverProposedEvenIfAnAddressWasTypedIn()
    {
        var withAChildsAddress = new Dictionary<PersonColumn, IReadOnlyList<string>>(Addresses)
        {
            [PersonColumn.Gwen] = ["gwen@example.com"],
        };

        Assert.Null(OwnerColumn.Propose("gwen@example.com", withAChildsAddress));
    }

    // ---- what the app actually uses ----

    /// <summary>
    /// An explicit choice wins. Somebody who signs in with a shared household account, or with an
    /// address they have not entered anywhere, has to be able to say so and be believed.
    /// </summary>
    [Fact]
    public void AnExplicitChoiceBeatsTheProposal()
    {
        Assert.Equal(
            PersonColumn.Ashley,
            OwnerColumn.Resolve(
                chosen: PersonColumn.Ashley,
                accountEmail: "storyworthtelling@gmail.com",
                Addresses));
    }

    [Fact]
    public void WithNoChoiceTheProposalStands()
    {
        Assert.Equal(
            PersonColumn.Matt,
            OwnerColumn.Resolve(chosen: null, accountEmail: "matt@work.example", Addresses));
    }

    /// <summary>
    /// And with neither, the first person with a mailbox — which is what the app did before this
    /// existed, so a phone that has been running since before the setting behaves exactly as it did
    /// rather than changing its mind on an update.
    /// </summary>
    [Fact]
    public void WithNeitherItIsWhatItAlwaysWas()
    {
        Assert.Equal(
            PersonIdentity.WithMailboxes[0],
            OwnerColumn.Resolve(chosen: null, accountEmail: null, Addresses));
    }

    /// <summary>
    /// The fallback is never a child, whatever the addresses say. Resolve always returns a column
    /// somebody could have written a note in.
    /// </summary>
    [Fact]
    public void TheFallbackIsAlwaysSomebodyWithAMailbox()
    {
        var resolved = OwnerColumn.Resolve(chosen: null, accountEmail: "nobody@example.com", Addresses);

        Assert.True(PersonIdentity.HasMailbox(resolved));
    }
}
