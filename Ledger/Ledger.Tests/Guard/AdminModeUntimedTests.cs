using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guards G27 and R-G6 (D133, mockup 5): no string on the connect path may state how long Admin mode
/// takes to open or how long it stays open — those are Flyleaf's to change, and D133 is explicit that
/// no Index screen repeats them (mockup 5's own notes: "How long Admin mode takes to open and how long
/// it stays open are Flyleaf's to change, so no Index screen repeats them.").
///
/// <para>The first version of this guard scanned two files for digit-shaped durations ("6 hours",
/// "60-minute"), which left two holes. It matched only digits, so "opens in six hours" or "an hour"
/// passed; and it read only the connect page, while the connect path also speaks from the Add-an-account
/// chooser, from <c>MainPage</c> (the not-configured sentence), and from the Domain copy class. It now
/// reads all of those, and bans the unit words themselves — a connect string has no business naming an
/// hour or a minute whether the number is a digit, a word, or missing ("minutes", "an hour").</para>
///
/// <para>R-G6 is the other half: the old flow ended in a Done button the owner had to remember to tap.
/// Coming back from Chrome is enough now (the page checks for a finished session when it reappears),
/// so neither connect-page file may carry a Done button, handler or label.</para>
/// </summary>
public class AdminModeUntimedTests
{
    // The unit words, whole-word, with or without a digit or a spelled-out number in front of them.
    // Wider than the first version on purpose: "an hour", "six hours" and "a few minutes" are all
    // durations a bare-digit pattern cannot see.
    private static readonly Regex DurationPattern =
        new(@"\b(hours?|minutes?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Literals that contain a unit word and are NOT about Admin mode. Each is named, with its reason,
    // rather than excused by a looser pattern — a new one has to be added here on purpose.
    private static readonly string[] UnrelatedLiterals =
    [
        // MainPage: the Plaid /transactions/get "product not ready" retry hint on an expanded account.
        "try again in a minute",
    ];

    private static readonly Regex DoneButtonPattern =
        new(@"\bdone\b|doneButton|OnDoneClicked", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static TheoryData<string> ConnectFiles()
    {
        var data = new TheoryData<string>
        {
            Path.Combine("Ledger.Android", "Pages", "PlaidLinkPage.xaml"),
            Path.Combine("Ledger.Android", "Pages", "PlaidLinkPage.xaml.cs"),
            Path.Combine("Ledger.Android", "Pages", "MainPage.xaml.cs"),
            Path.Combine("Ledger.Android", "Views", "AddAccountChooserCard.cs"),
        };

        var domainConnect = Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Domain", "Connect");
        foreach (var file in Directory.EnumerateFiles(domainConnect, "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            data.Add(Path.Combine("Ledger.Domain", "Connect", Path.GetFileName(file)));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ConnectFiles))]
    public void NoStringLiteralStatesAnAdminModeDuration(string relativePath)
    {
        var source = Read(relativePath);
        foreach (var literal in UnrelatedLiterals)
        {
            source = source.Replace(literal, string.Empty, StringComparison.Ordinal);
        }

        var offenders = DurationPattern.Matches(source).Select(m => m.Value).ToList();
        Assert.True(offenders.Count == 0,
            $"{relativePath} names a duration unit (D133 forbids stating how long Admin mode takes or "
            + $"lasts, in digits or in words): {string.Join(", ", offenders)}");
    }

    [Theory]
    [InlineData("PlaidLinkPage.xaml")]
    [InlineData("PlaidLinkPage.xaml.cs")]
    public void TheConnectPageHasNoDoneButton(string fileName)
    {
        var source = Read(Path.Combine("Ledger.Android", "Pages", fileName));

        var offenders = DoneButtonPattern.Matches(source).Select(m => m.Value).ToList();
        Assert.True(offenders.Count == 0,
            $"{fileName} still carries the tap-Done flow (R-G6, mockup 5: coming back is enough): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheConnectCopyNeverTellsTheOwnerToTapDone()
    {
        var domainConnect = Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Domain", "Connect");
        var offenders = Directory.EnumerateFiles(domainConnect, "*.cs")
            .Where(f => DoneButtonPattern.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Domain connect strings mention Done (R-G6): " + string.Join(", ", offenders));
    }

    private static string Read(string relativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), relativePath);
        Assert.True(File.Exists(path), $"{relativePath} not found at {path}.");
        return File.ReadAllText(path);
    }
}
