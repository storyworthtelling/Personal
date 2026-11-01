using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Storyworthtelling.Diagnostics;

/// <summary>The flows a trail can describe. Each is a wire name in the logs, so renaming one is a decision.</summary>
public static class SignInFlowKinds
{
    public const string GmailApi = "gmail-api";
    public const string GoogleDeviceAccount = "google-device-account";
    public const string TidalPkce = "tidal-pkce";
    public const string TidalDevice = "tidal-device";
    public const string PlaidHostedLink = "plaid-hosted-link";
    public const string RemarkablePairing = "remarkable-pairing";
    public const string SleepNumber = "sleep-number";

    /// <summary>Flyleaf's restricted browser passing a sign-in redirect on (or blocking it); joined to the app's own trail by state.</summary>
    public const string RedirectHop = "flyleaf-redirect-hop";
}

/// <summary>Which way the sign-in left the app.</summary>
public static class SignInBrowserPaths
{
    public const string AccountPicker = "account picker";
    public const string CustomTab = "custom tab";
    public const string ChromeByPackage = "chrome by package";
    public const string RestrictedBrowser = "flyleaf restricted browser";
    public const string None = "none";
}

public enum SignInStepKind
{
    Started,
    NoBrowser,
    BrowserRefused,
    LeftApp,
    Hop,
    Returned,
    ProviderError,
    StateMismatch,
    StaleRedirect,
    ExchangeFailed,
    TokenReceived,
    NoReturn,
    AccountPicker,
    ConsentShown,
    ConsentDenied,
    RecoverableAuth,
}

/// <summary>One step: when, what, and the words that go with it.</summary>
/// <param name="Outcome">A short machine word for steps that branch (picker chosen/cancelled, hop handed/blocked, no-return resumed/timeout).</param>
public sealed record SignInStep(DateTimeOffset At, SignInStepKind Kind, string Detail, string? Outcome = null);

/// <summary>
/// The ordered record of one sign-in (LOG-LOOP-PLAN.md §4.2, G2): did a token come back, and if not,
/// where it stopped. The flow id is the OAuth <c>state</c> where there is one — it is in the redirect URL,
/// so Flyleaf's hop and the app's own trail join on it — else <see cref="NewFlowId"/>.
/// <para>
/// O9 keeps the full redirect URL, one-time code included. A3 keeps every standing credential out: the
/// token step has no parameter that could carry a token, and a redirect's <c>access_token</c>,
/// <c>refresh_token</c>, <c>id_token</c>, <c>code_verifier</c>, <c>client_secret</c> and <c>password</c>
/// values are withheld before the URL is stored.
/// </para>
/// Immutable: every step returns a new trail, so a trail written to the log can never change under it.
/// </summary>
public sealed partial class SignInTrail
{
    public static readonly TimeSpan NoReturnTimeout = TimeSpan.FromMinutes(10);

    private SignInTrail(string flowKind, string flowId, IReadOnlyList<SignInStep> steps)
    {
        FlowKind = flowKind;
        FlowId = flowId;
        Steps = steps;
    }

    public string FlowKind { get; }
    public string FlowId { get; }
    public IReadOnlyList<SignInStep> Steps { get; }

    public bool Succeeded => Steps.Any(s => s.Kind == SignInStepKind.TokenReceived);

    /// <summary>Whether the trail has reached an end, good or bad; a finished trail takes no NoReturn.</summary>
    public bool IsFinished => Steps.Any(IsTerminal);

    public static string NewFlowId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    public static SignInTrail Start(
        string flowKind, string flowId, DateTimeOffset at,
        string? clientId, string? redirectUri, IReadOnlyList<string> scopes, string browserPath)
    {
        var detail = new StringBuilder();
        detail.Append("path ").Append(browserPath);
        detail.Append("; scopes ").Append(scopes.Count == 0 ? "(none)" : string.Join(' ', scopes));
        if (clientId is not null)
        {
            detail.Append("; client ").Append(clientId);
        }
        if (redirectUri is not null)
        {
            detail.Append("; redirect ").Append(redirectUri);
        }
        return new SignInTrail(flowKind, flowId, [new SignInStep(at, SignInStepKind.Started, detail.ToString())]);
    }

    public SignInTrail NoBrowser(DateTimeOffset at, string reason) => With(at, SignInStepKind.NoBrowser, reason);

    public SignInTrail BrowserRefused(DateTimeOffset at, string policyDecision) =>
        With(at, SignInStepKind.BrowserRefused, policyDecision);

    public SignInTrail LeftApp(DateTimeOffset at) => With(at, SignInStepKind.LeftApp, "left the app");

    /// <summary>Flyleaf's restricted browser saw the redirect: handed back to a package, or blocked and why.</summary>
    public SignInTrail Hop(DateTimeOffset at, string? handedTo, string? blockedReason) =>
        blockedReason is not null
            ? With(at, SignInStepKind.Hop, "blocked: " + blockedReason, "blocked")
            : With(at, SignInStepKind.Hop, "handed back to " + (handedTo ?? "(unknown)"), "handed");

    public SignInTrail Returned(DateTimeOffset at, string redirectUrl) =>
        With(at, SignInStepKind.Returned, WithholdCredentials(redirectUrl));

    public SignInTrail ProviderError(DateTimeOffset at, string error, string? description) =>
        With(at, SignInStepKind.ProviderError, description is null ? error : $"{error} — {description}");

    public SignInTrail StateMismatch(DateTimeOffset at, string expected, string received) =>
        With(at, SignInStepKind.StateMismatch, $"expected {expected}, received {received}");

    public SignInTrail StaleRedirect(DateTimeOffset at, string received) =>
        With(at, SignInStepKind.StaleRedirect, "received " + received);

    public SignInTrail ExchangeFailed(DateTimeOffset at, int httpStatus, string body) =>
        With(at, SignInStepKind.ExchangeFailed,
            string.Create(CultureInfo.InvariantCulture, $"HTTP {httpStatus}: {WithholdCredentials(body)}"),
            httpStatus.ToString(CultureInfo.InvariantCulture));

    /// <summary>A token came back. Its scopes, expiry and type are recorded; the token itself has nowhere to go.</summary>
    public SignInTrail TokenReceived(DateTimeOffset at, IReadOnlyList<string> grantedScopes, DateTimeOffset? expires, string tokenType) =>
        With(at, SignInStepKind.TokenReceived, string.Create(CultureInfo.InvariantCulture,
            $"scopes {string.Join(' ', grantedScopes)}; type {tokenType}; expires {(expires is null ? "unknown" : expires.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))}"),
            string.Join(' ', grantedScopes));

    public SignInTrail AccountChosen(DateTimeOffset at) => With(at, SignInStepKind.AccountPicker, "account chosen", "chosen");

    public SignInTrail AccountCancelled(DateTimeOffset at) => With(at, SignInStepKind.AccountPicker, "picker cancelled", "cancelled");

    public SignInTrail ConsentShown(DateTimeOffset at) => With(at, SignInStepKind.ConsentShown, "consent screen shown");

    public SignInTrail ConsentDenied(DateTimeOffset at, string reason) => With(at, SignInStepKind.ConsentDenied, reason);

    public SignInTrail RecoverableAuth(DateTimeOffset at, string detail) => With(at, SignInStepKind.RecoverableAuth, detail);

    /// <summary>A flow with its own clock (a device code's window) ran out with nothing back.</summary>
    public SignInTrail TimedOut(DateTimeOffset at, string detail) => With(at, SignInStepKind.NoReturn, detail, "timeout");

    /// <summary>
    /// Marks the trail NoReturn when the app came back (Back, a task switch) with nothing having returned,
    /// or when <see cref="NoReturnTimeout"/> has passed. A finished trail is returned as it is.
    /// </summary>
    public SignInTrail CheckNoReturn(DateTimeOffset now, bool resumed)
    {
        if (IsFinished)
        {
            return this;
        }
        var started = Steps[0].At;
        var waited = now - started;
        if (waited >= NoReturnTimeout)
        {
            return With(now, SignInStepKind.NoReturn, "timed out after 10 minutes", "timeout");
        }
        // A resume is a no-return only if nothing but Flyleaf's hop has happened since the app was left:
        // once an account picker or a redirect has answered, the app resuming is the flow going on.
        var leftIndex = Steps.ToList().FindLastIndex(s => s.Kind == SignInStepKind.LeftApp);
        var leftAt = leftIndex < 0 ? (DateTimeOffset?)null : Steps[leftIndex].At;
        var nothingSince = leftIndex >= 0 && Steps.Skip(leftIndex + 1).All(s => s.Kind == SignInStepKind.Hop);
        if (resumed && leftAt is not null && nothingSince)
        {
            return With(now, SignInStepKind.NoReturn,
                string.Create(CultureInfo.InvariantCulture, $"resumed after {(int)(now - leftAt.Value).TotalSeconds} s with no redirect"),
                "resumed");
        }
        return this;
    }

    public string ToLogText()
    {
        var builder = new StringBuilder();
        builder.Append("SIGN-IN ").Append(FlowKind).Append(' ').Append(FlowId);
        var start = Steps[0].At;
        foreach (var step in Steps)
        {
            builder.Append("\n  +").Append(((int)(step.At - start).TotalSeconds).ToString(CultureInfo.InvariantCulture))
                .Append(" s ").Append(step.Kind).Append(": ").Append(step.Detail);
        }
        builder.Append("\nWhy: ").Append(SignInDiagnosis.Explain(this));
        return builder.ToString();
    }

    public string ToJson() => JsonSerializer.Serialize(
        new TrailDto(FlowKind, FlowId, Steps.Select(s => new StepDto(s.At, s.Kind.ToString(), s.Detail, s.Outcome)).ToList()));

    public static SignInTrail FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<TrailDto>(json)
            ?? throw new FormatException("The sign-in trail is empty.");
        if (dto.Steps.Count == 0)
        {
            throw new FormatException("The sign-in trail has no steps.");
        }
        return new SignInTrail(dto.FlowKind, dto.FlowId, dto.Steps
            .Select(s => new SignInStep(s.At, Enum.Parse<SignInStepKind>(s.Kind), s.Detail, s.Outcome))
            .ToList());
    }

    private SignInTrail With(DateTimeOffset at, SignInStepKind kind, string detail, string? outcome = null) =>
        new(FlowKind, FlowId, [.. Steps, new SignInStep(at, kind, detail, outcome)]);

    private static bool IsTerminal(SignInStep step) => step.Kind switch
    {
        SignInStepKind.TokenReceived or SignInStepKind.NoReturn or SignInStepKind.ProviderError
            or SignInStepKind.StateMismatch or SignInStepKind.StaleRedirect or SignInStepKind.ExchangeFailed
            or SignInStepKind.ConsentDenied or SignInStepKind.NoBrowser or SignInStepKind.BrowserRefused => true,
        SignInStepKind.AccountPicker => step.Outcome == "cancelled",
        SignInStepKind.Hop => step.Outcome == "blocked",
        _ => false,
    };

    /// <summary>Replaces the value of every standing-credential parameter with <c>[withheld]</c>.</summary>
    public static string WithholdCredentials(string text) =>
        CredentialParameter().Replace(text, m => m.Groups["name"].Value + "=[withheld]");

    [GeneratedRegex(@"(?<name>(?:access_token|refresh_token|id_token|code_verifier|client_secret|password))(?:=|"":\s*"")(?<value>[^&#\s""]*)", RegexOptions.IgnoreCase)]
    private static partial Regex CredentialParameter();

    private sealed record TrailDto(string FlowKind, string FlowId, List<StepDto> Steps);

    private sealed record StepDto(DateTimeOffset At, string Kind, string Detail, string? Outcome);
}
