using System.Net;
using Storyworthtelling.Voice.Secrets;
using Xunit;

namespace Storyworthtelling.Voice.Tests.Secrets;

/// <summary>
/// A backup that failed said the likely cause was the Drive full-access scope needing a new grant, whatever the cause,
/// and put the raw exception's type and message in the sentence a Setup page shows. The words are now plain, and the
/// scope sentence is said only when Google actually turned the request away (401 or 403).
/// </summary>
public class VaultPublishMessageTests
{
    private const string Pass = "example-passphrase";

    private sealed class LoadThrowingStore(Exception failure) : ISecretsVaultStore
    {
        public Task<SecretsVaultLoadResult> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            Task.FromException<SecretsVaultLoadResult>(failure);

        public Task SaveAsync(SecretsVault vault, string passphrase, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task NoGoogleAccountSaysToSignInAndNothingAboutTheScope()
    {
        var service = new SecretsVaultService(new LoadThrowingStore(new InvalidOperationException("Sign in with Google first.")), () => Pass);

        var result = await service.PublishAsync(v => v, default);

        Assert.Equal(SecretsVaultPublishStatus.WriteFailed, result.Status);
        Assert.False(result.ScopeLikely);
        Assert.DoesNotContain("scope", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", result.Message);
        Assert.Contains("Sign in with Google", result.Message);
        Assert.Contains("Your settings are saved on this device.", result.Message);
    }

    [Fact]
    public async Task AForbiddenDriveSaysTheScopeMayNeedGrantingAgain()
    {
        var forbidden = new HttpRequestException("Response status code does not indicate success: 403 (Forbidden).", null, HttpStatusCode.Forbidden);
        var service = new SecretsVaultService(new LoadThrowingStore(forbidden), () => Pass);

        var result = await service.PublishAsync(v => v, default);

        Assert.True(result.ScopeLikely);
        Assert.Contains("scope", result.Message);
        Assert.DoesNotContain("HttpRequestException", result.Message);
    }

    [Fact]
    public async Task ADroppedNetworkIsSaidPlainly()
    {
        var offline = new HttpRequestException("Unable to resolve host \"example.com\": No address associated with hostname");
        var service = new SecretsVaultService(new LoadThrowingStore(offline), () => Pass);

        var result = await service.PublishAsync(v => v, default);

        Assert.False(result.ScopeLikely);
        Assert.Contains("the phone has no network.", result.Message);
        Assert.DoesNotContain("example.com", result.Message);
        Assert.DoesNotContain("scope", result.Message);
    }
}
