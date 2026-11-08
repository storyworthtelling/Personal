using System.Text.Json;
using Storyworthtelling.Voice.Secrets;
using Storyworthtelling.Voice.Tests.Secrets;

namespace Storyworthtelling.Voice.Tests;

// Privacy report §5: the voice profile carried verbatim passages of the owner's manuscripts, in plaintext, in a
// file any app with the full Drive scope could read. It is sealed under the vault passphrase now, in a new file,
// with the old plaintext one read until the sealed copy is confirmed and then trashed.
public class SealedVoiceProfileTests
{
    private const string Pass = "invented sample passphrase";

    private readonly Dictionary<string, SealedDriveFileTests.MemoryDrive> drives = new(StringComparer.Ordinal);

    private SealedDriveFileTests.MemoryDrive Drive(string name) =>
        drives.TryGetValue(name, out var drive) ? drive : drives[name] = new SealedDriveFileTests.MemoryDrive();

    private DriveVoiceProfileStore Store(string? passphrase = Pass) => DriveVoiceProfileStore.Sealed(Drive, () => passphrase);

    private static VoiceProfile Profile(string passage) => VoiceProfile.Empty with
    {
        Examples = [new VoiceExample(passage, Guid.Parse("00000000-0000-0000-0000-000000000001"))],
    };

    [Fact]
    public async Task WhatIsSavedIsSealedAndLoadsBack()
    {
        await Store().SaveAsync(Profile("an invented passage of the manuscript"), default);

        var sealedText = Drive(DriveVoiceProfileStore.SealedFileName).Content!;
        Assert.DoesNotContain("invented passage", sealedText, StringComparison.Ordinal);
        Assert.Equal("an invented passage of the manuscript", (await Store().LoadAsync(default)).Examples.Single().Text);
    }

    [Fact]
    public async Task ThePlaintextProfileIsReadUntilTheSealedOneIsWrittenThenTrashed()
    {
        Drive(DriveVoiceProfileStore.FileName).Content = JsonSerializer.Serialize(Profile("old plaintext passage"));

        Assert.Equal("old plaintext passage", (await Store().LoadAsync(default)).Examples.Single().Text);

        await Store().SaveAsync(Profile("new passage"), default);

        Assert.Null(Drive(DriveVoiceProfileStore.FileName).Content);
        Assert.Equal("new passage", (await Store().LoadAsync(default)).Examples.Single().Text);
    }

    [Fact]
    public async Task AnAppWithNoPassphraseStillReadsThePlaintextProfileButNeverWritesOne()
    {
        Drive(DriveVoiceProfileStore.FileName).Content = JsonSerializer.Serialize(Profile("old plaintext passage"));

        Assert.Equal("old plaintext passage", (await Store(passphrase: null).LoadAsync(default)).Examples.Single().Text);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(passphrase: null).SaveAsync(Profile("x"), default));
        Assert.Null(Drive(DriveVoiceProfileStore.SealedFileName).Content);
    }

    [Fact]
    public async Task ASealedProfileThatWillNotOpenIsNeutralCopyAndIsNotWrittenOver()
    {
        Drive(DriveVoiceProfileStore.SealedFileName).Content = VaultEnvelope.Write("{}", "another passphrase");
        var before = Drive(DriveVoiceProfileStore.SealedFileName).Content;

        Assert.Empty((await Store().LoadAsync(default)).Examples);
        await Assert.ThrowsAsync<IOException>(() => Store().SaveAsync(Profile("x"), default));
        Assert.Equal(before, Drive(DriveVoiceProfileStore.SealedFileName).Content);
    }
}
