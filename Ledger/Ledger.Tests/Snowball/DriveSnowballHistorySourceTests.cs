using Index.Domain.Model;
using Index.Infrastructure.Snowball;
using Index.Tests.Support;
using Storyworthtelling.Voice;
using Xunit;

namespace Index.Tests.Snowball;

public class DriveSnowballHistorySourceTests
{
    private const string Passphrase = "an-invented-passphrase-for-tests";

    private sealed class CountingFile(string? content) : IDriveTextFile
    {
        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public Exception? FailWith { get; init; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            if (FailWith is not null)
            {
                throw FailWith;
            }
            return Task.FromResult(content);
        }

        public Task WriteAsync(string text, CancellationToken cancellationToken)
        {
            Writes++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void TheFileIsNamedAsTheOwnerUploadsIt() =>
        Assert.Equal("storyworthtelling-index-snowball-history.json", DriveSnowballHistorySource.FileName);

    [Fact]
    public async Task ASealedFileLoadsTheHistory()
    {
        var file = new CountingFile(SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase));

        var result = await new DriveSnowballHistorySource(file).ReadAsync(Passphrase, default);

        Assert.Equal(SnowballHistoryStatus.Loaded, result.Status);
        Assert.Equal(700, result.History!.Rows.Count);
        Assert.Equal(0, file.Writes); // reading never writes the owner's Drive
    }

    [Fact]
    public async Task NoFileIsAbsent()
    {
        var result = await new DriveSnowballHistorySource(new CountingFile(null)).ReadAsync(Passphrase, default);

        Assert.Equal(SnowballHistoryStatus.Absent, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task NoPassphraseNeedsOneAndDriveIsNotTouched(string? passphrase)
    {
        var file = new CountingFile(SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase));

        var result = await new DriveSnowballHistorySource(file).ReadAsync(passphrase, default);

        Assert.Equal(SnowballHistoryStatus.NeedsPassphrase, result.Status);
        Assert.Equal(0, file.Reads);
    }

    [Fact]
    public async Task AWrongPassphraseIsReportedAsSuch()
    {
        var file = new CountingFile(SnowballHistorySealer.Seal(SyntheticSnowballHistory.Json, Passphrase));

        var result = await new DriveSnowballHistorySource(file).ReadAsync("some other passphrase", default);

        Assert.Equal(SnowballHistoryStatus.WrongPassphrase, result.Status);
    }

    [Fact]
    public async Task ADriveFailureIsUnreadableWithTheReasonNotAnException()
    {
        var file = new CountingFile(null) { FailWith = new HttpRequestException("the network dropped") };

        var result = await new DriveSnowballHistorySource(file).ReadAsync(Passphrase, default);

        Assert.Equal(SnowballHistoryStatus.Unreadable, result.Status);
        Assert.Contains("HttpRequestException", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        var file = new CountingFile(null) { FailWith = new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new DriveSnowballHistorySource(file).ReadAsync(Passphrase, default));
    }

    [Fact]
    public async Task AGarbledFileIsUnreadable()
    {
        var result = await new DriveSnowballHistorySource(new CountingFile("garbled")).ReadAsync(Passphrase, default);

        Assert.Equal(SnowballHistoryStatus.Unreadable, result.Status);
    }
}
