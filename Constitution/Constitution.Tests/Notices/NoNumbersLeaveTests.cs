using System.Text.RegularExpressions;
using Constitution.Domain.Notices;

namespace Constitution.Tests.Notices;

/// <summary>
/// C-7.4.3 — no health number reaches Flyleaf's home, and C-9.1.3's head half: every notification
/// Constitution posts is built from <see cref="NoticeNotifications"/>'s fixed words alone, so no number
/// can get into one, and the permission to post is declared. Reads the head's source by content because
/// the head cannot run here.
/// </summary>
public class NoNumbersLeaveTests
{
    private static readonly string[] NumberWords =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "hundred", "percent",
    ];

    private static string PosterSource()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "Waiting", "NoticePoster.cs");
        Assert.True(File.Exists(path), "Constitution.Android/Waiting/NoticePoster.cs was not found.");
        return File.ReadAllText(path);
    }

    [Fact]
    public void NoticesCarryNoDigits()
    {
        Assert.NotEmpty(NoticeNotifications.ShortList);
        foreach (var kind in NoticeNotifications.ShortList)
        {
            var note = NoticeNotifications.For(kind)!;
            var words = Regex.Split((note.Title + " " + note.Text).ToLowerInvariant(), @"[^a-z]+");
            Assert.DoesNotContain(note.Title + note.Text, char.IsDigit);
            Assert.DoesNotContain(note.Title + note.Text, char.IsNumber);
            Assert.Empty(words.Intersect(NumberWords));
        }
    }

    [Fact]
    public void ThePosterBuildsFromThePlanAndNothingElse()
    {
        var source = PosterSource();
        Assert.Contains("NoticeNotifications.Plan(", source, StringComparison.Ordinal);
        Assert.Contains("NoticeNotifications.RoleExtraKey", source, StringComparison.Ordinal);
        Assert.Contains("NoticeNotifications.ChannelId", source, StringComparison.Ordinal);
        Assert.Contains("SetContentTitle(note.Title)", source, StringComparison.Ordinal);
        Assert.Contains("SetContentText(note.Text)", source, StringComparison.Ordinal);

        // No interpolation, no concatenation into the words, no count, no badge number.
        Assert.DoesNotContain("$\"", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"SetContent(?:Title|Text)\([^)]*\+", source);
        Assert.DoesNotContain("SetNumber(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetSubText(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetContentInfo(", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Count", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePosterPutsTheRoleOnTheNotificationItself()
    {
        Assert.Matches(@"PutString\(\s*NoticeNotifications\.RoleExtraKey\s*,\s*note\.RoleWire\s*\)", PosterSource());
    }

    [Fact]
    public void OnlyThePosterPostsNotifications()
    {
        var others = ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .Where(file => Path.GetFileName(file) != "NoticePoster.cs")
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\bNotificationManager\b|\bNotification\.Builder\b"))
            .Select(ConstitutionSource.Relative)
            .ToList();
        Assert.Empty(others);
    }

    [Fact]
    public void PostingIsPermittedAndTheBoardIsWatched()
    {
        var permissions = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "ManifestPermissions.cs"));
        Assert.Contains("android.permission.POST_NOTIFICATIONS", permissions, StringComparison.Ordinal);

        var board = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "Settings", "ConnectionNoticeBoard.cs"));
        Assert.Contains("public static void Observe(", board, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryEntryPointAttachesThePoster()
    {
        // The board is the process's; a worker that raises a notice in a process the shell never opened would
        // otherwise raise it to nobody.
        foreach (var relative in new[]
        {
            "ConstitutionActivity.cs", "Body/BodySyncWorker.cs", "MealWeek/MealWeekWorker.cs",
            "ReadBack/ReadBackWorker.cs", "WorkoutWeek/WorkoutWeekWorker.cs",
        })
        {
            var source = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", relative));
            Assert.Contains("NoticePoster.Attach(", source, StringComparison.Ordinal);
        }
    }
}
