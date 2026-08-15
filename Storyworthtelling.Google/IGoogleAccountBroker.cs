using global::Google.Apis.Auth.OAuth2;
using global::Google.Apis.Drive.v3;
using global::Google.Apis.Services;

namespace Storyworthtelling.Google;

/// <summary>
/// The sign-in-once broker: one Google account per install (persisted app-side in the vault's per-person
/// section — the wife's separate install binds to her own account, #10), reached with per-app minimal
/// scopes. It centralises the Drive-service construction every app's <c>MauiProgram</c> duplicates today, so
/// apps stop hand-rolling their own <c>DriveService</c> factory closures. The native <c>AccountManager</c>
/// picker and token minting stay in each Android head and are supplied here as a token delegate, keeping this
/// library pure .NET.
/// </summary>
public interface IGoogleAccountBroker
{
    /// <summary>The chosen account's email, or null before an account has been picked on this install.</summary>
    string? AccountEmail { get; }

    /// <summary>An access token carrying exactly the requested (minimal) scopes.</summary>
    Task<string> GetAccessTokenAsync(params string[] scopes);

    /// <summary>A Drive service authenticated for the requested scopes — the shared replacement for the
    /// per-app DriveFactory closures.</summary>
    Task<DriveService> CreateDriveServiceAsync(params string[] scopes);
}

/// <summary>
/// The default broker over a head-supplied token delegate. The delegate takes the AccountManager
/// auth-token-type (built by <see cref="GoogleScopes.ToTokenType"/>) and returns an access token; the head
/// backs it with its native device-account provider. This type owns only the account identity, the scope
/// wiring, and service construction — all testable without Google.
/// </summary>
public sealed class GoogleAccountBroker : IGoogleAccountBroker
{
    private readonly Func<string, CancellationToken, Task<string>> tokenForType;
    private readonly string applicationName;

    public GoogleAccountBroker(
        string? accountEmail,
        Func<string, CancellationToken, Task<string>> tokenForType,
        string applicationName)
    {
        AccountEmail = accountEmail;
        this.tokenForType = tokenForType;
        this.applicationName = applicationName;
    }

    public string? AccountEmail { get; }

    public Task<string> GetAccessTokenAsync(params string[] scopes) =>
        tokenForType(GoogleScopes.ToTokenType(scopes), CancellationToken.None);

    public async Task<DriveService> CreateDriveServiceAsync(params string[] scopes)
    {
        var token = await GetAccessTokenAsync(scopes);
        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token),
            ApplicationName = applicationName,
        });
    }
}
