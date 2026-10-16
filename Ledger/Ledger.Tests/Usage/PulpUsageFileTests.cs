using Index.Domain.Usage;
using Index.Infrastructure.Usage;
using Index.Tests.Support;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Index.Tests.Usage;

/// <summary>
/// Pulp's usage through Drive (Pulp D320), for the install that shows a charge on a phone Pulp never ran
/// beside: the file's codec, how it fills a ledger the local receiver left short, and the read that never
/// guesses. Services, merchants and counts are invented.
/// </summary>
public class PulpUsageFileTests
{
    private const string Passphrase = "an-invented-usage-passphrase";

    private const string Good =
        "{\"version\":1,\"entries\":[{\"service\":\"streamly\",\"month\":\"2026-09\",\"count\":14,\"lastOpened\":\"2026-09-18\",\"merchants\":[\"Streamly\",\"Streamly.com\"]}]}";

    private static PulpUsageReceipt Receipt(string service, string month, int count, params string[] merchants) =>
        new(service, month, count, null, merchants);

    // ---- the codec ----

    [Fact]
    public void AWellFormedFileDecodesToTheLedgerItDescribes()
    {
        var ledger = PulpUsageFile.Decode(Good);

        var entry = Assert.Single(ledger!.Entries);
        Assert.Equal(("streamly", "2026-09", 14), (entry.Service, entry.YearMonth, entry.Count));
        Assert.Equal(new DateOnly(2026, 9, 18), entry.LastOpened);
        Assert.Equal(["Streamly", "Streamly.com"], entry.Merchants);
    }

    [Fact]
    public void ADatelessEntryIsNoDateNotARefusal()
    {
        var ledger = PulpUsageFile.Decode(
            "{\"version\":1,\"entries\":[{\"service\":\"streamly\",\"month\":\"2026-09\",\"count\":2,\"merchants\":[\"Streamly\"]}]}");

        Assert.Null(Assert.Single(ledger!.Entries).LastOpened);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("[]")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":2,\"entries\":[]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"service\":\"streamly\",\"month\":\"2026-13\",\"count\":1,\"merchants\":[\"Streamly\"]}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"service\":\"streamly\",\"month\":\"2026-09\",\"count\":\"many\",\"merchants\":[\"Streamly\"]}]}")]
    [InlineData("{\"version\":1,\"entries\":[{\"service\":\"streamly\",\"month\":\"2026-09\",\"count\":1,\"merchants\":[]}]}")]
    public void AGarbledOrNewerFileDecodesToNothingRatherThanAGuess(string text)
    {
        Assert.Null(PulpUsageFile.Decode(text));
    }

    // ---- filling the ledger ----

    [Fact]
    public void TheLocalReceiverWinsWhereItHasAMonthAndDriveFillsTheRest()
    {
        var local = new PulpUsageLedger([Receipt("streamly", "2026-09", 9, "Streamly")]);
        var shared = new PulpUsageLedger(
        [
            Receipt("streamly", "2026-09", 14, "Streamly"),
            Receipt("streamly", "2026-08", 6, "Streamly"),
            Receipt("movieplus", "2026-09", 3, "MoviePlus"),
        ]);

        var merged = local.WithSharedFrom(shared);

        Assert.Equal(
            [("streamly", "2026-08", 6), ("movieplus", "2026-09", 3), ("streamly", "2026-09", 9)],
            merged.Entries.Select(entry => (entry.Service, entry.YearMonth, entry.Count)));
    }

    [Fact]
    public void AnEmptyLocalLedgerTakesTheSharedOneWhole()
    {
        var shared = new PulpUsageLedger([Receipt("streamly", "2026-09", 14, "Streamly")]);

        Assert.Equal(shared.Entries, PulpUsageLedger.Empty.WithSharedFrom(shared).Entries);
    }

    [Fact]
    public void TheLineAppearsUnderAChargeFromTheSharedLedgerAlone()
    {
        var ledger = PulpUsageLedger.Empty.WithSharedFrom(PulpUsageFile.Decode(Good)!);

        Assert.Equal("Opened 14 times in Pulp", PulpUsageLine.For(ledger, "Streamly", "2026-09", new DateOnly(2026, 9, 20)));
    }

    // ---- the read ----

    private static string Sealed(string plaintext, string passphrase = Passphrase) => VaultEnvelope.Write(plaintext, passphrase);

    [Fact]
    public void TheFileIsNamedAsPulpWritesIt() =>
        Assert.Equal("storyworthtelling-pulp-usage.json", DrivePulpUsageSource.FileName);

    [Fact]
    public async Task ASealedFileLoads()
    {
        var file = new InMemoryDriveTextFile();
        await file.WriteAsync(Sealed(Good), default);

        var result = await new DrivePulpUsageSource(file).ReadAsync(Passphrase, default);

        Assert.Equal(PulpUsageReadStatus.Loaded, result.Status);
        Assert.Single(result.Ledger!.Entries);
    }

    [Fact]
    public async Task NoPassphraseAsksNothingOfDrive()
    {
        var file = new ThrowingFile();

        var result = await new DrivePulpUsageSource(file).ReadAsync(" ", default);

        Assert.Equal(PulpUsageReadStatus.NeedsPassphrase, result.Status);
        Assert.Equal(0, file.Reads);
    }

    [Fact]
    public async Task AWrongPassphraseIsItsOwnStatusAndYieldsNoLedger()
    {
        var file = new InMemoryDriveTextFile();
        await file.WriteAsync(Sealed(Good), default);

        var result = await new DrivePulpUsageSource(file).ReadAsync("another-invented-passphrase", default);

        Assert.Equal(PulpUsageReadStatus.WrongPassphrase, result.Status);
        Assert.Null(result.Ledger);
    }

    [Theory]
    [InlineData("not an envelope")]
    [InlineData("SEALED:garbage")]
    public async Task AGarbledFileIsCorruptAndYieldsNoLedger(string content)
    {
        var file = new InMemoryDriveTextFile();
        await file.WriteAsync(content.StartsWith("SEALED:", StringComparison.Ordinal) ? Sealed(content[7..]) : content, default);

        var result = await new DrivePulpUsageSource(file).ReadAsync(Passphrase, default);

        Assert.Equal(PulpUsageReadStatus.Corrupt, result.Status);
        Assert.Null(result.Ledger);
    }

    [Fact]
    public async Task AnAbsentFileIsAbsentNotAnEmptyLedgerThatWouldWipeWhatWasKept()
    {
        var result = await new DrivePulpUsageSource(new InMemoryDriveTextFile()).ReadAsync(Passphrase, default);

        Assert.Equal(PulpUsageReadStatus.Absent, result.Status);
        Assert.Null(result.Ledger);
    }

    [Fact]
    public async Task DriveBeingUnreachableIsAStatusNotAnException()
    {
        var result = await new DrivePulpUsageSource(new ThrowingFile()).ReadAsync(Passphrase, default);

        Assert.Equal(PulpUsageReadStatus.Unreadable, result.Status);
    }

    [Fact]
    public async Task AFailedReadIsLoggedWithItsExceptionBeforeTheStatusIsReturned()
    {
        var logger = new CapturingLogger<DrivePulpUsageSource>();

        var result = await new DrivePulpUsageSource(new ThrowingFile(), logger).ReadAsync(Passphrase, default);

        Assert.Equal(PulpUsageReadStatus.Unreadable, result.Status);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
    }

    // ---- the throttle: a read that failed is asked again soon, one that worked is not ----

    private static readonly DateTime Noon = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AReadThatWorkedWaitsHalfAnHourBeforeTheNext()
    {
        var throttle = new PulpUsageThrottle();

        Assert.True(throttle.TryBegin(Noon));
        throttle.End(Noon, succeeded: true);

        Assert.False(throttle.TryBegin(Noon.AddMinutes(29)));
        Assert.True(throttle.TryBegin(Noon.AddMinutes(31)));
    }

    [Fact]
    public void AReadThatFailedIsAskedAgainAfterTwoMinutesNotThirty()
    {
        var throttle = new PulpUsageThrottle();

        Assert.True(throttle.TryBegin(Noon));
        throttle.End(Noon, succeeded: false);

        Assert.False(throttle.TryBegin(Noon.AddMinutes(1)));
        Assert.True(throttle.TryBegin(Noon.AddMinutes(3)));
    }

    [Fact]
    public void ARunningReadIsNotStartedASecondTime()
    {
        var throttle = new PulpUsageThrottle();

        Assert.True(throttle.TryBegin(Noon));
        Assert.False(throttle.TryBegin(Noon.AddHours(1)));
    }

    [Fact]
    public void OnlyALoadedFileOrNoFileAtAllCountsAsAReadThatWorked()
    {
        Assert.True(PulpUsageThrottle.Worked(PulpUsageReadStatus.Loaded));
        Assert.True(PulpUsageThrottle.Worked(PulpUsageReadStatus.Absent));
        foreach (var status in new[] { PulpUsageReadStatus.Unreadable, PulpUsageReadStatus.Corrupt, PulpUsageReadStatus.WrongPassphrase, PulpUsageReadStatus.NeedsPassphrase })
        {
            Assert.False(PulpUsageThrottle.Worked(status));
        }
    }

    private sealed class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }

    private sealed class ThrowingFile : Storyworthtelling.Voice.IDriveTextFile
    {
        public int Reads { get; private set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            throw new InvalidOperationException("offline");
        }

        public Task WriteAsync(string content, CancellationToken cancellationToken) => throw new InvalidOperationException("offline");
    }
}
