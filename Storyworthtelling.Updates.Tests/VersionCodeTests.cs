namespace Storyworthtelling.Updates.Tests;

/// <summary>
/// The suite's one versionCode formula, and the guard that every app's band clears what
/// that app has already shipped.
///
/// This exists because the old arrangement failed twice in the same way. versionCode was
/// the workflow's run_number, so renaming build-focusphone.yml to build-flyleaf.yml reset
/// the counter and the next build looked <i>older</i> than what was installed; the fix was a
/// 100000 floor. Splitting Post and Plate into their own workflows reset it again, and the
/// fix was a 200000 floor. A floor is a ratchet — it needs a human to notice and bump it —
/// so the formula stops depending on a counter at all.
/// </summary>
public class VersionCodeTests
{
    private static readonly DateTime Epoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Band1AtTheEpochIsTheBottomOfItsBand()
    {
        Assert.Equal(100_000_000, VersionCode.For(1, Epoch));
    }

    [Fact]
    public void EachBandStartsAHundredMillionHigher()
    {
        Assert.Equal(200_000_000, VersionCode.For(2, Epoch));
        Assert.Equal(700_000_000, VersionCode.For(7, Epoch));
    }

    [Fact]
    public void OneMinuteLaterIsOneHigher()
    {
        Assert.Equal(VersionCode.For(1, Epoch) + 1, VersionCode.For(1, Epoch.AddMinutes(1)));
    }

    [Fact]
    public void SecondsWithinAMinuteDoNotMoveIt()
    {
        // Deliberate: the unit is the minute, so two builds finishing in the same minute tie.
        // The later one is then not installable over the earlier, which the next build fixes.
        Assert.Equal(VersionCode.For(1, Epoch), VersionCode.For(1, Epoch.AddSeconds(59)));
    }

    [Fact]
    public void LaterIsAlwaysLarger()
    {
        var moments = new[]
        {
            Epoch.AddDays(1), Epoch.AddMonths(6), Epoch.AddYears(3), Epoch.AddYears(6).AddDays(249),
        };

        for (var i = 1; i < moments.Length; i++)
        {
            Assert.True(VersionCode.For(1, moments[i]) > VersionCode.For(1, moments[i - 1]),
                $"{moments[i]:O} did not produce a larger code than {moments[i - 1]:O}");
        }
    }

    [Fact]
    public void AWorkflowRenameCannotAffectIt()
    {
        // The whole point: nothing in the formula is a counter, so there is nothing to reset.
        var moment = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(VersionCode.For(1, moment), VersionCode.For(1, moment));
    }

    [Fact]
    public void LocalTimeIsConvertedRatherThanRead()
    {
        var utc = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        Assert.Equal(VersionCode.For(1, utc), VersionCode.For(1, local));
    }

    [Fact]
    public void AnUnspecifiedKindIsRefusedRatherThanGuessed()
    {
        var ambiguous = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => VersionCode.For(1, ambiguous));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public void ABandOutsideTheUsableRangeIsRefused(int band)
    {
        // Band 21 at any real date exceeds int.MaxValue, which Android silently mangles.
        Assert.Throws<ArgumentOutOfRangeException>(() => VersionCode.For(band, Epoch));
    }

    [Fact]
    public void ATimeBeforeTheEpochIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VersionCode.For(1, Epoch.AddMinutes(-1)));
    }

    [Fact]
    public void ATimeThatWouldOverflowItsBandIsRefused()
    {
        // 100,000,000 minutes is ~190 years; past that a code would spill into the next band
        // and start colliding with another app's numbering.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VersionCode.For(1, Epoch.AddMinutes(VersionCode.BandSize)));
    }

    [Fact]
    public void EveryBandStaysWithinInt32ForTheNextCentury()
    {
        var farFuture = Epoch.AddYears(100);

        for (var band = VersionCode.MinBand; band <= VersionCode.MaxBand; band++)
        {
            var code = VersionCode.For(band, farFuture);
            Assert.InRange(code, 1, int.MaxValue);
        }
    }

    // ---- The guard: the CI formula and this one must not drift apart ----

    [Fact]
    public void TheWorkflowActionComputesTheSameFormula()
    {
        // The formula exists twice: here, and in bash in the composite action every workflow
        // calls. Two implementations of one rule is exactly how drift happens, so the constants
        // are compared directly. They were verified to agree on a live instant when written.
        var action = File.ReadAllText(RepoFile(".github/actions/version-code/action.yml"));

        Assert.Contains($"BAND_SIZE={VersionCode.BandSize}", action);
        Assert.Contains($"MIN_BAND={VersionCode.MinBand}", action);
        Assert.Contains($"MAX_BAND={VersionCode.MaxBand}", action);
        Assert.Contains($"date -u -d '{VersionCode.Epoch:yyyy-MM-dd} 00:00:00' +%s", action);
        Assert.Contains("BAND * BAND_SIZE + MINUTES", action);
    }

    [Fact]
    public void EveryAppWorkflowUsesTheSharedAction()
    {
        // A workflow that computes its own code is a workflow that can drift back to a counter.
        var workflows = Directory.GetFiles(RepoFile(".github/workflows"), "build-*.yml")
            .Where(f => !Path.GetFileName(f).StartsWith("build-shared-libs"))
            .ToList();

        Assert.NotEmpty(workflows);
        foreach (var workflow in workflows)
        {
            var text = File.ReadAllText(workflow);
            Assert.True(text.Contains("./.github/actions/version-code"),
                $"{Path.GetFileName(workflow)} does not use the shared version-code action.");
            // Only the versionCode matters. ApplicationDisplayVersion may keep run_number:
            // it is a display string, never compared, so a reset there is harmless.
            Assert.False(text.Contains("ApplicationVersion=${{ github.run_number")
                || text.Contains("+ RUN_NUMBER"),
                $"{Path.GetFileName(workflow)} still derives its versionCode from run_number, "
                + "which a workflow rename resets.");
        }
    }

    private static string RepoFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, relative);
    }

    // ---- The guard: a band must clear what its app has already shipped ----

    [Fact]
    public void EveryAppsBandClearsWhatItHasAlreadyShipped()
    {
        // An app whose new code is lower than its installed one is simply rejected by the
        // channel — silently, and forever, because the code never catches up on its own.
        // This is the check nobody ran before the two floor bumps.
        var firstUnifiedBuild = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        foreach (var app in SuiteVersionBands.All)
        {
            var code = VersionCode.For(app.Band, firstUnifiedBuild);
            Assert.True(code > app.LastShippedBeforeUnification,
                $"{app.Package} would ship {code}, which is not above the {app.LastShippedBeforeUnification} "
                + "it has already shipped — the update channel would reject it.");
        }
    }

    [Fact]
    public void NoTwoAppsShareABandUnlessTheyShareAWorkflow()
    {
        // Sharing a band is harmless — versionCodes are per package — but it is worth being
        // deliberate about, since a shared band means two apps' codes interleave in the log.
        var shared = SuiteVersionBands.All
            .GroupBy(a => a.Band)
            .Where(g => g.Count() > 1)
            .Select(g => $"band {g.Key}: {string.Join(", ", g.Select(a => a.Package))}")
            .ToList();

        Assert.Equal(["band 1: com.storyworthtelling.focusphone.launcher, "
            + "com.storyworthtelling.focusphone.audio, com.storyworthtelling.focusphone.communication, "
            + "com.storyworthtelling.focusphone.camera, com.storyworthtelling.ledger"], shared);
    }

    /// <summary>
    /// Folio has to clear Canonizer's timestamp codes, for exactly the reason Foreword has to clear
    /// DailyLoader's — and this guard exists because the first attempt got it wrong.
    ///
    /// <para>Canonizer's csproj computed <c>yearDigit + MMddHHmm</c> from August 2026, its own comment
    /// saying "(matches DailyLoader)". Band 1 was chosen from a <c>LastShippedBeforeUnification</c> of
    /// <b>169</b> — the bare <c>run_number</c> era, recorded without noticing the csproj had been
    /// computing nine-digit codes since. So the suite's own "check nobody ran" ran, and passed, on a
    /// number that was wrong; the phone kept a build stamped 2026.08.11.1608, versionCode 608111608,
    /// and every Folio build since has been silently refused.</para>
    /// </summary>
    [Fact]
    public void FolioSitsAboveCanonizersTimestampCodes()
    {
        var folio = SuiteVersionBands.All.Single(a => a.Package == "com.storyworthtelling.storyboarder");

        // The build actually found on the owner's device: 6 + 08111608.
        const int onTheDevice = 608_111_608;

        Assert.True(folio.LastShippedBeforeUnification >= onTheDevice,
            $"The recorded ceiling {folio.LastShippedBeforeUnification} is below the {onTheDevice} known "
            + "to be installed, so the band was chosen against a number that is not true.");
        Assert.True(VersionCode.For(folio.Band, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))
            > onTheDevice,
            "Folio would ship below what is on the phone, and the channel refuses it silently.");
    }

    [Fact]
    public void ForewordSitsAboveDailyLoadersTimestampCodes()
    {
        // Foreword keeps com.storyworthtelling.dailyloader, so it has to clear the old
        // csproj scheme (yearDigit + MMddHHmm, ~609,000,000 in 2026) to upgrade in place
        // rather than needing an uninstall. Its display version is 2.0.x regardless — the
        // band is a numbering space, not the version the owner sees.
        var foreword = SuiteVersionBands.All.Single(a => a.Package == "com.storyworthtelling.dailyloader");

        Assert.Equal(7, foreword.Band);
        Assert.True(VersionCode.For(foreword.Band, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))
            > 699_999_999);
    }
}

/// <summary>
/// A project that names this library in its code must reference it.
///
/// <para>Written after the obvious happened: three MAUI heads were given code calling
/// <see cref="BuildLine"/> without a ProjectReference, and CI caught it as
/// <c>CS0234: the type or namespace name 'Updates' does not exist</c>. The compiler is the real
/// guard — but the MAUI heads cannot be compiled without the maui-android workload, which this
/// sandbox does not have, so nothing local said a word. This test does, by reading the projects
/// themselves.</para>
/// </summary>
public class ProjectReferenceTests
{
    [Fact]
    public void EveryProjectThatNamesTheUpdateLibraryReferencesIt()
    {
        var root = RepoRoot();
        var missing = new List<string>();

        foreach (var project in Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(project)!;
            if (directory.Contains($"{Path.DirectorySeparatorChar}Storyworthtelling.Updates", StringComparison.Ordinal))
            {
                continue;
            }

            var sources = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

            // Comments are stripped first. A doc comment that *mentions* the library — "kept identical
            // by a parity guard", "this rule was extracted from Storyworthtelling.Updates" — is prose,
            // not a using directive, and a guard that cannot tell the two apart reports the file that
            // explains the rule as the file that breaks it.
            var namesIt = sources.Any(f => StripComments(File.ReadAllText(f))
                .Contains("Storyworthtelling.Updates", StringComparison.Ordinal));
            if (!namesIt)
            {
                continue;
            }

            var references = File.ReadAllText(project);
            if (!references.Contains("Storyworthtelling.Updates.csproj", StringComparison.Ordinal))
            {
                missing.Add(Path.GetRelativePath(root, project));
            }
        }

        Assert.True(missing.Count == 0,
            "These projects use Storyworthtelling.Updates without referencing it, which fails as CS0234 "
            + $"in CI and cannot be caught locally for a MAUI head:\n{string.Join("\n", missing)}");
    }

    private static string StripComments(string source) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(
                source, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline),
            @"//.*?$", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
