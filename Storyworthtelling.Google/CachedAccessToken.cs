namespace Storyworthtelling.Google;

/// <summary>
/// The device-account access token, reused until shortly before it expires (audit 01 §10). Every app kept a copy of
/// this lock-and-cache. A fresh fetch is handed the stale token so the platform can be told to forget it first,
/// and a failed fetch caches nothing. The fetch itself (Android's AccountManager) is the caller's.
/// </summary>
public sealed class CachedAccessToken(TimeSpan? lifetime = null, Func<DateTimeOffset>? clock = null)
{
    /// <summary>Google issues an hour; this leaves a margin so a call never starts on a token about to lapse.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(45);

    private readonly Lock gate = new();
    private readonly TimeSpan keepFor = lifetime ?? DefaultLifetime;
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    private string? token;
    private DateTimeOffset expires;

    /// <param name="fetch">Blocking; given the stale token (or null) and returns a fresh one, or throws.</param>
    public Task<string> GetAsync(Func<string?, string> fetch)
    {
        lock (gate)
        {
            if (token is not null && now() < expires)
            {
                return Task.FromResult(token);
            }
        }

        return Task.Run(() =>
        {
            string? stale;
            lock (gate)
            {
                stale = token;
            }

            var fresh = fetch(stale);
            lock (gate)
            {
                token = fresh;
                expires = now() + keepFor;
            }

            return fresh;
        });
    }
}
