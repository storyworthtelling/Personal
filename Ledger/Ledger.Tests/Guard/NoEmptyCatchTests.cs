using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Guard G13 (D145): a caught exception is never dropped with nothing to show for it — not even
/// in Diagnostics. Reads every <c>.cs</c> file in Index's own source (Domain, Infrastructure, the
/// Android head) by text, the same content-reading pattern the rest of this folder uses for the
/// Android-only files a desktop-TFM test project cannot load as types, and fails on a catch block
/// whose body is completely empty — no statement, no comment, nothing. That is the exact shape of
/// bug the plan names for this guard: "Swallow a refresh exception", with no trace anywhere.
///
/// <para>A catch that explains its own best-effort choice with a comment — "Drive being
/// unreachable just means can't tell right now" — is <b>not</b> what this guard is for. That is a
/// documented, reviewed decision, and it is the suite's own idiom everywhere (Post, Foreword and
/// Folio all catch and deliberately do nothing on a background sync's best-effort path, each with
/// a comment saying so). This guard is narrower, and it is also the only shape the two existing
/// legitimate exceptions below actually take.</para>
///
/// <para>Two files are exempt outright: <c>PipelineFileLogger.cs</c> IS the file logger — an
/// empty catch there avoids the logger recursively failing to log its own failure to log — and
/// <c>MainApplication.cs</c>'s nested <c>CrashLogger</c> class is the crash logger for the same
/// reason. Everywhere else, a truly empty catch is exactly the D145 bug.</para>
/// </summary>
public class NoEmptyCatchTests
{
    private static readonly string[] ExemptFiles = ["PipelineFileLogger.cs"];
    private static readonly string[] ExemptEnclosingTypes = ["CrashLogger"];

    private static readonly Regex EmptyCatch = new(@"catch(\s*\([^)]*\))?\s*\{\s*\}", RegexOptions.Compiled);

    [Fact]
    public void NoCatchBlockIsCompletelyEmptyOutsideTheLogger()
    {
        var violations = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (ExemptFiles.Contains(Path.GetFileName(file)))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match match in EmptyCatch.Matches(text))
            {
                var enclosingType = EnclosingTypeName(text, match.Index);
                if (enclosingType is not null && ExemptEnclosingTypes.Contains(enclosingType))
                {
                    continue;
                }

                var line = text[..match.Index].Count(c => c == '\n') + 1;
                violations.Add($"{RepoRelative(file)}:{line}");
            }
        }

        Assert.True(violations.Count == 0,
            "A catch block here does absolutely nothing — no log, no comment, no state change — so a "
            + "real failure vanishes with no trace, not even in Diagnostics:\n" + string.Join("\n", violations));
    }

    /// <summary>
    /// The guard's own self-check (write-it-first rule): reproduces the exact shape of bug this
    /// guard exists to catch, and proves the pattern it is checked with actually matches it.
    /// </summary>
    [Fact]
    public void TheGuardsOwnPatternMatchesATrulyEmptyCatchBlock()
    {
        const string reintroducedBug = "catch (Exception) { }";
        Assert.Matches(EmptyCatch, reintroducedBug);

        const string documentedBestEffort = "catch { /* Drive being unreachable just means can't tell right now */ }";
        Assert.DoesNotMatch(EmptyCatch, documentedBestEffort);
    }

    // ---- helpers ----

    private static string? EnclosingTypeName(string text, int position)
    {
        var matches = Regex.Matches(text[..position], @"\bclass\s+(\w+)");
        return matches.Count == 0 ? null : matches[^1].Groups[1].Value;
    }

    private static IEnumerable<string> SourceFiles()
    {
        var root = DesignTestHelpers.LedgerRoot();
        foreach (var project in new[] { "Ledger.Domain", "Ledger.Infrastructure", "Ledger.Android" })
        {
            var projectRoot = Path.Combine(root, project);
            if (!Directory.Exists(projectRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/") || normalized.Contains("/Migrations/"))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string RepoRelative(string path) =>
        Path.GetRelativePath(Path.GetDirectoryName(DesignTestHelpers.LedgerRoot())!, path).Replace('\\', '/');
}
