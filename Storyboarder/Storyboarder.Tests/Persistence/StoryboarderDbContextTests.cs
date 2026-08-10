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
        var novel = new Novel { Title = "Test Novel", GoogleDocId = "doc-1", Stage = ProjectStage.RenderingSceneImages };
        novel.Source = new SourceText { NovelId = novel.Id, FullText = "Once upon a time.", CharacterCount = 17 };
        novel.Chunks.Add(new Chunk { NovelId = novel.Id, Ordinal = 0, Text = "Once upon a time." });
        novel.DesignRefs.Add(new DesignRef
        {
            NovelId = novel.Id, Kind = ReferenceKind.Character, Name = "Alice", CanonicalDescription = "short"
        });
        novel.Scenes.Add(new Scene
        {
            NovelId = novel.Id, Number = 1, ChapterNumber = 1, ChapterLabel = "Chapter 1",
            StartOffset = 0, EndOffset = 17, ImageStatus = WorkStatus.Completed, ImagePath = "/tmp/scene-1.png"
        });
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
                .Include(n => n.Scenes)
                .Include(n => n.ChunkAnalyses)
                .Include(n => n.Analysis)
                .Single();

            Assert.Equal("Test Novel", loaded.Title);
            Assert.Equal(ProjectStage.RenderingSceneImages, loaded.Stage);
            Assert.Equal(17, loaded.Source!.CharacterCount);
            Assert.Single(loaded.Chunks);
            Assert.Equal(ReferenceKind.Character, Assert.Single(loaded.DesignRefs).Kind);
            var scene = Assert.Single(loaded.Scenes);
            Assert.Equal("/tmp/scene-1.png", scene.ImagePath);
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
            Assert.Empty(context.Scenes);
            Assert.Empty(context.SourceTexts);
            Assert.Empty(context.ChunkAnalyses);
            Assert.Empty(context.BookAnalyses);
        }
    }

    [Fact]
    public void DueUnitsQuery_ReturnsPendingAndElapsedRetries_Only()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var novel = new Novel { Title = "N" };
        novel.Scenes.AddRange(
        [
            new Scene { NovelId = novel.Id, Number = 1, ImageStatus = WorkStatus.Pending },
            new Scene { NovelId = novel.Id, Number = 2, ImageStatus = WorkStatus.Completed },
            new Scene { NovelId = novel.Id, Number = 3, ImageStatus = WorkStatus.NeedsRetry, NextAttemptAtUtc = now.AddMinutes(-1) },
            new Scene { NovelId = novel.Id, Number = 4, ImageStatus = WorkStatus.NeedsRetry, NextAttemptAtUtc = now.AddMinutes(5) },
            new Scene { NovelId = novel.Id, Number = 5, ImageStatus = WorkStatus.Failed }
        ]);

        using (var context = database.NewContext())
        {
            context.Novels.Add(novel);
            context.SaveChanges();
        }

        using (var context = database.NewContext())
        {
            var due = context.Scenes
                .Where(s => s.ImageStatus == WorkStatus.Pending ||
                            (s.ImageStatus == WorkStatus.NeedsRetry && s.NextAttemptAtUtc <= now))
                .OrderBy(s => s.Number)
                .Select(s => s.Number)
                .ToList();

            Assert.Equal([1, 3], due);
        }
    }
}
