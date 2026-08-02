using FocusPhone.Domain.Audio;
using Xunit;

namespace FocusPhone.Tests.Audio;

public class OpmlImportSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static OpmlSubscription Sub(string title, string url) => new(title, url);

    [Fact]
    public void Items_AllNew_AreCheckedByDefault()
    {
        var selection = new OpmlImportSelection(
            [Sub("A", "https://a.com/feed"), Sub("B", "https://b.com/feed")],
            new SubscriptionList());

        Assert.Equal(["A", "B"], selection.Items.Select(item => item.Title).ToArray());
        Assert.All(selection.Items, item => Assert.False(item.AlreadySubscribed));
        Assert.All(selection.Items, item => Assert.True(item.Checked));
    }

    [Fact]
    public void Items_AlreadySubscribed_AreFlaggedAndUnchecked()
    {
        var existing = new SubscriptionList();
        existing.Add("https://a.com/feed", "A", Now);

        var selection = new OpmlImportSelection(
            [Sub("A", "https://a.com/feed"), Sub("B", "https://b.com/feed")],
            existing);

        var a = selection.Items.Single(item => item.FeedUrl == "https://a.com/feed");
        var b = selection.Items.Single(item => item.FeedUrl == "https://b.com/feed");
        Assert.True(a.AlreadySubscribed);
        Assert.False(a.Checked);
        Assert.False(b.AlreadySubscribed);
        Assert.True(b.Checked);
    }

    [Fact]
    public void Items_DedupesDuplicateFeedUrlsInTheOpml()
    {
        var selection = new OpmlImportSelection(
            [Sub("A", "https://a.com/feed"), Sub("A copy", "https://a.com/feed")],
            new SubscriptionList());

        var item = Assert.Single(selection.Items);
        Assert.Equal("A", item.Title);
    }

    [Fact]
    public void Items_PreservesOpmlOrder()
    {
        var selection = new OpmlImportSelection(
            [Sub("C", "https://c.com/feed"), Sub("A", "https://a.com/feed"), Sub("B", "https://b.com/feed")],
            new SubscriptionList());

        Assert.Equal(["C", "A", "B"], selection.Items.Select(item => item.Title).ToArray());
    }

    [Fact]
    public void Apply_AddsOnlySelectedNewFeeds()
    {
        var selection = new OpmlImportSelection(
            [Sub("A", "https://a.com/feed"), Sub("B", "https://b.com/feed"), Sub("C", "https://c.com/feed")],
            new SubscriptionList());

        var result = selection.Apply(["https://a.com/feed", "https://c.com/feed"], Now);

        Assert.Equal(["A", "C"], result.Ordered.Select(subscription => subscription.Title).ToArray());
    }

    [Fact]
    public void Apply_KeepsExistingSubscriptionsAndAppendsNew()
    {
        var existing = new SubscriptionList();
        existing.Add("https://a.com/feed", "A", Now);

        var selection = new OpmlImportSelection(
            [Sub("A", "https://a.com/feed"), Sub("B", "https://b.com/feed")],
            existing);

        var result = selection.Apply(["https://b.com/feed"], Now);

        Assert.Equal(["A", "B"], result.Ordered.Select(subscription => subscription.Title).ToArray());
    }

    [Fact]
    public void Apply_SelectingAnAlreadySubscribedFeed_IsANoOp()
    {
        var existing = new SubscriptionList();
        existing.Add("https://a.com/feed", "A", Now);

        var selection = new OpmlImportSelection([Sub("A", "https://a.com/feed")], existing);

        var result = selection.Apply(["https://a.com/feed"], Now);

        var only = Assert.Single(result.Ordered);
        Assert.Equal("A", only.Title);
    }

    [Fact]
    public void Apply_NothingSelected_LeavesTheListUnchanged()
    {
        var existing = new SubscriptionList();
        existing.Add("https://a.com/feed", "A", Now);

        var selection = new OpmlImportSelection(
            [Sub("A", "https://a.com/feed"), Sub("B", "https://b.com/feed")],
            existing);

        var result = selection.Apply([], Now);

        var only = Assert.Single(result.Ordered);
        Assert.Equal("A", only.Title);
    }

    [Fact]
    public void Apply_DoesNotMutateTheOriginalList()
    {
        var existing = new SubscriptionList();
        existing.Add("https://a.com/feed", "A", Now);

        var selection = new OpmlImportSelection([Sub("B", "https://b.com/feed")], existing);

        selection.Apply(["https://b.com/feed"], Now);

        Assert.Single(existing.Ordered);
    }
}
