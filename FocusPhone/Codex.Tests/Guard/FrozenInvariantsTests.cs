using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The in-place-upgrade guard for the Flyleaf (P5) rename pilot. The launcher head
/// rebrands FocusPhone.* → Codex.* and Launcher → Flyleaf, but an installed device
/// only upgrades in place — no uninstall, no re-pairing, the device-owner update
/// channel still recognizing it — while a handful of identifiers stay byte-identical.
/// These tests fail if any of them drift, so the rename can never silently break the
/// home role or the update channel. The assertions read the repo by content (not by
/// project/namespace names), so they keep protecting the same invariants after the
/// folders and namespaces are renamed.
/// </summary>
public class FrozenInvariantsTests
{
    // The app id and owner namespace are the load-bearing constants; a device
    // recognizes an update only when the package name is unchanged.
    [Fact]
    public void LauncherAppIdAndOwnerPrefixAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher", PolicyContext.LauncherPackage);
        Assert.Equal("com.storyworthtelling.", PolicyContext.OwnerPackagePrefix);
        Assert.Equal("com.storyworthtelling.focusphone.communication", PolicyContext.CommunicationPackage);
        Assert.Equal("com.storyworthtelling.focusphone.camera", PolicyContext.CameraPackage);
        Assert.Equal("com.storyworthtelling.focusphone.audio", PolicyContext.AudioPackage);
    }

    // Exactly one head declares the frozen ApplicationId — the launcher. The rename
    // may move/rename the csproj, but the literal must survive it.
    [Fact]
    public void LauncherCsprojDeclaresFrozenApplicationId()
    {
        var declaring = EnumerateRepoFiles("*.csproj")
            .Where(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{PolicyContext.LauncherPackage}</ApplicationId>", StringComparison.Ordinal))
            .ToList();

        Assert.True(declaring.Count == 1,
            $"Exactly one csproj must declare the frozen launcher ApplicationId " +
            $"'{PolicyContext.LauncherPackage}'; found {declaring.Count}:\n" +
            string.Join("\n", declaring.Select(RepoRelative)));
    }

    // The home Activity must keep MainLauncher + CategoryHome, or Flyleaf stops being
    // the device home app after the upgrade.
    [Fact]
    public void HomeActivityKeepsLauncherAndHomeIntent()
    {
        var declaresHome = EnumerateRepoFiles("*.cs").Any(file =>
        {
            var source = File.ReadAllText(file);
            return source.Contains("MainLauncher = true", StringComparison.Ordinal)
                && source.Contains("Intent.CategoryHome", StringComparison.Ordinal);
        });

        Assert.True(declaresHome,
            "No Activity declares 'MainLauncher = true' with 'Intent.CategoryHome' — " +
            "the rebranded launcher would no longer register as the device home app.");
    }

    // The update channel stays the 'apps-latest' rolling release, matched by the owner
    // namespace (not by APK filename), so renaming workflows/assets can't break it.
    [Fact]
    public void UpdateChannelIsAppsLatestByOwnerNamespace()
    {
        var githubRoot = Path.Combine(RepoRoot(), ".github");
        var mentionsAppsLatest = Directory
            .EnumerateFiles(githubRoot, "*.yml", SearchOption.AllDirectories)
            .Any(file => File.ReadAllText(file).Contains("apps-latest", StringComparison.Ordinal));

        Assert.True(mentionsAppsLatest,
            "The 'apps-latest' rolling-release tag is gone from .github — the device-owner update channel would break.");

        // The install decision is package-id based: an owner app upgrades, a stray package is skipped.
        var upgraded = Storyworthtelling.Updates.UpdateScope.Decide(
            Storyworthtelling.Updates.UpdateRole.Custodian, PolicyContext.LauncherPackage,
            [new Storyworthtelling.Updates.InstallCandidate(PolicyContext.LauncherPackage, 2)],
            new Dictionary<string, long> { [PolicyContext.LauncherPackage] = 1 });
        Assert.Single(upgraded);

        var skipped = Storyworthtelling.Updates.UpdateScope.Decide(
            Storyworthtelling.Updates.UpdateRole.Custodian, PolicyContext.LauncherPackage,
            [new Storyworthtelling.Updates.InstallCandidate("com.example.other", 9)],
            new Dictionary<string, long>());
        Assert.Empty(skipped);
    }

    // The device-owner binding is keyed to the admin RECEIVER's component name, and the
    // notification-listener grant to the listener SERVICE's — both provisioned once (adb /
    // QR) against the original names. The rename moved these classes to `namespace Flyleaf`,
    // so their manifest `Name=` must stay the literal old component name, or an upgraded
    // device silently loses device-owner control / notification access. This is exactly why
    // those two classes are NOT renamed; the guard fails if either component name drifts.
    [Fact]
    public void DeviceAdminAndNotificationListenerComponentNamesAreFrozen()
    {
        var adminComponent = $"{PolicyContext.LauncherPackage}.FocusDeviceAdminReceiver";
        var listenerComponent = $"{PolicyContext.LauncherPackage}.TileNotificationListener";

        Assert.Equal(1, CountCsFilesDeclaring(adminComponent, "Name ="));
        Assert.Equal(1, CountCsFilesDeclaring(listenerComponent, "Name ="));
    }

    private static int CountCsFilesDeclaring(string componentName, string attributeHint) =>
        EnumerateRepoFiles("*.cs").Count(file =>
        {
            var source = File.ReadAllText(file);
            return source.Contains($"\"{componentName}\"", StringComparison.Ordinal)
                && source.Contains(attributeHint, StringComparison.Ordinal);
        });

    // The owner namespace is the update channel's whole trust boundary, and it now
    // lives in the shared update library — Codex.Domain keeps its own copy so it
    // stays dependency-free, which means the two can drift. This is the guard that
    // says they cannot.
    [Fact]
    public void OwnerPrefixMatchesTheSharedUpdateLibrary()
    {
        Assert.Equal(Storyworthtelling.Updates.SuiteApps.OwnerPackagePrefix, PolicyContext.OwnerPackagePrefix);
    }

    // ---- Score (P6): the same invariants, for the second head through the rename ----

    // Exactly one head declares the frozen audio ApplicationId. Score renames its
    // folder, csproj and namespace; the package name is what a device matches an
    // update against, so it survives all three.
    [Fact]
    public void AudioCsprojDeclaresFrozenApplicationId()
    {
        var declaring = EnumerateRepoFiles("*.csproj")
            .Where(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{PolicyContext.AudioPackage}</ApplicationId>", StringComparison.Ordinal))
            .ToList();

        Assert.True(declaring.Count == 1,
            $"Exactly one csproj must declare the frozen audio ApplicationId " +
            $"'{PolicyContext.AudioPackage}'; found {declaring.Count}:\n" +
            string.Join("\n", declaring.Select(RepoRelative)));
    }

    // Three registered component names outlive the process that registered them: the
    // media session's foreground SERVICE, which a device may already be running across
    // the upgrade; the WorkManager WORKER, whose scheduled request is persisted by
    // class name in WorkManager's own database; and the DOWNLOAD service, which is a
    // foreground service that can likewise be mid-fetch when the upgrade lands.
    // Renaming any of them strands the thing that refers to it — a service
    // notification, a download in flight, or a Friday run that silently never fires
    // again. This is why those classes carry an explicit Name/Register rather than
    // taking the namespace default.
    [Fact]
    public void RegisteredComponentNamesAreFrozen()
    {
        Assert.Equal(1, CountCsFilesDeclaring($"{PolicyContext.AudioPackage}.PlaybackService", "Name ="));
        Assert.Equal(1, CountCsFilesDeclaring($"{PolicyContext.AudioPackage}.FridayPicksWorker", "Register("));
        Assert.Equal(1, CountCsFilesDeclaring($"{PolicyContext.AudioPackage}.DownloadService", "Name ="));

        // Score's own update worker, for the phone with no custodian. Persisted by
        // class name in WorkManager's database exactly as the Friday picks worker is,
        // so a rename strands a scheduled run that then silently never fires.
        Assert.Equal(1, CountCsFilesDeclaring($"{PolicyContext.AudioPackage}.ScoreUpdateWorker", "Register("));
    }

    // Every on-device store the head owns, by SharedPreferences file name. The file
    // name is the only handle Android gives you on the data, so a renamed constant
    // reads as an empty app — no subscriptions, no resume points, no equalizer.
    //
    // Playlists are the sharpest case, because unlike the queues and the episode cache
    // they are not re-derivable from anything: a hand-made list exists nowhere else.
    // Downloads are the next sharpest — the files survive a rename, but the dates do
    // not, and without them the sweep has no idea what has been held too long.
    //
    // Every prefs name in the repo is unique across all four heads, so "declared
    // exactly once" catches both a rename and a drop without this test needing to know
    // which folder the head lives in.
    [Fact]
    public void AudioPreferenceFileNamesAreFrozen()
    {
        string[] frozen =
        [
            "artwork", "downloads", "equalizer", "fridayPicks", "listeningHistory",
            "music", "music-queue", "playlists", "podcast-queue", "podcastEpisodeCache",
            "podcastProgress", "podcastSettings", "podcastSubscriptions", "podcasts",
            "scoreUpdateStatus",
        ];

        var declared = EnumerateRepoFiles("*.cs")
            .SelectMany(file => PreferenceNames(File.ReadAllText(file)))
            .ToList();

        var missing = frozen.Where(name => declared.Count(found => found == name) != 1).ToList();

        Assert.True(missing.Count == 0,
            "These SharedPreferences file names must each be declared exactly once — a rename " +
            "orphans the data behind them: " + string.Join(", ", missing));
    }

    // Matches any constant whose name ends in PreferencesName, so a class holding
    // more than one — the two queue stores — is still fully covered.
    private static IEnumerable<string> PreferenceNames(string source)
    {
        const string Declaration = "PreferencesName = \"";
        var index = source.IndexOf(Declaration, StringComparison.Ordinal);
        while (index >= 0)
        {
            var start = index + Declaration.Length;
            var end = source.IndexOf('"', start);
            if (end < 0)
            {
                yield break;
            }
            yield return source[start..end];
            index = source.IndexOf(Declaration, end, StringComparison.Ordinal);
        }
    }

    // The two Drive files the head reads its own backups back out of, found by NAME.
    // Rename either and the restore finds nothing — the library and the taste profile
    // are still sitting in Drive under the old name, invisible.
    [Fact]
    public void AudioDriveFileNamesAreFrozen()
    {
        Assert.Equal("focusphone-audio-library.json", Codex.Infrastructure.Podcasts.DriveAudioLibraryStore.FileName);
        Assert.Equal("storyworthtelling-taste.json", Codex.Infrastructure.Music.DriveTasteProfileStore.FileName);
    }

    // The heads sign with the one shared repo keystore, so they share a single
    // signature the update channel accepts across the suite. Asserted by capability
    // (shared keystore + alias driving a signed Android build), not by head name, so
    // the rename can't spuriously trip it.
    [Fact]
    public void HeadsSignWithTheSharedKeystore()
    {
        var signingWorkflow = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml")
            .Select(File.ReadAllText)
            .FirstOrDefault(text =>
                text.Contains("secrets.KEYSTORE_BASE64", StringComparison.Ordinal)
                && text.Contains("AndroidSigningKeyStore", StringComparison.Ordinal));

        Assert.True(signingWorkflow is not null,
            "No workflow signs an Android head with the shared secrets.KEYSTORE_BASE64 keystore.");
        Assert.Contains("secrets.KEY_ALIAS", signingWorkflow!, StringComparison.Ordinal);
    }

    // ---- Post (P7): the same invariants, for the third head through the rename ----

    // Exactly one head declares the frozen communication ApplicationId. Post renames
    // its folder, csproj and namespace; the package name is what a device matches an
    // update against, so it survives all three.
    [Fact]
    public void CommunicationCsprojDeclaresFrozenApplicationId()
    {
        var declaring = EnumerateRepoFiles("*.csproj")
            .Where(file => File.ReadAllText(file)
                .Contains($"<ApplicationId>{PolicyContext.CommunicationPackage}</ApplicationId>", StringComparison.Ordinal))
            .ToList();

        Assert.True(declaring.Count == 1,
            $"Exactly one csproj must declare the frozen communication ApplicationId " +
            $"'{PolicyContext.CommunicationPackage}'; found {declaring.Count}:\n" +
            string.Join("\n", declaring.Select(RepoRelative)));
    }

    // The head's two SharedPreferences files hold the mail account and the sync
    // watermark. The rename moves them to postal names, which is only safe because a
    // one-time copy-across reads the old files first — so the OLD literals must keep
    // existing somewhere in the source. Delete the migration and the credentials are
    // orphaned on device with no error: the app simply looks unconfigured.
    [Fact]
    public void CommunicationPreferenceMigrationLiteralsSurvive()
    {
        string[] frozen = ["account", "emailSync"];

        var missing = frozen
            .Where(name => CountCsFilesContaining($"\"{name}\"") == 0)
            .ToList();

        Assert.True(missing.Count == 0,
            "These original SharedPreferences file names must stay in the source so the one-time " +
            "copy-across to the postal names can still find them: " + string.Join(", ", missing));
    }

    // The timeline database is named once, and that name is the only handle Android
    // gives you on the file. Renaming it reads as a device with no history — and once
    // the ghost archive lands, the history is no longer re-derivable from the sources.
    [Fact]
    public void TimelineDatabaseFileNameIsFrozen()
    {
        Assert.Equal(1, CountCsFilesContaining("\"timeline.db\""));
    }

    // The notification listener's access is granted once during provisioning against
    // this component name, and the listener is KEPT after the rename as the fallback
    // path for RCS (which never reaches the SMS provider). Renaming the class would
    // revoke listener access silently on the next upgrade.
    [Fact]
    public void MessageBridgeListenerComponentNameIsFrozen()
    {
        Assert.Equal(1, CountCsFilesDeclaring(
            $"{PolicyContext.CommunicationPackage}.MessageBridgeListener", "Name ="));
    }

    // The periodic email sync is enqueued with ExistingPeriodicWorkPolicy.Keep, so on
    // an upgraded device the request WorkManager already persisted wins — and it names
    // the worker by JNI class name. Move the class and Keep hands the sync to a name
    // that no longer resolves: it never runs again, and nothing says so. The class is
    // therefore Registered under its pre-rename name, and the two unique work names
    // are frozen alongside it, since those are what Keep and Replace match on.
    [Fact]
    public void EmailSyncWorkerRegistrationAndWorkNamesAreFrozen()
    {
        Assert.Equal(1, CountCsFilesDeclaring(
            $"{PolicyContext.CommunicationPackage}.EmailSyncWorker", "Register("));
        Assert.Equal(1, CountCsFilesContaining("\"focusphone-email-sync\""));
        Assert.Equal(1, CountCsFilesContaining("\"focusphone-email-sync-once\""));
    }

    // Every owner-namespace head must actually reach the rolling release, or the
    // device-owner update channel never sees it. This is not hypothetical: the
    // publish step used to match heads with a `FocusPhone.*.Android` glob, which
    // silently stopped matching a head each time one was renamed — it missed Flyleaf,
    // and then missed Score for an entire phase. A workflow that builds a head but
    // does not publish it looks completely healthy from the outside, which is exactly
    // what a guard is for.
    [Fact]
    public void EveryOwnerHeadIsPublishedToTheRollingRelease()
    {
        var publishSteps = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), ".github", "workflows"), "*.yml")
            .Select(File.ReadAllText)
            .Where(text => text.Contains("publish-rolling-release", StringComparison.Ordinal))
            .ToList();

        var unpublished = EnumerateRepoFiles("*.csproj")
            .Where(file => File.ReadAllText(file)
                .Contains("<ApplicationId>com.storyworthtelling.", StringComparison.Ordinal))
            .Select(file => Path.GetDirectoryName(RepoRelative(file))!.Replace(Path.DirectorySeparatorChar, '/'))
            .Where(directory => !publishSteps.Any(text =>
                text.Contains($"{directory}/bin/Release/net10.0-android/publish/", StringComparison.Ordinal)))
            .ToList();

        Assert.True(unpublished.Count == 0,
            "These heads declare an owner-namespace ApplicationId but no workflow publishes their " +
            "signed APK to the apps-latest rolling release, so the phone will never install them: " +
            string.Join(", ", unpublished));
    }

    // Android will not even OFFER Post the SMS role unless all four of these components
    // are declared WITH their intent filters. An app missing one is simply absent from the
    // "default SMS app" list, with nothing anywhere saying why — the same class of silent
    // disqualification as the device-admin receiver in P5, and the reason this is a guard.
    //
    // Losing the role is not cosmetic: without it Post cannot send a text or delete one
    // for real, which is half of what this phase exists to fix.
    //
    // The filter has to be found inside an [IntentFilter] attribute, not merely somewhere
    // in the file. The first version of this test looked for the action string anywhere in
    // the declaring file and passed happily with the attribute deleted, because the action
    // is also the value of the constant the attribute referred to.
    [Fact]
    public void SmsRoleComponentsAreDeclaredWithTheirFilters()
    {
        (string Component, string Filter)[] required =
        [
            ("SmsDeliverReceiver", "SmsDeliver"),
            ("MmsDeliverReceiver", "WapPushDeliver"),
            ("RespondViaMessageService", "RespondViaMessage"),
            ("SmsSendToActivity", "ActionSendto"),
        ];

        var sources = EnumerateRepoFiles("*.cs").Select(File.ReadAllText).ToList();

        var missing = required.Where(component => !sources.Any(source =>
                source.Contains($"\"{PolicyContext.CommunicationPackage}.{component.Component}\"", StringComparison.Ordinal)
                && IntentFilters(source).Any(filter => filter.Contains(component.Filter, StringComparison.Ordinal))))
            .Select(component => component.Component)
            .ToList();

        Assert.True(missing.Count == 0,
            "These components are what qualify Post for the SMS role; without every one of them, filter " +
            "included, the phone will not offer it the role and nothing will say so: " + string.Join(", ", missing));
    }

    // The share-target filter is the only way Google's Call Notes can reach Post: it
    // exports through the share sheet and nowhere else. Invisible when missing and silent
    // when it goes — the note simply never arrives and the owner is left typing it.
    //
    // Matched on the attribute rather than on the file, and on ActionSend as a whole
    // token: an earlier version of this looked for "Intent.ActionSend" anywhere in the
    // source and passed happily on Intent.ActionSendto, which is a different filter on a
    // different component.
    [Fact]
    public void CallNoteShareTargetIsDeclared()
    {
        var declared = EnumerateRepoFiles("*.cs")
            .Select(File.ReadAllText)
            .SelectMany(IntentFilters)
            .Any(filter =>
                filter.Contains("Intent.ActionSend]", StringComparison.Ordinal)
                && filter.Contains("\"text/plain\"", StringComparison.Ordinal));

        Assert.True(declared,
            "No component declares an ACTION_SEND / text/plain filter — Call Notes has no way into Post.");
    }

    // Every [IntentFilter(...)] attribute in a source file, as text.
    private static IEnumerable<string> IntentFilters(string source)
    {
        const string Attribute = "[IntentFilter(";
        var index = source.IndexOf(Attribute, StringComparison.Ordinal);
        while (index >= 0)
        {
            var depth = 0;
            var end = index + Attribute.Length - 1;
            for (; end < source.Length; end++)
            {
                if (source[end] == '(')
                {
                    depth++;
                }
                else if (source[end] == ')' && --depth == 0)
                {
                    break;
                }
            }
            yield return source[index..Math.Min(end + 1, source.Length)];
            index = source.IndexOf(Attribute, Math.Min(end + 1, source.Length), StringComparison.Ordinal);
        }
    }
    private static int CountCsFilesContaining(string literal) =>
        EnumerateRepoFiles("*.cs").Count(file =>
            File.ReadAllText(file).Contains(literal, StringComparison.Ordinal));

    // ---- repo helpers (root = the ancestor holding .github; survives the rename) ----

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repo root (.github) above " + AppContext.BaseDirectory);
    }

    private static string RepoRelative(string file) => Path.GetRelativePath(RepoRoot(), file);

    private static IEnumerable<string> EnumerateRepoFiles(string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(RepoRoot(), pattern, SearchOption.AllDirectories))
        {
            var parts = file.Split(Path.DirectorySeparatorChar);
            if (parts.Any(part => part is "obj" or "bin" or ".git"))
            {
                continue;
            }
            yield return file;
        }
    }
}
