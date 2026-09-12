using Microsoft.Extensions.Logging.Abstractions;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Prayer;
using Storyworthtelling.Remarkable;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// The prayer morning, end to end against fakes: read yesterday's sheet back, fold it into the
/// encrypted archive, compose today's two pages, upload, and only then throw yesterday's away.
///
/// <para>The ordering is the whole design. The sheet is the only copy of what was written until it
/// has been transcribed and saved, so every step that could fail has to fail <b>before</b> the
/// trash, not after it.</para>
/// </summary>
public class PrayerDeliveryServiceTests
{
    private const string Passphrase = "the one thing not in the vault";

    private static readonly DateOnly Today = new(2026, 9, 6);

    private sealed class Reader : IPrayerReader
    {
    }

    private static string Read(PrayerText text) => text.ReadBy(new Reader());

    // ---- fakes ----

    private sealed class FakeArchive : IPrayerArchive
    {
        public PrayerArchive Stored { get; set; } = new();

        public PrayerArchiveStatus Status { get; set; } = PrayerArchiveStatus.Loaded;

        public PrayerArchive? Saved { get; private set; }

        public int Saves { get; private set; }

        public Task<PrayerArchiveRead> LoadAsync(string passphrase, CancellationToken cancellationToken) =>
            Task.FromResult(Status == PrayerArchiveStatus.Loaded
                ? new PrayerArchiveRead(Status, Stored)
                : PrayerArchiveRead.Nothing(Status));

        public Task SaveAsync(PrayerArchive archive, string passphrase, CancellationToken cancellationToken)
        {
            if (Status is PrayerArchiveStatus.WrongPassphrase or PrayerArchiveStatus.Corrupt)
            {
                throw new PrayerArchiveLockedException("locked");
            }

            Saved = archive;
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTranscriber(JournalRead read) : IJournalTranscriber
    {
        public int Calls { get; private set; }

        public Task<JournalRead> Read(byte[] annotatedPdf, DateOnly date)
        {
            Calls++;
            return Task.FromResult(read);
        }
    }

    private sealed class FakePastoral : IPastoralComposer
    {
        public PastoralBrief? Asked { get; private set; }

        public Task<PrayerText> Compose(PastoralBrief brief, CancellationToken cancellationToken)
        {
            Asked = brief;
            return Task.FromResult(PrayerText.Of("A short list this morning."));
        }
    }

    private sealed class FakePages : IPrayerPdfComposer
    {
        public PrayerDocument? Composed { get; private set; }

        public byte[] Compose(PrayerDocument document)
        {
            Composed = document;
            return [37, 80, 68, 70];
        }
    }

    private sealed class FakeRemarkable : IRemarkableClient
    {
        public List<string> Uploaded { get; } = [];

        public List<string> Trashed { get; } = [];

        public RemarkableDoc? Yesterday { get; set; }

        public bool UploadFails { get; set; }

        public Task Upload(string title, byte[] pdf)
        {
            if (UploadFails)
            {
                throw new InvalidOperationException("upload failed");
            }

            Uploaded.Add(title);
            return Task.CompletedTask;
        }

        public Task Upload(string title, byte[] pdf, string? parentFolderId) => Upload(title, pdf);

        public Task<string> EnsureFolder(string name) => Task.FromResult(name);

        public Task<IReadOnlyList<RemarkableDoc>> ListRecent() =>
            Task.FromResult<IReadOnlyList<RemarkableDoc>>(
                Yesterday is null ? [] : [Yesterday]);

        public Task<RemarkableDoc?> DownloadByTitle(string title) =>
            Task.FromResult(Yesterday is { } doc && doc.Title == title ? doc : null);

        public Task MoveToTrash(string id)
        {
            Trashed.Add(id);
            return Task.CompletedTask;
        }
    }

    private static RemarkableDoc YesterdaysSheet() => new()
    {
        Id = "doc-1",
        Title = PrayerTitles.Document(Today.AddDays(-1)),
        PdfBytes = [37, 80, 68, 70, 9],
    };

    private static PrayerDeliveryService Service(
        FakeArchive archive, FakeTranscriber transcriber, FakeRemarkable remarkable,
        FakePages pages, FakePastoral pastoral) =>
        new(archive, transcriber, pastoral, pages, remarkable, () => Passphrase,
            NullLogger<PrayerDeliveryService>.Instance);

    private static JournalRead Wrote(string journalled, params string[] added) =>
        new(true, PrayerText.Of(journalled), [.. added.Select(PrayerText.Of)]);

    // ---- the morning ----

    [Fact]
    public async Task ItUploadsTodaysDocumentUnderTodaysTitle()
    {
        var remarkable = new FakeRemarkable();

        await Service(new FakeArchive(), new FakeTranscriber(JournalRead.Nothing), remarkable,
            new FakePages(), new FakePastoral()).Deliver(Today);

        Assert.Equal([PrayerTitles.Document(Today)], remarkable.Uploaded);
    }

    /// <summary>
    /// A line written under "Add to the list" becomes something held, from this morning. This is the
    /// half of the owner's answer that lives on paper: a one-off request is written, not typed.
    /// </summary>
    [Fact]
    public async Task ALineWrittenUnderTheStripBecomesSomethingHeld()
    {
        var archive = new FakeArchive();
        var remarkable = new FakeRemarkable { Yesterday = YesterdaysSheet() };

        await Service(archive, new FakeTranscriber(Wrote("", "Dad — the scan on the 14th")),
            remarkable, new FakePages(), new FakePastoral()).Deliver(Today);

        var held = Assert.Single(archive.Saved!.Requests);
        Assert.Equal("Dad — the scan on the 14th", Read(held.Text));
        Assert.Equal(Today, held.Held);
        Assert.Null(held.Answered);
    }

    /// <summary>
    /// And what was written in the free space becomes an archive entry dated to <b>the page's</b>
    /// day, not to today. It is what <c>From before</c> selects on, so dating it wrong moves a
    /// memory to the wrong morning.
    /// </summary>
    [Fact]
    public async Task WhatWasWrittenIsFiledUnderTheDayItWasWrittenOn()
    {
        var archive = new FakeArchive();
        var remarkable = new FakeRemarkable { Yesterday = YesterdaysSheet() };

        await Service(archive, new FakeTranscriber(Wrote("Asked for patience.")), remarkable,
            new FakePages(), new FakePastoral()).Deliver(Today);

        var entry = Assert.Single(archive.Saved!.Journal);
        Assert.Equal(Today.AddDays(-1), entry.Date);
        Assert.Equal("Asked for patience.", Read(entry.Text));
    }

    /// <summary>
    /// Yesterday's sheet is thrown away only once it has been read <b>and</b> the archive has taken
    /// what was on it. Every other order loses a morning's writing on a bad network.
    /// </summary>
    [Fact]
    public async Task YesterdaysSheetIsTrashedOnlyAfterItIsReadAndSaved()
    {
        var archive = new FakeArchive();
        var remarkable = new FakeRemarkable { Yesterday = YesterdaysSheet() };

        await Service(archive, new FakeTranscriber(Wrote("Something.")), remarkable,
            new FakePages(), new FakePastoral()).Deliver(Today);

        Assert.Equal(["doc-1"], remarkable.Trashed);
        Assert.Equal(1, archive.Saves);
    }

    [Fact]
    public async Task AFailedTranscriptionLeavesYesterdaysSheetWhereItIs()
    {
        var remarkable = new FakeRemarkable { Yesterday = YesterdaysSheet() };

        await Service(new FakeArchive(), new FakeTranscriber(JournalRead.Nothing), remarkable,
            new FakePages(), new FakePastoral()).Deliver(Today);

        Assert.Empty(remarkable.Trashed);
    }

    /// <summary>
    /// The worst case, and the reason the archive refuses rather than overwrites: a passphrase that
    /// will not open the file. The morning still delivers a page, and yesterday's sheet <b>stays on
    /// the tablet</b> — it is the only copy of what is on it, and nothing here can keep it.
    /// </summary>
    [Fact]
    public async Task ALockedArchiveStillDeliversAndKeepsYesterdaysSheet()
    {
        var archive = new FakeArchive { Status = PrayerArchiveStatus.WrongPassphrase };
        var remarkable = new FakeRemarkable { Yesterday = YesterdaysSheet() };

        await Service(archive, new FakeTranscriber(Wrote("Something worth keeping.")), remarkable,
            new FakePages(), new FakePastoral()).Deliver(Today);

        Assert.Equal([PrayerTitles.Document(Today)], remarkable.Uploaded);
        Assert.Empty(remarkable.Trashed);
        Assert.Equal(0, archive.Saves);
    }

    /// <summary>
    /// An upload that fails leaves yesterday's sheet alone too — otherwise a morning with no new
    /// sheet also has no old one, and there is nothing on the tablet to write on at all.
    /// </summary>
    [Fact]
    public async Task AFailedUploadLeavesYesterdaysSheetWhereItIs()
    {
        var remarkable = new FakeRemarkable { Yesterday = YesterdaysSheet(), UploadFails = true };

        await Service(new FakeArchive(), new FakeTranscriber(Wrote("Something.")), remarkable,
            new FakePages(), new FakePastoral()).Deliver(Today);

        Assert.Empty(remarkable.Trashed);
    }

    /// <summary>
    /// The strike-through is earned by being printed, so the mark goes back into the archive after
    /// the page is composed. Skip it and an answered request prints struck through every morning
    /// forever.
    /// </summary>
    [Fact]
    public async Task AStrikeThatWasPrintedIsMarkedInTheArchive()
    {
        var archive = new FakeArchive
        {
            Stored = new PrayerArchive
            {
                Requests =
                [
                    new PrayerRequest
                    {
                        Id = "team",
                        Text = PrayerText.Of("the team decision"),
                        Held = Today.AddDays(-12),
                        Answered = Today.AddDays(-1),
                    },
                ],
            },
        };

        var pages = new FakePages();

        await Service(archive, new FakeTranscriber(JournalRead.Nothing), new FakeRemarkable(),
            pages, new FakePastoral()).Deliver(Today);

        Assert.True(Assert.Single(pages.Composed!.Holding).Struck);
        Assert.True(Assert.Single(archive.Saved!.Requests).StrikePrinted);
    }

    /// <summary>
    /// This morning's rotation member reaches the page, and the pastoral line is asked about the
    /// list rather than about the rotation — a name that was simply next is not something being
    /// carried.
    /// </summary>
    [Fact]
    public async Task TheRotationReachesThePageAndThePastoralLineSeesTheList()
    {
        var archive = new FakeArchive
        {
            Stored = new PrayerArchive
            {
                Requests =
                [
                    new PrayerRequest
                    {
                        Id = "gwen",
                        Text = PrayerText.Of("Gwen, settling in"),
                        Held = Today.AddDays(-44),
                    },
                ],
                Rotations =
                [
                    new PrayerRotation
                    {
                        Id = "missionaries",
                        Name = "Missionaries",
                        Members = [PrayerText.Of("the Aldens"), PrayerText.Of("the Boyds")],
                        Began = Today.AddDays(-1),
                    },
                ],
            },
        };

        var pages = new FakePages();
        var pastoral = new FakePastoral();

        await Service(archive, new FakeTranscriber(JournalRead.Nothing), new FakeRemarkable(),
            pages, pastoral).Deliver(Today);

        var pick = Assert.Single(pages.Composed!.Rotations);
        Assert.Equal("Missionaries", pick.Rotation);
        Assert.Equal("the Boyds", Read(pick.Member));

        Assert.Equal(1, pastoral.Asked!.Held);
        Assert.Equal("held 6 weeks", pastoral.Asked.LongestHeld);
    }

    /// <summary>
    /// And what came before is on the page, selected by date out of the archive. Nothing read it to
    /// decide.
    /// </summary>
    [Fact]
    public async Task WhatCameBeforeIsOnThePage()
    {
        var archive = new FakeArchive
        {
            Stored = new PrayerArchive
            {
                Journal =
                [
                    new JournalEntry
                    {
                        Date = Today.AddDays(-7),
                        Text = PrayerText.Of("Still turning it over."),
                    },
                ],
            },
        };

        var pages = new FakePages();

        await Service(archive, new FakeTranscriber(JournalRead.Nothing), new FakeRemarkable(),
            pages, new FakePastoral()).Deliver(Today);

        var surfaced = Assert.Single(pages.Composed!.FromBefore);
        Assert.Equal("A week ago", surfaced.When);
    }

    /// <summary>
    /// With no passphrase there is nothing to open and nothing to save, and the page is still
    /// delivered — an install whose passphrase a factory reset wiped gets a blank prayer sheet to
    /// write on rather than no morning at all.
    /// </summary>
    [Fact]
    public async Task WithNoPassphraseTheSheetIsStillDelivered()
    {
        var archive = new FakeArchive { Status = PrayerArchiveStatus.Empty };
        var remarkable = new FakeRemarkable();

        var service = new PrayerDeliveryService(
            archive, new FakeTranscriber(JournalRead.Nothing), new FakePastoral(), new FakePages(),
            remarkable, () => string.Empty, NullLogger<PrayerDeliveryService>.Instance);

        await service.Deliver(Today);

        Assert.Equal([PrayerTitles.Document(Today)], remarkable.Uploaded);
    }
}
