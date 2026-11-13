namespace Codex.Infrastructure.Music;

/// <summary>
/// The last TIDAL access token, kept until a minute before it expires (efficiency audit §3). Every search, pick and
/// saved track used to open the vault and refresh the token; now only the first does until the token runs out.
/// Only a successful refresh with a lifetime is kept. Thread-safe.
/// </summary>
public sealed class TidalTokenCache
{
    /// <summary>A token is not handed out in its last minute, so a call made with it does not expire mid-flight.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(1);

    private readonly object gate = new();
    private TidalTokenPollResult? token;
    private DateTimeOffset usableUntil;

    public TidalTokenPollResult? Current(DateTimeOffset now)
    {
        lock (gate)
        {
            return token is not null && now < usableUntil ? token : null;
        }
    }

    public void Keep(TidalTokenPollResult result, DateTimeOffset now)
    {
        if (result.Status != TidalTokenPollStatus.Success || string.IsNullOrEmpty(result.AccessToken) || result.ExpiresIn <= 0)
        {
            return;
        }

        lock (gate)
        {
            token = result;
            usableUntil = now.AddSeconds(result.ExpiresIn) - Margin;
        }
    }

    public void Forget()
    {
        lock (gate)
        {
            token = null;
        }
    }
}
