using Folio.Domain.Imaging;
using SkiaSharp;
using Folio.Domain.Model;
using Folio.Domain.External;
using Folio.Infrastructure.Configuration;
using Folio.Infrastructure.Imaging;
using Folio.Infrastructure.Services;
using Folio.Tests.Support;
using Moq;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace Folio.Tests.Report;

/// <summary>
/// G20 — a changed scene keeps its predecessor's image until the report is composed, and the scene
/// block prints the two together (F41).
///
/// <para>The bug this reintroduces is sweeping the previous draft's images when the new draft
/// renders. It is the tidy thing to do — the old draft is finished, its pictures are megabytes, and
/// the new draft has its own — and it is why the retention rule is load-bearing rather than
/// incidental. Nothing fails when it is swept: the pair simply prints one panel, and the before/after
/// the whole cross-draft read exists for quietly stops existing.</para>
/// </summary>
public class ScenePairRetentionTests : IDisposable
{
    private readonly TestDatabase database = new();
    private readonly string outputRoot =
        Path.Combine(Path.GetTempPath(), "folio-pairs-" + Guid.NewGuid().ToString("N"));

    private FileSystemImageStore Images =>
        new(Options.Create(new StorageOptions { OutputRoot = outputRoot }));

    public void Dispose()
    {
        database.Dispose();
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The structural half: the report's scene evidence has somewhere to put the predecessor. A
    /// record with one image cannot print a pair however carefully the pipeline keeps the file.
    /// </summary>
    [Fact]
    public void SceneEvidenceCarriesThePredecessorsImage()
    {
        var members = typeof(SceneEvidence).GetProperties().Select(property => property.Name).ToList();

        Assert.Contains("PreviousImage", members);
    }

    [Fact]
    public void APairIsShownOnlyWhenBothHalvesExist()
    {
        var both = new SceneEvidence("Ch. 1 · Scene 1", "p", "v", [1], [2]);
        var onlyNew = new SceneEvidence("Ch. 1 · Scene 1", "p", "v", [1], null);
        var onlyOld = new SceneEvidence("Ch. 1 · Scene 1", "p", "v", null, [2]);

        Assert.True(both.ShowsPair);
        Assert.False(onlyNew.ShowsPair);
        Assert.False(onlyOld.ShowsPair,
            "A scene whose own render failed must not print its predecessor alone — the reader would "
            + "take the old picture for this draft's.");
    }

    /// <summary>
    /// The behavioural half, and the one the bug breaks: after the second draft has its own images,
    /// the first draft's are still readable, so the diff can pair them.
    /// </summary>
    [Fact]
    public async Task AChangedScenesPredecessorIsStillOnDiskAfterTheNewDraftRenders()
    {
        var (_, current) = SeedTwoDrafts();

        var diff = await NewService().GetDraftDiffAsync(current, CancellationToken.None);

        var pair = Assert.Single(diff!.ChangedScenes);
        Assert.NotNull(pair.PreviousImage);
        Assert.NotNull(pair.NewImage);
        Assert.NotEqual(pair.PreviousImage, pair.NewImage);
    }

    /// <summary>An unchanged scene has no pair: the same picture twice is not a before and after.</summary>
    [Fact]
    public async Task AnUnchangedSceneIsNotPaired()
    {
        var (_, current) = SeedTwoDrafts(changed: false);

        var diff = await NewService().GetDraftDiffAsync(current, CancellationToken.None);

        Assert.Empty(diff!.ChangedScenes);
    }

    /// <summary>
    /// The rule stated where it can be broken. "Free the local copies after archiving" is the one
    /// thing in the app that deletes rendered scenes, and it used to free the archiving draft's
    /// own — correct about that draft's report, which is delivered by then, and wrong about every
    /// draft after it, whose before/after has nowhere to reach.
    ///
    /// <para>So a sweep has to go through <see cref="SceneRetention"/>, which knows the two drafts a
    /// pair can still reach. Source-level, because the next sweep will be written in a cleanup path
    /// that does not exist yet, and a behavioural test cannot cover a path nobody has written.</para>
    /// </summary>
    [Fact]
    public void NoSweepDeletesSceneImagesWithoutAskingWhatIsStillPairedAgainst()
    {
        var root = Path.Combine(RepoRoot(), "Storyboarder", "Folio.Infrastructure");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Where(file => !file.EndsWith("FileSystemImageStore.cs", StringComparison.Ordinal))
            // Deleting a whole novel takes its images with it, which is not a sweep.
            .Where(file => !file.EndsWith("ProjectService.cs", StringComparison.Ordinal))
            .Select(file => (Name: Path.GetFileName(file), Text: Strip(File.ReadAllText(file))))
            .Where(file => file.Text.Contains("imageStore.Delete", StringComparison.Ordinal)
                && !file.Text.Contains("SceneRetention.Freeable", StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These delete rendered images without asking SceneRetention which drafts are still "
            + "paired against: " + string.Join(", ", offenders)
            + ". A changed scene's predecessor has to survive, or the pair prints one panel.");
    }

    /// <summary>
    /// The rule itself: a pair reaches back one draft, so archiving keeps two — this draft, which
    /// the next one will pair against, and the one before it, which this draft's own diff shows.
    /// </summary>
    [Fact]
    public void ArchivingKeepsThisDraftAndTheOneItWasMadeFrom()
    {
        Guid one = Guid.NewGuid(), two = Guid.NewGuid(), three = Guid.NewGuid(), four = Guid.NewGuid();

        var freeable = SceneRetention.Freeable(four, three, [one, two, three, four]);

        Assert.Equal([one, two], freeable);
    }

    [Fact]
    public void AFirstDraftArchivingFreesNothing()
    {
        var only = Guid.NewGuid();

        Assert.Empty(SceneRetention.Freeable(only, null, [only]));
    }

    /// <summary>The printed half: a paired scene draws two pictures, an unpaired one draws one.</summary>
    [Fact]
    public void APairedSceneBlockPrintsTwoPictures()
    {
        var single = Pdf([new SceneEvidence("Ch. 1 · Scene 1", "Purpose", "Verdict", SampleImages.Png(), null)]);
        var paired = Pdf([new SceneEvidence("Ch. 1 · Scene 1", "Purpose", "Verdict", SampleImages.Png(), SampleImages.Png())]);

        Assert.True(Pictures(paired) > Pictures(single),
            "The paired scene block drew no more pictures than the unpaired one — the predecessor was "
            + "carried all the way to the renderer and then not printed.");
    }

    private static int Pictures(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return document.GetPages().Sum(page => page.GetImages().Count());
    }

    private static byte[] Pdf(IReadOnlyList<SceneEvidence> scenes) =>
        new AnalysisDocumentRenderer().Render(
            SampleAnalysis.Of(),
            new ReportCover("The Salt Line", 3, 94_200, new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), null, null),
            scenes);

    private ProjectService NewService() =>
        new(database, new Mock<IGoogleDocsClient>().Object, Images, new FakeClock(DateTimeOffset.UnixEpoch),
            Options.Create(new RateLimitOptions()));

    /// <summary>A first draft with a rendered scene, and a second whose matching scene changed.</summary>
    private (Guid Previous, Guid Current) SeedTwoDrafts(bool changed = true)
    {
        using var context = database.NewContext();
        var images = Images;

        var first = new Novel { Title = "Quest", GoogleDocId = "doc", DraftNumber = 1 };
        first.ProjectId = first.Id;
        var second = new Novel { Title = "Quest", GoogleDocId = "doc", DraftNumber = 2, ProjectId = first.Id, PreviousDraftId = first.Id };

        var before = new Scene
        {
            NovelId = first.Id, Number = 1, ChapterNumber = 1, ChapterLabel = "Ch. 1",
            ImageStatus = WorkStatus.Completed,
            ImagePath = images.Save($"{first.Id}/scenes/scene-1.png", SampleImages.Png()),
        };
        var after = new Scene
        {
            NovelId = second.Id, Number = 1, ChapterNumber = 1, ChapterLabel = "Ch. 1",
            ImageStatus = WorkStatus.Completed, PreviousSceneId = before.Id, Changed = changed,
            ImagePath = images.Save($"{second.Id}/scenes/scene-1.png", SampleImages.Png(color: SKColors.Blue)),
        };

        context.Novels.AddRange(first, second);
        context.Scenes.AddRange(before, after);
        context.SaveChanges();
        return (first.Id, second.Id);
    }

    /// <summary>Comments are stripped so the sentence explaining the rule is not read as breaking it.</summary>
    private static string Strip(string source) => string.Join('\n',
        source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DESIGN.md")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }
}
