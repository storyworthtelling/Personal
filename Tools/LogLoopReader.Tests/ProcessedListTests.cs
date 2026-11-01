using System.Text.Json;
using LogLoopReader;
using Storyworthtelling.Diagnostics;
using Storyworthtelling.Voice.Logs;

namespace LogLoopReader.Tests;

// Phase 4 (LOG-LOOP-PLAN.md §5.6): the session can trash only what the owner's account owns, so her phone's files
// are listed instead — names only — in swt-log-processed.json, and her phone trashes its own on its next upload
// (LogUploaderTests holds that half). The list names only files the session finished, only her phone's, and keeps a
// name until the file has actually gone from the folder.
public class ProcessedListTests
{
    private const string HerDone = "swt-log.pulp.bbbbbbbbbbbb.000001-000003.json";
    private const string HerStillTriaging = "swt-log.pulp.bbbbbbbbbbbb.000004-000004.json";
    private const string OwnerDone = "swt-log.post.aaaaaaaaaaaa.000001-000002.json";
    private const string HerLastWeek = "swt-log.index.cccccccccccc.000001-000001.json";
    private const string HerLastWeekGone = "swt-log.index.cccccccccccc.000000-000000.json";

    private static readonly Dictionary<string, string> Phones = new()
    {
        [HerDone] = LogPhone.Partner,
        [HerStillTriaging] = LogPhone.Partner,
        [OwnerDone] = LogPhone.Owner,
        [HerLastWeek] = LogPhone.Partner,
    };

    private static readonly Manifest.Entry[] ThisWeek =
    [
        new(HerDone, "1", "done", "Decrypted"),
        new(HerStillTriaging, "2", "triaged", "Decrypted"),
        new(OwnerDone, "3", "done", "Decrypted"),
        new(HerLastWeek, "4", "decrypted", "Decrypted"),
    ];

    [Fact]
    public void OnlyHerFinishedFilesAreListed()
    {
        var names = ProcessedList.Names(ThisWeek, Phones.GetValueOrDefault, previous: []);

        Assert.Equal([HerDone], names);
    }

    [Fact]
    public void ANameStaysListedUntilItsFileHasGone()
    {
        var names = ProcessedList.Names(ThisWeek, Phones.GetValueOrDefault, previous: [HerLastWeek, HerLastWeekGone]);

        Assert.Equal([HerLastWeek, HerDone], names); // sorted, ordinal
    }

    [Fact]
    public void AnUnreadableFileIsNeverListed()
    {
        Manifest.Entry[] manifest = [new(HerDone, "1", "done", "WrongKey")];

        Assert.Empty(ProcessedList.Names(manifest, Phones.GetValueOrDefault, previous: []));
    }

    [Fact]
    public void TheFileIsTheShapeHerPhoneReadsUnderTheNameItLooksFor()
    {
        var json = ProcessedList.Json([HerDone, HerLastWeek]);

        Assert.Equal(DriveLogFolder.ProcessedListName, ProcessedList.FileName);
        using var document = JsonDocument.Parse(json);
        Assert.Equal([HerDone, HerLastWeek], document.RootElement.GetProperty("names").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal([HerDone], ProcessedList.Previous(json).Where(n => n == HerDone));
        Assert.Empty(ProcessedList.Previous("not json"));
    }
}
