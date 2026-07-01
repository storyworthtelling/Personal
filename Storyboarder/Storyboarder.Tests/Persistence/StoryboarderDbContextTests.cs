using Microsoft.EntityFrameworkCore;
using Storyboarder.Domain.Model;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Persistence;

public class StoryboarderDbContextTests : IDisposable
{
    private readonly TestDatabase database = new();

    public void Dispose() => database.Dispose();

    private static Novel SampleNovel()
    {
        var novel = new Novel { Title = "Test Novel", GoogleDocId = "doc-1", Stage = ProjectStage.Storyboarding };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = "Once upon a time.", CharacterCount = 17 };
        novel.Chunks.Add(new Chunk { NovelId = novel.Id, Ordinal = 0, Text = "Once upon a time." });
        novel.DesignRefs.Add(new DesignRef
        {
            NovelId = novel.Id, Kind = ReferenceKind.Character, Name = "Alice", CanonicalDescription = "short"
        });

        var volume = new Volume { NovelId = novel.Id, Number = 1, Title = "Volume One" };
        var page = new Page { VolumeId = volume.Id, ChapterNumber = 1, PageNumber = 1 };
        page.Panels.Add(new Panel { PageId = page.Id, PanelNumber = 1, VisualPrompt = "Alice wakes." });
        volume.Pages.Add(page);
        novel.Volumes.Add(volume);

        novel.ChunkAnalyses.Add(new ChunkAnalysis
        {
            NovelId = novel.Id, Ordinal = 0, Status = WorkStatus.Completed, NotesJson = "{}"
        });
        novel.Analysis = new BookAnalysis
        {
            NovelId = novel.Id, ConsolidatedJson = "{}", PdfStatus = WorkStatus.Completed, PdfPath = "/tmp/a.pdf"
        };
        return novel;
    }

    [Fact]
    public void SavesAndReloads_FullGraph()
    {
        var novel = SampleNovel();
        using (var context = database.NewContext())
        {
            context.Novels.Add(novel);
            context.SaveChanges();
        }

        using (var context = database.NewContext())
        {
            var loaded = context.Novels
                .Include(n => n.Source)
                .Include(n => n.Chunks)
                .Include(n => n.DesignRefs)
                .Include(n => n.Volumes).ThenInclude(v => v.Pages).ThenInclude(p => p.Panels)
                .Include(n => n.ChunkAnalyses)
                .Include(n => n.Analysis)
                .Single();

            Assert.Equal("Test Novel", loaded.Title);
            Assert.Equal(ProjectStage.Storyboarding, loaded.Stage);
            Assert.Equal(17, loaded.Source!.CharacterCount);
            Assert.Single(loaded.Chunks);
            Assert.Equal(ReferenceKind.Character, Assert.Single(loaded.DesignRefs).Kind);
            var panel = Assert.Single(Assert.Single(Assert.Single(loaded.Volumes).Pages).Panels);
            Assert.Equal("Alice wakes.", panel.VisualPrompt);
            Assert.Equal(0, Assert.Single(loaded.ChunkAnalyses).Ordinal);
            Assert.Equal("/tmp/a.pdf", loaded.Analysis!.PdfPath);
        }
    }

    [Fact]
    public void DeletingNovel_CascadesToChildren()
    {
        var novel = SampleNovel();
        using (var context = database.NewContext())
        {
            context.Novels.Add(novel);
            context.SaveChanges();
        }

        using (var context = database.NewContext())
        {
            context.Novels.Remove(context.Novels.Single());
            context.SaveChanges();
        }

        using (var context = database.NewContext())
        {
            Assert.Empty(context.Novels);
            Assert.Empty(context.Chunks);
            Assert.Empty(context.DesignRefs);
            Assert.Empty(context.Volumes);
            Assert.Empty(context.Pages);
            Assert.Empty(context.Panels);
            Assert.Empty(context.SourceTexts);
            Assert.Empty(context.ChunkAnalyses);
            Assert.Empty(context.BookAnalyses);
        }
    }

    [Fact]
    public void DueUnitsQuery_ReturnsPendingAndElapsedRetries_Only()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var page = new Page();
        page.Panels.AddRange(
        [
            new Panel { PageId = page.Id, PanelNumber = 1, Status = WorkStatus.Pending },
            new Panel { PageId = page.Id, PanelNumber = 2, Status = WorkStatus.Completed },
            new Panel { PageId = page.Id, PanelNumber = 3, Status = WorkStatus.NeedsRetry, NextAttemptAtUtc = now.AddMinutes(-1) },
            new Panel { PageId = page.Id, PanelNumber = 4, Status = WorkStatus.NeedsRetry, NextAttemptAtUtc = now.AddMinutes(5) },
            new Panel { PageId = page.Id, PanelNumber = 5, Status = WorkStatus.Failed }
        ]);
        var volume = new Volume { Number = 1 };
        volume.Pages.Add(page);
        var novel = new Novel { Title = "N" };
        novel.Volumes.Add(volume);

        using (var context = database.NewContext())
        {
            context.Novels.Add(novel);
            context.SaveChanges();
        }

        using (var context = database.NewContext())
        {
            var due = context.Panels
                .Where(p => p.Status == WorkStatus.Pending ||
                            (p.Status == WorkStatus.NeedsRetry && p.NextAttemptAtUtc <= now))
                .OrderBy(p => p.PanelNumber)
                .Select(p => p.PanelNumber)
                .ToList();

            Assert.Equal([1, 3], due);
        }
    }
}
