using Foreword.Domain.Calendar;
using Foreword.Domain.Install;
using Foreword.Domain.Models;
using Foreword.Tests.Calendar;
using Xunit;

namespace Foreword.Tests.Install;

/// <summary>
/// Whose phone this is.
///
/// <para>Foreword has only ever run on one, and the answer was baked in as a default:
/// <c>Household.Alex</c>, in four places, none of them a setting. On a second install every one
/// of those is wrong in the same quiet way — a hand-written note with nobody named on it goes into
/// somebody else's column, nothing errors, and the event is simply on the wrong person's day.</para>
///
/// <para>So it is inferred from the account that is signed in, because that is a fact the app
/// already has: an address entered on the calendar screen names the person outright, and otherwise
/// the shared vault's owner account is the account holder and any other account is their partner.
/// Nobody is asked.</para>
/// </summary>
public class OwnerColumnTests
{
    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> Addresses =
        new Dictionary<PersonColumn, IReadOnlyList<string>>
        {
            [Household.Alex] = ["alex.home@example.com", "alex@work.example"],
            [Household.Sam] = ["sam@example.com"],
        };

    private static readonly IReadOnlyDictionary<PersonColumn, IReadOnlyList<string>> NoAddresses =
        new Dictionary<PersonColumn, IReadOnlyList<string>>();

    [Fact]
    public void TheSignedInAccountProposesWhoseInstallThisIs()
    {
        Assert.Equal(
            Household.Sam,
            OwnerColumn.Propose("sam@example.com", Addresses, householdAccount: null, Household.Layout));
    }

    /// <summary>
    /// Any of a person's addresses, not only the first. The work address is the one somebody is
    /// more likely to have signed in with on a phone they use for work.
    /// </summary>
    [Fact]
    public void AnyOfTheirAddressesProposesThem()
    {
        Assert.Equal(Household.Alex, OwnerColumn.Propose("alex@work.example", Addresses, householdAccount: null, Household.Layout));
    }

    [Fact]
    public void AddressesAreMatchedWithoutCaseOrSurroundingSpace()
    {
        Assert.Equal(
            Household.Sam,
            OwnerColumn.Propose("  Sam@Example.COM ", Addresses, householdAccount: null, Household.Layout));
    }

    /// <summary>
    /// An account nobody has claimed proposes nothing when there is no household account to compare
    /// it with either. It is not a failure — it is a phone whose vault has not been read yet, and
    /// picking somebody would be a guess.
    /// </summary>
    [Theory]
    [InlineData("someone.else@example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnclaimedAccountProposesNothing(string? email)
    {
        Assert.Null(OwnerColumn.Propose(email, Addresses, householdAccount: null, Household.Layout));
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
            [Household.Riley] = ["riley@example.com"],
        };

        Assert.Null(OwnerColumn.Propose("riley@example.com", withAChildsAddress, householdAccount: null, Household.Layout));
    }

    // ---- the household account: who owns the shared vault ----

    /// <summary>
    /// The shared vault records the account that first wrote it, and that account is the household's
    /// — the account holder. So the phone signed in to it is theirs, with no address entered anywhere.
    /// </summary>
    [Fact]
    public void TheAccountThatOwnsTheVaultIsTheAccountHolder()
    {
        Assert.Equal(
            Household.Layout.WithAccounts[0],
            OwnerColumn.Propose("owner@example.com", NoAddresses, householdAccount: "owner@example.com", Household.Layout));
    }

    /// <summary>
    /// And any other account signed in beside it is the partner's: the vault has two readers and one
    /// writer, and the one that is not the writer is the other person with a mailbox.
    /// </summary>
    [Fact]
    public void AnyOtherAccountIsThePartner()
    {
        Assert.Equal(
            Household.Layout.WithAccounts[1],
            OwnerColumn.Propose("partner@example.com", NoAddresses, householdAccount: "owner@example.com", Household.Layout));
    }

    [Fact]
    public void TheHouseholdAccountIsMatchedWithoutCaseOrSurroundingSpace()
    {
        Assert.Equal(
            Household.Layout.WithAccounts[0],
            OwnerColumn.Propose(" Owner@Example.com ", NoAddresses, householdAccount: "owner@example.COM", Household.Layout));
    }

    /// <summary>
    /// An address entered on the calendar screen is the more specific fact, so it wins — somebody
    /// who signed in with the household account on their own phone has said who they are there.
    /// </summary>
    [Fact]
    public void AnEnteredAddressBeatsTheHouseholdAccount()
    {
        Assert.Equal(
            Household.Sam,
            OwnerColumn.Propose("sam@example.com", Addresses, householdAccount: "sam@example.com", Household.Layout));
    }

    /// <summary>
    /// A vault nobody has written yet says nothing about anybody. Guessing "partner" from an empty
    /// owner would make the first phone to install the app somebody else's.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnownedVaultProposesNothing(string? household)
    {
        Assert.Null(OwnerColumn.Propose("someone@example.com", NoAddresses, household, Household.Layout));
    }

    // ---- what the app actually uses ----

    [Fact]
    public void TheProposalStands()
    {
        Assert.Equal(
            Household.Layout.WithAccounts[1],
            OwnerColumn.Resolve("partner@example.com", NoAddresses, householdAccount: "owner@example.com", Household.Layout));
    }

    /// <summary>
    /// With nothing to go on, the first person with a mailbox — which is what the app did before this
    /// existed, so a phone that has been running since before the setting behaves exactly as it did
    /// rather than changing its mind on an update.
    /// </summary>
    [Fact]
    public void WithNothingToGoOnItIsWhatItAlwaysWas()
    {
        Assert.Equal(
            Household.Layout.WithAccounts[0],
            OwnerColumn.Resolve(accountEmail: null, Addresses, householdAccount: null, Household.Layout));
    }

    /// <summary>
    /// The fallback is never a child, whatever the addresses say. Resolve always returns a column
    /// somebody could have written a note in.
    /// </summary>
    [Fact]
    public void TheFallbackIsAlwaysSomebodyWithAMailbox()
    {
        var resolved = OwnerColumn.Resolve("nobody@example.com", Addresses, householdAccount: null, Household.Layout);

        Assert.True(Household.Layout.HasAccount(resolved));
    }
}
