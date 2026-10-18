using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Body;
using Constitution.Domain.Notices;
using Constitution.Domain.Waiting;
using Constitution.Infrastructure.Backup;
using Constitution.Tests.Guard;

namespace Constitution.Tests.Body;

/// <summary>
/// C-7.4.2 (G13, D98) — Body stays on the install. The one thing that may carry a Body value off it is
/// the owner's own encrypted backup (C-7.4.1); the six voice seats may be handed the <i>facts</i> they
/// need (D98) and nothing else leaves. This is a guard over every outbound path that exists now, built
/// so that the paths still to come extend it by adding a line, not by rewriting it.
///
/// <list type="number">
/// <item><b>Payload types.</b> Walking the type graph from the Diagnostics, Notices and Waiting types
/// (what is shared, posted or shown), no member, constructor or method reaches a type from the Body
/// namespace or the backup's history, and no member is named for a reading's value. The way to keep a
/// reading out of a shared file is for the model to have nowhere to put one.</item>
/// <item><b>The head.</b> No file in <c>Constitution.Android</c> that handles Body also contains a way
/// off the phone (an intent extra or share, a broadcast, a notification, the clipboard), and no logging
/// call or outbound call has a Body value in its arguments, however it got there (a type, a value
/// word, or a local the file declared with a Body type or filled from one). Counts are shapes and are
/// allowed; Diagnostics prints counts.</item>
/// <item><b>Infrastructure.</b> The only files that may name a value-carrying Body type are those on
/// <see cref="BodyOutbound.InfrastructureAllowlist"/>: the backup folder (the one entry allowed to reach
/// Drive), the install's own local Body store and readers (<c>Body/</c>, which hold Body here and must
/// contain no way to the network, Drive or the voice's model), and, by exact file once they exist, the
/// voice-seat fact builders D98 permits (also local: the composer that sends the facts names no Body).</item>
/// </list>
///
/// <para><b>Extending it.</b> Release D's household file adds its record types to
/// <see cref="BodyOutbound.PayloadNamespaces"/> or <see cref="BodyOutbound.PayloadRoots"/> and its
/// record kinds to <see cref="BodyOutbound.HouseholdRecordKinds"/>; a new voice-seat fact builder adds
/// its exact file to <see cref="BodyOutbound.SeatFactBuilderFiles"/>. Nothing else in this file changes.</para>
///
/// <para><b>Non-vacuous, three ways.</b> Each scan must cover a floor of what it should (types, files,
/// outbound sites of each kind); each detector must fire on a planted sample built at run time; and a
/// planted tree in a temp directory must come back named by file and line. <b>What it cannot catch:</b>
/// a Body value that reaches a log through a path the file's own text does not show (a helper in another
/// file that formats it), which is why the Body-handling files are also forbidden from the outward
/// channels outright; and a value carried in a string built from a literal. Those are review's job.</para>
/// </summary>
public class BodyStaysHomeTests
{
    // ---- 1. the payload types ----

    [Fact]
    public void NoOutboundTypeReachesABodyType()
    {
        var walk = TypeGraph.Walk(BodyOutbound.PayloadTypes(), BodyVocabulary.Current.ForbiddenTypes);

        Assert.True(
            walk.Leaks.Count == 0,
            "An outbound payload type can hold Body data; Body never leaves except in its backup:\n" + string.Join("\n", walk.Leaks));
    }

    [Fact]
    public void NoOutboundMemberIsNamedForAReading()
    {
        var walk = TypeGraph.Walk(BodyOutbound.PayloadTypes(), BodyVocabulary.Current.ForbiddenTypes);

        var smells = walk.Reached.SelectMany(TypeGraph.ReadingNamedMembers).ToList();

        Assert.True(smells.Count == 0, "A payload member is named for a Body value:\n" + string.Join("\n", smells));
    }

    [Fact]
    public void TheTypeWalkCoversTheOutboundSurface()
    {
        var roots = BodyOutbound.PayloadTypes();
        var walk = TypeGraph.Walk(roots, BodyVocabulary.Current.ForbiddenTypes);
        var names = walk.Reached.Select(type => type.Name).ToHashSet();

        Assert.True(roots.Count >= 15, $"Only {roots.Count} payload types were found; the guard is not reading the real surface.");
        foreach (var expected in new[]
        {
            nameof(Notice), nameof(NoticeKind), nameof(NoticeBoard), nameof(WaitingItem), nameof(WaitingPageModel),
            "DiagnosticsSnapshot", "DiagnosticsCounts", "ComponentOutcome", "DocumentDelivery", "CredentialStatus",
        })
        {
            Assert.Contains(expected, names);
        }

        Assert.True(walk.Reached.Count >= roots.Count);
    }

    [Fact]
    public void EveryTypeInTheBodyNamespaceIsForbiddenToThem()
    {
        var forbidden = BodyVocabulary.Current.ForbiddenTypes;

        foreach (var type in new[] { typeof(BodyReading), typeof(BodySignal), typeof(BodyWindow), typeof(IBodyStore), typeof(BodyHistory), typeof(DecodedBodyBackup) })
        {
            Assert.Contains(type, forbidden);
        }

        var inTheNamespace = typeof(BodyReading).Assembly.GetTypes()
            .Where(type => type.Namespace == "Constitution.Domain.Body" && !type.IsNested)
            .Where(type => type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null)
            .ToList();
        Assert.True(inTheNamespace.Count >= 9, "The Body namespace looks empty; the guard is not reading the real one.");
        Assert.All(inTheNamespace, type => Assert.Contains(type, forbidden));
    }

    // Planted samples: each holds, in a different way, something an outbound type must not.
    private sealed record HoldsAReading(BodyReading Latest);

    private sealed record HoldsAListOfReadings(IReadOnlyList<BodyReading> History);

    private sealed record HoldsAnArrayOfReadings(BodyReading[] Days);

    private sealed record HoldsASignalInADictionary(Dictionary<string, List<BodySignal>> BySource);

    private sealed record HoldsAWindowDeep(HoldsAWindowInner Inner);

    private sealed record HoldsAWindowInner(BodyWindow Window);

    private sealed record HoldsAHistoryNullable(BodyHistory? Past);

    private sealed class TakesASignalInItsConstructor
    {
        public TakesASignalInItsConstructor(BodySignal signal) => Signal = signal.ToString();

        public string Signal { get; }
    }

    private sealed class ReturnsAReadingFromAMethod
    {
        public static BodyReading Latest() => new(BodySignal.Weight, new DateOnly(2026, 11, 17), 1);
    }

    private sealed class TakesAStoreInAMethod
    {
        public static string Describe(IBodyStore store) => store.IsEmpty.ToString();
    }

    private sealed record HoldsAFieldNamedForAWeight(double LatestWeight);

    private sealed record HoldsAFieldNamedForReadings(int[] ReadingValues);

    private sealed record HoldsAHeartRate(double RestingHeartRate);

    private enum AnEnumNamedForAReading
    {
        None,
        TodaysWeightValue,
    }

    private sealed record Clean(string Title, int Count, DateTimeOffset At, IReadOnlyList<string> Lines);

    public static IEnumerable<object[]> TypesThatLeak() =>
    [
        [typeof(HoldsAReading)],
        [typeof(HoldsAListOfReadings)],
        [typeof(HoldsAnArrayOfReadings)],
        [typeof(HoldsASignalInADictionary)],
        [typeof(HoldsAWindowDeep)],
        [typeof(HoldsAHistoryNullable)],
        [typeof(TakesASignalInItsConstructor)],
        [typeof(ReturnsAReadingFromAMethod)],
        [typeof(TakesAStoreInAMethod)],
    ];

    public static IEnumerable<object[]> TypesNamedForAReading() =>
    [
        [typeof(HoldsAFieldNamedForAWeight)],
        [typeof(HoldsAFieldNamedForReadings)],
        [typeof(HoldsAHeartRate)],
        [typeof(AnEnumNamedForAReading)],
    ];

    [Theory]
    [MemberData(nameof(TypesThatLeak))]
    public void TheWalkFlagsAPlantedBodyMember(Type sample)
    {
        var walk = TypeGraph.Walk([sample], BodyVocabulary.Current.ForbiddenTypes);

        Assert.NotEmpty(walk.Leaks);
    }

    [Theory]
    [MemberData(nameof(TypesNamedForAReading))]
    public void TheWalkFlagsAPlantedReadingName(Type sample)
    {
        var walk = TypeGraph.Walk([sample], BodyVocabulary.Current.ForbiddenTypes);

        Assert.NotEmpty(walk.Reached.SelectMany(TypeGraph.ReadingNamedMembers));
    }

    [Fact]
    public void TheWalkLeavesACleanTypeAlone()
    {
        var walk = TypeGraph.Walk([typeof(Clean)], BodyVocabulary.Current.ForbiddenTypes);

        Assert.Empty(walk.Leaks);
        Assert.Empty(walk.Reached.SelectMany(TypeGraph.ReadingNamedMembers));
        Assert.Contains(typeof(Clean), walk.Reached);
    }

    // ---- 2. the head ----

    private static string HeadRoot() => Path.Combine(ConstitutionSource.Root(), "Constitution.Android");

    private static ScanResult ScanHead(string root) =>
        GuardScan.Run(root, (path, text) =>
            path.EndsWith(".cs", StringComparison.Ordinal)
                ? HeadScan.Find(path, text, BodyVocabulary.Current, BodyOutbound.HeadFilesMayHandleBodyAndLeave)
                : []);

    [Fact]
    public void NoOutboundSiteInTheHeadCarriesBody()
    {
        var result = ScanHead(HeadRoot());

        Assert.True(
            result.Hits.Count == 0,
            "A file in Constitution.Android that handles Body has a way off the phone, or a log or outbound call carries a Body value:\n"
            + string.Join("\n", result.Hits));
    }

    [Fact]
    public void TheHeadScanIsNotVacuous()
    {
        var files = ScanHead(HeadRoot()).Scanned.Where(path => path.EndsWith(".cs", StringComparison.Ordinal)).ToList();
        var sites = files
            .SelectMany(path => HeadScan.Sites(File.ReadAllText(Path.Combine(HeadRoot(), path))).Select(site => site.Kind))
            .ToList();

        Assert.True(files.Count >= 40, $"Only {files.Count} head source files were scanned.");
        Assert.Contains("intent extra", sites);
        Assert.Contains("share intent", sites);
        Assert.True(sites.Count(kind => kind == "log") >= 2, "The scan found too few logging calls in the head to be reading it.");
    }

    public static IEnumerable<object[]> PlantedLeaks() =>
    [
        [
            "intent extra in a file that handles Body",
            "using Constitution.Domain.Body;\nclass A { void F(Intent i, BodyReading r) { i.PutExtra(\"w\", r.Value); } }",
        ],
        [
            "share intent in a file that handles Body",
            "class A { IBodyStore store; void F() { var s = new Intent(Intent.ActionSend); } }",
        ],
        [
            "broadcast in a file that handles Body",
            "class A { BodyHistory history; void F() { SendBroadcast(intent); } }",
        ],
        [
            "notification in a file that handles Body",
            "class A { BodyReading latest; void F() { var b = new NotificationCompat.Builder(context, id); } }",
        ],
        [
            "clipboard in a file that handles Body",
            "class A { ChartGeometry g; void F() { clipboard.SetPrimaryClip(clip); } }",
        ],
        [
            "log carrying a Body value",
            "class A { void F(double weightLb) { Log.Info(Tag, $\"latest {weightLb}\"); } }",
        ],
        [
            "log carrying a Body value",
            "class A { void F(BodyReading r) { Log.Warn(Source, r.ToString()); } }",
        ],
        [
            "log carrying a Body value",
            "class A { void F(IBodyStore store) { var all = store.All(); var latest = all[0]; log.Outcome($\"read {latest.Value}\"); } }",
        ],
        [
            "log carrying a Body value",
            "class A { void F(IReadOnlyList<BodyReading> readings) { Log.Debug(Tag, readings[0].Value.ToString()); } }",
        ],
        [
            "log carrying a Body value",
            "class A { void F(double bpm) { Console.WriteLine(bpm); } }",
        ],
        [
            "intent extra carrying a Body value",
            "class A { void F(double bpm) { i.PutExtra(\"k\", bpm); } }",
        ],
        [
            "log carrying a Body value",
            "class A { void F() { Log.Error(Tag, $\"{(true ? \"x\" : $\"{restingHeartRate}\")}\"); } }",
        ],
    ];

    [Theory]
    [MemberData(nameof(PlantedLeaks))]
    public void APlantedLeakIsCaught(string kind, string source)
    {
        var findings = HeadScan.Find("Services/Planted.cs", source, BodyVocabulary.Current, []);

        Assert.Contains(findings, finding => finding.Kind == kind);
    }

    public static IEnumerable<object[]> CleanSources() =>
    [
        // A way off the phone in a file that does not touch Body: the diagnostics share.
        ["class A { void F() { var send = new Intent(Intent.ActionSend); send.PutExtra(Intent.ExtraText, text); } }"],
        // Words in literals and comments are not values.
        ["class A { void F() { Log.Warn(Source, \"weight read was denied\"); } }"],
        ["class A { void F() { Log.Warn(Source, $\"{phrase} steps done\"); } }"],
        ["class A { /* i.PutExtra(\"w\", BodyReading) */ void F() { } // Log.Info(Tag, weightLb)\n }"],
        ["class A { void F() { var text = \"IBodyStore\"; Log.Info(Tag, text); } }"],
        // Body without a way off the phone.
        ["using Constitution.Domain.Body;\nclass A { void F(BodyReading r) { chart.Draw(r.Value); } }"],
        // A count is a shape.
        ["class A { void F(IReadOnlyList<BodyReading> readings) { Log.Info(Tag, $\"read {readings.Count} days\"); } }"],
        // The backup's own outcome is not a reading.
        ["class A { void F(BodyBackup backup) { log.Outcome(Words(result.Outcome)); } }"],
        // A logging call whose arguments are plain.
        ["class A { void F() { log.Outcome(ConnectionFailure.LogPhrase(act, failure)); } }"],
        // Naming a signal is the trail's convention ("body read weight: ok"); a signal is a name, not a value.
        ["class A { void F(BodySignal s) { log.Outcome($\"body read {BodySignals.Id(s)}: ok\"); var send = new Intent(Intent.ActionSend); } }"],
        // A tally is a shape.
        ["class A { void F(IBodyStore store, IReadOnlyList<BodyReading> samples) { log.Outcome($\"{samples.Count} usable, empty {store.IsEmpty}\"); } }"],
    ];

    [Theory]
    [MemberData(nameof(CleanSources))]
    public void ACleanSourceIsLeftAlone(string source)
    {
        Assert.Empty(HeadScan.Find("Services/Clean.cs", source, BodyVocabulary.Current, []));
    }

    [Fact]
    public void AFileOnTheHeadAllowlistIsPermitted()
    {
        const string source = "class A { IBodyStore store; void F() { var s = new Intent(Intent.ActionSend); } }";

        Assert.NotEmpty(HeadScan.Find("Services/Planted.cs", source, BodyVocabulary.Current, []));
        Assert.Empty(HeadScan.Find("Services/Planted.cs", source, BodyVocabulary.Current, ["Services/Planted.cs"]));
    }

    [Fact]
    public void TheHeadAllowlistStartsEmptyAndNamesFilesNotFolders()
    {
        Assert.All(BodyOutbound.HeadFilesMayHandleBodyAndLeave, path =>
        {
            Assert.EndsWith(".cs", path, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(HeadRoot(), path)), $"{path} is on the head allowlist but does not exist.");
        });
    }

    [Fact]
    public void APlantedTreeIsNamedByFileAndLine()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Services/Leaky.cs",
                "using Constitution.Domain.Body;\nclass Leaky\n{\n    void F(Intent i, BodyReading r)\n    {\n        i.PutExtra(\"w\", r.Value);\n    }\n}\n");
            GuardScan.Plant(root, "Services/Fine.cs", "class Fine { void F() { Log.Info(Tag, \"ready\"); } }\n");
            GuardScan.Plant(root, "Notes.md", "i.PutExtra(\"w\", BodyReading)\n");

            var result = ScanHead(root.FullName);

            Assert.Equal(2, result.Scanned.Count(path => path.EndsWith(".cs", StringComparison.Ordinal)));
            Assert.All(result.Hits, hit => Assert.Equal("Services/Leaky.cs", hit.File));
            Assert.Contains(result.Hits, hit => hit.Line == 6 && hit.Kind == "intent extra in a file that handles Body");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheSanitiserBlanksCommentsAndLiteralsButKeepsCodeAndPositions()
    {
        const string source =
            "var a = \"IBodyStore\"; // BodyReading\n"
            + "var b = @\"Body\"\"Reading\"; /* BodySignal\n BodyWindow */ var c = $\"x {weightLb} y {{BodyHistory}}\";\n"
            + "var d = 'x'; var e = '\\'';";

        var clean = HeadScan.Sanitise(source);

        Assert.Equal(source.Length, clean.Length);
        Assert.Equal(source.Count(ch => ch == '\n'), clean.Count(ch => ch == '\n'));
        Assert.DoesNotContain("IBodyStore", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("BodyReading", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("BodySignal", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("BodyWindow", clean, StringComparison.Ordinal);
        Assert.DoesNotContain("BodyHistory", clean, StringComparison.Ordinal);
        Assert.Contains("weightLb", clean, StringComparison.Ordinal);
        Assert.Contains("var d", clean, StringComparison.Ordinal);
    }

    // ---- 3. Infrastructure ----

    private static string InfrastructureRoot() => Path.Combine(ConstitutionSource.Root(), "Constitution.Infrastructure");

    [Fact]
    public void OnlyTheBackupNamesBodyInInfrastructure()
    {
        var violations = InfrastructureBody.Violations(InfrastructureRoot(), BodyOutbound.InfrastructureAllowlist);

        Assert.True(
            violations.Count == 0,
            "A file in Constitution.Infrastructure names a Body type outside the allowlist (the backup, and the voice-seat fact builders, D98):\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void TheInfrastructureScanIsNotVacuous()
    {
        var files = GuardScan.Files(InfrastructureRoot()).Where(path => path.EndsWith(".cs", StringComparison.Ordinal)).ToList();
        var referencing = InfrastructureBody.Referencing(InfrastructureRoot());

        Assert.True(files.Count >= 20, $"Only {files.Count} Infrastructure source files were found.");
        Assert.Contains("Backup/BodyBackup.cs", referencing);
        Assert.Contains("Backup/BodyHistory.cs", referencing);
        Assert.Contains(referencing, path => path.StartsWith("Body/", StringComparison.Ordinal));
        Assert.True(referencing.Count >= 4);
        Assert.All(referencing, path => Assert.True(
            BodyOutbound.InfrastructureAllowlist.Any(entry => entry.Allows(path)),
            $"{path} names a Body type and is not allowlisted."));
    }

    [Fact]
    public void TheAllowlistIsTheBackupFolderAndExactFiles()
    {
        var folders = BodyOutbound.InfrastructureAllowlist.Where(entry => entry.Path.EndsWith('/')).ToList();

        Assert.Equal(["Backup/", "Body/"], folders.Select(entry => entry.Path));
        Assert.Equal(["Backup/"], BodyOutbound.InfrastructureAllowlist.Where(entry => entry.MayLeave).Select(entry => entry.Path));
        Assert.All(BodyOutbound.InfrastructureAllowlist, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Reason));
            if (!entry.Path.EndsWith('/'))
            {
                Assert.EndsWith(".cs", entry.Path, StringComparison.Ordinal);
                Assert.True(
                    File.Exists(Path.Combine(InfrastructureRoot(), entry.Path)),
                    $"{entry.Path} is on the allowlist but does not exist.");
            }
        });
        Assert.All(BodyOutbound.SeatFactBuilderFiles, path => Assert.StartsWith("Voice/", path, StringComparison.Ordinal));
    }

    [Fact]
    public void APlantedInfrastructureLeakIsNamedByFile()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Voice/SeatLeak.cs", "using Constitution.Domain.Body;\nclass SeatLeak { IBodyStore store; }\n");
            GuardScan.Plant(root, "Vault/Sneaky.cs", "class Sneaky { void F(BodyReading r) { } }\n");
            GuardScan.Plant(root, "Backup/Fine.cs", "class Fine { IBodyStore store; BodyHistory history; }\n");
            GuardScan.Plant(root, "Voice/Mention.cs", "// IBodyStore is not used here\nclass Mention { string s = \"BodyReading\"; }\n");

            var violations = InfrastructureBody.Violations(root.FullName, BodyOutbound.InfrastructureAllowlist);

            Assert.Equal(["Vault/Sneaky.cs", "Voice/SeatLeak.cs"], violations.Order().ToArray());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheLocalBodyFilesHaveNoWayOut()
    {
        var offenders = InfrastructureBody.LocalFilesWithAWayOut(InfrastructureRoot(), BodyOutbound.InfrastructureAllowlist);

        Assert.True(
            offenders.Count == 0,
            "A file allowed to name Body because it keeps Body on the install can reach the network, Drive or the voice's model:\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void APlantedLocalFileWithAWayOutIsNamedAndTheBackupIsNot()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Body/Leaky.cs", "class Leaky { IBodyStore store; IDriveTextFile drive; }\n");
            GuardScan.Plant(root, "Body/Sends.cs", "class Sends { BodyReading r; HttpClient client; }\n");
            GuardScan.Plant(root, "Body/Local.cs", "class Local { IBodyStore store; string path; }\n");
            GuardScan.Plant(root, "Backup/Writes.cs", "class Writes { IBodyStore store; IDriveTextFile drive; }\n");

            var offenders = InfrastructureBody.LocalFilesWithAWayOut(root.FullName, BodyOutbound.InfrastructureAllowlist);

            Assert.Equal(["Body/Leaky.cs", "Body/Sends.cs"], offenders.Order().ToArray());
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ASeatFactBuilderIsPermittedByExactFileOnly()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, "Voice/BodySeatFacts.cs", "class BodySeatFacts { IBodyStore store; }\n");
            GuardScan.Plant(root, "Voice/Other.cs", "class Other { IBodyStore store; }\n");
            var allowlist = new[] { new AllowedPath("Voice/BodySeatFacts.cs", "D98: the voice-seat fact builder") };

            var violations = InfrastructureBody.Violations(root.FullName, allowlist);

            Assert.Equal(["Voice/Other.cs"], violations);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // ---- 4. the household file's record kinds (Release D) ----

    private static readonly Regex BodyKindWords = new(
        @"(?i)body|reading|weight|heart|sleep|steps|calori|health|signal|strength|workout-load",
        RegexOptions.CultureInvariant);

    [Fact]
    public void NoHouseholdRecordKindIsNamedForBody()
    {
        Assert.All(BodyOutbound.HouseholdRecordKinds, kind =>
            Assert.False(BodyKindWords.IsMatch(kind), $"The household record kind '{kind}' is named for Body data; Body never crosses (G13)."));
    }

    [Fact]
    public void TheKindCheckFlagsBodyAndLeavesTheHouseholdsOwnKindsAlone()
    {
        foreach (var bad in new[] { "body-reading", "weight-log", "sleep", "health-sync", "heart-rate", "signal-history" })
        {
            Assert.True(BodyKindWords.IsMatch(bad), bad);
        }

        foreach (var fine in new[] { "recipe", "meal-plan", "taste-profile", "pantry", "roster", "rating", "planning-lease" })
        {
            Assert.False(BodyKindWords.IsMatch(fine), fine);
        }
    }

    [Fact]
    public void TheRegistriesReleaseDExtendsAreNamedAndVisible()
    {
        // Not a check on today's contents: it pins that the extension points exist under these names.
        Assert.NotNull(BodyOutbound.PayloadNamespaces);
        Assert.NotNull(BodyOutbound.PayloadRoots);
        Assert.NotNull(BodyOutbound.HouseholdRecordKinds);
        Assert.NotNull(BodyOutbound.SeatFactBuilderFiles);
        Assert.NotNull(BodyOutbound.HeadFilesMayHandleBodyAndLeave);
        Assert.Contains("Constitution.Domain.Diagnostics", BodyOutbound.PayloadNamespaces);
        Assert.Contains("Constitution.Domain.Notices", BodyOutbound.PayloadNamespaces);
        Assert.Contains("Constitution.Domain.Waiting", BodyOutbound.PayloadNamespaces);
    }
}

// ---------------------------------------------------------------------------------------------
// The registries and the scanners. The registries are what a later release edits; the scanners are
// what the tests above exercise, and they take their input as arguments (a root directory, a source
// text, an allowlist) so a planted sample can be run through exactly the code the real tree is.
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A place Body may be named: a folder (ends in a slash) or one exact file, and why. <paramref name="MayLeave"/>
/// is true only for the backup, whose whole job is to put an encrypted copy in the person's own Drive; every
/// other entry keeps Body on the install, and a file under it that can reach the network, Drive or the voice's
/// model fails the guard.
/// </summary>
internal sealed record AllowedPath(string Path, string Reason, bool MayLeave = false)
{
    public bool Allows(string relative) =>
        Path.EndsWith('/') ? relative.StartsWith(Path, StringComparison.Ordinal) : relative == Path;
}

/// <summary>Everything a later release extends. Edit these lists; no test in this file changes.</summary>
internal static class BodyOutbound
{
    /// <summary>
    /// Namespaces in <c>Constitution.Domain</c> whose types are what is shared, posted or shown. Release D
    /// (the household file) adds the namespace holding its record types here.
    /// </summary>
    public static readonly IReadOnlyList<string> PayloadNamespaces =
    [
        "Constitution.Domain.Diagnostics",
        "Constitution.Domain.Notices",
        "Constitution.Domain.Waiting",
    ];

    /// <summary>Individual payload types outside those namespaces: a broadcast's payload, the household record base.</summary>
    public static readonly IReadOnlyList<Type> PayloadRoots = [];

    /// <summary>
    /// The household file's record kinds, as their wire names. Empty until Release D writes the file;
    /// the file's kinds list should be referenced here so a Body-named kind fails the build of the tests.
    /// </summary>
    public static readonly IReadOnlyList<string> HouseholdRecordKinds = [];

    /// <summary>
    /// The voice-seat fact builders D98 permits to read Body, by exact file under Constitution.Infrastructure.
    /// None exists yet: the seats are fed from <c>BodyMonthFacts</c> in the Domain, which carries no reading.
    /// Add the exact file when one is written; a folder is never listed.
    /// </summary>
    public static readonly IReadOnlyList<string> SeatFactBuilderFiles = [];

    /// <summary>
    /// Files in Constitution.Android that may both handle Body and contain a way off the phone. Empty, and
    /// it should stay short: add a file only with a reason a reviewer accepts. Each entry below says
    /// what actually leaves, and it is never a Body value.
    /// <list type="bullet">
    /// <item>Health/ActivityHealthPermissionRequester.cs: the only intent extra is this app's own package
    /// name, to open Health Connect's settings page (Release B review).</item>
    /// <item>Settings/DiagnosticsExport.cs: shares the closed Diagnostics text, whose types hold only
    /// counts, enums and validated identifiers (guarded by DiagnosticsSnapshotShapeTests); it touches Body
    /// only to read the last Body-sync OUTCOME.</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlyList<string> HeadFilesMayHandleBodyAndLeave =
    [
        "Health/ActivityHealthPermissionRequester.cs",
        "Settings/DiagnosticsExport.cs",
    ];

    /// <summary>Where Body may be named in Constitution.Infrastructure.</summary>
    public static IReadOnlyList<AllowedPath> InfrastructureAllowlist { get; } =
    [
        new AllowedPath("Backup/", "the private, encrypted Body backup: the only place Body leaves the install (C-7.4.1, D98)", MayLeave: true),
        new AllowedPath("Body/", "the install's own Body store and readers: they hold Body here and have no way out"),
        .. SeatFactBuilderFiles.Select(file => new AllowedPath(file, "a voice-seat fact builder: the facts a seat needs and no more (D98)")),
    ];

    /// <summary>Every type in the payload namespaces, plus the individually registered roots.</summary>
    public static IReadOnlyList<Type> PayloadTypes() =>
    [
        .. typeof(Notice).Assembly.GetTypes()
            .Where(type => type.Namespace is { } ns && PayloadNamespaces.Contains(ns) && !type.IsNested)
            .Where(type => type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null),
        .. PayloadRoots,
    ];
}

/// <summary>
/// What counts as Body: every type in the Body namespace, plus the backup's history snapshot. Most carry
/// or read readings; a few only <i>name</i> things (<see cref="NameOnly"/>), and naming a signal in a
/// log line ("body read weight: ok") is the trail's own convention, so the file and call rules look
/// only for the types that carry a value. The payload-type rule is stricter and forbids all of them: a
/// shared type has no business holding even a signal.
/// </summary>
internal sealed class BodyVocabulary
{
    /// <summary>Types that name a signal or a window and carry no reading.</summary>
    public static readonly IReadOnlySet<string> NameOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(BodySignal), nameof(BodySignals), nameof(BodyWindow), nameof(BodyWindows), nameof(RisingIs),
    };

    private BodyVocabulary(IReadOnlySet<Type> forbidden)
    {
        ForbiddenTypes = forbidden;
        ValueTypeNames = forbidden
            .Select(type => type.Name)
            .Where(name => !NameOnly.Contains(name))
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var alternatives = string.Join("|", ValueTypeNames.Select(Regex.Escape));
        ValueTypeToken = new Regex($@"\b(?:{alternatives})\b|\bConstitution\.Domain\.Body\b", RegexOptions.CultureInvariant);
        DeclaredWithValueType = new Regex(
            $@"\b(?:{alternatives})\b(?:\s*,\s*[\w.]+(?:<[^<>]*>)?)*[>\]\[\?]*\s+(?<name>[A-Za-z_]\w*)\s*(?=[=;,){{:]|$)",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);
    }

    public static BodyVocabulary Current { get; } = Build();

    /// <summary>Every type a shared payload must not reach.</summary>
    public IReadOnlySet<Type> ForbiddenTypes { get; }

    /// <summary>The names of the types that carry a value.</summary>
    public IReadOnlyList<string> ValueTypeNames { get; }

    /// <summary>A value-carrying Body type's name, or the Body namespace, anywhere in a text.</summary>
    public Regex ValueTypeToken { get; }

    /// <summary>A declaration of something typed with a value-carrying Body type: the capture is its name.</summary>
    public Regex DeclaredWithValueType { get; }

    /// <summary>
    /// An identifier that names a measurement. Looked for in the arguments of a logging or outbound call,
    /// after string literals are blanked, so a message that merely says "weight" is not a value.
    /// </summary>
    public Regex ValueWord { get; } = new(
        @"(?i)weight|bpm|heartrate|\bhrv\b|calories|kcal|bodyfat|sleephours|stepcount|dailysteps|\bsteps\b",
        RegexOptions.CultureInvariant);

    private static BodyVocabulary Build()
    {
        var inTheNamespace = typeof(BodyReading).Assembly.GetTypes()
            .Where(type => type.Namespace == "Constitution.Domain.Body" && !type.IsNested)
            .Where(type => type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null);
        var forbidden = new HashSet<Type>(inTheNamespace) { typeof(BodyHistory), typeof(DecodedBodyBackup) };
        return new BodyVocabulary(forbidden);
    }
}

internal sealed record WalkResult(IReadOnlyList<Type> Reached, IReadOnlyList<string> Leaks);

/// <summary>
/// Walks the types an outbound payload can reach: through fields, properties, constructor parameters,
/// method parameters and return types, nested types and base types, into generic arguments and array
/// elements, and across every type in the Constitution assemblies. It stops at the framework.
/// </summary>
internal static class TypeGraph
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Regex ReadingName = new(
        @"(?i)reading|weight|calorie|kcal|heartrate|bpm|bodyfat|bodycomposition|sleephours|strength|hrv|steps",
        RegexOptions.CultureInvariant);

    public static WalkResult Walk(IEnumerable<Type> roots, IReadOnlySet<Type> forbidden)
    {
        var rootTypes = roots.ToList();
        var reached = new List<Type>();
        var seen = new HashSet<Type>();
        var leaks = new List<string>();
        var queue = new Queue<Type>();

        // Constitution's own assemblies, and wherever the roots came from (a planted sample lives in the
        // test assembly). Anything else is the framework or a library, and is not walked into.
        var own = new HashSet<Assembly>(rootTypes.Select(root => root.Assembly))
        {
            typeof(BodyReading).Assembly,
            typeof(BodyHistory).Assembly,
        };

        void Reach(Type type)
        {
            if (seen.Add(type))
            {
                reached.Add(type);
                queue.Enqueue(type);
            }
        }

        void Consider(string where, Type used)
        {
            foreach (var leaf in Leaves(used))
            {
                if (forbidden.Contains(leaf))
                {
                    leaks.Add($"{where} -> {leaf.Name}");
                }
                else if (own.Contains(leaf.Assembly))
                {
                    Reach(leaf);
                }
            }
        }

        foreach (var root in rootTypes)
        {
            Consider("(payload root)", root);
            Reach(root);
        }

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            foreach (var (where, used) in References(type, own))
            {
                Consider(where, used);
            }
        }

        return new WalkResult(reached, leaks);
    }

    /// <summary>The members of a type that are named for a measurement: fields, properties, constructor parameters and enum values.</summary>
    public static IEnumerable<string> ReadingNamedMembers(Type type) =>
        MemberNames(type).Where(pair => ReadingName.IsMatch(pair.Name)).Select(pair => $"{type.Name}.{pair.Name}");

    private static IEnumerable<(string Name, bool IsType)> MemberNames(Type type)
    {
        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type))
            {
                yield return (name, false);
            }

            yield break;
        }

        foreach (var field in type.GetFields(Declared))
        {
            yield return (Clean(field.Name), false);
        }

        foreach (var property in type.GetProperties(Declared))
        {
            yield return (property.Name, false);
        }

        foreach (var constructor in type.GetConstructors(Declared))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return (parameter.Name ?? string.Empty, false);
            }
        }
    }

    private static IEnumerable<(string Where, Type Used)> References(Type type, IReadOnlySet<Assembly> own)
    {
        if (type.IsEnum)
        {
            yield break;
        }

        foreach (var field in type.GetFields(Declared))
        {
            yield return ($"{type.Name}.{Clean(field.Name)}", field.FieldType);
        }

        foreach (var property in type.GetProperties(Declared))
        {
            yield return ($"{type.Name}.{property.Name}", property.PropertyType);
        }

        foreach (var constructor in type.GetConstructors(Declared))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return ($"{type.Name}.ctor({parameter.Name})", parameter.ParameterType);
            }
        }

        foreach (var method in type.GetMethods(Declared).Where(method => !method.IsSpecialName))
        {
            yield return ($"{type.Name}.{method.Name}() returns", method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                yield return ($"{type.Name}.{method.Name}({parameter.Name})", parameter.ParameterType);
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return ($"{type.Name}.{nested.Name}", nested);
        }

        if (type.BaseType is { } baseType && own.Contains(baseType.Assembly))
        {
            yield return ($"{type.Name} : {baseType.Name}", baseType);
        }
    }

    private static IEnumerable<Type> Leaves(Type type)
    {
        if (type.IsGenericParameter)
        {
            yield break;
        }

        if (type.HasElementType)
        {
            foreach (var leaf in Leaves(type.GetElementType()!))
            {
                yield return leaf;
            }

            yield break;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var leaf in Leaves(argument))
                {
                    yield return leaf;
                }
            }

            yield return type.GetGenericTypeDefinition();
            yield break;
        }

        yield return type;
    }

    // "<Latest>k__BackingField" is the property Latest.
    private static string Clean(string name) =>
        name.StartsWith('<') && name.IndexOf('>') is var close and > 1 ? name[1..close] : name;
}

/// <summary>A way off the phone, or a log call, found in a source file.</summary>
/// <param name="Kind">What it is: "intent extra", "share intent", "broadcast", "notification", "clipboard" or "log".</param>
/// <param name="Index">Where it starts in the source.</param>
/// <param name="Outward">True for the ways off the phone; false for a log call, which is judged by its arguments only.</param>
/// <param name="Arguments">For a call, the text between its parentheses (literals blanked); empty otherwise.</param>
internal sealed record Site(string Kind, int Index, bool Outward, string Arguments);

/// <summary>
/// Finds the ways off the phone in a source file, and what is passed through them. Works on the text
/// with comments and the inside of string literals blanked (so a message that says "weight", or a
/// comment that names a Body type, is not a finding) and with the code inside interpolation holes kept.
/// It is a lexer and a few regexes, not a compiler, and says so in the class summary of the test.
/// </summary>
internal static class HeadScan
{
    private sealed record Channel(string Kind, Regex Pattern, bool Outward, bool HasArguments);

    private static readonly Channel[] Channels =
    [
        new("intent extra", new Regex(@"\bPut\w*Extras?\s*\(", RegexOptions.CultureInvariant), true, true),
        new("broadcast", new Regex(@"\bSend(?:Ordered|Sticky|StickyOrdered)?Broadcast(?:AsUser)?\s*\(", RegexOptions.CultureInvariant), true, true),
        new("share intent", new Regex(@"\bIntent\.ActionSend\w*|\bACTION_SEND\w*|\bShareCompat\b|\bCreateChooser\s*\(", RegexOptions.CultureInvariant), true, false),
        new("notification", new Regex(
            @"\bNotificationCompat\b|\bNotificationManager(?:Compat)?\b|\bNotification\.Builder\b|\bSetContent(?:Text|Title|Info)\s*\(|\bSetSubText\s*\(|\bBigTextStyle\b",
            RegexOptions.CultureInvariant), true, false),
        new("clipboard", new Regex(@"\bClipData\b|\bSetPrimaryClip\s*\(|\bClipboardManager\b", RegexOptions.CultureInvariant), true, false),
        new("log", new Regex(
            @"\bLog\.(?:Verbose|Debug|Info|Warn|Error|Wtf|V|D|I|W|E)\s*\(|\bConsole\.(?:Write|WriteLine)\s*\(|\b(?:Debug|Trace)\.(?:Write|WriteLine|WriteIf|WriteLineIf|Print)\s*\("
            + @"|\.Outcome\s*\(|\bnew\s+DiagnosticEntry\s*\(|\.Log(?:Information|Warning|Error|Debug|Trace|Critical)\s*\(",
            RegexOptions.CultureInvariant), false, true),
    ];

    private static readonly Regex Assignment = new(
        @"(?<![\w.])(?<name>[A-Za-z_]\w*)\s*=(?![=>])\s*(?<expr>[^;{}]*);", RegexOptions.CultureInvariant);

    private static readonly Regex ForEach = new(
        @"\bforeach\s*\(\s*(?:var|[\w<>\[\]\.,\?]+)\s+(?<name>[A-Za-z_]\w*)\s+in\s+(?<expr>[^)]*)\)", RegexOptions.CultureInvariant);

    // A count, a length, an emptiness or an any-ness is a shape; Diagnostics prints counts.
    private const string ShapeOnly = @"(?!\s*\.\s*(?:Count|Length|IsEmpty|Any)\b)";

    public static IReadOnlyList<Site> Sites(string source) => SitesIn(Sanitise(source));

    public static IReadOnlyList<Finding> Find(
        string path, string source, BodyVocabulary vocabulary, IReadOnlyCollection<string> allowedFiles)
    {
        var code = Sanitise(source);
        var sites = SitesIn(code);
        if (sites.Count == 0)
        {
            return [];
        }

        var handlesBody = vocabulary.ValueTypeToken.IsMatch(code);
        var allowed = allowedFiles.Contains(path);
        var tainted = handlesBody ? TaintedNames(code, vocabulary) : [];
        var findings = new List<Finding>();

        foreach (var site in sites)
        {
            var line = GuardScan.LineOf(source, site.Index);
            if (site.Outward && handlesBody && !allowed)
            {
                findings.Add(new Finding(line, $"{site.Kind} in a file that handles Body"));
            }

            if (site.Arguments.Length > 0 && CarriesBody(site.Arguments, vocabulary, tainted))
            {
                findings.Add(new Finding(line, $"{site.Kind} carrying a Body value"));
            }
        }

        return findings;
    }

    private static List<Site> SitesIn(string code)
    {
        var sites = new List<Site>();
        foreach (var channel in Channels)
        {
            foreach (Match match in channel.Pattern.Matches(code))
            {
                sites.Add(new Site(channel.Kind, match.Index, channel.Outward, channel.HasArguments ? Arguments(code, match.Index + match.Length) : string.Empty));
            }
        }

        return sites.OrderBy(site => site.Index).ToList();
    }

    // The text after an opening parenthesis, up to its match (or the end, if the file ends first).
    private static string Arguments(string code, int afterOpen)
    {
        var depth = 1;
        for (var i = afterOpen; i < code.Length; i++)
        {
            if (code[i] == '(')
            {
                depth++;
            }
            else if (code[i] == ')' && --depth == 0)
            {
                return code[afterOpen..i];
            }
        }

        return code[afterOpen..];
    }

    private static bool CarriesBody(string arguments, BodyVocabulary vocabulary, HashSet<string> tainted)
    {
        if (vocabulary.ValueTypeToken.IsMatch(arguments) || vocabulary.ValueWord.IsMatch(arguments))
        {
            return true;
        }

        return tainted.Count > 0 && Mentions(arguments, tainted);
    }

    private static bool Mentions(string text, HashSet<string> names) =>
        Regex.IsMatch(
            text,
            $@"(?<![\w.])(?:{string.Join("|", names.Select(Regex.Escape))})\b{ShapeOnly}",
            RegexOptions.CultureInvariant);

    // Names the file declares with a Body value type, and names filled from one of those or from a value
    // word. Per file and not per method: coarse, and a false hit is fixed by renaming a local.
    private static HashSet<string> TaintedNames(string code, BodyVocabulary vocabulary)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in vocabulary.DeclaredWithValueType.Matches(code))
        {
            names.Add(match.Groups["name"].Value);
        }

        for (var pass = 0; pass < 4; pass++)
        {
            var before = names.Count;
            foreach (var match in Assignment.Matches(code).Concat(ForEach.Matches(code)))
            {
                var expression = match.Groups["expr"].Value;
                if (vocabulary.ValueTypeToken.IsMatch(expression)
                    || vocabulary.ValueWord.IsMatch(expression)
                    || (names.Count > 0 && Mentions(expression, names)))
                {
                    names.Add(match.Groups["name"].Value);
                }
            }

            if (names.Count == before)
            {
                break;
            }
        }

        return names;
    }

    // ---- the lexer ----

    /// <summary>
    /// The same text with every comment and the inside of every string and character literal replaced by
    /// spaces, newlines kept so positions and line numbers do not move. The code inside the holes of an
    /// interpolated string is kept. Raw interpolated strings with more than one dollar sign are treated as
    /// plain text.
    /// </summary>
    public static string Sanitise(string source)
    {
        var text = source.ToCharArray();
        var index = 0;
        ScanCode(source, text, ref index, insideHole: false);
        return new string(text);
    }

    private static void Blank(char[] text, int index)
    {
        if (text[index] is not ('\n' or '\r'))
        {
            text[index] = ' ';
        }
    }

    private static void ScanCode(string source, char[] text, ref int i, bool insideHole)
    {
        var depth = 0;
        while (i < source.Length)
        {
            var c = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    Blank(text, i++);
                }

                continue;
            }

            if (c == '/' && next == '*')
            {
                Blank(text, i++);
                Blank(text, i++);
                while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                {
                    Blank(text, i++);
                }

                for (var closing = 0; closing < 2 && i < source.Length; closing++)
                {
                    Blank(text, i++);
                }

                continue;
            }

            if (c == '\'')
            {
                i = CharLiteral(source, text, i);
                continue;
            }

            if (c == '"' || (c is '$' or '@' && StartsString(source, i)))
            {
                Literal(source, text, ref i);
                continue;
            }

            if (insideHole)
            {
                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    if (depth == 0)
                    {
                        return;
                    }

                    depth--;
                }
            }

            i++;
        }
    }

    private static bool StartsString(string source, int i)
    {
        while (i < source.Length && source[i] is '$' or '@')
        {
            i++;
        }

        return i < source.Length && source[i] == '"';
    }

    // Returns the index after the literal; a lone quote that is not a literal is stepped over.
    private static int CharLiteral(string source, char[] text, int i)
    {
        if (i + 1 < source.Length && source[i + 1] == '\\')
        {
            var close = i + 3;
            while (close < source.Length && source[close] != '\'' && source[close] != '\n')
            {
                close++;
            }

            if (close < source.Length && source[close] == '\'')
            {
                for (var k = i + 1; k < close; k++)
                {
                    Blank(text, k);
                }

                return close + 1;
            }

            return i + 1;
        }

        if (i + 2 < source.Length && source[i + 2] == '\'' && source[i + 1] != '\'')
        {
            Blank(text, i + 1);
            return i + 3;
        }

        return i + 1;
    }

    private static void Literal(string source, char[] text, ref int i)
    {
        var verbatim = false;
        var interpolated = false;
        var dollars = 0;
        while (source[i] is '$' or '@')
        {
            if (source[i] == '$')
            {
                interpolated = true;
                dollars++;
            }
            else
            {
                verbatim = true;
            }

            i++;
        }

        var quotes = 0;
        while (i + quotes < source.Length && source[i + quotes] == '"')
        {
            quotes++;
        }

        if (quotes >= 3)
        {
            i += quotes;
            while (i < source.Length)
            {
                if (source[i] == '"')
                {
                    var run = 0;
                    while (i + run < source.Length && source[i + run] == '"')
                    {
                        run++;
                    }

                    if (run >= quotes)
                    {
                        i += run;
                        return;
                    }

                    for (var k = 0; k < run; k++)
                    {
                        Blank(text, i + k);
                    }

                    i += run;
                    continue;
                }

                if (interpolated && dollars == 1 && source[i] == '{')
                {
                    i++;
                    ScanCode(source, text, ref i, insideHole: true);
                    if (i < source.Length)
                    {
                        i++;
                    }

                    continue;
                }

                Blank(text, i++);
            }

            return;
        }

        i++;
        while (i < source.Length)
        {
            var ch = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            if (!verbatim && ch == '\\')
            {
                Blank(text, i);
                if (i + 1 < source.Length)
                {
                    Blank(text, i + 1);
                }

                i += 2;
                continue;
            }

            if (ch == '"')
            {
                if (verbatim && next == '"')
                {
                    Blank(text, i);
                    Blank(text, i + 1);
                    i += 2;
                    continue;
                }

                i++;
                return;
            }

            if (interpolated && ch == '{')
            {
                if (next == '{')
                {
                    Blank(text, i);
                    Blank(text, i + 1);
                    i += 2;
                    continue;
                }

                i++;
                ScanCode(source, text, ref i, insideHole: true);
                if (i < source.Length)
                {
                    i++;
                }

                continue;
            }

            if (interpolated && ch == '}' && next == '}')
            {
                Blank(text, i);
                Blank(text, i + 1);
                i += 2;
                continue;
            }

            Blank(text, i++);
        }
    }
}

/// <summary>Finds which Infrastructure files name a Body type that carries a value.</summary>
internal static class InfrastructureBody
{
    // Where the network, Drive and the voice's model calls live. A file that can reach any of them is a
    // way off the phone.
    private static readonly Regex OutwardIo = new(
        @"\b(?:IDriveTextFile|DriveTextFile|HttpClient|HttpRequestMessage|HttpMessageHandler|IHttpClientFactory|WebClient|WebRequest|Socket|TcpClient|UdpClient|SmtpClient"
        + @"|IVoiceClient|VoiceClient|ISeatComposer|SeatComposer|ISecretsVaultStore|DriveSecretsVaultStore|DrivePersonalVaultStore)\b",
        RegexOptions.CultureInvariant);

    /// <summary>Every .cs file under <paramref name="root"/> (relative path) that names a value-carrying Body type.</summary>
    public static IReadOnlyList<string> Referencing(string root) =>
        GuardScan.Run(root, (path, text) =>
                path.EndsWith(".cs", StringComparison.Ordinal) && BodyVocabulary.Current.ValueTypeToken.IsMatch(HeadScan.Sanitise(text))
                    ? [new Finding(1, "names a Body type")]
                    : [])
            .Hits.Select(hit => hit.File).Distinct().Order().ToList();

    /// <summary>The files that name Body and are not on the allowlist.</summary>
    public static IReadOnlyList<string> Violations(string root, IEnumerable<AllowedPath> allowlist)
    {
        var entries = allowlist.ToList();
        return Referencing(root).Where(path => !entries.Any(entry => entry.Allows(path))).ToList();
    }

    /// <summary>
    /// The files that are allowed to name Body only because they keep it on the install (the local store,
    /// a fact builder) and yet can reach the network, Drive or the voice's model.
    /// </summary>
    public static IReadOnlyList<string> LocalFilesWithAWayOut(string root, IEnumerable<AllowedPath> allowlist)
    {
        var local = allowlist.Where(entry => !entry.MayLeave).ToList();
        var offenders = new List<string>();
        foreach (var path in Referencing(root).Where(path => local.Any(entry => entry.Allows(path))))
        {
            var text = HeadScan.Sanitise(File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))));
            if (OutwardIo.IsMatch(text))
            {
                offenders.Add(path);
            }
        }

        return offenders;
    }
}
