using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

public class GroupNamesTests
{
    private const string Key = "group:mms:17";

    [Fact]
    public void KeyFor_IsTheMergersThreadKey()
    {
        var thread = Assert.Single(new TimelineMerger().Merge(
        [
            new TimelineItem(TimelineItemKind.Message, "5550100001", DateTimeOffset.UnixEpoch, "a", true, "mms:1")
            {
                GroupId = "mms:17",
            },
        ]));

        Assert.Equal(thread.ContactKey, GroupNames.KeyFor("mms:17"));
    }

    [Fact]
    public void Unnamed_HasNoTitle()
    {
        Assert.Null(GroupNames.Empty.TitleFor(Key));
    }

    [Fact]
    public void OwnersName_WinsOverTheLearnedOne()
    {
        var names = GroupNames.Empty.Learn(Key, "Avery, Blake").Name(Key, "Book club");

        Assert.Equal("Book club", names.TitleFor(Key));
        Assert.Equal("Book club", names.OwnersNameFor(Key));
    }

    [Fact]
    public void LearnedName_IsUsedUntilTheOwnerNamesIt()
    {
        var names = GroupNames.Empty.Learn(Key, "Weekend plans");

        Assert.Equal("Weekend plans", names.TitleFor(Key));
        Assert.Null(names.OwnersNameFor(Key));
    }

    [Fact]
    public void BlankName_ClearsTheOwnersName()
    {
        var names = GroupNames.Empty.Learn(Key, "Weekend plans").Name(Key, "Book club").Name(Key, "   ");

        Assert.Equal("Weekend plans", names.TitleFor(Key));
    }

    [Fact]
    public void Name_IsTrimmedAndKeptToOneLine()
    {
        var names = GroupNames.Empty.Name(Key, "  Book\tclub\n ");

        Assert.Equal("Book club", names.TitleFor(Key));
    }

    [Fact]
    public void LongName_IsCut()
    {
        var names = GroupNames.Empty.Name(Key, new string('a', 200));

        Assert.Equal(GroupNames.MaxLength, names.TitleFor(Key)!.Length);
    }

    [Fact]
    public void Learn_ReportsWhetherAnythingChanged()
    {
        var names = GroupNames.Empty.Learn(Key, "Weekend plans");

        Assert.Same(names, names.Learn(Key, "Weekend plans"));
        Assert.Same(names, names.Learn(Key, "  "));
        Assert.NotSame(names, names.Learn(Key, "Weekend plans 2"));
    }

    [Fact]
    public void StoredForm_RoundTrips()
    {
        var names = GroupNames.Empty
            .Name(Key, "Book club")
            .Learn("group:rcs:conv-1", "Weekend plans")
            .Name("group:rcs:conv-1", "Away team");

        var restored = GroupNames.From(names.NamedStored(), names.LearnedStored());

        Assert.Equal("Book club", restored.TitleFor(Key));
        Assert.Equal("Away team", restored.TitleFor("group:rcs:conv-1"));
        Assert.Null(restored.Name("group:rcs:conv-1", "").OwnersNameFor("group:rcs:conv-1"));
        Assert.Equal("Weekend plans", restored.Name("group:rcs:conv-1", "").TitleFor("group:rcs:conv-1"));
    }

    // A key with a tab in it would split wrongly on the way back in; garbled entries are
    // skipped rather than thrown on, since one bad row must not cost every other name.
    [Fact]
    public void GarbledStoredEntries_AreSkipped()
    {
        var restored = GroupNames.From(["no separator", "\tno key", $"{Key}\tBook club"], []);

        Assert.Equal("Book club", restored.TitleFor(Key));
    }
}
