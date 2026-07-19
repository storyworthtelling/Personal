using FocusPhone.Domain.Timeline;
using Xunit;

namespace FocusPhone.Tests.Timeline;

public class IdentityResolverTests
{
    private const string Phone = "5551234567";
    private const string Email = "alice@example.com";
    private const string OtherPhone = "5559998888";

    [Fact]
    public void PerKeyResolver_ResolvesEveryKeyToItself()
    {
        Assert.Equal(Phone, PerKeyIdentityResolver.Instance.ResolveThreadKey(Phone));
        Assert.Equal(Email, PerKeyIdentityResolver.Instance.ResolveThreadKey(Email));
    }

    [Fact]
    public void Association_MergesPhoneAndEmailIntoOneIdentity()
    {
        var map = new IdentityMap(
            associations: [[Phone, Email]],
            manualLinks: [],
            standaloneKeys: []);

        Assert.Equal(map.ResolveThreadKey(Phone), map.ResolveThreadKey(Email));
    }

    [Fact]
    public void UnrelatedKeys_ResolveSeparately()
    {
        var map = new IdentityMap([[Phone, Email]], [], []);

        Assert.NotEqual(map.ResolveThreadKey(Phone), map.ResolveThreadKey(OtherPhone));
    }

    [Fact]
    public void CanonicalId_IsDeterministicRegardlessOfInputOrder()
    {
        var forward = new IdentityMap([[Phone, Email]], [], []);
        var reversed = new IdentityMap([[Email, Phone]], [], []);

        Assert.Equal(forward.ResolveThreadKey(Phone), reversed.ResolveThreadKey(Phone));
        Assert.Equal(forward.ResolveThreadKey(Email), reversed.ResolveThreadKey(Email));
    }

    [Fact]
    public void ManualLink_MergesKeysNotSharingAContactCard()
    {
        var map = new IdentityMap(
            associations: [],
            manualLinks: [(Phone, Email)],
            standaloneKeys: []);

        Assert.Equal(map.ResolveThreadKey(Phone), map.ResolveThreadKey(Email));
    }

    [Fact]
    public void ManualLink_IsTransitive()
    {
        var map = new IdentityMap(
            associations: [],
            manualLinks: [(Phone, Email), (Email, OtherPhone)],
            standaloneKeys: []);

        Assert.Equal(map.ResolveThreadKey(Phone), map.ResolveThreadKey(OtherPhone));
    }

    [Fact]
    public void Unlink_PinsKeyToItself_EvenWhenAContactCardAssociatesIt()
    {
        var map = new IdentityMap(
            associations: [[Phone, Email]],
            manualLinks: [],
            standaloneKeys: [Email]);

        Assert.Equal(Email, map.ResolveThreadKey(Email));
        Assert.NotEqual(map.ResolveThreadKey(Phone), map.ResolveThreadKey(Email));
    }

    [Fact]
    public void Unlink_WinsOverAManualLink()
    {
        var map = new IdentityMap(
            associations: [],
            manualLinks: [(Phone, Email)],
            standaloneKeys: [Email]);

        Assert.NotEqual(map.ResolveThreadKey(Phone), map.ResolveThreadKey(Email));
    }
}
