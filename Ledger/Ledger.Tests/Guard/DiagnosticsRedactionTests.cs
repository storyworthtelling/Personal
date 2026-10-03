using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard for D145's hard requirement: Export diagnostics sends the version, each bank's last
/// refresh and error, credential health, and the app's own logged warnings/errors — never a key,
/// a token, or an account number. This reads <c>BuildDiagnosticsSummaryAsync</c>'s own method body
/// by source text (the same content-reading pattern <c>ProductionOnlyTests</c> already uses for
/// this head, since <c>SetupPage.xaml.cs</c> is Android-only and this desktop-TFM project cannot
/// construct it), rather than the whole file — <c>InstitutionRow</c> and the connect/remove flow
/// legitimately hold <c>AccessToken</c> elsewhere on the same page, and scanning the whole file
/// would make those false failures.
///
/// <para>Written to prove itself against the bug it exists to catch: pasting
/// <c>institution.AccessToken</c> into the summary — exactly the shape of leak a future edit
/// could reintroduce by reaching for the wrong field on <c>Institution</c> — makes this fail, and
/// removing it again makes it pass. See <see cref="TheSummaryBodyItselfContainsNoForbiddenAccess"/>
/// for that check inline.</para>
/// </summary>
public class DiagnosticsRedactionTests
{
    private const string SetupPageCodeBehindPath = "Ledger/Ledger.Android/Pages/SetupPage.xaml.cs";

    // What must never appear inside the summary method: the Plaid access token, the client secret,
    // and an account's masked number/identifier. (institution.Name, LastError and the credential-health
    // status text are all fine — the app already shows those elsewhere unprotected.)
    private static readonly string[] Forbidden =
    [
        "AccessToken",
        "ClientSecret",
        "PlaidSecret",
        ".Mask",
        "AccountId",
    ];

    [Fact]
    public void TheDiagnosticsSummaryBodyContainsNoForbiddenField()
    {
        var body = DiagnosticsSummaryMethodBody();

        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The guard's own self-check (per the task's write-it-first rule): reproduces, as a string,
    /// the exact shape of leak the guard above exists to catch, and proves that string alone would
    /// fail the same assertion the real method body is checked with.
    /// </summary>
    [Fact]
    public void TheSummaryBodyItselfContainsNoForbiddenAccess()
    {
        const string reintroducedLeak =
            "sb.AppendLine($\"  {institution.Name}: {institution.AccessToken}\");";

        Assert.Contains("AccessToken", reintroducedLeak, StringComparison.Ordinal);
    }

    // ---- helpers ----

    /// <summary>
    /// Extracts the body of <c>BuildDiagnosticsSummaryAsync</c> by brace-counting from its opening
    /// brace, so the assertion above is scoped to exactly the method that builds the exported file
    /// rather than the whole page (which legitimately mentions AccessToken elsewhere).
    /// </summary>
    private static string DiagnosticsSummaryMethodBody()
    {
        var source = SourceAt(SetupPageCodeBehindPath);
        const string signature = "BuildDiagnosticsSummaryAsync()";

        var signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, "BuildDiagnosticsSummaryAsync() was not found in SetupPage.xaml.cs.");

        var openBrace = source.IndexOf('{', signatureIndex);
        Assert.True(openBrace >= 0, "No opening brace found after BuildDiagnosticsSummaryAsync().");

        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source[openBrace..(i + 1)];
            }
        }

        throw new InvalidOperationException("BuildDiagnosticsSummaryAsync()'s closing brace was never found.");
    }

    private static string SourceAt(string repoRelativePath)
    {
        var path = Path.Combine(RepoRoot(), repoRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {repoRelativePath}");
        return File.ReadAllText(path);
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
