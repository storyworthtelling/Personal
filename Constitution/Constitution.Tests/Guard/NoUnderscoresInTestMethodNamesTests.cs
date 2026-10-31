using System.Text.RegularExpressions;

namespace Constitution.Tests.Guard;

/// <summary>
/// The house rule is no underscores in any identifier (CLAUDE.md, Code Style), and that includes test
/// method names: the repo's tests read as sentences in PascalCase, not Given_When_Then. Android resource
/// ids such as <c>area_settings</c> are generated names, not methods, and are not what this reads.
/// </summary>
public class NoUnderscoresInTestMethodNamesTests
{
    // A method declaration (any access) returning Task, Task of something, or void, whose name holds an underscore.
    private static readonly Regex Declaration = new(
        @"(?:public|private|internal|protected)\s+(?:static\s+)?(?:async\s+)?(?:Task|void)(?:<[^>\r\n]*>)?\s+\w*_\w*\s*\(",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>The offending declarations in one source text, as the line they sit on.</summary>
    internal static IReadOnlyList<string> Offenders(string source) =>
        Declaration.Matches(source).Select(match => match.Value).ToList();

    private static IReadOnlyList<string> TestFiles()
    {
        var tests = Path.Combine(ConstitutionSource.Root(), "Constitution.Tests");
        return ConstitutionSource.Files(".cs")
            .Where(file => file.StartsWith(tests, StringComparison.Ordinal))
            .ToList();
    }

    [Fact]
    public void NoTestMethodNameHoldsAnUnderscore()
    {
        var files = TestFiles();
        Assert.True(files.Count >= 30, $"The guard found only {files.Count} test files; it is not reading the real suite.");

        var offenders = files
            .SelectMany(file => Offenders(File.ReadAllText(file)).Select(line => ConstitutionSource.Relative(file) + ": " + line))
            .ToList();

        Assert.True(offenders.Count == 0, "Test method names with underscores:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheDetectorFiresOnAPlantedUnderscoreName()
    {
        // Assembled at run time so this file holds no underscored declaration of its own.
        var planted = "    public async Task " + "ASaveAfterARestore" + "_" + "StillWorks()\n    {\n    }\n";
        var plantedVoid = "    public void " + "Plain" + "_" + "Name()\n";
        var plantedGeneric = "    public static Task<int> " + "Also" + "_" + "Bad()\n";

        Assert.Single(Offenders(planted));
        Assert.Single(Offenders(plantedVoid));
        Assert.Single(Offenders(plantedGeneric));
    }

    [Fact]
    public void TheDetectorLeavesPascalCaseAndResourceIdsAlone()
    {
        Assert.Empty(Offenders("    public async Task TheNameIsAllWords()\n    {\n    }\n"));
        Assert.Empty(Offenders("    public void AlsoFine(int a_b)\n"));
        Assert.Empty(Offenders("    public const int Glyph = Resource.Drawable.area_settings;\n"));
    }
}
