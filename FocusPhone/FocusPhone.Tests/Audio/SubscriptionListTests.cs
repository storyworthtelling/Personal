using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class SubscriptionListTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Add_AppendsInOrder()
    {
        var list = new SubscriptionList();

        list.Add("https://a.com/feed", "A", Now);
        list.Add("https://b.com/feed", "B", Now);

        Assert.Equal(["A", "B"], list.Ordered.Select(subscription => subscription.Title).ToArray());
    }

    [Fact]
    public void Add_DedupesByFeedUrl()
    {
        var list = new SubscriptionList();

        list.Add("https://a.com/feed", "A", Now);
        list.Add("https://a.com/feed", "A again", Now);

        Assert.Single(list.Ordered);
    }

    [Fact]
    public void Remove_DropsTheShow()
    {
        var list = new SubscriptionList();
        list.Add("https://a.com/feed", "A", Now);
        list.Add("https://b.com/feed", "B", Now);

        list.Remove("https://a.com/feed");

        Assert.Equal("B", Assert.Single(list.Ordered).Title);
    }

    [Fact]
    public void Move_ReordersShows()
    {
        var list = new SubscriptionList();
        list.Add("https://a.com/feed", "A", Now);
        list.Add("https://b.com/feed", "B", Now);
        list.Add("https://c.com/feed", "C", Now);

        list.Move(0, 2);

        Assert.Equal(["B", "C", "A"], list.Ordered.Select(subscription => subscription.Title).ToArray());
    }

    [Fact]
    public void WithUnplayed_HidesShowsWithNothingUnplayed()
    {
        var list = new SubscriptionList();
        list.Add("https://a.com/feed", "A", Now);
        list.Add("https://b.com/feed", "B", Now);

        var visible = list.WithUnplayed(new Dictionary<string, int>
        {
            ["https://a.com/feed"] = 0,
            ["https://b.com/feed"] = 3,
        });

        Assert.Equal("B", Assert.Single(visible).Title);
    }

    [Fact]
    public void WithUnplayed_HidesShowsMissingFromTheCount()
    {
        var list = new SubscriptionList();
        list.Add("https://a.com/feed", "A", Now);

        Assert.Empty(list.WithUnplayed(new Dictionary<string, int>()));
    }

    [Fact]
    public void Order_SurvivesExportAndRestore()
    {
        var list = new SubscriptionList();
        list.Add("https://a.com/feed", "A", Now);
        list.Add("https://b.com/feed", "B", Now);
        list.Move(0, 1);

        var restored = new SubscriptionList(list.Export());

        Assert.Equal(["B", "A"], restored.Ordered.Select(subscription => subscription.Title).ToArray());
    }
}
