using System.Globalization;
using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Storyworthtelling.Household;
using static Constitution.Tests.HouseholdFile.LeaseRig;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// How the lease is kept in the household file: two shapes of record under the one frozen kind, each
/// written by exactly one install (a heartbeat) or keyed to exactly one week (a claim), through the typed
/// plumbing. The field names and role words are typed out here on purpose: they are wire names.
/// </summary>
public class LeaseRecordsTests
{
    private static readonly DateTimeOffset Now = Edt(10, 27, 9);

    [Fact]
    public void BothShapesLiveUnderTheFrozenKind()
    {
        Assert.Equal("planning-lease", LeaseKeys.HeartbeatKey(Sam).Kind);
        Assert.Equal("planning-lease", LeaseKeys.ClaimKey(WeekOf(Now)).Kind);
        Assert.Equal(ConstitutionKinds.PlanningLease, LeaseKeys.ClaimKey(1).Kind);
    }

    [Fact]
    public void AHeartbeatIsKeyedByItsInstallWithoutCase()
    {
        Assert.Equal(LeaseKeys.HeartbeatKey("Install-Sam"), LeaseKeys.HeartbeatKey("install-sam"));
        Assert.NotEqual(LeaseKeys.HeartbeatKey(Sam), LeaseKeys.HeartbeatKey(Lee));
        Assert.StartsWith("beat:", LeaseKeys.HeartbeatKey(Sam).Id);
    }

    [Fact]
    public void PunctuationNeverMergesTwoInstallKeys()
    {
        Assert.NotEqual(LeaseKeys.HeartbeatKey("a:b"), LeaseKeys.HeartbeatKey("a/b"));
        Assert.NotEqual(LeaseKeys.HeartbeatKey("a:b"), LeaseKeys.HeartbeatKey("a%3Ab"));
    }

    [Fact]
    public void AClaimIsKeyedByItsWeek()
    {
        Assert.Equal("week:" + WeekOf(Now).ToString(CultureInfo.InvariantCulture), LeaseKeys.ClaimKey(WeekOf(Now)).Id);
        Assert.NotEqual(LeaseKeys.ClaimKey(WeekOf(Now)), LeaseKeys.ClaimKey(WeekOf(Now) + 1));
    }

    [Fact]
    public void ABeatIsWrittenInTheFrozenFields()
    {
        var clock = new HybridLogicalClock();
        var document = LeaseWriter.Beat(HouseholdDocument.Empty, clock, Sam, RosterRole.Owner, Now, Lease);

        var record = Assert.IsType<HouseholdRecord>(document.Live(LeaseKeys.HeartbeatKey(Sam)));
        Assert.Equal(["beat", "install", "lease", "role"], record.Fields.Keys.ToArray());
        Assert.Equal(Sam, record.Fields["install"]);
        Assert.Equal("owner", record.Fields["role"]);
        Assert.Equal(Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), record.Fields["beat"]);
        Assert.Equal(((long)Lease.TotalMilliseconds).ToString(CultureInfo.InvariantCulture), record.Fields["lease"]);
        Assert.Equal(Sam, record.Writer);
    }

    [Fact]
    public void ARoleIsStoredAsAWord()
    {
        var partner = LeaseWriter.Beat(HouseholdDocument.Empty, new HybridLogicalClock(), Lee, RosterRole.Partner, Now, Lease);
        Assert.Equal("partner", partner.Live(LeaseKeys.HeartbeatKey(Lee))!.Fields["role"]);
    }

    [Fact]
    public void ATakeWritesABeatAndAClaimInWireFields()
    {
        var document = LeaseWriter.Take(HouseholdDocument.Empty, new HybridLogicalClock(), Sam, RosterRole.Owner, Now, Zone, Lease);

        var week = WeekOf(Now);
        var claim = Assert.IsType<HouseholdRecord>(document.Live(LeaseKeys.ClaimKey(week)));
        Assert.Equal(["holder", "lease", "role", "taken", "week"], claim.Fields.Keys.ToArray());
        Assert.Equal(Sam, claim.Fields["holder"]);
        Assert.Equal(week.ToString(CultureInfo.InvariantCulture), claim.Fields["week"]);
        Assert.Equal(Now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), claim.Fields["taken"]);
        Assert.NotNull(document.Live(LeaseKeys.HeartbeatKey(Sam)));
    }

    [Fact]
    public void WhatIsWrittenReadsBack()
    {
        var clock = new HybridLogicalClock();
        var document = LeaseWriter.Take(HouseholdDocument.Empty, clock, Lee, RosterRole.Partner, Now, Zone, Lease);

        var view = LeaseView.Read(document);
        Assert.Empty(view.UnreadableRecords);
        var beat = view.Heartbeats[Lee];
        Assert.Equal(new LeaseHeartbeat(Lee, RosterRole.Partner, Now, Lease), beat);
        var claim = view.Claims[WeekOf(Now)];
        Assert.Equal(new LeaseClaim(Lee, RosterRole.Partner, WeekOf(Now), Now, Lease), claim);
    }

    [Fact]
    public void ABeatEditsItsRecordInPlace()
    {
        var clock = new HybridLogicalClock();
        var first = LeaseWriter.Beat(HouseholdDocument.Empty, clock, Sam, RosterRole.Owner, Now, Lease);
        var second = LeaseWriter.Beat(first, clock, Sam, RosterRole.Owner, Now.AddHours(6), Lease);

        Assert.Single(second.Records);
        Assert.True(second.Live(LeaseKeys.HeartbeatKey(Sam))!.Stamp > first.Live(LeaseKeys.HeartbeatKey(Sam))!.Stamp);
        Assert.Equal(Now.AddHours(6), LeaseView.Read(second).Heartbeats[Sam].Beat);
    }

    [Fact]
    public void AFieldANewerWriterAddedSurvivesABeat()
    {
        var stamp = new HlcTimestamp(Now.AddDays(-1).ToUnixTimeMilliseconds(), 0);
        var withExtra = BeatRecord(Sam, "owner", Now.AddDays(-1), stamp).Edit(
            new Dictionary<string, string> { ["battery"] = "low" }, 2, new HlcTimestamp(stamp.WallMillis, 1), Sam);
        var document = HouseholdDocument.Empty.With(withExtra);

        var beaten = LeaseWriter.Beat(document, new HybridLogicalClock(), Sam, RosterRole.Owner, Now, Lease);

        Assert.Equal("low", beaten.Live(LeaseKeys.HeartbeatKey(Sam))!.Fields["battery"]);
        Assert.Equal(2, beaten.Live(LeaseKeys.HeartbeatKey(Sam))!.SchemaVersion);
    }

    [Fact]
    public void AWriteOutranksEverythingItRead()
    {
        // A phone whose own clock is a day slow still writes a beat that wins over every record it read.
        var ahead = HouseholdDocument.Empty.With(BeatRecord(Lee, "partner", Now.AddDays(1), new HlcTimestamp(Now.AddDays(1).ToUnixTimeMilliseconds(), 0)));
        var slow = LeaseWriter.Beat(ahead, new HybridLogicalClock(), Sam, RosterRole.Owner, Now, Lease);

        Assert.True(slow.Live(LeaseKeys.HeartbeatKey(Sam))!.Stamp > ahead.MaxStamp);
    }

    [Fact]
    public void AReleaseTombstonesOnlyTheCallersOwnClaim()
    {
        var week = WeekOf(Now);
        var samsClaim = LeaseWriter.Take(HouseholdDocument.Empty, new HybridLogicalClock(), Sam, RosterRole.Owner, Now, Zone, Lease);

        var byLee = LeaseWriter.Release(samsClaim, new HybridLogicalClock(), Lee, week, Now.AddHours(1));
        Assert.NotNull(byLee.Live(LeaseKeys.ClaimKey(week)));

        var bySam = LeaseWriter.Release(samsClaim, new HybridLogicalClock(), Sam, week, Now.AddHours(1));
        Assert.Null(bySam.Live(LeaseKeys.ClaimKey(week)));
        Assert.True(bySam.Find(LeaseKeys.ClaimKey(week))!.IsTombstone);
    }

    [Fact]
    public void ReleasingNothingChangesNothing()
    {
        var document = LeaseWriter.Beat(HouseholdDocument.Empty, new HybridLogicalClock(), Sam, RosterRole.Owner, Now, Lease);
        Assert.Same(document, LeaseWriter.Release(document, new HybridLogicalClock(), Sam, WeekOf(Now), Now));
    }

    [Fact]
    public void ABeatIsDueWhenNoneOrAPeriodOld()
    {
        Assert.True(LeaseWriter.NeedsHeartbeat(HouseholdDocument.Empty, Sam, Now));

        var beaten = LeaseWriter.Beat(HouseholdDocument.Empty, new HybridLogicalClock(), Sam, RosterRole.Owner, Now, Lease);
        Assert.False(LeaseWriter.NeedsHeartbeat(beaten, Sam, Now));
        Assert.False(LeaseWriter.NeedsHeartbeat(beaten, Sam, Now + LeaseTerms.HeartbeatPeriod - TimeSpan.FromMinutes(1)));
        Assert.True(LeaseWriter.NeedsHeartbeat(beaten, Sam, Now + LeaseTerms.HeartbeatPeriod));
        Assert.True(LeaseWriter.NeedsHeartbeat(beaten, Lee, Now));
    }

    [Fact]
    public void ApplyWritesOnlyWhatWasDecided()
    {
        var clock = new HybridLogicalClock();
        var empty = HouseholdDocument.Empty;
        var week = WeekOf(Now);

        var taken = LeaseWriter.Apply(empty, LeaseDecision.Take(week, Sam), clock, Sam, RosterRole.Owner, Now, Zone, Lease);
        Assert.NotNull(taken.Live(LeaseKeys.ClaimKey(week)));

        Assert.Same(taken, LeaseWriter.Apply(taken, LeaseDecision.Plan(week, Sam), clock, Sam, RosterRole.Owner, Now, Zone, Lease));
        Assert.Same(taken, LeaseWriter.Apply(taken, LeaseDecision.DoNotPlan(LeaseReason.HeldByAnother, week), clock, Sam, RosterRole.Owner, Now, Zone, Lease));

        var released = LeaseWriter.Apply(taken, LeaseDecision.Release(week, Sam), clock, Sam, RosterRole.Owner, Now.AddDays(8), Zone, Lease);
        Assert.Null(released.Live(LeaseKeys.ClaimKey(week)));
    }

    [Fact]
    public void ATakeUsesTheWeekOfNowOnTheHouseholdClock()
    {
        // 03:30 UTC on Monday 2 November is still Sunday evening at home: the claim is for the week that is ending.
        var sundayAtHome = new DateTimeOffset(2026, 11, 2, 3, 30, 0, TimeSpan.Zero);
        var document = LeaseWriter.Take(HouseholdDocument.Empty, new HybridLogicalClock(), Sam, RosterRole.Owner, sundayAtHome, Zone, Lease);

        Assert.NotNull(document.Live(LeaseKeys.ClaimKey(LeaseWeek.IndexOf(Edt(10, 27, 9), Zone))));
    }

    [Fact]
    public void ASurvivingFileRoundTripsThroughTheCodec()
    {
        var document = LeaseWriter.Take(HouseholdDocument.Empty, new HybridLogicalClock(), Sam, RosterRole.Owner, Now, Zone, Lease);
        var back = HouseholdDocumentCodec.Deserialize(HouseholdDocumentCodec.Serialize(document));

        Assert.True(document.ContentEquals(back));
        Assert.Equal(WeekOf(Now), LeaseView.Read(back).Claims.Keys.Single());
    }

    [Fact]
    public void NoLeaseSourceReadsTheWallClock()
    {
        var directory = Path.Combine(RepoRoot(), "Constitution", "Constitution.Domain", "Household", "Lease");
        var files = Directory.GetFiles(directory, "*.cs");
        Assert.NotEmpty(files);
        string[] banned = ["DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "Stopwatch", "Environment.TickCount", "TimeProvider"];
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in banned)
            {
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{Path.GetFileName(file)} reads the clock with {word}; time is passed in.");
            }
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Constitution", "Constitution.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
