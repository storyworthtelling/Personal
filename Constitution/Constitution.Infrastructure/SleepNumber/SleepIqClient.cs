using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Constitution.Domain.Credentials;
using Constitution.Infrastructure.Credentials;
using Constitution.Infrastructure.Vault;
using Storyworthtelling.Voice.Health;

namespace Constitution.Infrastructure.SleepNumber;

/// <summary>
/// The one place that knows the Sleep Number cloud ("SleepIQ"). Every endpoint, query name, JSON field and
/// encoding below is a BEST GUESS from the unofficial API's general shape; the live-endpoint spike was
/// skipped by the owner (D97) and nothing here has met the real service. When the real thing disagrees,
/// this is the only file to fix, and the log (below) says exactly what it answered.
///
/// <para><b>Assumed shape</b> (base <see cref="BaseUrl"/>; paths are relative to it):</para>
/// <list type="bullet">
/// <item><c>PUT login</c>, JSON body with a <c>login</c> and a <c>password</c> field (the household login) returns
///   <c>{"key":"&lt;session key&gt;", ...}</c>. 401/403 = the login is refused.</item>
/// <item><c>GET sleeper?_k=KEY</c> returns <c>{"sleepers":[{"sleeperId":"...","side":0|1,"active":true,...}]}</c>.
///   <c>side</c> 0 is Left and 1 is Right (the strings "left"/"right" are also read). A sleeper with
///   <c>"active":false</c> is skipped. The reply also carries a first name; it is never read.</item>
/// <item><c>GET sleepData?_k=KEY&amp;interval=D1&amp;sleeper=ID&amp;includeSlices=false&amp;date=yyyy-MM-dd</c> returns
///   <c>{"sleepData":[{"days":[{"date":"yyyy-MM-dd","totalSleepSessionTime":SECONDS}]}]}</c>. The day record is
///   keyed by the morning the sleep ended on. Zero seconds, or no record for that date, means no reading.</item>
/// <item>The session key rides in the <c>_k</c> query parameter. It is held in a field only, never written anywhere.
///   A 401/403 on a data call drops it, signs in once more and retries once.</item>
/// </list>
///
/// <para><b>Failures are typed, never thrown for the network.</b> 401/403 is <see cref="SleepIqFailure.SignInRejected"/>
/// (and, through <see cref="CredentialReportingHandler"/>, the only thing that flags the credential); 408, 429, 5xx,
/// a dropped connection or a timeout is <see cref="SleepIqFailure.Offline"/>; any other status or a reply that is not
/// the assumed shape is <see cref="SleepIqFailure.UnexpectedShape"/>. Only the caller's own cancellation propagates.</para>
///
/// <para><b>The log</b> gets one phrase per call outcome: the endpoint label and the status code
/// (<c>sleepiq sign-in: 200</c>), plus a reason built only from this class's own constants when the shape was wrong
/// (<c>sleepiq sleepers: unexpected shape (no sleepers list)</c>) or the exception type when offline. Never a body,
/// token, id, name, reading or exception message.</para>
///
/// <para>Not thread-safe: one worker calls it at a time.</para>
/// </summary>
public sealed class SleepIqClient : ISleepIqClient
{
    /// <summary>The single place the service's address is written.</summary>
    public const string BaseUrl = "https://prod-api.sleepiq.sleepnumber.com/rest/";

    private const string SignInLabel = "sign-in";
    private const string SleepersLabel = "sleepers";
    private const string SleepDataLabel = "sleep-data";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient http;
    private readonly IConstitutionConnections login;
    private readonly ISleepIqLog log;

    private string? sessionKey;
    private IReadOnlyList<BedSleeper>? knownSleepers;

    /// <param name="http">Built by <see cref="CreateHttpClient"/> so the credential reporting is in place.</param>
    /// <param name="login">The household login as the vault restored it.</param>
    public SleepIqClient(HttpClient http, IConstitutionConnections login, ISleepIqLog log)
    {
        this.http = http;
        this.login = login;
        this.log = log;
    }

    /// <summary>
    /// The HttpClient this class expects: the service's base address, a timeout, and the shared
    /// <see cref="CredentialReportingHandler"/> under <see cref="CredentialIds.SleepNumber"/>, so only an
    /// auth rejection flags the login and success clears it. A null sink makes the wrapper a pass-through.
    /// </summary>
    public static HttpClient CreateHttpClient(HttpMessageHandler inner, ICredentialHealthSink? sink) =>
        new(new CredentialReportingHandler(CredentialIds.SleepNumber, sink) { InnerHandler = inner })
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = RequestTimeout,
        };

    public async Task<SleepIqFailure> SignInAsync(CancellationToken cancellationToken)
    {
        sessionKey = null;
        var account = login.SleepNumberLogin;
        var word = login.SleepNumberPassword;
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(word))
        {
            log.Outcome($"sleepiq {SignInLabel}: no login set up");
            return SleepIqFailure.NoLogin;
        }

        var reply = await SendAsync(SignInLabel, () => new HttpRequestMessage(HttpMethod.Put, "login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, string> { ["login"] = account, ["password"] = word }),
                Encoding.UTF8,
                "application/json"),
        }, cancellationToken).ConfigureAwait(false);
        if (!reply.IsOk)
        {
            return reply.Failure;
        }

        using (reply.Value)
        {
            if (reply.Value!.RootElement.ValueKind == JsonValueKind.Object
                && reply.Value.RootElement.TryGetProperty("key", out var key)
                && key.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(key.GetString()))
            {
                sessionKey = key.GetString();
                return SleepIqFailure.None;
            }
        }

        log.Outcome($"sleepiq {SignInLabel}: unexpected shape (no key)");
        return SleepIqFailure.UnexpectedShape;
    }

    public async Task<SleepIqResult<IReadOnlyList<BedSleeper>>> SleepersAsync(CancellationToken cancellationToken)
    {
        var reply = await AuthorizedAsync(SleepersLabel, key => new HttpRequestMessage(HttpMethod.Get, $"sleeper?_k={Uri.EscapeDataString(key)}"), cancellationToken)
            .ConfigureAwait(false);
        if (!reply.IsOk)
        {
            return SleepIqResult<IReadOnlyList<BedSleeper>>.Failed(reply.Failure);
        }

        using (reply.Value)
        {
            var parsed = ParseSleepers(reply.Value!.RootElement, out var reason);
            if (parsed is null)
            {
                log.Outcome($"sleepiq {SleepersLabel}: unexpected shape ({reason})");
                return SleepIqResult<IReadOnlyList<BedSleeper>>.Failed(SleepIqFailure.UnexpectedShape);
            }

            knownSleepers = parsed;
            return SleepIqResult<IReadOnlyList<BedSleeper>>.Ok(parsed);
        }
    }

    public async Task<SleepIqResult<BedNight?>> NightAsync(BedSide side, DateOnly day, CancellationToken cancellationToken)
    {
        var sleeper = knownSleepers?.FirstOrDefault(known => known.Side == side);
        if (sleeper is null)
        {
            var listed = await SleepersAsync(cancellationToken).ConfigureAwait(false);
            if (!listed.IsOk)
            {
                return SleepIqResult<BedNight?>.Failed(listed.Failure);
            }

            sleeper = listed.Value!.FirstOrDefault(known => known.Side == side);
        }

        if (sleeper is null)
        {
            log.Outcome($"sleepiq side: none on the {side.ToString().ToLowerInvariant()}");
            return SleepIqResult<BedNight?>.Failed(SleepIqFailure.NoSuchSide);
        }

        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var reply = await AuthorizedAsync(
            SleepDataLabel,
            key => new HttpRequestMessage(
                HttpMethod.Get,
                $"sleepData?_k={Uri.EscapeDataString(key)}&interval=D1&sleeper={Uri.EscapeDataString(sleeper.SleeperId)}&includeSlices=false&date={date}"),
            cancellationToken).ConfigureAwait(false);
        if (!reply.IsOk)
        {
            return SleepIqResult<BedNight?>.Failed(reply.Failure);
        }

        using (reply.Value)
        {
            var outcome = ParseNight(reply.Value!.RootElement, day, out var asleep, out var reason);
            if (outcome == NightParse.WrongShape)
            {
                log.Outcome($"sleepiq {SleepDataLabel}: unexpected shape ({reason})");
                return SleepIqResult<BedNight?>.Failed(SleepIqFailure.UnexpectedShape);
            }

            return SleepIqResult<BedNight?>.Ok(outcome == NightParse.NoReading ? null : new BedNight(day, asleep));
        }
    }

    // ---- transport ----

    /// <summary>A data call with the session key; on a refusal it signs in once more and tries once more.</summary>
    private async Task<SleepIqResult<JsonDocument>> AuthorizedAsync(
        string label, Func<string, HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            if (sessionKey is null)
            {
                var signedIn = await SignInAsync(cancellationToken).ConfigureAwait(false);
                if (signedIn != SleepIqFailure.None)
                {
                    return SleepIqResult<JsonDocument>.Failed(signedIn);
                }
            }

            var key = sessionKey!;
            var reply = await SendAsync(label, () => build(key), cancellationToken).ConfigureAwait(false);
            if (reply.Failure == SleepIqFailure.SignInRejected && attempt == 0)
            {
                sessionKey = null;
                continue;
            }

            return reply;
        }
    }

    /// <summary>One HTTP exchange, turned into a logged, typed result. The caller owns (disposes) the document.</summary>
    private async Task<SleepIqResult<JsonDocument>> SendAsync(string label, Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        try
        {
            using var request = build();
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            log.Outcome($"sleepiq {label}: {status}");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return SleepIqResult<JsonDocument>.Failed(SleepIqFailure.SignInRejected);
            }

            if (status is 408 or 429 or >= 500)
            {
                return SleepIqResult<JsonDocument>.Failed(SleepIqFailure.Offline);
            }

            if (!response.IsSuccessStatusCode)
            {
                return SleepIqResult<JsonDocument>.Failed(SleepIqFailure.UnexpectedShape);
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return SleepIqResult<JsonDocument>.Ok(JsonDocument.Parse(text));
            }
            catch (JsonException)
            {
                log.Outcome($"sleepiq {label}: unexpected shape (not json)");
                return SleepIqResult<JsonDocument>.Failed(SleepIqFailure.UnexpectedShape);
            }
        }
        catch (HttpRequestException error)
        {
            return Offline(label, error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            return Offline(label, error);
        }
    }

    private SleepIqResult<JsonDocument> Offline(string label, Exception error)
    {
        // The type only: a message can carry the address or a fragment of the request.
        log.Outcome($"sleepiq {label}: offline ({error.GetType().Name})");
        return SleepIqResult<JsonDocument>.Failed(SleepIqFailure.Offline);
    }

    // ---- reading the assumed shapes ----

    private static IReadOnlyList<BedSleeper>? ParseSleepers(JsonElement root, out string reason)
    {
        reason = "no sleepers list";
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("sleepers", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var found = new List<BedSleeper>();
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                reason = "sleeper is not an object";
                return null;
            }

            if (entry.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            var id = ReadId(entry);
            if (id is null)
            {
                reason = "sleeper without an id";
                return null;
            }

            if (!entry.TryGetProperty("side", out var rawSide) || SideFrom(rawSide) is not { } side)
            {
                reason = "sleeper without a known side";
                return null;
            }

            found.Add(new BedSleeper(id, side));
        }

        return found;
    }

    private static string? ReadId(JsonElement entry)
    {
        if (!entry.TryGetProperty("sleeperId", out var id))
        {
            return null;
        }

        var text = id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number => id.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>The side encoding: 0 is Left and 1 is Right, or the words. Anything else is not a side.</summary>
    private static BedSide? SideFrom(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.Number && raw.TryGetInt32(out var number))
        {
            return number switch { 0 => BedSide.Left, 1 => BedSide.Right, _ => null };
        }

        if (raw.ValueKind == JsonValueKind.String)
        {
            var word = raw.GetString();
            if (string.Equals(word, "left", StringComparison.OrdinalIgnoreCase))
            {
                return BedSide.Left;
            }

            if (string.Equals(word, "right", StringComparison.OrdinalIgnoreCase))
            {
                return BedSide.Right;
            }
        }

        return null;
    }

    private enum NightParse
    {
        WrongShape,
        NoReading,
        Reading,
    }

    private static NightParse ParseNight(JsonElement root, DateOnly day, out TimeSpan asleep, out string reason)
    {
        asleep = TimeSpan.Zero;
        reason = "no sleepData list";
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("sleepData", out var sleepData)
            || sleepData.ValueKind != JsonValueKind.Array)
        {
            return NightParse.WrongShape;
        }

        foreach (var sleeper in sleepData.EnumerateArray())
        {
            if (sleeper.ValueKind != JsonValueKind.Object
                || !sleeper.TryGetProperty("days", out var days)
                || days.ValueKind != JsonValueKind.Array)
            {
                reason = "no days list";
                return NightParse.WrongShape;
            }

            foreach (var record in days.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object
                    || !record.TryGetProperty("date", out var rawDate)
                    || rawDate.ValueKind != JsonValueKind.String
                    || !DateOnly.TryParseExact(rawDate.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var recordDay))
                {
                    reason = "day without a date";
                    return NightParse.WrongShape;
                }

                if (recordDay != day)
                {
                    continue;
                }

                if (!record.TryGetProperty("totalSleepSessionTime", out var seconds)
                    || seconds.ValueKind != JsonValueKind.Number
                    || !seconds.TryGetDouble(out var total)
                    || total < 0)
                {
                    reason = "day without a sleep time";
                    return NightParse.WrongShape;
                }

                if (total == 0)
                {
                    return NightParse.NoReading;
                }

                asleep = TimeSpan.FromSeconds(total);
                return NightParse.Reading;
            }
        }

        return NightParse.NoReading;
    }
}
