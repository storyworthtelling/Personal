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
        // Added with the weekly workout worker shell (P13): a deliberate edit, approved by the owner.
        "com.storyworthtelling.constitution.WorkoutWeekWorker",
        // Added with the hourly meal-week worker (P13): a deliberate edit.
        "com.storyworthtelling.constitution.MealWeekWorker",
    ];

    private static readonly string[] ExpectedActivities =
    [
        "com.storyworthtelling.constitution.MainActivity",
        // Added with the Health Connect manifest (C-7.1.1): the permissions-rationale page, and the
        // alias Android 14 resolves VIEW_PERMISSION_USAGE through. A deliberate edit.
        "com.storyworthtelling.constitution.HealthRationaleActivity",
        "com.storyworthtelling.constitution.ViewPermissionUsageActivity",
    ];

    private static readonly string[] ExpectedReceivers =
    [
        // Added with the budget contract (PBI C-11.1.1): Index addresses this exact component. A deliberate edit, owner-approved.
        "com.storyworthtelling.constitution.BudgetReceiver",
    ];

    private static readonly string[] ExpectedDeclaredPermissions =
    [
        // The signature permission that protects the receiver above. Declared by Constitution, held by Index. A deliberate edit.
        "com.storyworthtelling.constitution.RECEIVE_BUDGET",
    ];

    private static readonly string[] ExpectedWorkNames =
    [
        "constitution-read-back",
        "constitution-body-sync",
        // Added with the weekly workout worker shell (P13): a deliberate edit, approved by the owner.
        "constitution-workout-week",
        // Added with the hourly meal-week worker (P13): a deliberate edit.
        "constitution-meal-week",
        // Added with the recipe-hour request (D120): a second hourly request of the same worker, aimed at thirty-five past, a deliberate edit.
        "constitution-meal-recipe-hour",
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
        // Added with Settings (C-8.1.1): which equipment this install has ticked. A deliberate edit.
        "equipment-owned",
        // Added with First run (C-8.3.1): the owner has explicitly confirmed the equipment checklist. A deliberate edit.
        "equipment-confirmed",
        // Added with the Release A review (S8): when a login was saved or the tablet paired by hand, so an
        // older failure is not held against the new credential. A deliberate edit.
        "credential-sleep-number-saved-at",
        "credential-remarkable-saved-at",
        // Added with reading the signals (C-7.1.2): which side of the bed is this install's owner, so the bed's
        // night can fill one the watch missed. A deliberate edit.
        "sleeper-side",
    ];

    /// <summary>
    /// The household file is found by name in the owner's Drive and shared by that name: rename it and the
    /// partner's phone looks for a file that is not there while the old one sits unread. Added with the
    /// household file's kinds (C-6.1.2), a deliberate edit; the kinds themselves are held by
    /// <c>ConstitutionKindsTests</c>.
    /// </summary>
    private const string ExpectedHouseholdFileName = "storyworthtelling-constitution-household.json";

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

    [Fact]
    public void HouseholdFileNameIsFrozen()
    {
        Assert.Equal(ExpectedHouseholdFileName, ConstitutionIdentity.HouseholdFileName);
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

    /// <summary>
    /// The rationale activity is what Health Connect opens when the owner asks why Constitution wants
    /// a permission, and it finds it by intent filter on the merged manifest's registered names. Move
    /// either name and the request opens nothing, with no error anywhere.
    /// </summary>
    [Fact]
    public void ActivitiesAreFrozen()
    {
        Assert.Equal(ExpectedActivities, ConstitutionIdentity.Activities);
        Assert.Equal("com.storyworthtelling.constitution.HealthRationaleActivity", ConstitutionIdentity.HealthRationaleActivityName);
        Assert.Equal("com.storyworthtelling.constitution.ViewPermissionUsageActivity", ConstitutionIdentity.PermissionUsageAliasName);
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

    /// <summary>
    /// Index sends the month's grocery budget to this exact component, under this action, behind this signature permission.
    /// Move any of the three and the broadcast reaches nothing (or is refused) with no error on either phone.
    /// </summary>
    [Fact]
    public void BudgetContractNamesAreFrozen()
    {
        Assert.Equal(ExpectedReceivers, ConstitutionIdentity.Receivers);
        Assert.Equal(ExpectedDeclaredPermissions, ConstitutionIdentity.DeclaredPermissions);
        Assert.Equal("com.storyworthtelling.constitution.BudgetReceiver", ConstitutionIdentity.BudgetReceiverName);
        Assert.Equal("com.storyworthtelling.constitution.action.BUDGET", ConstitutionIdentity.BudgetAction);
        Assert.Equal("com.storyworthtelling.constitution.RECEIVE_BUDGET", ConstitutionIdentity.BudgetPermission);
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
        Assert.NotEmpty(ConstitutionIdentity.Activities);
        Assert.NotEmpty(ConstitutionIdentity.Workers);
        Assert.NotEmpty(ConstitutionIdentity.WorkNames);
        Assert.NotEmpty(ConstitutionIdentity.PreferenceKeys);
        Assert.NotEmpty(ConstitutionIdentity.Receivers);
        Assert.NotEmpty(ConstitutionIdentity.DeclaredPermissions);

        Assert.NotEmpty(ExpectedActivities);
        Assert.NotEmpty(ExpectedWorkers);
        Assert.NotEmpty(ExpectedWorkNames);
        Assert.NotEmpty(ExpectedPreferenceKeys);
        Assert.NotEmpty(ExpectedReceivers);
        Assert.NotEmpty(ExpectedDeclaredPermissions);
        Assert.False(string.IsNullOrWhiteSpace(ConstitutionIdentity.HouseholdFileName));
        Assert.False(string.IsNullOrWhiteSpace(ExpectedHouseholdFileName));

        Assert.All(ConstitutionIdentity.Activities.Concat(ConstitutionIdentity.Receivers).Concat(ConstitutionIdentity.DeclaredPermissions).Concat(ConstitutionIdentity.Workers).Concat(ConstitutionIdentity.WorkNames).Concat(ConstitutionIdentity.PreferenceKeys),
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
