using Xunit;
using Foreword.Infrastructure.Vault;
using Storyworthtelling.Voice.Secrets;

namespace Foreword.Tests.Vault;

// Duplication audit §2: Foreword wrote its NYT keys, the reMarkable token and the household's Maps section into
// the shared vault unconditionally, so an install that had not paired the tablet or set the address wrote blanks
// over what another app (or the other phone) published. It now follows VaultMerge.Pick.
public class ForewordVaultSectionTests
{
    private static readonly SecretsVault Published = SecretsVault.Empty with
    {
        Nyt = new NytSecrets("cookie-in-vault", "nyt-key-in-vault"),
        Remarkable = new RemarkableSecrets("rm-from-folio"),
        Foreword = new ForewordSecrets("anthropic-in-vault", "{\"mapping\":1}"),
        Maps = new MapsSecrets("maps-in-vault", "1 Example Street", 39.78, -89.65),
    };

    private static readonly ForewordVaultSection.Local Blank = new("", "", "", "", "", "", 0, 0);

    [Fact]
    public void BlanksOnAFreshInstallLeaveEverySharedValueAlone()
    {
        var merged = ForewordVaultSection.Merge(Published, Blank, [], new Dictionary<string, string>());

        Assert.Equal(Published.Nyt.SessionCookie, merged.Nyt.SessionCookie);
        Assert.Equal(Published.Nyt.ApiKey, merged.Nyt.ApiKey);
        Assert.Equal("rm-from-folio", merged.Remarkable.DeviceToken);
        Assert.Equal("anthropic-in-vault", merged.Foreword.AnthropicApiKey);
        Assert.Equal("maps-in-vault", merged.Maps.ApiKey);
        Assert.Equal("1 Example Street", merged.Maps.HomeAddress);
        Assert.Equal(39.78, merged.Maps.HomeLatitude);
    }

    [Fact]
    public void TheCalendarMappingInTheSharedSectionIsNeverTouched()
    {
        var merged = ForewordVaultSection.Merge(Published, Blank, [ForewordVaultSection.Anthropic], new Dictionary<string, string>());

        Assert.Equal("{\"mapping\":1}", merged.Foreword.CalendarMappingJson);
    }

    [Fact]
    public void WhatWasJustChangedHereReplacesTheVaultsAndNothingElseDoes()
    {
        var local = new ForewordVaultSection.Local("cookie-new", "nyt-key-stale", "rm-new", "anthropic-stale", "maps-stale", "2 Example Road", 40.1, -88.2);

        var merged = ForewordVaultSection.Merge(
            Published, local, [ForewordVaultSection.NytCookie, ForewordVaultSection.Remarkable, ForewordVaultSection.Home], new Dictionary<string, string>());

        Assert.Equal("cookie-new", merged.Nyt.SessionCookie);
        Assert.Equal("nyt-key-in-vault", merged.Nyt.ApiKey);
        Assert.Equal("rm-new", merged.Remarkable.DeviceToken);
        Assert.Equal("anthropic-in-vault", merged.Foreword.AnthropicApiKey);
        Assert.Equal("maps-in-vault", merged.Maps.ApiKey);
        Assert.Equal("2 Example Road", merged.Maps.HomeAddress);
        Assert.Equal(40.1, merged.Maps.HomeLatitude);
        Assert.Equal(-88.2, merged.Maps.HomeLongitude);
    }
}
