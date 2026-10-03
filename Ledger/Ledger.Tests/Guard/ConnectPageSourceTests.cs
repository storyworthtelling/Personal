using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// R-G7 and the rest of §1.2, at the source level — <c>PlaidLinkPage</c> lives in a
/// <c>net10.0-android</c> project this desktop-TFM test project cannot load, so these read the file
/// (the same idiom <c>LedgerBinderShellTests</c> and <c>HouseholdGateSourceTests</c> use).
///
/// <para>Each test names the bug it exists for. The one that matters most: the page used to gate on
/// <c>IsRestrictedViewer(defaultBrowser)</c>, which is true on the locked phone even while Admin mode
/// is open, so the page said "Admin mode needed" forever and no bank could ever be connected.</para>
/// </summary>
public class ConnectPageSourceTests
{
    private static string Root => DesignTestHelpers.LedgerRoot();

    private static string PageSource() =>
        File.ReadAllText(Path.Combine(Root, "Ledger.Android", "Pages", "PlaidLinkPage.xaml.cs"));

    private static string MainPageSource() =>
        File.ReadAllText(Path.Combine(Root, "Ledger.Android", "Pages", "MainPage.xaml.cs"));

    [Fact]
    public void ThePageGatesOnTheAdminModeState_NotOnTheDefaultBrowser()
    {
        var source = PageSource();

        Assert.Contains("ConnectBrowserPolicy.DecideAdminMode(", source);
        Assert.Contains("ConnectBrowserPolicy.ChromePackage", source);
        Assert.DoesNotContain("IsRestrictedViewer(", source);
        Assert.DoesNotContain("ResolveDefaultBrowserPackage", source);
    }

    [Fact]
    public void TheSuspendedFlagIsReadFromApplicationInfo()
    {
        // The member names below were read out of Mono.Android's metadata (GetApplicationInfo has a
        // PackageInfoFlags overload and, from API 33, an ApplicationInfoFlags one; ApplicationInfo.Flags
        // carries ApplicationInfoFlags.Suspended), not remembered.
        var source = PageSource();

        Assert.Contains("GetApplicationInfo(", source);
        Assert.Contains("ApplicationInfoFlags.Suspended", source);
        Assert.Contains("PackageManager.NameNotFoundException", source);
    }

    [Fact]
    public void ChromeIsDeclaredInTheManifestQueries()
    {
        // Without it Android 11+ hides the package: GetApplicationInfo throws NameNotFoundException on
        // a phone that has Chrome, and the page would report ChromeMissing on the locked phone.
        var manifest = File.ReadAllText(
            Path.Combine(Root, "Ledger.Android", "Platforms", "Android", "AndroidManifest.xml"));

        var queries = Regex.Match(manifest, "<queries>(?<body>.*?)</queries>", RegexOptions.Singleline);
        Assert.True(queries.Success, "AndroidManifest.xml has no <queries> block.");
        Assert.Matches("<package\\s+android:name=\"com\\.android\\.chrome\"\\s*/>", queries.Groups["body"].Value);
    }

    [Fact]
    public void OpeningThePageNeverOpensTheBrowser_OnlyTheButtonsDo()
    {
        var source = PageSource();

        // Exactly one place launches Chrome, inside the one method that opens the hosted link, and it
        // names the package: an unnamed link would resolve to the restricted viewer on the locked phone.
        Assert.Single(Regex.Matches(source, @"\.StartActivity\("));
        var opener = MethodBody(source, "OpenHostedLink");
        Assert.Contains(".StartActivity(", opener);
        Assert.Contains("SetPackage(ConnectBrowserPolicy.ChromePackage)", opener);

        // ...and the only callers of that method are click handlers.
        var callers = Regex.Matches(source, @"(?<!void\s)\bOpenHostedLink\(")
            .Select(m => m.Index)
            .Select(i => EnclosingMethod(source, i))
            .Where(name => name != "OpenHostedLink")
            .Distinct()
            .ToList();
        Assert.NotEmpty(callers);
        Assert.All(callers, name => Assert.StartsWith("On", name));
        Assert.All(callers, name => Assert.EndsWith("Clicked", name));
    }

    [Fact]
    public void TheHeaderIsConnectABank_AndTheAccountsTabIsLit()
    {
        var source = PageSource();

        Assert.Contains("Wrap(LedgerArea.Accounts, \"Connect a bank\")", source);
        Assert.DoesNotContain("Wrap(LedgerArea.Settings", source);
        Assert.DoesNotContain("Connect an account", source);
        Assert.DoesNotContain("Reached only from Settings", source);
    }

    [Fact]
    public void BothConnectEntryPointsCheckThatPlaidIsConfigured()
    {
        // Bug: the chooser (and the Waiting page's reconnect) pushed the page with no keys set, and
        // the owner got a raw "Couldn't start Plaid Link: ..." error instead of a plain sentence.
        // SetupPage already checks SettingsStore.IsConfigured before pushing; so does the one shared
        // push both of these now go through.
        var source = MainPageSource();

        var push = MethodBody(source, "PushPlaidLinkAsync");
        Assert.Contains("SettingsStore.IsConfigured", push);
        Assert.Contains("ConnectCopy.NotConfigured", push);

        var reconnect = MethodBody(source, "OpenReconnectFromNoticeAsync");
        Assert.Contains("PushPlaidLinkAsync(", reconnect);
        var chooser = MethodBody(source, "OnPrimaryBarButtonClicked");
        Assert.Contains("PushPlaidLinkAsync(", chooser);

        // No other route to the page that skips the check.
        Assert.Single(Regex.Matches(source, @"GetRequiredService<PlaidLinkPage>"));
    }

    [Fact]
    public void ReconnectStillRunsInUpdateMode_ThroughTheSharedPush()
    {
        // D150 (G16): the reconnect must still hand the existing access token to the page.
        var push = MethodBody(MainPageSource(), "PushPlaidLinkAsync");
        Assert.Contains("BeginUpdateMode(", push);
    }

    // ---- reading C# without a parser: brace-match from the method's declaration ----

    private static string MethodBody(string source, string name)
    {
        var declaration = Regex.Match(
            source, @"^[ \t]*(?:(?:public|private|protected|internal|static|async|override)\s+)+[\w<>\[\]?,\s]+?\b"
                + Regex.Escape(name) + @"\s*\(", RegexOptions.Multiline);
        Assert.True(declaration.Success, $"No method named {name}.");

        var open = source.IndexOf('{', declaration.Index);
        var arrow = source.IndexOf("=>", declaration.Index, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
        {
            return source[arrow..source.IndexOf(';', arrow)];
        }

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"Unbalanced braces in {name}.");
    }

    private static string EnclosingMethod(string source, int index)
    {
        var declarations = Regex.Matches(
            source, @"^[ \t]*(?:(?:public|private|protected|internal|static|async|override)\s+)+[\w<>\[\]?,\s]+?\b(?<name>\w+)\s*\(",
            RegexOptions.Multiline);
        return declarations.Where(m => m.Index < index).Last().Groups["name"].Value;
    }
}
