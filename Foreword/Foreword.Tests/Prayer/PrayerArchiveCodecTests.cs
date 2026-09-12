using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// The shape of the private archive on Drive, pinned before a single morning has written one.
///
/// <para>This is the same lesson as the calendar mapping and the stored task: the file's field
/// names <i>are</i> the contract, a rename deserialises to an empty default rather than failing,
/// and the loss is silent. Here the loss is worse than a setting — it is the only copy of what the
/// owner wrote, because the journal page is trashed off the tablet once it has been read.</para>
///
/// <para>So the load-bearing case is written as the document as it would sit on Drive, not as a
/// round trip. A round trip passes happily with both ends renamed together, which is precisely the
/// bug.</para>
/// </summary>
public class PrayerArchiveCodecTests
{
    private sealed class Reader : IPrayerReader
    {
    }

    private static readonly Reader Permitted = new();

    private const string OnDrive = """
        {
          "requests": [
            {
              "id": "dad-scan",
              "text": "Dad — the scan on the 14th",
              "held": "2026-08-28",
              "answered": null,
              "strikePrinted": false
            },
            {
              "id": "team",
              "text": "Work — the decision about the team",
              "held": "2026-08-25",
              "answered": "2026-09-05",
              "strikePrinted": true
            }
          ],
          "journal": [
            {
              "date": "2026-09-05",
              "text": "Asked for patience with the mornings, and got a week of them."
            }
          ]
        }
        """;

    [Fact]
    public void TheDocumentAlreadyOnDriveIsReadExactlyAsItSits()
    {
        var archive = PrayerArchiveCodec.Read(OnDrive);

        Assert.Equal(2, archive.Requests.Count);

        var dad = archive.Requests[0];
        Assert.Equal("dad-scan", dad.Id);
        Assert.Equal("Dad — the scan on the 14th", dad.Text.ReadBy(Permitted));
        Assert.Equal(new DateOnly(2026, 8, 28), dad.Held);
        Assert.Null(dad.Answered);
        Assert.False(dad.StrikePrinted);

        var team = archive.Requests[1];
        Assert.Equal(new DateOnly(2026, 9, 5), team.Answered);
        Assert.True(team.StrikePrinted);

        var entry = Assert.Single(archive.Journal);
        Assert.Equal(new DateOnly(2026, 9, 5), entry.Date);
        Assert.Equal(
            "Asked for patience with the mornings, and got a week of them.",
            entry.Text.ReadBy(Permitted));
    }

    /// <summary>
    /// And what it writes is readable by the version above — so an old build and a new one can both
    /// open the same file, which is the only property that matters on a file nobody backs up
    /// separately.
    /// </summary>
    [Fact]
    public void WhatItWritesItReadsBack()
    {
        var original = PrayerArchiveCodec.Read(OnDrive);

        var again = PrayerArchiveCodec.Read(PrayerArchiveCodec.Write(original, Permitted));

        Assert.Equal(
            original.Requests.Select(request => request.Text.ReadBy(Permitted)),
            again.Requests.Select(request => request.Text.ReadBy(Permitted)));
        Assert.Equal(
            original.Requests.Select(request => request.Answered),
            again.Requests.Select(request => request.Answered));
        Assert.Equal(
            original.Journal.Select(entry => entry.Date),
            again.Journal.Select(entry => entry.Date));
    }

    /// <summary>
    /// The field names are written out here rather than trusted to a naming policy, because the
    /// policy is a setting and the file is forever.
    /// </summary>
    [Fact]
    public void TheFieldNamesAreTheOnesAlreadyOnDrive()
    {
        var json = PrayerArchiveCodec.Write(PrayerArchiveCodec.Read(OnDrive), Permitted);

        foreach (var name in new[]
            { "\"requests\"", "\"journal\"", "\"id\"", "\"text\"", "\"held\"", "\"answered\"", "\"strikePrinted\"", "\"date\"" })
        {
            Assert.Contains(name, json);
        }
    }

    /// <summary>
    /// An absent file is an empty archive, and a garbled one is an empty archive too — the same
    /// choice the other stores in this suite make. A first morning and a corrupted morning both
    /// produce a page with nothing held, rather than an exception inside a delivery.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public void NothingUsableIsAnEmptyArchive(string content)
    {
        var archive = PrayerArchiveCodec.Read(content);

        Assert.Empty(archive.Requests);
        Assert.Empty(archive.Journal);
    }

    /// <summary>
    /// A valid but empty archive is preserved as empty rather than treated as absent. It is the
    /// state of a household that has answered everything, and re-seeding it would put answered
    /// requests back on the page.
    /// </summary>
    [Fact]
    public void AValidEmptyArchiveIsPreserved()
    {
        var archive = PrayerArchiveCodec.Read("""{"requests":[],"journal":[]}""");

        Assert.Empty(archive.Requests);
        Assert.Empty(archive.Journal);
    }

    /// <summary>
    /// Writing needs a declared reader, because writing the file is the one moment the text has to
    /// become a string. Borrowing the caller's permission keeps the list of readers at four — the
    /// codec does not become a fifth thing allowed to see it.
    /// </summary>
    [Fact]
    public void WritingWithoutAReaderIsRefused()
    {
        var archive = PrayerArchiveCodec.Read(OnDrive);

        Assert.Throws<ArgumentNullException>(() => PrayerArchiveCodec.Write(archive, null!));
    }
}
