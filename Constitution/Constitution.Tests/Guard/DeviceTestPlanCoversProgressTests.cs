using System.Text.RegularExpressions;

namespace Constitution.Tests.Guard;

/// <summary>
/// The device test plan (C-13.1.1) is the owner's checklist for everything the tests could only prove
/// against fakes. PROGRESS marks those rows "review (Device)"; a row that is waiting on the owner's phone
/// but missing from the plan would simply never be checked, and nothing else would notice. This reads
/// PROGRESS and the plan by content and fails when a pending id is not named in the plan.
/// </summary>
public class DeviceTestPlanCoversProgressTests
{
    private const string PlanFile = "CONSTITUTION-P13-DEVICE-TEST-PLAN.md";
    private const string ProgressFile = "CONSTITUTION-P13-PROGRESS.md";

    private static readonly Regex PbiId = new(@"C-\d+\.\d+\.\d+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>The PBI ids of every table row whose status column names the Device level.</summary>
    internal static IReadOnlySet<string> PendingIds(string progress)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var line in progress.Split('\n'))
        {
            if (!line.StartsWith("| ", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('|').Select(cell => cell.Trim()).ToArray();
            // cells[0] is empty (before the first bar), cells[1] the PBI, cells[2] the status.
            if (cells.Length < 4 || !cells[2].Contains("Device", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in PbiId.Matches(cells[1]))
            {
                ids.Add(match.Value);
            }
        }

        return ids;
    }

    internal static IReadOnlyList<string> Missing(IReadOnlySet<string> pending, string plan) =>
        pending.Where(id => !Regex.IsMatch(plan, Regex.Escape(id) + @"(?![\d.]*\d)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))).ToList();

    private static string Read(string name) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), name));

    [Fact]
    public void PendingIdsAreReadFromDeviceRowsOnly()
    {
        const string table = "| PBI | Status |\n| C-1.1.1 | review | x |\n| C-2.2.2 (head) and C-2.2.3 | review (Device) | y |\n";
        var ids = PendingIds("| PBI | Status | Commit | Red | Verify |\n| C-1.1.1 | review | a | b | c |\n| C-2.2.2 and C-2.2.3 | review (Device) | a | b | c |\n" + table);
        Assert.Equal(new[] { "C-2.2.2", "C-2.2.3" }, ids.ToArray());
    }

    [Fact]
    public void AnIdMissingFromThePlanIsReportedAndALongerIdDoesNotCoverIt()
    {
        var pending = new SortedSet<string>(StringComparer.Ordinal) { "C-6.1.3", "C-7.1.1" };
        var missing = Missing(pending, "Covers C-6.1.31 and C-7.1.1.");
        Assert.Equal(new[] { "C-6.1.3" }, missing.ToArray());
    }

    [Fact]
    public void ProgressHasDevicePbisToCover()
    {
        Assert.NotEmpty(PendingIds(Read(ProgressFile)));
    }

    [Fact]
    public void EveryDeviceCheckPendingPbiAppearsInTheDeviceTestPlan()
    {
        var plan = Read(PlanFile);
        var missing = Missing(PendingIds(Read(ProgressFile)), plan);
        Assert.True(missing.Count == 0, "Not in " + PlanFile + ": " + string.Join(", ", missing));
    }
}
