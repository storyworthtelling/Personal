using Foreword.Infrastructure.Calendar;
using Foreword.Infrastructure.Install;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// Nothing that is true of <b>one phone</b> rides the household's shared vault (Release I).
///
/// <para>The shared vault is a single encrypted file that both installs read and only one writes
/// (<see cref="VaultOwnership"/>), and Foreword's calendar mapping rides inside it. That is right
/// for household facts — which calendar belongs to which column is the same on both phones — and
/// wrong for anything else, in a way that is almost impossible to see in a diff: a per-install field
/// added to that document would be restored onto the second install from the first, and its owner's
/// handwriting would start filing under somebody else. Nothing errors. The morning simply belongs to
/// the wrong person.</para>
///
/// <para>So the household section's fields are enumerated here. A new one is a decision.</para>
/// </summary>
public class SharedVaultHoldsHouseholdFactsTests
{
    [Fact]
    public void TheSharedVaultsForewordSectionHoldsOnlyHouseholdFacts()
    {
        var fields = typeof(ForewordSecrets)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => name != "EqualityContract")
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(["AnthropicApiKey", "CalendarMappingJson"], fields);
    }

    /// <summary>
    /// And the mapping document itself carries no per-install answer. This is the one that would
    /// actually be written by accident: the mapping options are the obvious place to put "whose
    /// phone is this", right up until you notice where that document lives.
    /// </summary>
    [Fact]
    public void TheSharedMappingDocumentCarriesNothingAboutWhosePhoneThisIs()
    {
        var perInstall = typeof(InstallOptions)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.NotEmpty(perInstall);

        var shared = typeof(CalendarMappingOptions)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        var leaked = shared.Intersect(perInstall).ToArray();

        Assert.True(leaked.Length == 0,
            "These are true of one phone and are stored in the document both phones share:\n"
            + string.Join("\n", leaked));
    }

    /// <summary>
    /// The per-install bag still answers. The two guards above would both pass if it were deleted,
    /// which would put the answer back where it started — in the code, as one person's name.
    /// </summary>
    [Fact]
    public void ThePerInstallBagStillCarriesTheOwner()
    {
        var options = new InstallOptions { AccountEmail = "ashley@example.com" };

        Assert.Null(options.Owner);
        Assert.Equal("ashley@example.com", options.AccountEmail);
    }
}
