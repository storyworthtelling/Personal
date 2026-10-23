using System.Text.RegularExpressions;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>C-5.4.2: no confirm dialog anywhere on the Workouts tab. A skip happens at once and folds the dog-ear; the card is a card.</summary>
public class NoConfirmDialogTests
{
    // Assembled at run time so this guard's own source does not hold what it forbids.
    private static readonly string Dialog = "Dia" + "log";
    private static readonly string Confirm = "Conf" + "irm";
    private static readonly string Sure = "Are you " + "sure";

    internal static IReadOnlyList<string> TabFiles() =>
        ConstitutionSource.Files(".cs")
            .Where(file =>
            {
                var relative = ConstitutionSource.Relative(file);
                return relative == "Constitution.Android/WorkoutsArea.cs"
                    || relative.StartsWith("Constitution.Android/WorkoutsTab/", StringComparison.Ordinal);
            })
            .ToList();

    internal static bool Offends(string source) =>
        Regex.IsMatch(source, Dialog + "|" + Confirm + "|" + Sure, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void TheGuardReadsTheTabAndIsNotVacuous()
    {
        Assert.True(TabFiles().Count >= 6, "The Workouts tab files were not found: " + TabFiles().Count);
    }

    [Fact]
    public void NoTabFileMentionsADialogOrAConfirmation()
    {
        var offenders = TabFiles().Where(file => Offends(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A confirm dialog on the Workouts tab:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("new AlertDia" + "log.Builder(host)")]
    [InlineData("var ok = Conf" + "irm(host);")]
    [InlineData("\"Are you " + "sure?\"")]
    public void TheDetectorFiresOnPlantedConfirmations(string planted)
    {
        Assert.True(Offends(planted), planted);
    }
}
