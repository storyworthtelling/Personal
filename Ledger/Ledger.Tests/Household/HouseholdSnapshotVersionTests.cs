using System.Reflection;
using System.Text.Json;
using Index.Domain.External;
using Index.Domain.Household;
using Index.Domain.Model;
using Index.Infrastructure.Household;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Moq;
using Storyworthtelling.Voice.Secrets;
using Xunit;
using Xunit.Abstractions;

namespace Index.Tests.Household;

/// <summary>
/// R-G28 (remediation 3.6, owner decision R7): the household file gained a version and two series — Snowball's
/// weekly History and Savings' monthly Growth — so her install can draw the charts from the same shapes the
/// owner's does. What these hold, in order of how badly it hurts to lose it:
///
/// <para><b>The old file still reads.</b> The owner's phone may have published a version-less file months ago;
/// her phone is one install behind and must read it, and say the old sentence — not break.
/// <see cref="HouseholdV1Fixture"/> is that file captured from the ORIGINAL serialiser before the codec was
/// touched.</para>
///
/// <para><b>A newer file is refused, not misread, and never overwritten.</b> The silent failure is on the
/// other phone: a publisher that downgraded a file it could not understand would erase what a newer install
/// wrote, and nothing on either phone would say so.</para>
/// </summary>
public class HouseholdSnapshotVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 0, 0, TimeSpan.Zero);
    private const string Passphrase = "correct horse battery staple";
    private const string OwnerEmail = "owner@example.com";
    private const string HerEmail = "jordan@example.com";

    private readonly ITestOutputHelper output;

    public HouseholdSnapshotVersionTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static InMemoryDriveTextFile FileWith(string plaintext)
    {
        var file = new InMemoryDriveTextFile();
        file.WriteAsync(VaultEnvelope.Write(plaintext, Passphrase), default).GetAwaiter().GetResult();
        return file;
    }

    private static (HouseholdSnapshotPublisher Publisher, HouseholdSnapshotReader Reader, SnowballService Snowball,
        SavingsService Savings) Create(TestDatabase db, InMemoryDriveTextFile file)
    {
        var clock = new FakeClock(Now);
        var snowball = new SnowballService(db, clock, HistoryImportHarness.NewImport());
        var savings = new SavingsService(db, clock);
        var publisher = new HouseholdSnapshotPublisher(
            new AccountService(db, clock), snowball,
            new BudgetService(db, clock, new Mock<IPlaidClient>().Object), savings, file, clock);
        return (publisher, new HouseholdSnapshotReader(file), snowball, savings);
    }

    // The sheet's ten loans (here the invented stand-in), matched by exact name by the import
    // (HistoryImportTests). Each needs an APR and a weekly payment to count as a Snowball loan at all.
    private static readonly IReadOnlyList<string> FixtureLoanNames = SyntheticSnowballHistory.LoanNames;

    private static async Task SeedTheSheetsLoansAsync(TestDatabase db)
    {
        await using var context = db.CreateDbContext();
        foreach (var name in FixtureLoanNames)
        {
            context.Institutions.Add(new Institution
            {
                Name = name, AccessToken = "access-token-for-" + name, CreatedAtUtc = Now.UtcDateTime,
                LastRefreshedUtc = Now, RefreshStatus = WorkStatus.Completed,
                Accounts =
                {
                    new Account
                    {
                        Name = name, Kind = AccountKind.Loan, Mask = "9921",
                        Subtype = name == SyntheticSnowballHistory.MortgageName ? "mortgage" : "student",
                        CurrentBalance = name == SyntheticSnowballHistory.PaidOffName ? 0m : 1_000m,
                        Apr = 0.0499m, NextPaymentAmount = 100m, IsoCurrencyCode = "USD",
                    },
                },
            });
        }
        await context.SaveChangesAsync();
    }

    private static async Task SeedSavingsAsync(TestDatabase db, int months)
    {
        await using var context = db.CreateDbContext();
        var institution = new Institution
        {
            Name = "Redwood Bank", AccessToken = "tok", CreatedAtUtc = Now.UtcDateTime, LastRefreshedUtc = Now,
            RefreshStatus = WorkStatus.Completed,
        };
        var savings = new Account
        {
            Name = "Savings", Kind = AccountKind.Depository, Subtype = "savings", Mask = "7302",
            CurrentBalance = 11_870.64m, IsoCurrencyCode = "USD",
        };
        var noGoal = new Account
        {
            Name = "Share savings", Kind = AccountKind.Depository, Subtype = "savings", Mask = "1111",
            CurrentBalance = 1_350m, IsoCurrencyCode = "USD",
        };
        institution.Accounts.Add(savings);
        institution.Accounts.Add(noGoal);
        context.Institutions.Add(institution);
        context.SavingsGoals.Add(new SavingsGoal { AccountId = savings.Id, TargetAmount = 15_000m });

        var first = new DateOnly(2026, 9, 1).AddMonths(-(months - 1));
        for (var i = 0; i < months; i++)
        {
            var month = first.AddMonths(i);
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = savings.Id, MonthOf = month, Balance = 4_730.07m + (i * 281.5m) });
            context.SavingsSnapshots.Add(new SavingsSnapshot { AccountId = noGoal.Id, MonthOf = month, Balance = 2_000m + i });
        }
        await context.SaveChangesAsync();
    }

    private static string Plaintext(InMemoryDriveTextFile file)
    {
        var (status, plaintext) = VaultEnvelope.Read(file.Content, Passphrase);
        Assert.Equal(SecretsVaultStatus.Loaded, status);
        return plaintext!;
    }

    // ---- the version-less file: still reads, and says the old sentence ----

    [Fact]
    public async Task AVersionOneFileAsTheOriginalSerialiserWroteItStillReads()
    {
        var reader = new HouseholdSnapshotReader(FileWith(HouseholdV1Fixture.Json));

        var result = await reader.ReadAsync(Passphrase, default);

        Assert.Equal(HouseholdSnapshotStatus.Loaded, result.Status);
        var snapshot = result.Snapshot!;
        Assert.Equal(1, snapshot.Version); // absent means 1
        Assert.Equal(OwnerEmail, snapshot.PublishedByAccountEmail);
        Assert.Equal(2, snapshot.Institutions.Count);
        Assert.Equal(3150.25m, snapshot.Institutions[0].Accounts[0].CurrentBalance);
        Assert.True(snapshot.Institutions[1].IsStale);
        Assert.Equal(new DateOnly(2031, 9, 26), snapshot.Snowball.DebtFreeDate);
        Assert.Equal(280842.11m, snapshot.Snowball.TotalOwed);
        Assert.Equal(187.75m, snapshot.Savings.Accounts[0].MonthlyPace);
        Assert.Equal(185.20m, snapshot.Budget.Categories[0].ActualAmount);
    }

    [Fact]
    public async Task TheBudgetFieldsAddedLaterAreOptionalOnTheVersionTwoShape_SoAnOlderFileReadsWithoutThem()
    {
        // Remediation 3.2 item 6 added UncategorizedTotal/UncategorizedCount (summary) and LastMonthAmount
        // (per category) WITHOUT a version bump: they are additive and optional, and a file written before
        // they existed — this fixture is exactly that — must read as "none", not fail or be refused. A
        // future bump to 3 would lock out every install still on the older build, which is the cost an
        // optional field avoids.
        var reader = new HouseholdSnapshotReader(FileWith(HouseholdV1Fixture.Json));

        var budget = (await reader.ReadAsync(Passphrase, default)).Snapshot!.Budget;

        Assert.Equal(0m, budget.UncategorizedTotal);
        Assert.Equal(0, budget.UncategorizedCount);
        Assert.All(budget.Categories, c => Assert.Null(c.LastMonthAmount));
        Assert.Equal(2, HouseholdSnapshot.CurrentVersion);
    }

    [Fact]
    public async Task AVersionOneFileHasNoSeriesSoHerPagesSayTheOldSentenceAndOnlyThen()
    {
        var reader = new HouseholdSnapshotReader(FileWith(HouseholdV1Fixture.Json));

        var snapshot = (await reader.ReadAsync(Passphrase, default)).Snapshot!;

        Assert.Null(snapshot.SnowballHistory);
        Assert.Null(snapshot.SavingsGrowth);
        Assert.Equal("Growth isn\u2019t part of this summary yet.", HouseholdChartCopy.GrowthNotInSummary);
        Assert.Equal("History isn\u2019t part of this summary yet.", HouseholdChartCopy.SnowballNotInSummary);
    }

    [Fact]
    public void TheFixtureIsTheVersionlessShapeItClaimsToBe()
    {
        using var document = JsonDocument.Parse(HouseholdV1Fixture.Json);
        var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(
            ["PublishedAtUtc", "PublishedByAccountEmail", "Institutions", "Snowball", "Budget", "Savings"], keys);
    }

    // ---- version 2: the shape, and a round trip to the cent and the day ----

    [Fact]
    public async Task AVersionTwoFileRoundTripsDecimalsToTheCentAndDatesToTheDay()
    {
        var weeks = new List<DateOnly> { new(2024, 1, 5), new(2024, 1, 12), new(2024, 1, 19) };
        var history = new HouseholdSnowballHistory(weeks,
        [
            new HouseholdSnowballLoanHistory("Redwood Bank - Bluebird", false, false, [3_346.18m, 3_201.5m, 2_190.37m]),
            new HouseholdSnowballLoanHistory("Maple Ridge Mortgage", true, false, [172_904.26m, 172_640m, 0.01m]),
        ]);
        var growth = new List<HouseholdSavingsGrowth>
        {
            new("Redwood Bank", "Savings", 15_000.00m, [new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)], [11_630.12m, 11_870.64m]),
            new("Lakeside CU", "Share savings", null, [new DateOnly(2026, 9, 1)], [1_350m]),
        };
        var original = new HouseholdSnapshot(
            Now, OwnerEmail, [], new HouseholdSnowballSummary(new DateOnly(2031, 5, 2), 240, 1m, 0m, []),
            new HouseholdBudgetSummary("2026-09", false, []), new HouseholdSavingsSummary(0m, []),
            HouseholdSnapshot.CurrentVersion, history, growth);
        var file = FileWith(JsonSerializer.Serialize(original, VaultEnvelope.SerializerOptions));

        var back = (await new HouseholdSnapshotReader(file).ReadAsync(Passphrase, default)).Snapshot!;

        Assert.Equal(2, back.Version);
        Assert.Equal(weeks, back.SnowballHistory!.Weeks);
        Assert.Equal(2, back.SnowballHistory.LoansInFocusOrder.Count);
        Assert.Equal([3_346.18m, 3_201.5m, 2_190.37m], back.SnowballHistory.LoansInFocusOrder[0].Balances);
        Assert.Equal(0.01m, back.SnowballHistory.LoansInFocusOrder[1].Balances[2]);
        Assert.True(back.SnowballHistory.LoansInFocusOrder[1].IsMortgage);
        Assert.Equal(new DateOnly(2024, 1, 19), back.SnowballHistory.Weeks[2]);
        Assert.Equal(15_000.00m, back.SavingsGrowth![0].GoalTargetAmount);
        Assert.Equal([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)], back.SavingsGrowth[0].Months);
        Assert.Equal([11_630.12m, 11_870.64m], back.SavingsGrowth[0].Balances);
        Assert.Null(back.SavingsGrowth[1].GoalTargetAmount);
    }

    [Fact]
    public async Task ASeriesWhoseArraysDoNotLineUpIsDamagedNotDrawnWrong()
    {
        var history = new HouseholdSnowballHistory([new DateOnly(2024, 1, 5), new DateOnly(2024, 1, 12)],
            [new HouseholdSnowballLoanHistory("Harbor Auto Finance", false, false, [30_000m])]); // one balance, two weeks
        var broken = new HouseholdSnapshot(
            Now, OwnerEmail, [], new HouseholdSnowballSummary(new DateOnly(2031, 5, 2), 1, 1m, 0m, []),
            new HouseholdBudgetSummary("2026-09", false, []), new HouseholdSavingsSummary(0m, []),
            HouseholdSnapshot.CurrentVersion, history, []);
        var file = FileWith(JsonSerializer.Serialize(broken, VaultEnvelope.SerializerOptions));

        var result = await new HouseholdSnapshotReader(file).ReadAsync(Passphrase, default);

        Assert.Equal(HouseholdSnapshotStatus.Corrupt, result.Status);
    }

    // ---- an unknown FUTURE version: refused with a plain sentence, never overwritten ----

    private const string FutureJson = """
        {
          "Version": 3,
          "PublishedAtUtc": "2027-01-02T03:04:05+00:00",
          "PublishedByAccountEmail": "owner@example.com",
          "Institutions": "a shape this build has never seen",
          "Snowball": { "Reshaped": true }
        }
        """;

    [Fact]
    public async Task AnUnknownFutureVersionIsRefusedRatherThanMisread()
    {
        var reader = new HouseholdSnapshotReader(FileWith(FutureJson));

        var result = await reader.ReadAsync(Passphrase, default);

        Assert.Equal(HouseholdSnapshotStatus.NewerVersion, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal(3, result.NewerFileVersion);
        Assert.Equal(OwnerEmail, result.NewerFilePublisherEmail); // still knows whose file it is
        Assert.Equal(
            "The shared file was written by a newer Index than this one. Update Index on this phone to read it.",
            HouseholdChartCopy.NewerFile);
    }

    [Fact]
    public async Task ARefusedFutureFileStillTellsTheRoleLookupWhoPublishedIt()
    {
        var lookup = new HouseholdPublisherLookup(new HouseholdSnapshotReader(FileWith(FutureJson)));

        var publisher = await lookup.LookAsync(Passphrase, default);

        // Her install must still resolve as hers (a different account than the publisher) when the file is
        // newer than she can read; "unreadable" would leave her role undecided.
        Assert.Equal(HouseholdFileKind.Published, publisher.Kind);
        Assert.Equal(OwnerEmail, publisher.Email);
    }

    [Theory]
    [InlineData(OwnerEmail)] // even the account that wrote it: an older build must not downgrade its own newer file
    [InlineData(HerEmail)]
    public async Task ThePublisherNeverOverwritesAFileItCannotUnderstand(string signedInAs)
    {
        using var db = new TestDatabase();
        var file = FileWith(FutureJson);
        var before = file.Content;
        var (publisher, _, _, _) = Create(db, file);

        var result = await publisher.PublishAsync(Passphrase, signedInAs, default);

        Assert.Equal(before, file.Content);
        Assert.True(result.Ok); // a refusal is not a failure to retry, as with Phase 1.1's
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    // ---- the publisher writes both series, from the owner's database ----

    [Fact]
    public async Task ThePublishedFileIsVersionTwoWithTheSeventyWeekHistoryOfAllTenLoans()
    {
        using var db = new TestDatabase();
        await SeedTheSheetsLoansAsync(db);
        var file = new InMemoryDriveTextFile();
        var (publisher, reader, snowball, _) = Create(db, file);
        await snowball.ImportHistoryIfNeededAsync(default);

        var published = await publisher.PublishAsync(Passphrase, OwnerEmail, default);
        var snapshot = (await reader.ReadAsync(Passphrase, default)).Snapshot!;

        Assert.True(published.Ok);
        Assert.Null(published.Message);
        Assert.Equal(HouseholdSnapshot.CurrentVersion, snapshot.Version);
        var series = snapshot.SnowballHistory!;
        Assert.Equal(70, series.Weeks.Count);
        Assert.Equal(SyntheticSnowballHistory.Weeks[0], series.Weeks[0]);
        Assert.Equal(10, series.LoansInFocusOrder.Count);
        Assert.All(series.LoansInFocusOrder, loan => Assert.Equal(70, loan.Balances.Count));
        Assert.Contains(series.LoansInFocusOrder, loan => loan.Name == SyntheticSnowballHistory.MortgageName && loan.IsMortgage);
        Assert.Contains(series.LoansInFocusOrder, loan => loan.Name == SyntheticSnowballHistory.PaidOffName && loan.IsPaidOff);

        var size = file.Content!.Length;
        var plain = Plaintext(file).Length;
        output.WriteLine($"70 weeks x 10 loans: encrypted file {size:N0} chars, plaintext {plain:N0} chars.");
        Assert.True(size < 50_000, $"The household file grew to {size:N0} characters.");
    }

    [Fact]
    public async Task HerChartsAreDrawnFromTheSameShapesTheOwnersAre()
    {
        using var db = new TestDatabase();
        await SeedTheSheetsLoansAsync(db);
        var file = new InMemoryDriveTextFile();
        var (publisher, reader, snowball, _) = Create(db, file);
        await snowball.ImportHistoryIfNeededAsync(default);
        await publisher.PublishAsync(Passphrase, OwnerEmail, default);

        var owners = await snowball.GetHistoryAsync(default);
        var snapshot = (await reader.ReadAsync(Passphrase, default)).Snapshot!;
        var hers = HouseholdChartSeries.ToSnowballHistory(snapshot.SnowballHistory!);

        Assert.Equal(owners.Weeks, hers.Weeks);
        Assert.Equal(owners.LoansInFocusOrder.Count, hers.LoansInFocusOrder.Count);
        for (var i = 0; i < owners.LoansInFocusOrder.Count; i++)
        {
            var o = owners.LoansInFocusOrder[i];
            var h = hers.LoansInFocusOrder[i];
            Assert.Equal(o.Name, h.Name);
            Assert.Equal(o.Points, h.Points);
            Assert.Equal(o.Peak, h.Peak);
            Assert.Equal(o.IsPaidOff, h.IsPaidOff);
            Assert.Equal(o.IsMortgage, h.IsMortgage);
        }
    }

    [Fact]
    public void TheSnowballSeriesIsCappedToTheMostRecentWindow()
    {
        var weeks = Enumerable.Range(0, HouseholdSnowballHistory.MaxWeeks + 40)
            .Select(i => new DateOnly(2020, 1, 3).AddDays(7 * i)).ToList();
        var owners = new SnowballHistory(weeks,
        [
            new SnowballLoanHistory(Guid.NewGuid(), "Harbor Auto Finance", weeks.Select((w, i) => new SnowballHistoryPoint(w, 1_000m + i)).ToList(),
                1_000m + weeks.Count, false),
        ]);

        var series = HouseholdChartSeries.FromSnowballHistory(owners);

        Assert.Equal(HouseholdSnowballHistory.MaxWeeks, series.Weeks.Count);
        Assert.Equal(weeks[^1], series.Weeks[^1]);
        Assert.Equal(weeks[40], series.Weeks[0]);
        Assert.Equal(1_000m + 40, series.LoansInFocusOrder[0].Balances[0]);
    }

    [Fact]
    public async Task TheSavingsSeriesIsTwentyFourMonthsAndDrawsLikeTheOwnersGrowthChart()
    {
        using var db = new TestDatabase();
        await SeedSavingsAsync(db, months: 30);
        var file = new InMemoryDriveTextFile();
        var (publisher, reader, _, savings) = Create(db, file);

        await publisher.PublishAsync(Passphrase, OwnerEmail, default);
        var snapshot = (await reader.ReadAsync(Passphrase, default)).Snapshot!;

        var growth = snapshot.SavingsGrowth!;
        Assert.Equal(2, growth.Count);
        Assert.All(growth, a => Assert.Equal(HouseholdSavingsGrowth.MaxMonths, a.Months.Count));
        Assert.Equal(new DateOnly(2026, 9, 1), growth[0].Months[^1]);
        Assert.Equal(new DateOnly(2024, 10, 1), growth[0].Months[0]);
        Assert.Equal(15_000m, growth[0].GoalTargetAmount);
        Assert.Null(growth[1].GoalTargetAmount);

        var owners = await savings.GetGrowthAsync(default);
        var hers = HouseholdChartSeries.ToSavingsGrowth(snapshot, new DateOnly(2026, 9, 25))!;
        Assert.Equal(owners.Count, hers.Count);
        for (var i = 0; i < owners.Count; i++)
        {
            Assert.Equal(owners[i].AccountName, hers[i].AccountName);
            Assert.Equal(owners[i].InstitutionName, hers[i].InstitutionName);
            Assert.Equal(owners[i].GoalTargetAmount, hers[i].GoalTargetAmount);
            Assert.Equal(owners[i].Points.TakeLast(HouseholdSavingsGrowth.MaxMonths), hers[i].Points);
            // The dotted projection reaches the same month on both phones.
            Assert.Equal(owners[i].ReachesIt, hers[i].ReachesIt);
        }
        Assert.NotNull(hers[0].ReachesIt);
    }

    [Fact]
    public async Task AVersionOneSnapshotHasNoSavingsGrowthToConvert()
    {
        var snapshot = (await new HouseholdSnapshotReader(FileWith(HouseholdV1Fixture.Json))
            .ReadAsync(Passphrase, default)).Snapshot!;

        Assert.Null(HouseholdChartSeries.ToSavingsGrowth(snapshot, new DateOnly(2026, 9, 25)));
    }

    // ---- nothing that is a credential, a token or a full account number enters the file ----

    [Fact]
    public async Task TheSerialisedSnapshotCarriesNoTokenSecretOrAccountIdentifier()
    {
        using var db = new TestDatabase();
        await SeedTheSheetsLoansAsync(db);
        await SeedSavingsAsync(db, months: 5);
        var file = new InMemoryDriveTextFile();
        var (publisher, _, snowball, _) = Create(db, file);
        await snowball.ImportHistoryIfNeededAsync(default);
        await publisher.PublishAsync(Passphrase, OwnerEmail, default);

        var plaintext = Plaintext(file);

        Assert.DoesNotContain("access-token-for-", plaintext, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "AccessToken", "Secret", "PublicToken", "ItemId", "AccountId", "PlaidAccountId", "ClientId" })
        {
            Assert.DoesNotContain(forbidden, plaintext, StringComparison.OrdinalIgnoreCase);
        }

        // The Accounts side has always carried the 4-digit mask mockup 11 draws ("····7301"); nothing longer
        // may ever appear in it.
        using var document = JsonDocument.Parse(plaintext);
        foreach (var institution in document.RootElement.GetProperty("Institutions").EnumerateArray())
        {
            foreach (var account in institution.GetProperty("Accounts").EnumerateArray())
            {
                var mask = account.GetProperty("Mask").GetString();
                Assert.True(mask is null || mask.Length <= 4, $"Mask '{mask}' is longer than four characters.");
            }
        }
    }

    [Fact]
    public void TheNewSeriesTypesCarryNoIdentifierMaskTokenOrSecretMember()
    {
        var forbiddenFragments = new[] { "AccountId", "Mask", "Token", "Secret", "Password", "Number" };
        var types = new[]
        {
            typeof(HouseholdSnowballHistory), typeof(HouseholdSnowballLoanHistory), typeof(HouseholdSavingsGrowth),
        };

        foreach (var type in types)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.NotEqual("Id", property.Name);
                Assert.DoesNotContain(forbiddenFragments, f => property.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
                Assert.NotEqual(typeof(Guid), property.PropertyType);
            }
        }
    }
}
