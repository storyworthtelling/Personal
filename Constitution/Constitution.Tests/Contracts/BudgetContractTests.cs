using System.Text.RegularExpressions;
using Constitution.Domain;
using Constitution.Domain.Budgets;

namespace Constitution.Tests.Contracts;

/// <summary>
/// The budget contract held equal from both sides (PBI C-11.1.1), by content: Index's sender is read as text, because a
/// build never compares two apps. Constitution's names come from <see cref="ConstitutionIdentity"/>, whose own values are
/// pinned as literals in <c>ConstitutionInvariantsTests</c>; this file asks only whether Index's source says the same words.
///
/// <para><b>Red until the sender exists.</b> Index's side is a separate job. Until a file under <c>Ledger/</c> names the
/// <c>BudgetReceiver</c>, <see cref="IndexSendsToTheReceiverUnderTheAction"/> and its siblings fail, by design: a pass over
/// no sender would be the guard saying yes to a contract only one side had signed. They go green the moment the sender lands,
/// with no edit here, because the search is by content across Index's three code projects rather than for one file name.</para>
/// </summary>
public class BudgetContractTests
{
    private static readonly string[] IndexProjects = ["Ledger.Android", "Ledger.Domain", "Ledger.Infrastructure"];

    private static string LedgerRoot() => Path.Combine(Directory.GetParent(ConstitutionSource.Root())!.FullName, "Ledger");

    private static List<(string Path, string Text)> IndexFiles(params string[] extensions)
    {
        var root = LedgerRoot();
        Assert.True(Directory.Exists(root), "Ledger/ was not found beside Constitution/.");
        return IndexProjects
            .Select(project => Path.Combine(root, project))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(file => extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(file => (Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(file)))
            .ToList();
    }

    /// <summary>
    /// Every Index code file that speaks of the budget contract at all: by the contract's own words, or by the name of Index's
    /// contract class (<c>ConstitutionBudgetContract</c>), which is how the sending code refers to them without respelling them.
    /// </summary>
    private static List<(string Path, string Text)> SenderFiles() =>
        IndexFiles(".cs")
            .Where(file => Regex.IsMatch(file.Text, @"BudgetReceiver|RECEIVE_BUDGET|action\.BUDGET|ConstitutionBudget", RegexOptions.None, TimeSpan.FromSeconds(2)))
            .ToList();

    /// <summary>
    /// True when the text spells <paramref name="constitutionName"/> whole as one literal, or as the package literal on one
    /// side and the quoted suffix on the other (<c>Package + ".BudgetReceiver"</c>), which is how Index builds its own names.
    /// </summary>
    private static bool Spells(IEnumerable<(string Path, string Text)> files, string suffix)
    {
        var whole = ConstitutionIdentity.ApplicationId + suffix;
        var texts = files.Select(file => file.Text).ToList();
        return texts.Any(text => text.Contains("\"" + whole + "\"", StringComparison.Ordinal))
            || (texts.Any(text => text.Contains("\"" + ConstitutionIdentity.ApplicationId + "\"", StringComparison.Ordinal))
                && texts.Any(text => text.Contains("\"" + suffix + "\"", StringComparison.Ordinal)));
    }

    // ---- Constitution's side (green) ----

    [Fact]
    public void ConstitutionNamesTheAgreedWire()
    {
        // Each suffix is what Index's source has to spell too; the full values are pinned as literals in the invariants guard.
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".BudgetReceiver", ConstitutionIdentity.BudgetReceiverName);
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".action.BUDGET", ConstitutionIdentity.BudgetAction);
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".RECEIVE_BUDGET", ConstitutionIdentity.BudgetPermission);
        Assert.Equal(["month", "budget", "spent"], BudgetContract.Extras);
    }

    [Fact]
    public void TheGuardCanSeeIndexAtAll()
    {
        // A scan over no files passes everything for the wrong reason, so the scan itself is proved to read Index's code.
        var files = IndexFiles(".cs");
        Assert.True(files.Count > 50, $"The scan read only {files.Count} Index code files.");
        Assert.Contains(files, file => file.Path.EndsWith("Platforms/Android/UsageReceiver.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void TheScanReadsAGivenSenderCorrectly()
    {
        // The matcher, run on a sender written the way Index writes its names; proves the checks below can pass at all.
        var sample = new[]
        {
            ("Sample.cs",
             "const string P = \"" + ConstitutionIdentity.ApplicationId + "\"; var r = P + \".BudgetReceiver\"; var a = P + \".action.BUDGET\"; var x = P + \".RECEIVE_BUDGET\";"),
        };

        Assert.True(Spells(sample, ".BudgetReceiver"));
        Assert.True(Spells(sample, ".action.BUDGET"));
        Assert.True(Spells(sample, ".RECEIVE_BUDGET"));
        Assert.False(Spells(sample, ".SomethingElse"));
        Assert.False(Spells([("Empty.cs", "")], ".BudgetReceiver"));
    }

    // ---- Index's side (red until the sender exists) ----

    [Fact]
    public void IndexSendsToTheReceiverUnderTheAction()
    {
        var sender = SenderFiles();

        Assert.True(sender.Count > 0, "No Index source names the BudgetReceiver yet: Index's sending side has not landed.");
        Assert.True(Spells(sender, ".BudgetReceiver"), "Index does not spell Constitution's receiver name.");
        Assert.True(Spells(sender, ".action.BUDGET"), "Index does not send under Constitution's budget action.");
    }

    [Fact]
    public void IndexAddressesTheReceiverAsAnExplicitComponent()
    {
        var sender = SenderFiles().Where(file => file.Path.StartsWith("Ledger.Android/", StringComparison.Ordinal)).ToList();

        Assert.True(sender.Count > 0, "No Index.Android source names the BudgetReceiver yet.");
        Assert.Contains(sender, file => Regex.IsMatch(file.Text, @"SetComponent\(|SetClassName\(|new ComponentName\(", RegexOptions.None, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void IndexSendsWithTheSignaturePermission()
    {
        var sender = SenderFiles();
        Assert.True(sender.Count > 0, "No Index source names the BudgetReceiver yet.");
        Assert.True(Spells(sender, ".RECEIVE_BUDGET"), "Index does not spell Constitution's RECEIVE_BUDGET permission.");

        // Index must hold the permission (a uses-permission) for a protected receiver to take its broadcast, and must
        // pass it as the receiver permission when sending, or the broadcast is refused or reaches anyone.
        var android = IndexFiles(".cs", ".xml").Where(file => file.Path.StartsWith("Ledger.Android/", StringComparison.Ordinal)).ToList();
        var names = Regex.Matches(string.Join('\n', sender.Select(file => file.Text)), @"(\w+)\s*=\s*[^;]*RECEIVE_BUDGET", RegexOptions.None, TimeSpan.FromSeconds(2))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();
        var uses = android.Any(file =>
            Regex.IsMatch(file.Text, @"<uses-permission[^>]*constitution\.RECEIVE_BUDGET", RegexOptions.None, TimeSpan.FromSeconds(2))
            || Regex.IsMatch(file.Text, @"UsesPermission\(\s*(?:Name\s*=\s*)?[^)]*RECEIVE_BUDGET", RegexOptions.None, TimeSpan.FromSeconds(2))
            || names.Any(name => Regex.IsMatch(file.Text, @"UsesPermission\(\s*(?:Name\s*=\s*)?[^)]*\b" + Regex.Escape(name) + @"\b", RegexOptions.None, TimeSpan.FromSeconds(2))));
        Assert.True(uses, "Index does not declare a uses-permission for Constitution's RECEIVE_BUDGET.");

        Assert.Contains(android.Where(file => SenderFiles().Any(s => s.Path == file.Path)),
            file => file.Text.Contains("SendBroadcast(", StringComparison.Ordinal)
                && Regex.IsMatch(file.Text, @"SendBroadcast\([^;]*,[^;]*\)", RegexOptions.None, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void IndexCarriesTheThreeExtrasUnderTheirNames()
    {
        var sender = SenderFiles();
        Assert.True(sender.Count > 0, "No Index source names the BudgetReceiver yet.");

        var text = string.Join('\n', sender.Select(file => file.Text));
        foreach (var extra in BudgetContract.Extras)
        {
            Assert.True(text.Contains("\"" + extra + "\"", StringComparison.Ordinal), $"Index's sender never writes the \"{extra}\" extra.");
        }
    }
}
