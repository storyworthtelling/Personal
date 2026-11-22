namespace Watch.Domain.Keys;

/// <summary>
/// The one token a watch app holds (plan §2.4, P14 W-4.1.2), in memory and nowhere else: nothing here hands it out as text
/// or bytes, so there is nothing to write down, and a process that dies with it simply asks again. It is let go the moment
/// its life is over, when the phone says it has none, and when Google refuses it. The app asks when it needs one
/// (<see cref="AskDue"/>), at most once a minute, rather than the phone pushing one on a schedule: a token is on the wrist
/// only while something there is using it. Safe from more than one thread.
/// </summary>
public sealed class TokenHold
{
    private readonly Lock gate = new();
    private AccessToken? token;
    private DateTimeOffset until;
    private DateTimeOffset? lastAsk;

    /// <summary>Holds <paramref name="handoff"/>'s token for its life from <paramref name="now"/>, in place of any before it.</summary>
    public KeyOutcome Take(TokenHandoff handoff, DateTimeOffset now)
    {
        lock (gate)
        {
            token = handoff.Token;
            until = now + handoff.Life;
            return KeyOutcome.Held;
        }
    }

    /// <summary>The phone has no token: whatever is held goes too, since what refused the phone refuses the wrist.</summary>
    public KeyOutcome Refused(TokenNone none)
    {
        lock (gate)
        {
            token = null;
            return none.Why == TokenRefusal.SignIn ? KeyOutcome.SignIn : KeyOutcome.Failed;
        }
    }

    /// <summary>The token to call with, or null; past its life it is let go, not only hidden.</summary>
    public AccessToken? Current(DateTimeOffset now)
    {
        lock (gate)
        {
            if (token is not null && now >= until)
            {
                token = null;
            }
            return token;
        }
    }

    /// <summary>
    /// Lets go of <paramref name="rejected"/> if it is the token held, as Google refused it. A token handed over since is
    /// kept. Whether it was let go.
    /// </summary>
    public bool Forget(AccessToken rejected)
    {
        lock (gate)
        {
            if (token is null || !token.Equals(rejected))
            {
                return false;
            }
            token = null;
            return true;
        }
    }

    /// <summary>Whether to ask the phone now: none held, or the one held nearly done, and no ask in the last minute.</summary>
    public bool AskDue(DateTimeOffset now)
    {
        lock (gate)
        {
            var needs = token is null || now >= until - KeyLimits.RenewBefore;
            var spaced = lastAsk is not { } asked || now - asked >= KeyLimits.AskSpacing;
            return needs && spaced;
        }
    }

    /// <summary>An ask went to the phone at <paramref name="now"/>.</summary>
    public void Asked(DateTimeOffset now)
    {
        lock (gate)
        {
            lastAsk = now;
        }
    }

    public override string ToString()
    {
        lock (gate)
        {
            return token is null ? "TokenHold { none }" : "TokenHold { [access token] }";
        }
    }
}
