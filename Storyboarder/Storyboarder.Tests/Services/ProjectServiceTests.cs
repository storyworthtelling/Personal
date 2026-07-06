using Moq;
using Storyboarder.Domain.External;
using Storyboarder.Domain.Model;
using Storyboarder.Infrastructure.Services;
using Storyboarder.Tests.Support;
using Xunit;

namespace Storyboarder.Tests.Services;

public class ProjectServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TestDatabase database = new();
    private readonly FakeClock clock = new(Start);
    private readonly Mock<IGoogleDocsClient> docs = new();

    public void Dispose() => database.Dispose();

    private ProjectService NewService() => new(database, docs.Object, clock);

    private void SeedNovel(ProjectStage stage)
    {
        using var context = database.NewContext();
        context.Novels.Add(new Novel
        {
            Title = "Quest",
            GoogleDocId = "doc",
            Stage = stage,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task HasActiveWork_IsFalse_WhenNoNovels()
    {
        Assert.False(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(ProjectStage.Created)]
    [InlineData(ProjectStage.BuildingDesignBible)]
    [InlineData(ProjectStage.RenderingImages)]
    [InlineData(ProjectStage.ComposingAnalysis)]
    public async Task HasActiveWork_IsTrue_ForNonTerminalNovel(ProjectStage stage)
    {
        SeedNovel(stage);
        Assert.True(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(ProjectStage.Completed)]
    [InlineData(ProjectStage.Failed)]
    public async Task HasActiveWork_IsFalse_WhenAllNovelsTerminal(ProjectStage stage)
    {
        SeedNovel(stage);
        Assert.False(await NewService().HasActiveWorkAsync(CancellationToken.None));
    }
}
