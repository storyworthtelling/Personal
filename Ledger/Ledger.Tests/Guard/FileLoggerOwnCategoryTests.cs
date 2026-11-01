using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// The refresh log keeps this app's own categories at Information and everything else at Warning+.
/// "Own" is decided by the category's prefix, which is the root namespace. The rename to Index moved
/// every namespace from <c>Ledger.*</c> to <c>Index.*</c>; a prefix left behind quietly drops every
/// Information-level line from the workers and services, which is the whole of the diagnostic trail,
/// with no error anywhere.
/// </summary>
public class FileLoggerOwnCategoryTests
{
    [Fact]
    public void TheOwnCategoryPrefixIsTheAppsRootNamespace()
    {
        // The shared file logger takes the rule as an option; Index's is set where the head installs it.
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android", "MauiProgram.cs");
        var source = File.ReadAllText(path);

        Assert.Contains("category.StartsWith(\"Index\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("category.StartsWith(\"Ledger\"", source, StringComparison.Ordinal);
    }
}
