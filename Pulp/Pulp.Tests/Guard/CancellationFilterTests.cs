using System.Text.RegularExpressions;

namespace Pulp.Tests.Guard;

/// <summary>
/// "catch when not OperationCanceledException" treated a Drive/HTTP timeout (a TaskCanceledException) as
/// the user leaving, rethrew it from a fire-and-forget loop and killed sync for the process, or left a page
/// on "Opening the vault…" forever. The decision whether a cancellation was asked for lives in
/// <c>CancellationVerdict.IsUserCancel</c>; the head must use it and never filter on the type alone.
/// </summary>
public class CancellationFilterTests
{
    private static readonly Regex TypeOnlyFilter = new(@"when\s*\(\s*\w+\s+is\s+not\s+(System\.)?OperationCanceledException\s*\)", RegexOptions.Compiled);

    private static IReadOnlyList<(string Path, string Text)> Head() =>
        PulpSource.Files(".cs")
            .Select(file => (Path: PulpSource.Relative(file), Text: File.ReadAllText(file)))
            .Where(file => file.Path.StartsWith("Pulp.Android/", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void NoHeadFileFiltersOnTheCancellationTypeAlone()
    {
        var head = Head();
        Assert.NotEmpty(head);

        var offenders = head.Where(file => TypeOnlyFilter.IsMatch(file.Text))
            .Select(file => file.Path)
            .ToList();

        Assert.True(offenders.Count == 0, "These catch a timeout as if the user had left: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheHelperIsActuallyUsedWhereTheSyncAndVaultPathsCatch()
    {
        var head = Head();
        var callers = head.Where(file => file.Text.Contains("CancellationVerdict.IsUserCancel(", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToList();

        foreach (var required in new[]
        {
            "Pulp.Android/App/PulpServices.cs",
            "Pulp.Android/App/VaultPulpKeySource.cs",
            "Pulp.Android/Vault/PulpVault.cs",
            "Pulp.Android/Vault/VaultActivity.cs",
            "Pulp.Android/FirstRun/FirstRunActivity.cs",
        })
        {
            Assert.Contains(required, callers);
        }
    }
}
