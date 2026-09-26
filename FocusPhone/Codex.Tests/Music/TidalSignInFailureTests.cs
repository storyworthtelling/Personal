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

        // The point is that it names TIDAL's refusal rather than the network, and does
        // not offer a retry. The exact wording moved once already and may again.
        Assert.Contains("TIDAL", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reach", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", said, StringComparison.OrdinalIgnoreCase);
    }

    // The first version of this sent the owner to the developer portal to enable the
    // flow. There is nothing there to enable — TIDAL's Android SDK documents the
    // device flow as "(Only available for TIDAL internally developed applications for
    // now)" — so that text sent them looking for a setting that does not exist.
    [Fact]
    public void Describe_DoesNotSendYouHuntingForASettingThatDoesNotExist()
    {
        var said = TidalSignInFailure.Describe("invalid_request — Client is not a Limited Input Device client");

        Assert.DoesNotContain("portal", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("own apps", said, StringComparison.OrdinalIgnoreCase);
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

    // IsCredentialRejection is the seam TidalPairingActivity uses to decide whether a failure
    // is worth flagging on the credential health seam (Settings' "TIDAL rejected the app's
    // credentials" line) — only a genuinely bad client id/secret should flip that, not TIDAL
    // refusing a grant type or scope the app asked for.
    [Fact]
    public void IsCredentialRejection_TrueForInvalidOrUnauthorizedClient()
    {
        Assert.True(TidalSignInFailure.IsCredentialRejection("400 BadRequest: invalid_client — client authentication failed"));
        Assert.True(TidalSignInFailure.IsCredentialRejection("unauthorized_client"));
    }

    [Fact]
    public void IsCredentialRejection_FalseForEverythingElse()
    {
        Assert.False(TidalSignInFailure.IsCredentialRejection("invalid_request — Client is not a Limited Input Device client"));
        Assert.False(TidalSignInFailure.IsCredentialRejection("400 BadRequest: invalid_scope — w_sub not permitted"));
        Assert.False(TidalSignInFailure.IsCredentialRejection("No such host is known (auth.tidal.com:443)"));
        Assert.False(TidalSignInFailure.IsCredentialRejection(null));
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

    // The refusal the owner actually hit happens on TIDAL's page, where Score cannot see it, so
    // the waiting sentence is the only place to say what fixes it: the two settings on the app
    // in TIDAL's developer portal that the page checks before it shows a password box.
    [Fact]
    public void WhileAtTidal_SaysWhatAnErrorOnTidalsPageMeans()
    {
        var said = TidalSignInFailure.WhileAtTidal;

        Assert.Contains("redirect", said, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scopes", said, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("try again", said, StringComparison.OrdinalIgnoreCase);
    }
}
