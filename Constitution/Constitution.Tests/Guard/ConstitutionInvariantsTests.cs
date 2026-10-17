using System.Text.RegularExpressions;
using Constitution.Domain;

namespace Constitution.Tests.Guard;

/// <summary>
/// G1 — Constitution's frozen surface. Nothing fails at build time if one of these moves, and
/// nothing fails loudly at run time either: the phone keeps the old app and never sees another
/// update, a worker quietly stops running, or the app comes up with empty settings. These tests
/// fail instead.
///
/// <para>The expected values are typed out as literals on purpose. Everywhere else a second copy
/// of a constant is the disease; here it is the point, because the guard exists to notice the
/// value moving, so it has to carry its own record of what the value was. Adding a worker, a
/// work name or a preference key later is a deliberate edit to the expected lists below.</para>
/// </summary>
public class ConstitutionInvariantsTests
{
    private static readonly string[] ExpectedWorkers =
    [
        "com.storyworthtelling.constitution.ReadBackWorker",
        "com.storyworthtelling.constitution.BodySyncWorker",
    ];

    private static readonly string[] ExpectedWorkNames =
    [
        "constitution-read-back",
        "constitution-body-sync",
    ];

    private static readonly string[] ExpectedPreferenceKeys =
    [
        "constitution-settings",
        // Added with the shell (C-3.1.1): which pillars this install shows. One key per pillar.
        "pillar-workouts",
        "pillar-meals",
        "pillar-body",
        // Added with the Android vault services (C-4): the connections the vault restores. A deliberate edit.
        "connection-sleep-number-login",
        "connection-sleep-number-password",
        "connection-remarkable-token",
        "connection-claude-key",
        "connection-gemini-key",
        "connection-health-connect-choices",
        "connection-work-schedule",
    ];

    // ---- the package id ----

    /// <summary>
    /// A device recognises an update only when the package name is unchanged, and the
    /// device-owner update channel installs by package id. This is the app's address.
    /// </summary>
    [Fact]
    public void AppIdIsFrozen()
    {
        Assert.Equal("com.storyworthtelling.constitution", ConstitutionIdentity.ApplicationId);
    }

    /// <summary>The head's csproj and manifest are read by content: a build never compares them.</summary>
    [Fact]
    public void HeadUsesTheFrozenAppId()
    {
        var head = HeadFile("Constitution.Android.csproj");
        var declared = Regex.Match(File.ReadAllText(head), "<ApplicationId>([^<]*)</ApplicationId>");
        Assert.True(declared.Success, $"{ConstitutionSource.Relative(head)} declares no <ApplicationId>.");
        Assert.Equal(ConstitutionIdentity.ApplicationId, declared.Groups[1].Value);

        var manifest = HeadFile("AndroidManifest.xml");
        var package = Regex.Match(File.ReadAllText(manifest), "<manifest\\b[^>]*\\spackage=\"([^\"]*)\"");
        Assert.True(package.Success, $"{ConstitutionSource.Relative(manifest)} declares no package attribute.");
        Assert.Equal(ConstitutionIdentity.ApplicationId, package.Groups[1].Value);
    }

    /// <summary>
    /// The id lives in one constant. P8 found Plate's id copied into five files with only one of
    /// them guarded, so four could drift in silence.
    /// </summary>
    [Fact]
    public void TheApplicationIdIsNotCopiedAroundTheCode()
    {
        var files = ConstitutionSource.Files(".cs");
        Assert.NotEmpty(files);

        var stray = files
            .Where(file => File.ReadAllText(file).Contains("com.storyworthtelling.constitution", StringComparison.Ordinal))
            .Where(file => !ConstitutionSource.IsGuard(file, "ConstitutionIdentity.cs")
                && !ConstitutionSource.IsGuard(file, "ConstitutionInvariantsTests.cs"))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(stray.Count == 0,
            "The application id is copied instead of referenced; use ConstitutionIdentity.ApplicationId:\n"
            + string.Join("\n", stray));
    }

    // ---- registered Java names ----

    /// <summary>
    /// Without an explicit registration, .NET for Android generates the activity's Java name from
    /// the assembly and namespace, so a rename would move it and the launcher's shortcut with it.
    /// </summary>
    [Fact]
    public void MainActivityNameIsFrozen()
    {
        Assert.Equal("com.storyworthtelling.constitution.MainActivity", ConstitutionIdentity.MainActivityName);

        var activity = HeadFile("MainActivity.cs");
        Assert.Contains("Name = ConstitutionIdentity.MainActivityName", File.ReadAllText(activity), StringComparison.Ordinal);
    }

    // ---- workers, work names, preference keys ----

    /// <summary>
    /// WorkManager persists a scheduled row by the worker's <b>Java</b> class name, and periodic
    /// work is enqueued with <c>Keep</c>: move the class and WorkManager wakes, fails to build a
    /// class that no longer exists, and the job simply never runs again.
    /// </summary>
    [Fact]
    public void WorkersAreFrozen()
    {
        Assert.Equal(ExpectedWorkers, ConstitutionIdentity.Workers);
    }

    /// <summary>The unique work name is persisted beside the class name: change it and the phone runs two schedules or none.</summary>
    [Fact]
    public void WorkNamesAreFrozen()
    {
        Assert.Equal(ExpectedWorkNames, ConstitutionIdentity.WorkNames);
    }

    [Fact]
    public void PreferenceKeysAreFrozen()
    {
        Assert.Equal(ExpectedPreferenceKeys, ConstitutionIdentity.PreferenceKeys);
    }

    /// <summary>
    /// A key derived with <c>nameof(...)</c> renames itself when the property does, and the stored
    /// value is then simply not found (Folio's and Foreword's lesson). The identity file is
    /// scanned whole, so no worker name, work name or key can be derived either.
    /// </summary>
    [Fact]
    public void PreferenceKeysAreLiterals()
    {
        var identity = ConstitutionSource.Files(".cs")
            .SingleOrDefault(file => ConstitutionSource.IsGuard(file, "ConstitutionIdentity.cs"));
        Assert.True(identity is not null, "ConstitutionIdentity.cs was not found under Constitution/.");

        var code = Regex.Replace(File.ReadAllText(identity!), @"///.*|//.*", string.Empty);
        Assert.DoesNotContain("nameof(", code, StringComparison.Ordinal);
    }

    /// <summary>A guard over nothing is vacuous: an empty set would pass every test above for the wrong reason.</summary>
    [Fact]
    public void FrozenSetsAreNotEmpty()
    {
        Assert.NotEmpty(ConstitutionIdentity.Workers);
        Assert.NotEmpty(ConstitutionIdentity.WorkNames);
        Assert.NotEmpty(ConstitutionIdentity.PreferenceKeys);

        Assert.NotEmpty(ExpectedWorkers);
        Assert.NotEmpty(ExpectedWorkNames);
        Assert.NotEmpty(ExpectedPreferenceKeys);

        Assert.All(ConstitutionIdentity.Workers.Concat(ConstitutionIdentity.WorkNames).Concat(ConstitutionIdentity.PreferenceKeys),
            value => Assert.False(string.IsNullOrWhiteSpace(value), "A frozen set holds a blank value."));
    }

    // ---- helpers ----

    private static string HeadFile(string fileName)
    {
        var head = ConstitutionSource.Files(Path.GetExtension(fileName))
            .Where(file => Path.GetFileName(file) == fileName)
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .SingleOrDefault();
        Assert.True(head is not null, $"{fileName} was not found under Constitution/Constitution.Android/.");
        return head!;
    }
}
