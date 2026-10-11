using System.Text.RegularExpressions;
using Pulp.Domain;

namespace Pulp.Tests.Guard;

/// <summary>
/// G1 — Pulp's frozen surface. Nothing fails at build time if one of these moves, and nothing
/// fails loudly at run time either: the phone keeps the old app and never sees another update,
/// a worker quietly stops running, or the app comes up with empty settings. These tests fail
/// instead.
///
/// <para>The expected values are typed out as literals on purpose. Everywhere else a second copy
/// of a constant is the disease; here it is the point, because the guard exists to notice the
/// value moving, so it has to carry its own record of what the value was.</para>
/// </summary>
public class PulpInvariantsTests
{
    // ---- the package id ----

    /// <summary>
    /// A device recognises an update only when the package name is unchanged, and the
    /// device-owner update channel installs by package id. This is the app's address.
    /// </summary>
    [Fact]
    public void ApplicationIdIsFrozen()
    {
        Assert.Equal("com.storyworthtelling.pulp", PulpIdentity.ApplicationId);
    }

    [Fact]
    public void TheHeadDeclaresTheFrozenApplicationId()
    {
        var head = HeadProject();
        var declared = Regex.Match(File.ReadAllText(head), "<ApplicationId>([^<]*)</ApplicationId>");

        Assert.True(declared.Success, $"{PulpSource.Relative(head)} declares no <ApplicationId>.");
        Assert.Equal(PulpIdentity.ApplicationId, declared.Groups[1].Value);
    }

    /// <summary>
    /// The id lives in one constant. P8 found Plate's id copied into five files with only one of
    /// them guarded, so four could drift in silence.
    /// </summary>
    [Fact]
    public void TheApplicationIdIsNotCopiedAroundTheCode()
    {
        var files = PulpSource.Files(".cs");
        Assert.NotEmpty(files);

        var stray = files
            .Where(file => File.ReadAllText(file).Contains(PulpIdentity.ApplicationId, StringComparison.Ordinal))
            .Where(file => !PulpSource.IsGuard(file, "PulpIdentity.cs")
                && !PulpSource.IsGuard(file, "PulpInvariantsTests.cs"))
            .Select(PulpSource.Relative)
            .ToList();

        Assert.True(stray.Count == 0,
            "The application id is copied instead of referenced; use PulpIdentity.ApplicationId:\n"
            + string.Join("\n", stray));
    }

    // ---- registered Java names ----

    [Fact]
    public void MainActivityNameIsFrozen()
    {
        Assert.Equal("com.storyworthtelling.pulp.MainActivity", PulpIdentity.MainActivityName);
    }

    /// <summary>
    /// Without an explicit registration, .NET for Android generates the activity's Java name from
    /// the assembly and namespace, so a rename would move it and the launcher's shortcut with it.
    /// </summary>
    [Fact]
    public void MainActivityIsRegisteredUnderTheFrozenName()
    {
        var activity = PulpSource.Files(".cs")
            .SingleOrDefault(file => Path.GetFileName(file) == "MainActivity.cs");
        Assert.True(activity is not null, "MainActivity.cs was not found under Pulp/.");

        Assert.Contains("Name = PulpIdentity.MainActivityName", File.ReadAllText(activity!), StringComparison.Ordinal);
    }

    /// <summary>
    /// WorkManager persists a scheduled row by the worker's <b>Java</b> class name, and periodic
    /// work is enqueued with <c>Keep</c>: move the class and WorkManager wakes, fails to build a
    /// class that no longer exists, and the Friday stack simply never arrives again.
    /// </summary>
    [Theory]
    [InlineData(nameof(PulpIdentity.FridayStackWorkerName), "com.storyworthtelling.pulp.FridayStackWorker")]
    [InlineData(nameof(PulpIdentity.WeeklyRefreshWorkerName), "com.storyworthtelling.pulp.WeeklyRefreshWorker")]
    [InlineData(nameof(PulpIdentity.DailyChangesWorkerName), "com.storyworthtelling.pulp.DailyChangesWorker")]
    public void WorkerJavaNamesAreFrozen(string constant, string expected)
    {
        Assert.Equal(expected, ConstantValue(constant));
    }

    /// <summary>
    /// The unique work name is persisted beside the class name: change it and the old schedule is
    /// never replaced, so the phone runs two schedules or none.
    /// </summary>
    [Theory]
    [InlineData(nameof(PulpIdentity.FridayStackWorkName), "pulp-friday-stack")]
    [InlineData(nameof(PulpIdentity.WeeklyRefreshWorkName), "pulp-weekly-refresh")]
    [InlineData(nameof(PulpIdentity.DailyChangesWorkName), "pulp-daily-changes")]
    public void UniqueWorkNamesAreFrozen(string constant, string expected)
    {
        Assert.Equal(expected, ConstantValue(constant));
    }

    // ---- the household file ----

    /// <summary>
    /// Found by name on first discovery. Rename it and each install looks for a file the other
    /// never wrote, and the household quietly splits in two.
    /// </summary>
    [Fact]
    public void HouseholdFileNameIsFrozen()
    {
        Assert.Equal("storyworthtelling-pulp-household.json", PulpIdentity.HouseholdFileName);
    }

    // ---- stored settings ----

    /// <summary>
    /// A preference key derived with <c>nameof(...)</c> renames itself when the property does, and
    /// the stored value is then simply not found (Folio's and Foreword's lesson). Keys are literals.
    /// </summary>
    [Fact]
    public void NoPreferenceKeyIsDerivedFromAnIdentifier()
    {
        var files = PulpSource.Files(".cs");
        Assert.NotEmpty(files);

        var keyFromName = new Regex(
            @"\.(Get|Put)(String|Int|Long|Boolean|Float|StringSet)\(\s*nameof\(|Preferences\.(Get|Set)\(\s*nameof\(");

        var offenders = files
            .Where(file => !PulpSource.IsGuard(file, "PulpInvariantsTests.cs"))
            .Where(file => keyFromName.IsMatch(File.ReadAllText(file)))
            .Select(PulpSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A preference key is derived from an identifier; make it a string literal:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// "Hide watched" is remembered per playlist on this phone (D292). The preferences file and the key
    /// prefix are where an upgraded phone finds what the owner set; rename either and every playlist
    /// quietly forgets its setting.
    /// </summary>
    [Fact]
    public void HideWatchedPreferencesAreFrozen()
    {
        var preference = typeof(PulpIdentity).Assembly.GetType("Pulp.Domain.Library.HideWatchedPreference");
        Assert.True(preference is not null, "Pulp.Domain.Library.HideWatchedPreference was not found.");
        Assert.Equal("pulp-playlist-hide-watched", ConstantOf(preference!, "FileName"));
        Assert.Equal("hide-watched:", ConstantOf(preference!, "KeyPrefix"));
    }

    /// <summary>
    /// The store opens the frozen file and keys by the frozen key, and keeps the setting on this phone
    /// only — never in the shared household file, which the other install reads (D292).
    /// </summary>
    [Fact]
    public void TheHideWatchedStoreUsesTheFrozenNamesAndStaysOnThisPhone()
    {
        var store = PulpSource.Files(".cs")
            .SingleOrDefault(file => PulpSource.Relative(file) == "Pulp.Android/Playlists/HideWatchedStore.cs");
        Assert.True(store is not null, "Pulp.Android/Playlists/HideWatchedStore.cs was not found.");

        var source = File.ReadAllText(store!);
        Assert.Contains("GetSharedPreferences(HideWatchedPreference.FileName", source, StringComparison.Ordinal);
        Assert.Contains("HideWatchedPreference.Key(", source, StringComparison.Ordinal);
        // The household file is reached only through PulpServices; the store must not hold one.
        Assert.DoesNotContain("PulpServices", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HouseholdAsync", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the viewer opened or folded on the Sports tab is remembered on this phone (D241). The
    /// preferences file and the key prefix are where an upgraded phone finds it; rename either and every
    /// league and team goes back to the starting rule.
    /// </summary>
    [Fact]
    public void SportsFoldPreferencesAreFrozen()
    {
        var preference = typeof(PulpIdentity).Assembly.GetType("Pulp.Domain.Sports.SportsFoldPreference");
        Assert.True(preference is not null, "Pulp.Domain.Sports.SportsFoldPreference was not found.");
        Assert.Equal("pulp-sports-fold", ConstantOf(preference!, "FileName"));
        Assert.Equal("fold:", ConstantOf(preference!, "KeyPrefix"));
    }

    /// <summary>The fold store opens the frozen file, keys by the frozen prefix, and stays on this phone only.</summary>
    [Fact]
    public void TheSportsFoldStoreUsesTheFrozenNamesAndStaysOnThisPhone()
    {
        var store = PulpSource.Files(".cs")
            .SingleOrDefault(file => PulpSource.Relative(file) == "Pulp.Android/Sports/SportsFoldStore.cs");
        Assert.True(store is not null, "Pulp.Android/Sports/SportsFoldStore.cs was not found.");

        var source = File.ReadAllText(store!);
        Assert.Contains("GetSharedPreferences(SportsFoldPreference.FileName", source, StringComparison.Ordinal);
        Assert.Contains("SportsFoldPreference.KeyPrefix", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PulpServices", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HouseholdAsync", source, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static string ConstantOf(Type type, string name)
    {
        var field = type.GetField(name);
        Assert.True(field is not null && field.IsLiteral, $"{type.Name}.{name} is not a const.");
        return (string)field!.GetRawConstantValue()!;
    }

    private static string ConstantValue(string name)
    {
        var field = typeof(PulpIdentity).GetField(name);
        Assert.True(field is not null && field.IsLiteral, $"PulpIdentity.{name} is not a const.");
        return (string)field!.GetRawConstantValue()!;
    }

    private static string HeadProject()
    {
        var head = PulpSource.Files(".csproj")
            .SingleOrDefault(file => Path.GetFileName(file) == "Pulp.Android.csproj");
        Assert.True(head is not null, "Pulp.Android.csproj was not found under Pulp/.");
        return head!;
    }
}
