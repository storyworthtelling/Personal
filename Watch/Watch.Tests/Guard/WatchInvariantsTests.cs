using System.Text.RegularExpressions;
using Storyworthtelling.Updates;
using Watch.Domain;

namespace Watch.Tests.Guard;

/// <summary>
/// Plan §4: the watch's frozen surface. Nothing fails at build time if one of these moves, and nothing fails loudly at
/// run time either: the Data Layer delivers only between apps of one package and one signature, a listener hears only
/// the paths it names, the platform remembers a component by its registered name, and a stored preference is found by
/// its key. Move any of them and the phone's update simply never reaches the watch. These tests fail instead.
///
/// <para>The expected values are typed out as literals on purpose: the guard exists to notice a value moving, so it
/// carries its own record of what the value was. Adding a path, a component or a key is a deliberate edit here.</para>
/// </summary>
public class WatchInvariantsTests
{
    private static readonly string[] ExpectedPackages =
    [
        "com.storyworthtelling.focusphone.communication",
        "com.storyworthtelling.focusphone.audio",
        "com.storyworthtelling.constitution",
        "com.storyworthtelling.focusphone.launcher",
        "com.storyworthtelling.watch.point",
    ];

    private static readonly string[] ExpectedDashPaths =
    [
        "/dash/people", "/dash/thread", "/dash/send", "/dash/file", "/dash/delete", "/dash/undo", "/dash/read", "/dash/call-note", "/dash/mail",
        "/dash/photo",
    ];

    private static readonly string[] ExpectedInstallerComponents =
    [
        "com.storyworthtelling.focusphone.launcher.point.UpdateListenerService",
        "com.storyworthtelling.focusphone.launcher.point.InstallStatusReceiver",
        "com.storyworthtelling.focusphone.launcher.point.ReplacedReceiver",
    ];

    private static readonly string[] ExpectedInstallerPermissions =
    [
        "android.permission.REQUEST_INSTALL_PACKAGES",
        "android.permission.UPDATE_PACKAGES_WITHOUT_USER_ACTION",
        "android.permission.POST_NOTIFICATIONS",
    ];

    private static string Installer(params string[] parts) =>
        Path.Combine([WatchFiles.RepoRoot(), "Watch", "Point.Installer", .. parts]);

    private static string Text(string path)
    {
        Assert.True(File.Exists(path), $"{Path.GetRelativePath(WatchFiles.RepoRoot(), path)} isn’t there.");
        return File.ReadAllText(path);
    }

    // A source without its comments.
    private static string Code(string path) => Regex.Replace(Text(path), @"///.*|//.*", string.Empty);

    private static IEnumerable<string> InstallerSources() =>
        Directory.Exists(Installer())
            ? Directory.EnumerateFiles(Installer(), "*.cs", SearchOption.AllDirectories).Where(path => !WatchFiles.IsBuildOutput(path))
            : [];

    // ---- the packages (plan §2.2, PC1) ----

    [Fact]
    public void PackagesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.communication", WatchIdentity.DashPackage);
        Assert.Equal("com.storyworthtelling.focusphone.audio", WatchIdentity.WavePackage);
        Assert.Equal("com.storyworthtelling.constitution", WatchIdentity.BeatPackage);
        Assert.Equal("com.storyworthtelling.focusphone.launcher", WatchIdentity.InstallerPackage);
        Assert.Equal("com.storyworthtelling.watch.point", WatchIdentity.FacePackage);
        Assert.Equal(ExpectedPackages, WatchIdentity.Packages);
    }

    [Fact]
    public void EveryPackageIsTheSuites()
    {
        Assert.NotEmpty(WatchIdentity.Packages);
        Assert.All(WatchIdentity.Packages, package => Assert.True(SuiteApps.IsOwnApp(package), package));
    }

    [Fact]
    public void TheInstallerIsFlyleafsPackage()
    {
        // PC8: the Data Layer needs Flyleaf's package at both ends.
        var project = Text(Installer("Point.Installer.csproj"));
        Assert.Contains($"<ApplicationId>{WatchIdentity.InstallerPackage}</ApplicationId>", project, StringComparison.Ordinal);

        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));
        Assert.Matches($"<manifest\\b[^>]*\\spackage=\"{Regex.Escape(WatchIdentity.InstallerPackage)}\"", manifest);
    }

    [Fact]
    public void TheFaceHasItsOwnPackage()
    {
        var manifest = Text(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Point.Face", "AndroidManifest.xml"));

        Assert.Matches($"<manifest\\b[^>]*\\spackage=\"{Regex.Escape(WatchIdentity.FacePackage)}\"", manifest);
    }

    // ---- the Data Layer (plan §2.5) ----

    [Fact]
    public void PathsAreFrozen()
    {
        Assert.Equal("/update/apk", WatchIdentity.UpdateApkPath);
        Assert.Equal("/update/installed", WatchIdentity.UpdateInstalledPath);
    }

    [Fact]
    public void DashPathsAreFrozen()
    {
        // Post's listener and Dash's will each hear only the paths they name (P14 W-4.1.1).
        Assert.Equal("/dash/people", WatchIdentity.DashPeoplePath);
        Assert.Equal("/dash/thread", WatchIdentity.DashThreadPath);
        Assert.Equal("/dash/send", WatchIdentity.DashSendPath);
        Assert.Equal("/dash/file", WatchIdentity.DashFilePath);
        Assert.Equal("/dash/delete", WatchIdentity.DashDeletePath);
        Assert.Equal("/dash/undo", WatchIdentity.DashUndoPath);
        Assert.Equal("/dash/read", WatchIdentity.DashReadPath);
        Assert.Equal("/dash/call-note", WatchIdentity.DashCallNotePath);
        Assert.Equal("/dash/mail", WatchIdentity.DashMailPath);
        // B5: one photo of a conversation, asked by its id and answered shrunk to fit (W-5.1.3).
        Assert.Equal("/dash/photo", WatchIdentity.DashPhotoPath);
        Assert.Equal(ExpectedDashPaths, WatchIdentity.DashPaths);
    }

    [Fact]
    public void EveryDashPathIsItsOwn()
    {
        Assert.Equal(WatchIdentity.DashPaths.Count, WatchIdentity.DashPaths.Distinct(StringComparer.Ordinal).Count());
        Assert.All(WatchIdentity.DashPaths, path => Assert.StartsWith("/dash/", path, StringComparison.Ordinal));
        Assert.DoesNotContain(WatchIdentity.DashPaths, path => path == WatchIdentity.UpdateApkPath || path == WatchIdentity.UpdateInstalledPath);
    }

    [Fact]
    public void KeysPathIsFrozen()
    {
        // The handed-over token's one path (P14 W-4.1.2): a phone app and its watch app each hear it by this name.
        Assert.Equal("/keys/google", WatchIdentity.GoogleKeysPath);
        Assert.DoesNotContain(WatchIdentity.GoogleKeysPath, WatchIdentity.DashPaths);
        Assert.NotEqual(WatchIdentity.UpdateApkPath, WatchIdentity.GoogleKeysPath);
        Assert.NotEqual(WatchIdentity.UpdateInstalledPath, WatchIdentity.GoogleKeysPath);
    }

    [Fact]
    public void TheCapabilityIsFrozen()
    {
        Assert.Equal("point-installer", WatchIdentity.InstallerCapability);
    }

    [Fact]
    public void PostsHalfIsFrozen()
    {
        // Post's listener for Dash (P14 B2): Play services delivers to it by its registered name, Dash finds the phone by
        // the capability, and the book of the wrist's acts is kept on the phone under these names.
        Assert.Equal("com.storyworthtelling.focusphone.communication.DashListener", WatchIdentity.DashListenerName);
        Assert.StartsWith(WatchIdentity.DashPackage + ".", WatchIdentity.DashListenerName, StringComparison.Ordinal);
        Assert.Equal("post-relay", WatchIdentity.PostCapability);
        Assert.NotEqual(WatchIdentity.InstallerCapability, WatchIdentity.PostCapability);
        Assert.Equal("dashRelay", WatchIdentity.DashPreferences);
        Assert.Equal("acts", WatchIdentity.DashActsKey);
    }

    private static readonly string[] ExpectedDashComponents =
    [
        "com.storyworthtelling.focusphone.communication.dash.PeopleActivity",
        "com.storyworthtelling.focusphone.communication.dash.ConversationActivity",
        "com.storyworthtelling.focusphone.communication.dash.ReplyActivity",
        "com.storyworthtelling.focusphone.communication.dash.ActActivity",
    ];

    [Fact]
    public void DashsNamesAreFrozen()
    {
        // Dash on the watch (P14 B4): the launcher, and later the People waiting tile (W-5.1.7), open a screen by its
        // registered name, so a moved class opens nothing.
        Assert.Equal("com.storyworthtelling.focusphone.communication.dash.PeopleActivity", WatchIdentity.DashPeopleActivityName);
        Assert.Equal("com.storyworthtelling.focusphone.communication.dash.ConversationActivity", WatchIdentity.DashConversationActivityName);
        Assert.Equal("com.storyworthtelling.focusphone.communication.dash.ReplyActivity", WatchIdentity.DashReplyActivityName);
        // B5: filing or deleting a correspondence, and its Undo ring (W-5.1.4).
        Assert.Equal("com.storyworthtelling.focusphone.communication.dash.ActActivity", WatchIdentity.DashActActivityName);
        Assert.Equal(ExpectedDashComponents, WatchIdentity.DashComponents);
        Assert.All(WatchIdentity.DashComponents, name => Assert.StartsWith(WatchIdentity.DashPackage + ".dash.", name, StringComparison.Ordinal));
        Assert.DoesNotContain(WatchIdentity.DashListenerName, WatchIdentity.DashComponents);
    }

    [Fact]
    public void TheInstallerAdvertisesItsCapability()
    {
        var wear = Text(Installer("Resources", "values", "wear.xml"));

        Assert.Matches(@"<string-array\s+name=""android_wear_capabilities""", wear);
        Assert.Contains($"<item>{WatchIdentity.InstallerCapability}</item>", wear, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListenerHearsBothPaths()
    {
        var listener = InstallerSources().Select(File.ReadAllText).SingleOrDefault(source => source.Contains("class UpdateListenerService", StringComparison.Ordinal));
        Assert.True(listener is not null, "The installer has no UpdateListenerService.");

        Assert.Contains("DataPathPrefix = WatchIdentity.UpdateApkPath", listener, StringComparison.Ordinal);
        Assert.Contains("DataPath = WatchIdentity.UpdateInstalledPath", listener, StringComparison.Ordinal);
    }

    // ---- the installer's registered names, and what it keeps ----

    [Fact]
    public void ComponentsAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher.point.UpdateListenerService", WatchIdentity.UpdateListenerName);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.point.InstallStatusReceiver", WatchIdentity.InstallStatusReceiverName);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.point.ReplacedReceiver", WatchIdentity.ReplacedReceiverName);
        Assert.Equal(ExpectedInstallerComponents, WatchIdentity.InstallerComponents);
    }

    [Theory]
    [InlineData("UpdateListenerName")]
    [InlineData("InstallStatusReceiverName")]
    [InlineData("ReplacedReceiverName")]
    public void EachComponentIsRegisteredByItsName(string constant)
    {
        var registered = InstallerSources().Count(path => File.ReadAllText(path).Contains($"Name = WatchIdentity.{constant}", StringComparison.Ordinal));

        Assert.Equal(1, registered);
    }

    [Fact]
    public void StoredNamesAreFrozen()
    {
        Assert.Equal("point-installer", WatchIdentity.InstallerPreferences);
        Assert.Equal("results", WatchIdentity.ResultsKey);
        Assert.Equal("point-install", WatchIdentity.InstallChannel);
        Assert.Equal("sessions", WatchIdentity.SessionsKey);
    }

    [Fact]
    public void StatusExtrasAreFrozen()
    {
        // A session's status intent is made by one installer and can be read by the next (its own update), so its extras
        // are wire names (release A fix round, finding 5).
        Assert.Equal("package", WatchIdentity.StatusPackageExtra);
        Assert.Equal("versionCode", WatchIdentity.StatusVersionCodeExtra);
        Assert.Equal("action", WatchIdentity.StatusActionExtra);
        Assert.Equal("silence", WatchIdentity.StatusSilenceExtra);
        Assert.Equal("session", WatchIdentity.StatusSessionExtra);
    }

    [Fact]
    public void EachStatusIsJudgedByItsSession()
    {
        var receiver = Code(Installer("InstallStatusReceiver.cs"));
        var installer = Code(Installer("WatchInstaller.cs"));

        // The status intent carries its session and the action's frozen word, never an enum's name.
        Assert.Contains("WatchIdentity.StatusSessionExtra", receiver, StringComparison.Ordinal);
        Assert.Contains("WatchUpdateActions.Word(decision.Action)", receiver, StringComparison.Ordinal);
        Assert.Contains("WatchUpdateActions.FromWord(", receiver, StringComparison.Ordinal);
        Assert.DoesNotContain("Action.ToString()", receiver, StringComparison.Ordinal);
        Assert.DoesNotContain("Enum.TryParse", receiver, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"const string \w+Extra = """, receiver);
        Assert.Contains("InstallSessions.Judge(", receiver, StringComparison.Ordinal);

        // A session is recorded as abandoned before it is abandoned, and as begun before it is committed, so its status
        // always finds it.
        var abandoning = installer.IndexOf("SessionStore.Abandoning(", StringComparison.Ordinal);
        var abandon = installer.IndexOf("AbandonSession(", StringComparison.Ordinal);
        Assert.True(abandoning >= 0 && abandon > abandoning, "The installer abandons a session it hasn't recorded as abandoned.");
        var began = installer.IndexOf("SessionStore.Began(", StringComparison.Ordinal);
        var commit = installer.IndexOf(".Commit(", StringComparison.Ordinal);
        Assert.True(began >= 0 && commit > began, "The installer commits a session it hasn't recorded as begun.");

        // The store keeps the record with the domain's own rules, under the results' preferences file.
        var store = Code(Installer("Outcomes.cs"));
        Assert.Contains("InstallSessions.Abandoning(", store, StringComparison.Ordinal);
        Assert.Contains("InstallSessions.Began(", store, StringComparison.Ordinal);
        Assert.Contains("WatchIdentity.SessionsKey", store, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryWatchAppCarries32BitArm()
    {
        // .NET for Android builds 64-bit ARM and x64 unless told otherwise, and a watch whose system runs 32-bit userspace
        // will not install an APK with no armeabi-v7a code (release A fix round, finding 2). A library's code goes into the
        // app's APK, so the apps are what say it.
        var apps = Directory.EnumerateFiles(Path.Combine(WatchFiles.RepoRoot(), "Watch"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !WatchFiles.IsBuildOutput(path) && File.ReadAllText(path).Contains("<ApplicationId>", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(apps);

        foreach (var app in apps)
        {
            var identifiers = Regex.Match(File.ReadAllText(app), @"<RuntimeIdentifiers>([^<]*)</RuntimeIdentifiers>");
            var named = identifiers.Success ? identifiers.Groups[1].Value.Split(';', StringSplitOptions.TrimEntries) : [];
            Assert.True(named.Contains("android-arm") && named.Contains("android-arm64"),
                $"{Path.GetRelativePath(WatchFiles.RepoRoot(), app)} doesn't build for android-arm and android-arm64.");
        }
    }

    [Fact]
    public void TheWatchWorkflowRunsWhenAnythingTheWatchTestsReadChanges()
    {
        // Every path outside Watch/ that a watch test names from the repository's root must wake build-watch.yml, or a
        // change there alone (Flyleaf's half of the update channel, say) runs none of the guards that read it (release A
        // fix round, finding 6).
        var workflow = Text(Path.Combine(WatchFiles.RepoRoot(), ".github", "workflows", "build-watch.yml"));
        var filters = Regex.Matches(workflow, @"(?m)^\s+- '([^'!][^']*)'\s*$").Select(match => match.Groups[1].Value).ToList();
        var tests = Directory.EnumerateFiles(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Watch.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !WatchFiles.IsBuildOutput(path));

        var read = tests
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"RepoRoot\(\)\s*,\s*(""[^""]+""(?:\s*,\s*""[^""]+"")*)"))
            .Select(match => string.Join('/', Regex.Matches(match.Groups[1].Value, @"""([^""]+)""").Select(part => part.Groups[1].Value)))
            .Where(path => !path.StartsWith("Watch/", StringComparison.Ordinal) && path != "Watch")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Contains("FocusPhone/Flyleaf.Android", read);

        var missed = read
            .Where(path => !filters.Any(filter => filter == path
                || (filter.EndsWith("/**", StringComparison.Ordinal) && (path + "/").StartsWith(filter[..^2], StringComparison.Ordinal))))
            .ToList();
        Assert.True(missed.Count == 0, "build-watch.yml doesn't run when these change, though the watch tests read them:\n" + string.Join("\n", missed));
    }

    [Fact]
    public void TheIdentityIsLiterals()
    {
        var identity = Text(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Watch.Domain", "WatchIdentity.cs"));
        var code = Regex.Replace(identity, @"///.*|//.*", string.Empty);

        Assert.DoesNotContain("nameof(", code, StringComparison.Ordinal);
        Assert.All(Regex.Matches(code, @"public const string (\w+) = ([^;]+);").Select(match => match.Groups[2].Value.Trim()),
            value => Assert.Matches(@"^""[^""]*""$", value));
    }

    [Fact]
    public void TheIdsAreNotCopiedIntoTheInstaller()
    {
        Assert.NotEmpty(InstallerSources());
        var copied = InstallerSources()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"""(com\.storyworthtelling\.[\w.]+|/update/[\w/]*)"""))
            .Select(path => Path.GetRelativePath(WatchFiles.RepoRoot(), path))
            .ToList();

        Assert.True(copied.Count == 0, "Use WatchIdentity rather than a copy:\n" + string.Join("\n", copied));
    }

    // ---- Dash on the watch (P14 B4): Post's package, so the Data Layer joins the two by package and signature ----

    private static string Dash(params string[] parts) =>
        Path.Combine([WatchFiles.RepoRoot(), "Watch", "Dash.Wear", .. parts]);

    private static IEnumerable<string> DashSources() =>
        Directory.Exists(Dash())
            ? Directory.EnumerateFiles(Dash(), "*.cs", SearchOption.AllDirectories).Where(path => !WatchFiles.IsBuildOutput(path))
            : [];

    // Every Dash source, without its comments, as one text.
    private static string DashCode()
    {
        var sources = DashSources().ToList();
        Assert.True(sources.Count > 0, "Watch/Dash.Wear has no sources.");
        return string.Join("\n", sources.Select(Code));
    }

    [Fact]
    public void DashIsPostsPackage()
    {
        // PC1: the Data Layer delivers only between apps of one package and one signature.
        var project = Text(Dash("Dash.Wear.csproj"));
        Assert.Contains($"<ApplicationId>{WatchIdentity.DashPackage}</ApplicationId>", project, StringComparison.Ordinal);

        var manifest = Text(Dash("Properties", "AndroidManifest.xml"));
        Assert.Matches($"<manifest\\b[^>]*\\spackage=\"{Regex.Escape(WatchIdentity.DashPackage)}\"", manifest);
    }

    [Fact]
    public void DashIsAWatchApp()
    {
        var manifest = Text(Dash("Properties", "AndroidManifest.xml"));

        Assert.Matches(@"<uses-feature\s+android:name=""android\.hardware\.type\.watch""", manifest);
        // Not standalone: it is nothing without Post on the phone (A67).
        Assert.Matches(@"<meta-data\s+android:name=""com\.google\.android\.wearable\.standalone""\s+android:value=""false""", manifest);
    }

    [Fact]
    public void DashAsksForNoPermission()
    {
        // The Data Layer needs none, and the system's own dictation and keyboard ask for what they need themselves.
        var manifest = Text(Dash("Properties", "AndroidManifest.xml"));
        var attributes = DashSources().Count(path => Regex.IsMatch(File.ReadAllText(path), @"\[assembly:\s*UsesPermission"));

        Assert.DoesNotContain("<uses-permission", manifest, StringComparison.Ordinal);
        Assert.Equal(0, attributes);
    }

    [Fact]
    public void DashCanSeeTheSystemsInput()
    {
        // Android 11+ resolves another app's activity only for an intent the app names (W-5.1.2).
        var manifest = Text(Dash("Properties", "AndroidManifest.xml"));

        Assert.Matches(@"<intent>\s*<action\s+android:name=""android\.speech\.action\.RECOGNIZE_SPEECH""\s*/>\s*</intent>", manifest);
        Assert.Matches(@"<intent>\s*<action\s+android:name=""android\.support\.wearable\.input\.action\.REMOTE_INPUT""\s*/>\s*</intent>", manifest);
    }

    [Theory]
    [InlineData("DashPeopleActivityName")]
    [InlineData("DashConversationActivityName")]
    [InlineData("DashReplyActivityName")]
    [InlineData("DashActActivityName")]
    public void EachDashScreenIsRegisteredByItsName(string constant)
    {
        var registered = DashSources().Count(path => File.ReadAllText(path).Contains($"Name = WatchIdentity.{constant}", StringComparison.Ordinal));

        Assert.Equal(1, registered);
    }

    [Fact]
    public void PeopleWaitingIsTheOneWayIn()
    {
        // A14: Dash opens on People waiting, and it is the app's one launcher entry.
        var launchers = DashSources().Select(File.ReadAllText).Where(source => source.Contains("MainLauncher = true", StringComparison.Ordinal)).ToList();

        var people = Assert.Single(launchers);
        Assert.Contains("Name = WatchIdentity.DashPeopleActivityName", people, StringComparison.Ordinal);
    }

    [Fact]
    public void DashFindsThePhoneByItsCapability()
    {
        var code = DashCode();

        Assert.Contains("WatchIdentity.PostCapability", code, StringComparison.Ordinal);
        Assert.Contains("CapabilityClient.FilterReachable", code, StringComparison.Ordinal);
        Assert.Contains("RelayNodes.Choose(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DashReadsTheSameCodec()
    {
        // One message layout for both ends: Dash references Watch.Domain, as Post does, rather than a copy of it.
        var project = Text(Dash("Dash.Wear.csproj"));
        var code = DashCode();

        Assert.Contains(@"..\Watch.Domain\Watch.Domain.csproj", project, StringComparison.Ordinal);
        Assert.Contains(@"..\Watch.Ui.Wear\Watch.Ui.Wear.csproj", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Xamarin.GooglePlayServices.Wearable\" />", project, StringComparison.Ordinal);
        Assert.Contains("DashCodec.Decode(", code, StringComparison.Ordinal);
        Assert.Contains("DashCodec.Encode(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonSerializer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Utf8JsonWriter", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DashHearsOnlyItsOwnPaths()
    {
        // Post answers on the path it was asked on (B2); anything else reaching the wrist's ear is left alone.
        var ear = Assert.Single(DashSources().Select(Code), source => source.Contains("DashCodec.Decode(", StringComparison.Ordinal));
        var check = ear.IndexOf("WatchIdentity.DashPaths.Contains(", StringComparison.Ordinal);
        var decode = ear.IndexOf("DashCodec.Decode(", StringComparison.Ordinal);

        Assert.True(check >= 0 && decode > check, "Dash decodes a message before checking it came on one of its paths.");
        Assert.Contains("DashLogLines.Receipt(", ear, StringComparison.Ordinal);
        Assert.Contains("DashEnd.Dash", ear, StringComparison.Ordinal);
    }

    [Fact]
    public void DashMatchesEachAnswerToItsAsk()
    {
        var link = Assert.Single(DashSources().Select(Code), source => source.Contains("SendMessageAsync(", StringComparison.Ordinal));

        Assert.Contains("DashExchange.Awaits(", link, StringComparison.Ordinal);
        Assert.Contains("DashExchange.Answers(", link, StringComparison.Ordinal);
        Assert.Contains("DashExchange.WaitFor(", link, StringComparison.Ordinal);
        // An ask is waited for before it is sent, so an answer that comes back at once still finds it.
        var waiting = link.IndexOf("DashExchange.Awaits(", StringComparison.Ordinal);
        var sending = link.IndexOf("SendMessageAsync(", StringComparison.Ordinal);
        Assert.True(waiting >= 0 && sending > waiting, "Dash sends an ask before it waits for the answer.");
        // And its ear is in place, waited for within a bound, before the ask goes.
        Assert.Contains("AddListenerAsync(", link, StringComparison.Ordinal);
        Assert.Contains("BoundedAsync(registering, DashWaits.Find)", link, StringComparison.Ordinal);
    }

    [Fact]
    public void PeopleWaitingDrawsNoUnreadMark()
    {
        // Owner's ruling (27 Nov 2026): WD §1's "no unread marks" wins over the mockup's rule down a row's edge. A row
        // draws nothing in Dash's colour, and the layout places no rule.
        Assert.DoesNotContain(typeof(Watch.Domain.Dash.DashLayout).GetFields(), field => field.Name.Contains("Unread", StringComparison.Ordinal));

        var row = Code(Dash("PersonRowView.cs"));
        Assert.DoesNotContain("WatchApp.Dash", row, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawRect(", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMockupDrawsNoUnreadMark()
    {
        // The mockups are the specification, so the ruling is drawn there too: People waiting's row has no rule.
        var mockup = Text(Path.Combine(WatchFiles.RepoRoot(), "Watch", "design", "screens-r1.html"));
        var row = Regex.Match(mockup, @"function row\([^)]*\)\s*\{(?<body>[\s\S]*?)\n\}");

        Assert.True(row.Success, "The mockup has no row.");
        Assert.DoesNotContain("fillRect(", row.Groups["body"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("marks the unread", mockup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIdsAreNotCopiedIntoDash()
    {
        var copied = DashSources()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"""(com\.storyworthtelling\.[\w.]+|/dash/[\w/-]*|post-relay)"""))
            .Select(path => Path.GetRelativePath(WatchFiles.RepoRoot(), path))
            .ToList();

        Assert.NotEmpty(DashSources());
        Assert.True(copied.Count == 0, "Use WatchIdentity rather than a copy:\n" + string.Join("\n", copied));
    }

    [Fact]
    public void EveryDashDataLayerWaitIsBounded()
    {
        // A Data Layer call that never answers must not hold a screen for good (as release A learned on the phone).
        string[] calls = ["GetCapabilityAsync(", "SendMessageAsync("];
        var lines = DashSources().SelectMany(path => Code(path).Split('\n').Select((line, index) => (Path: Path.GetFileName(path), Line: line, Number: index + 1))).ToList();

        var waits = lines.Where(entry => calls.Any(call => entry.Line.Contains(call, StringComparison.Ordinal))).ToList();
        var unbounded = waits.Where(entry => !entry.Line.Contains("BoundedAsync(", StringComparison.Ordinal)).Select(entry => $"{entry.Path}:{entry.Number}: {entry.Line.Trim()}").ToList();

        Assert.Equal(calls.Length, calls.Count(call => waits.Any(entry => entry.Line.Contains(call, StringComparison.Ordinal))));
        Assert.True(unbounded.Count == 0, "A Data Layer call waited for with no bound:\n" + string.Join("\n", unbounded));
        Assert.Contains(".WaitAsync(", DashCode(), StringComparison.Ordinal);
    }

    [Fact]
    public void DashWaitsOffTheMainThread()
    {
        // Every ask and tell a screen makes runs on the pool, and only its answer comes back to the screen.
        var screens = DashSources().Where(path => Path.GetFileName(path).EndsWith("Activity.cs", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, screens.Count);

        var calls = screens.SelectMany(path => Code(path).Split('\n').Where(line => Regex.IsMatch(line, @"\.(AskAsync|TellAsync)\(")).Select(line => (Path: Path.GetFileName(path), Line: line))).ToList();
        Assert.NotEmpty(calls);
        var onTheScreen = calls.Where(call => !call.Line.Contains("Task.Run(", StringComparison.Ordinal)).Select(call => $"{call.Path}: {call.Line.Trim()}").ToList();
        Assert.True(onTheScreen.Count == 0, "A Data Layer wait started on the main thread:\n" + string.Join("\n", onTheScreen));
    }

    [Fact]
    public void DashRepliesThroughTheSystemsInput()
    {
        // W-5.1.2: dictation or the keyboard, both the system's own.
        var code = DashCode();

        Assert.Contains("RecognizerIntent.ActionRecognizeSpeech", code, StringComparison.Ordinal);
        Assert.Contains("RemoteInput.GetResultsFromIntent(", code, StringComparison.Ordinal);
        Assert.Contains("SendAttempt.Reply(", code, StringComparison.Ordinal);
        Assert.Contains(".Again(", code, StringComparison.Ordinal);
        Assert.Contains("DashWords.For(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DashFilesAndDeletesUndoFirst()
    {
        // W-5.1.4 (A14, WD §9, O5): a row's long press opens the choice; the act goes at once, with no confirmation, under
        // the watch's own name and carrying the window the ring shows; a tap on the ring takes it back.
        var people = Code(Dash("PeopleActivity.cs"));
        var act = Code(Dash("ActActivity.cs"));

        Assert.Contains(".LongClick +=", people, StringComparison.Ordinal);
        Assert.Contains("ActActivity.For(", people, StringComparison.Ordinal);
        Assert.Contains("WristAct.File(", act, StringComparison.Ordinal);
        Assert.Contains("WristAct.Delete(", act, StringComparison.Ordinal);
        Assert.Contains("UndoRingView.WindowFor(", act, StringComparison.Ordinal);
        Assert.Contains(".Open(act.Window,", act, StringComparison.Ordinal);
        Assert.Contains(".Undo(", act, StringComparison.Ordinal);
        Assert.Contains(".Closed()", act, StringComparison.Ordinal);
        Assert.Contains("DashLayout.UndoGlyphCentre", act, StringComparison.Ordinal);
        Assert.Contains("DashWords.TapToUndo", Code(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Watch.Domain", "Dash", "WristAct.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUndoIsToldEvenWhenTheScreenHasGone()
    {
        // The act and its undo are told on the pool whether or not the screen is still there: only drawing waits for it.
        var act = Code(Dash("ActActivity.cs"));
        var carrier = Code(Dash("ActCarrier.cs"));

        Assert.Contains("Task.Run(() => ActCarrier.ActAsync(", act, StringComparison.Ordinal);
        Assert.Contains("Task.Run(() => ActCarrier.UndoAsync(", act, StringComparison.Ordinal);
        Assert.Contains(".Told(await PostLink.Shared.TellAsync(", carrier, StringComparison.Ordinal);
        Assert.Contains(".UndoTold(await PostLink.Shared.TellAsync(", carrier, StringComparison.Ordinal);
        Assert.DoesNotContain("Activity", carrier, StringComparison.Ordinal);
        Assert.DoesNotContain("IsFinishing", carrier, StringComparison.Ordinal);
    }

    [Fact]
    public void ARowNamesItsLongPress()
    {
        // TalkBack reaches the choice too (WD §12): the row's long press says what it does.
        var row = Code(Dash("PersonRowView.cs"));

        Assert.Contains("ActionLongClick", row, StringComparison.Ordinal);
        Assert.Contains("DashWords.FileOrDelete", row, StringComparison.Ordinal);
        Assert.Contains("LongClickable = true", row, StringComparison.Ordinal);
    }

    [Fact]
    public void TheUndoRingOpensForTheActsWindow()
    {
        // The act carries the window the ring shows (B1's dash/file and dash/delete), so the kit says it before it opens.
        var ring = Code(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Watch.Ui.Wear", "UndoRingView.cs"));

        Assert.Contains("public static TimeSpan WindowFor(Context", ring, StringComparison.Ordinal);
        Assert.Contains("public void Open(TimeSpan window, Action onUndo, Action onFinal)", ring, StringComparison.Ordinal);
        Assert.Contains("GlyphCentre", ring, StringComparison.Ordinal);
    }

    [Fact]
    public void DashShowsPhotosAsTheMockupDrawsThem()
    {
        // W-5.1.3 (mock: A group text with a photo): each photo asked for on its own, newest first, held for the process,
        // drawn in the mockup's picture with who and when under it, and decoded off the main thread.
        var conversation = Code(Dash("ConversationActivity.cs"));
        var photo = Code(Dash("PhotoView.cs"));

        Assert.Contains("new DashPhotoAsk(", conversation, StringComparison.Ordinal);
        Assert.Contains("Conversation.PhotosToAsk(", conversation, StringComparison.Ordinal);
        Assert.Contains("HeldPhotos", DashCode(), StringComparison.Ordinal);
        Assert.Contains("DashWords.Caption(", conversation, StringComparison.Ordinal);
        Assert.Contains("DashLayout.ReplyToAllCentre", conversation, StringComparison.Ordinal);
        foreach (var figure in new[] { "DashLayout.PhotoWidth", "DashLayout.PhotoHeight", "DashLayout.CaptionCentre" })
        {
            Assert.Contains(figure, conversation, StringComparison.Ordinal);
        }
        Assert.Contains("DashLayout.PhotoCorner", photo, StringComparison.Ordinal);
        Assert.Contains("DashWords.Photo(", photo, StringComparison.Ordinal);

        var decoding = Regex.Match(photo, @"Task\.Run\((?<body>[\s\S]*?)\);");
        Assert.True(decoding.Success && decoding.Groups["body"].Value.Contains("BitmapFactory.DecodeByteArray(", StringComparison.Ordinal),
            "A photo is decoded on the main thread.");
    }

    [Fact]
    public void DashTellsThePhoneWhatItRead()
    {
        // A14: read on the watch is read on the phone, through what the wrist drew.
        Assert.Contains("Conversation.ReadFor(", DashCode(), StringComparison.Ordinal);
    }

    [Fact]
    public void DashNeverConfirmsOrToasts()
    {
        // WD §6, §9: success is silent and an error a quiet line; there are no dialogs and no toasts.
        var code = DashCode();

        Assert.DoesNotContain("AlertDialog", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Toast", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Vibrator", code, StringComparison.Ordinal);
    }

    [Fact]
    public void DashCarriesPlexMono()
    {
        // WD §5: the watch's one family. The kit reads it from the app's assets; an app that draws words carries it.
        var project = Text(Dash("Dash.Wear.csproj"));
        foreach (var weight in new[] { "Regular", "Medium", "SemiBold" })
        {
            Assert.Matches($@"<AndroidAsset\s+Include=""\.\.\\Fonts\\IBMPlexMono-{weight}\.ttf""\s+Link=""Assets\\fonts\\IBMPlexMono-{weight}\.ttf""\s*/>", project);
        }

        var kit = Directory.EnumerateFiles(Path.Combine(WatchFiles.RepoRoot(), "Watch", "Watch.Ui.Wear"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !WatchFiles.IsBuildOutput(path)).Select(Code);
        Assert.Contains(kit, source => source.Contains("Typeface.CreateFromAsset(", StringComparison.Ordinal));
    }

    [Fact]
    public void DashCarriesItsIcon()
    {
        // The glyph on a round black icon (WD §3), generated by FaceStudio from the kit's own geometry.
        foreach (var density in new[] { "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
        {
            Assert.True(File.Exists(Dash("Resources", $"mipmap-{density}", "ic_launcher.png")), $"Dash has no {density} icon.");
        }
        Assert.Contains("Icon = \"@mipmap/ic_launcher\"", DashCode(), StringComparison.Ordinal);
    }

    // ---- the form words (A4's, held on the watch's side too) ----

    [Fact]
    public void FormWordsAreFrozen()
    {
        Assert.Equal("form", ApkForms.Field);
        Assert.Equal("phone", ApkForms.PhoneWord);
        Assert.Equal("watch", ApkForms.WatchWord);
        Assert.Equal("android.hardware.type.watch", ApkForms.WatchFeature);
        Assert.Equal(".watch.apk", ApkForms.WatchAssetSuffix);
    }

    // ---- the installer's manifest ----

    [Fact]
    public void TheInstallerIsAWatchApp()
    {
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));

        Assert.Matches(@"<uses-feature\s+android:name=""android\.hardware\.type\.watch""", manifest);
        Assert.Matches(@"<meta-data\s+android:name=""com\.google\.android\.wearable\.standalone""\s+android:value=""false""", manifest);
    }

    [Fact]
    public void TheInstallerAsksForThreePermissions()
    {
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));
        var asked = Regex.Matches(manifest, @"<uses-permission\s+android:name=""([^""]+)""").Select(match => match.Groups[1].Value);
        var attributes = InstallerSources().SelectMany(path => Regex.Matches(File.ReadAllText(path), @"\[assembly:\s*UsesPermission").Cast<Match>()).ToList();

        Assert.Equal(ExpectedInstallerPermissions.Order(), asked.Order());
        Assert.Empty(attributes);
    }

    [Fact]
    public void TheInstallerSeesTheWatchApps()
    {
        // Android 11+ shows an app only the packages it names; the installer compares versions with what it can see.
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));
        var queried = Regex.Matches(manifest, @"<package\s+android:name=""([^""]+)""").Select(match => match.Groups[1].Value);

        Assert.Equal(WatchIdentity.Packages.Where(package => package != WatchIdentity.InstallerPackage).Order(), queried.Order());
    }

    [Fact]
    public void TheInstallerHasNoScreen()
    {
        // WD §11: Point has no screens besides the face; the installer shows only the system's own prompt.
        var sources = InstallerSources().Select(File.ReadAllText).ToList();
        var manifest = Text(Installer("Properties", "AndroidManifest.xml"));

        Assert.NotEmpty(sources);
        Assert.DoesNotContain(sources, source => source.Contains("[Activity", StringComparison.Ordinal));
        Assert.DoesNotContain("<activity", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("android.intent.category.LAUNCHER", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInstallerNeverBuzzes()
    {
        // WD §7: four patterns, each with one meaning; asking to install is not one of them.
        var sources = string.Join("\n", InstallerSources().Select(File.ReadAllText));

        Assert.Contains("EnableVibration(false)", sources, StringComparison.Ordinal);
        Assert.Contains("NotificationImportance.Low", sources, StringComparison.Ordinal);
        Assert.DoesNotContain("Vibrator", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void FrozenSetsAreNotEmpty()
    {
        Assert.NotEmpty(ExpectedPackages);
        Assert.NotEmpty(ExpectedInstallerComponents);
        Assert.NotEmpty(ExpectedInstallerPermissions);
        Assert.NotEmpty(ExpectedDashPaths);
        Assert.NotEmpty(WatchIdentity.DashPaths);
        Assert.NotEmpty(WatchIdentity.Packages);
        Assert.NotEmpty(WatchIdentity.InstallerComponents);
    }

    // ---- Flyleaf's half (W-3.1.3): the phone end of the same contract ----

    private static string Flyleaf(params string[] parts) =>
        Path.Combine([WatchFiles.RepoRoot(), "FocusPhone", "Flyleaf.Android", .. parts]);

    private static IEnumerable<string> FlyleafSources() =>
        Directory.EnumerateFiles(Flyleaf(), "*.cs", SearchOption.AllDirectories).Where(path => !WatchFiles.IsBuildOutput(path));

    [Fact]
    public void ThePhonesNamesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher.WatchReportListener", WatchIdentity.PhoneReportListenerName);
        Assert.Equal("watchUpdates", WatchIdentity.PhonePreferences);
        Assert.Equal("sent", WatchIdentity.SentKey);
        Assert.Equal("lastRun", WatchIdentity.LastRunKey);
    }

    [Fact]
    public void FlyleafRegistersItsListenerByName()
    {
        var listener = FlyleafSources().Select(File.ReadAllText)
            .Where(source => source.Contains($"Name = WatchIdentity.{nameof(WatchIdentity.PhoneReportListenerName)}", StringComparison.Ordinal))
            .ToList();

        var source = Assert.Single(listener);
        Assert.Contains(": WearableListenerService", source, StringComparison.Ordinal);
        Assert.Contains("MessageClient.ActionMessageReceived", source, StringComparison.Ordinal);
        Assert.Contains("DataPath = WatchIdentity.UpdateInstalledPath", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FlyleafAsksForTheInstallerByItsCapability()
    {
        var sources = string.Join("\n", FlyleafSources().Select(File.ReadAllText));

        Assert.Contains("WatchIdentity.InstallerCapability", sources, StringComparison.Ordinal);
        Assert.Contains("UpdatePaths.ApkPathFor", sources, StringComparison.Ordinal);
        Assert.Contains("UpdateApkFrame.Header", sources, StringComparison.Ordinal);
        Assert.Contains("InstalledCodec.Encode(new InstalledAsk())", sources, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIdsAreNotCopiedIntoFlyleaf()
    {
        // Flyleaf names the watch's paths, capability and stores only through WatchIdentity, so the two ends cannot drift.
        string[] watchWords =
        [
            WatchIdentity.UpdateApkPath, WatchIdentity.UpdateInstalledPath, WatchIdentity.InstallerCapability,
            WatchIdentity.PhoneReportListenerName, WatchIdentity.PhonePreferences,
        ];
        Assert.All(watchWords, word => Assert.False(string.IsNullOrEmpty(word)));

        var copied = FlyleafSources()
            .Where(path => watchWords.Any(word => File.ReadAllText(path).Contains($"\"{word}", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(WatchFiles.RepoRoot(), path))
            .ToList();

        Assert.True(copied.Count == 0, "Use WatchIdentity rather than a copy:\n" + string.Join("\n", copied));
    }

    [Fact]
    public void FlyleafReadsTheSameCodec()
    {
        // One frame and one message layout for both ends: Flyleaf references Watch.Domain rather than a copy of it.
        var project = Text(Flyleaf("Flyleaf.Android.csproj"));

        Assert.Contains(@"..\..\Watch\Watch.Domain\Watch.Domain.csproj", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Xamarin.GooglePlayServices.Wearable\" />", project, StringComparison.Ordinal);
        Assert.DoesNotContain(FlyleafSources(), path => File.ReadAllText(path).Contains("BinaryPrimitives", StringComparison.Ordinal));
    }

    [Fact]
    public void TheWatchHalfRunsOffTheMainThread()
    {
        // "Install from GitHub now" awaits the watch half from the settings screen; hashing and sending a 10–60 MB APK
        // there froze it (release A fix round, finding 3).
        var service = Code(Flyleaf("UpdateService.cs"));
        var send = Regex.Match(service, @"public static Task SendToWatchAsync\([^)]*\)\s*=>(?<body>[^;]+;)", RegexOptions.Singleline);
        Assert.True(send.Success, "UpdateService has no SendToWatchAsync.");
        Assert.Contains("Task.Run(", send.Groups["body"].Value, StringComparison.Ordinal);

        var half = Code(Flyleaf("WatchUpdates.cs"));
        Assert.Contains("UpdateApkFrame.DescribeAsync(", half, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateApkFrame.Describe(", half, StringComparison.Ordinal);
        Assert.Contains(".ReadAsync(", half, StringComparison.Ordinal);
        Assert.Contains(".WriteAsync(", half, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bapk\.Read\(", half);
    }

    [Fact]
    public void EveryDataLayerWaitIsBounded()
    {
        // A Data Layer call that never answers held the watch's gate for good (release A fix round, finding 4): every one
        // is waited for at most its own wait, cut to what is left of the run.
        var half = Code(Flyleaf("WatchUpdates.cs"));
        string[] calls = ["GetCapabilityAsync(", "SendMessageAsync(", "OpenChannelAsync(", "GetOutputStreamAsync(", "CloseAsync("];

        var lines = half.Split('\n');
        var unbounded = lines
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => calls.Any(call => entry.Line.Contains(call, StringComparison.Ordinal)))
            .Where(entry => !entry.Line.Contains("BoundedAsync(", StringComparison.Ordinal))
            .Select(entry => $"WatchUpdates.cs:{entry.Number}: {entry.Line.Trim()}")
            .ToList();

        Assert.NotEmpty(lines.Where(line => calls.Any(call => line.Contains(call, StringComparison.Ordinal))));
        Assert.True(unbounded.Count == 0, "A Data Layer call waited for with no bound:\n" + string.Join("\n", unbounded));
        Assert.Contains("WatchReports.WaitAsync(ticket, deadline.Cut(", half, StringComparison.Ordinal);
        Assert.Contains("WatchReports.WaitForAsync(", half, StringComparison.Ordinal);
        Assert.Contains("deadline.Cut(DateTimeOffset.UtcNow, WatchSendPlan.ReportWait)", half, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerHandsItsDeadlineDown()
    {
        // One deadline, from the start of DoWork, reaches the watch half; the owner's tap takes one from the tap.
        var service = Code(Flyleaf("UpdateService.cs"));
        var settings = Code(Flyleaf("LauncherSettingsActivity.cs"));
        var half = Code(Flyleaf("WatchUpdates.cs"));

        Assert.Matches(@"public override Result DoWork\(\)\s*=>\s*HeadDiagnostics\.Worker\(nameof\(UpdateWorker\),\s*\(\)\s*=>\s*Check\(RunDeadline\.From\(DateTimeOffset\.UtcNow\)\)\);", service);
        Assert.Contains("SendToWatchAsync(ApplicationContext, force: false, deadline)", service, StringComparison.Ordinal);
        Assert.Contains("WatchGate.TryRunAsync(", service, StringComparison.Ordinal);
        Assert.Matches(@"var deadline = RunDeadline\.From\(DateTimeOffset\.UtcNow\);[\s\S]*SendToWatchAsync\(this, force: true, deadline\)", settings);
        Assert.Contains("deadline.Allows(DateTimeOffset.UtcNow, WatchSendPlan.SendNeeds)", half, StringComparison.Ordinal);
        Assert.DoesNotContain("SendBudget", half, StringComparison.Ordinal);
        Assert.DoesNotContain("Stopwatch", half.Split("class WatchReports")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void FlyleafsBuildWatchesTheWatchsContracts()
    {
        // build-flyleaf.yml publishes the head by folder name, so CiHygieneTests' closure (which follows .csproj paths
        // named in a workflow) cannot see that Flyleaf now builds Watch.Domain. A codec change must still rebuild Flyleaf,
        // or the phone sends a frame the installer no longer reads.
        var workflow = Text(Path.Combine(WatchFiles.RepoRoot(), ".github", "workflows", "build-flyleaf.yml"));

        Assert.Contains("- 'Watch/Watch.Domain/**'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWatchWorkflowChecksEveryFrozenPackage()
    {
        // build-watch.yml's APP ID CHECK reads each APK's package back against its line of the table (P14 W-3.2.1). The
        // workflow types the packages out, so it is held to the frozen ids here, from the watch's side.
        var workflow = Text(Path.Combine(WatchFiles.RepoRoot(), ".github", "workflows", "build-watch.yml"));

        Assert.Matches($@"(?m)^\s+Watch/Point\.Installer/Point\.Installer\.csproj {Regex.Escape(WatchIdentity.InstallerPackage)}\s*$", workflow);
        Assert.Matches($@"(?m)^\s+Watch/Dash\.Wear/Dash\.Wear\.csproj {Regex.Escape(WatchIdentity.DashPackage)}\s*$", workflow);
        Assert.Matches($@"(?m)^\s+Watch/Dash\.Wear/Dash\.Wear\.csproj {Regex.Escape(WatchIdentity.DashPackage)}\s*$", workflow);
        Assert.Contains($"FACE_PACKAGE: {WatchIdentity.FacePackage}", workflow, StringComparison.Ordinal);
    }
}
