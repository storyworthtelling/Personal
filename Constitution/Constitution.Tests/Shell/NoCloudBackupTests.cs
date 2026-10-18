using System.Text.RegularExpressions;

namespace Constitution.Tests.Shell;

/// <summary>
/// Android's Auto Backup uploads an app's files and preferences to the Google account and restores
/// them onto any new device, with no restore gate. Constitution keeps health readings, the Sleep
/// Number login and the vault passphrase on the phone, and its only sanctioned way off is the
/// encrypted Body backup (G13, D98). So the app opts out of Auto Backup entirely, and no file it
/// writes is placed in the backed-up part of storage.
/// </summary>
public sealed class NoCloudBackupTests
{
    private static readonly Regex BackedUpDirectory = new(@"(?<!NoBackup)\bFilesDir\b", RegexOptions.Compiled);

    private static IReadOnlyList<string> HeadSources() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).Contains("Constitution.Android", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void TheApplicationOptsOutOfAutoBackup()
    {
        var sources = HeadSources();
        Assert.True(sources.Count >= 10, "The scan should cover the head's sources, found " + sources.Count);

        var declaration = sources
            .Select(File.ReadAllText)
            .FirstOrDefault(text => text.Contains("[assembly: Application(", StringComparison.Ordinal));
        Assert.NotNull(declaration);
        Assert.Matches(@"\[assembly:\s*Application\([^\]]*AllowBackup\s*=\s*false", declaration!);
    }

    [Fact]
    public void NoFileIsPlacedInTheBackedUpDirectory()
    {
        var sources = HeadSources();
        Assert.True(sources.Count >= 10, "The scan should cover the head's sources, found " + sources.Count);

        var offenders = new List<string>();
        foreach (var file in sources)
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                var code = lines[index].TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (BackedUpDirectory.IsMatch(lines[index]))
                {
                    offenders.Add($"{ConstitutionSource.Relative(file)}:{index + 1}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Use NoBackupFilesDir so Auto Backup never carries the file:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("var d = context.FilesDir?.AbsolutePath;", true)]
    [InlineData("var d = context.NoBackupFilesDir?.AbsolutePath;", false)]
    [InlineData("var d = appContext.FilesDir;", true)]
    public void TheDetectorTellsThemApart(string line, bool flagged)
    {
        Assert.Equal(flagged, BackedUpDirectory.IsMatch(line));
    }
}
