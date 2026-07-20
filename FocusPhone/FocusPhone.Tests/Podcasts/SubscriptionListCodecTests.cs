using FocusPhone.Domain.Audio;
using FocusPhone.Infrastructure.Podcasts;
using Xunit;

namespace FocusPhone.Tests.Podcasts;

public class SubscriptionListCodecTests
{
    private static readonly DateTimeOffset When = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EncodeThenDecode_RoundTripsSubscriptions()
    {
        var list = new SubscriptionList();
        list.Add("https://a.com/feed", "Show A", When);
        list.Add("https://b.com/feed", "Show B", When);

        var decoded = SubscriptionListCodec.Decode(SubscriptionListCodec.Encode(list.Export()));

        Assert.Equal(list.Export(), decoded);
    }

    [Fact]
    public void Decoded_RebuildsAWorkingSubscriptionList_PreservingOrder()
    {
        var list = new SubscriptionList();
        list.Add("https://a.com/feed", "Show A", When);
        list.Add("https://b.com/feed", "Show B", When);
        list.Move(0, 1);

        var restored = new SubscriptionList(SubscriptionListCodec.Decode(SubscriptionListCodec.Encode(list.Export())));

        Assert.Equal(["Show B", "Show A"], restored.Ordered.Select(subscription => subscription.Title).ToArray());
    }

    [Fact]
    public void Decode_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(SubscriptionListCodec.Decode(null));
        Assert.Empty(SubscriptionListCodec.Decode(""));
    }
}
