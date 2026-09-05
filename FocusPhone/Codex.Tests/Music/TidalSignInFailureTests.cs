using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TidalSignInFailureTests
{
    // The real one, from the device: TIDAL refuses the device-code grant because the
    // app in the developer portal is not registered for it. The screen said "Couldn't
    // reach TIDAL. Try again in a moment.", which is wrong twice — TIDAL was reached,
    // and no amount of trying again will change a registration.
    [Fact]
    public void Describe_NamesTheRegistrationProblemRatherThanBlamingTheNetwork()
    {
        var said = TidalSignInFailure.Describe(
            "TIDAL device_authorization refused the request with 400 BadRequest: " +
            "invalid_request — Client is not a Limited Input Device client");

        Assert.Contains("isn't registered", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_TellsYouWhereTheFixIs()
    {
        var said = TidalSignInFailure.Describe("invalid_request — Client is not a Limited Input Device client");

        Assert.Contains("developer", said, StringComparison.OrdinalIgnoreCase);
    }

    // Wrong credentials are the owner's to fix too, and are a different fix.
    [Fact]
    public void Describe_SeparatesARejectedClientFromAnUnregisteredOne()
    {
        var said = TidalSignInFailure.Describe("400 BadRequest: invalid_client — client authentication failed");

        Assert.Contains("credentials", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_SeparatesAScopeTheClientMayNotHave()
    {
        var said = TidalSignInFailure.Describe("400 BadRequest: invalid_scope — w_sub not permitted");

        Assert.Contains("permission", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", said, StringComparison.OrdinalIgnoreCase);
    }

    // A genuine network failure IS worth retrying, and must keep saying so.
    [Fact]
    public void Describe_StillOffersARetryWhenTidalWasNotReached()
    {
        var said = TidalSignInFailure.Describe("No such host is known (auth.tidal.com:443)");

        Assert.Contains("try again", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_OnNothingUseful_StillOffersARetryRatherThanADeadEnd()
    {
        Assert.Contains("try again", TidalSignInFailure.Describe(null), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("try again", TidalSignInFailure.Describe("  "), StringComparison.OrdinalIgnoreCase);
    }

    // Whatever it says, it is one plain sentence on a settings screen — not a stack
    // trace. The trace is in Diagnostics, which is where a trace belongs.
    [Fact]
    public void Describe_IsAlwaysOneShortSentence()
    {
        foreach (var message in new[]
        {
            "invalid_request — Client is not a Limited Input Device client",
            "invalid_client — client authentication failed",
            "invalid_scope — nope",
            "No such host is known",
            null,
        })
        {
            var said = TidalSignInFailure.Describe(message);

            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.True(said.Length <= 140, $"{said.Length} chars: {said}");
            Assert.DoesNotContain("\n", said);
        }
    }
}
