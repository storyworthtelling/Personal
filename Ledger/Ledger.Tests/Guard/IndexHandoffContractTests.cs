using System.Text.RegularExpressions;
using Index.Domain.Identity;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G2: <c>BalanceReceiver</c>'s declared manifest shape (its C# attributes, plus the raw
/// AndroidManifest.xml permission declaration) against <c>IndexHandoff</c>'s OWN constants — Post's
/// already-shipped, frozen contract (<c>FocusPhone/Codex.Domain/Post/MoneyMail.cs</c>).
///
/// <para>Reads <c>MoneyMail.cs</c>'s constants directly by content rather than retyping them here, so a
/// future change to Post's contract fails THIS test loudly (a diff against the live file) instead of
/// Index silently going on answering to an action or permission nobody sends any more. Every expected
/// value is checked against <see cref="IndexIdentity.ApplicationId"/>'s own package prefix rather than
/// the raw string, matching <c>BalanceReceiver.cs</c>'s own <c>IndexIdentity.ApplicationId + "..."</c>
/// idiom (<c>IndexInvariantsTests.TheApplicationIdIsNotCopiedAroundTheCode</c> forbids retyping the
/// package string elsewhere in this app's source, this test file included).</para>
/// </summary>
public class IndexHandoffContractTests
{
    [Fact]
    public void Package_MatchesIndexIdentitysApplicationId()
    {
        Assert.Equal(IndexIdentity.ApplicationId, HandoffConstant("Package"));
    }

    [Fact]
    public void ReceiverClassName_MatchesIndexHandoffsByteForByte()
    {
        var suffix = SuffixAfterApplicationId(HandoffConstant("Receiver"));
        Assert.Contains(
            $"Name = IndexIdentity.ApplicationId + \"{suffix}\"", ReceiverSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void Action_MatchesIndexHandoffsByteForByte()
    {
        var suffix = SuffixAfterApplicationId(HandoffConstant("Action"));
        var receiver = ReceiverSource();
        Assert.Contains(
            $"public const string HandoffAction = IndexIdentity.ApplicationId + \"{suffix}\";",
            receiver, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([BalanceReceiver.HandoffAction])]", receiver, StringComparison.Ordinal);
    }

    [Fact]
    public void Permission_MatchesIndexHandoffsByteForByte_OnTheReceiverAttribute()
    {
        var suffix = SuffixAfterApplicationId(HandoffConstant("Permission"));
        Assert.Contains(
            $"Permission = IndexIdentity.ApplicationId + \"{suffix}\"", ReceiverSource(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Post enforces the permission by requiring the RECEIVING app hold it; declaring the
    /// &lt;permission&gt; tag itself is Index's own job (IndexHandoff's own remark). A receiver whose
    /// attribute names a permission nobody DECLARES is silently unenforceable — Android just never
    /// grants it to anyone, including Post. The raw manifest has no constant-reference idiom to check
    /// against (XML has no C# constants), so this is the one assertion in this file that reads
    /// IndexHandoff's full literal value directly.
    /// </summary>
    [Fact]
    public void Permission_IsDeclaredInTheManifest_AtSignatureProtectionLevel()
    {
        var expected = HandoffConstant("Permission");
        var manifest = File.ReadAllText(ManifestPath());
        Assert.Contains(
            $"<permission android:name=\"{expected}\" android:protectionLevel=\"signature\" />",
            manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void BalanceExtraKey_MatchesIndexHandoffsByteForByte()
    {
        var expected = HandoffConstant("BalanceExtra");
        Assert.Contains($"public const string BalanceExtra = \"{expected}\";", ReceiverSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void AsOfExtraKey_MatchesIndexHandoffsByteForByte()
    {
        var expected = HandoffConstant("AsOfExtra");
        Assert.Contains($"public const string AsOfExtra = \"{expected}\";", ReceiverSource(), StringComparison.Ordinal);
    }

    // The owner's own, deliberate exception to "nothing about who the owner banks with
    // belongs in an intent" (IndexHandoff's own doc comment records the decision) — Index
    // needs it to route a balance to the right account, so the receiver reads it too.
    [Fact]
    public void SenderExtraKey_MatchesIndexHandoffsByteForByte()
    {
        var expected = HandoffConstant("SenderExtra");
        Assert.Contains($"public const string SenderExtra = \"{expected}\";", ReceiverSource(), StringComparison.Ordinal);
    }

    // G2 also covers that the receiver is actually WIRED to use the sender name it reads —
    // not just declaring a matching constant while still only logging. Source-level rather
    // than a runtime check because BalanceReceiver derives from Android.App.BroadcastReceiver
    // and this Tests project targets the desktop TFM only (no Android SDK here).
    [Fact]
    public void ReceiverSource_RoutesTheSenderNameIntoAccountService()
    {
        var receiver = ReceiverSource();
        Assert.Contains("GetStringExtra(SenderExtra)", receiver, StringComparison.Ordinal);
        Assert.Contains("ApplyBalanceReceiptAsync(", receiver, StringComparison.Ordinal);
    }

    // ---- helpers ----

    /// <summary>Strips <see cref="IndexIdentity.ApplicationId"/>'s own prefix off one of IndexHandoff's
    /// full values, so a test can check BalanceReceiver.cs's <c>IndexIdentity.ApplicationId + "suffix"</c>
    /// idiom without embedding the raw package string here.</summary>
    private static string SuffixAfterApplicationId(string fullValue)
    {
        Assert.StartsWith(IndexIdentity.ApplicationId, fullValue, StringComparison.Ordinal);
        return fullValue[IndexIdentity.ApplicationId.Length..];
    }

    private static string ReceiverSource()
    {
        var path = Path.Combine(
            RepoRoot(), "Ledger", "Ledger.Android", "Platforms", "Android", "BalanceReceiver.cs");
        Assert.True(File.Exists(path), $"BalanceReceiver.cs not found at {path}.");
        return File.ReadAllText(path);
    }

    private static string ManifestPath() =>
        Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "Platforms", "Android", "AndroidManifest.xml");

    /// <summary>Reads one <c>IndexHandoff.&lt;name&gt; = "...";</c> string constant's literal value
    /// straight out of Post's own, frozen source file — never retyped.</summary>
    private static string HandoffConstant(string name)
    {
        var path = Path.Combine(
            RepoRoot(), "FocusPhone", "Codex.Domain", "Post", "MoneyMail.cs");
        Assert.True(File.Exists(path), $"MoneyMail.cs not found at {path} — is FocusPhone checked out?");
        var source = File.ReadAllText(path);

        var match = Regex.Match(
            source, $@"public const string {Regex.Escape(name)}\s*=\s*""([^""]*)""\s*;");
        Assert.True(match.Success, $"Could not find IndexHandoff.{name}'s literal value in MoneyMail.cs.");
        return match.Groups[1].Value;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
