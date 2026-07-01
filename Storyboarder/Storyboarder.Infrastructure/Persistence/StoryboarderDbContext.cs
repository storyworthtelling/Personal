using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Storyboarder.Domain.Model;

namespace Storyboarder.Infrastructure.Persistence;

// The data-access layer (per the repo's architecture: EF Core DbContext directly, no repository
// wrapper). All pipeline state lives here so the background worker resumes purely from the database.
public sealed class StoryboarderDbContext(DbContextOptions<StoryboarderDbContext> options)
    : DbContext(options)
{
    public DbSet<Novel> Novels => Set<Novel>();
    public DbSet<SourceText> SourceTexts => Set<SourceText>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<DesignRef> DesignRefs => Set<DesignRef>();
    public DbSet<Volume> Volumes => Set<Volume>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Panel> Panels => Set<Panel>();
    public DbSet<ChunkAnalysis> ChunkAnalyses => Set<ChunkAnalysis>();
    public DbSet<BookAnalysis> BookAnalyses => Set<BookAnalysis>();

    // SQLite has no native DateTimeOffset, and the provider cannot translate comparisons on the
    // default TEXT mapping. Storing UTC ticks as integers makes the worker's "due units" time
    // comparisons translate to plain integer comparisons.
    private static readonly ValueConverter<DateTimeOffset, long> TicksConverter =
        new(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));

    private static readonly ValueConverter<DateTimeOffset?, long?> NullableTicksConverter =
        new(value => value.HasValue ? value.Value.UtcTicks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Novel>(entity =>
        {
            entity.HasKey(novel => novel.Id);
            entity.Property(novel => novel.Stage).HasConversion<int>();
            entity.Property(novel => novel.CreatedAtUtc).HasConversion(TicksConverter);
            entity.Property(novel => novel.UpdatedAtUtc).HasConversion(TicksConverter);
            entity.HasOne(novel => novel.Source)
                .WithOne()
                .HasForeignKey<SourceText>(source => source.NovelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(novel => novel.Chunks)
                .WithOne()
                .HasForeignKey(chunk => chunk.NovelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(novel => novel.DesignRefs)
                .WithOne()
                .HasForeignKey(reference => reference.NovelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(novel => novel.Volumes)
                .WithOne()
                .HasForeignKey(volume => volume.NovelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(novel => novel.ChunkAnalyses)
                .WithOne()
                .HasForeignKey(analysis => analysis.NovelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(novel => novel.Analysis)
                .WithOne()
                .HasForeignKey<BookAnalysis>(analysis => analysis.NovelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(novel => novel.Stage);
        });

        model.Entity<SourceText>(entity => entity.HasKey(source => source.Id));

        model.Entity<Chunk>(entity =>
        {
            entity.HasKey(chunk => chunk.Id);
            entity.Property(chunk => chunk.AnalysisStatus).HasConversion<int>();
            entity.Property(chunk => chunk.NextAttemptAtUtc).HasConversion(NullableTicksConverter);
            entity.HasIndex(chunk => new { chunk.NovelId, chunk.Ordinal });
            entity.HasIndex(chunk => new { chunk.AnalysisStatus, chunk.NextAttemptAtUtc });
        });

        model.Entity<DesignRef>(entity =>
        {
            entity.HasKey(reference => reference.Id);
            entity.Property(reference => reference.Kind).HasConversion<int>();
            entity.Property(reference => reference.ReferenceSheetStatus).HasConversion<int>();
            entity.Property(reference => reference.NextAttemptAtUtc).HasConversion(NullableTicksConverter);
            entity.HasIndex(reference => new { reference.ReferenceSheetStatus, reference.NextAttemptAtUtc });
        });

        model.Entity<Volume>(entity =>
        {
            entity.HasKey(volume => volume.Id);
            entity.Property(volume => volume.PdfStatus).HasConversion<int>();
            entity.HasMany(volume => volume.Pages)
                .WithOne()
                .HasForeignKey(page => page.VolumeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Page>(entity =>
        {
            entity.HasKey(page => page.Id);
            entity.Property(page => page.CompositionStatus).HasConversion<int>();
            entity.HasMany(page => page.Panels)
                .WithOne()
                .HasForeignKey(panel => panel.PageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(page => page.CompositionStatus);
        });

        model.Entity<Panel>(entity =>
        {
            entity.HasKey(panel => panel.Id);
            entity.Property(panel => panel.Status).HasConversion<int>();
            entity.Property(panel => panel.NextAttemptAtUtc).HasConversion(NullableTicksConverter);
            entity.HasIndex(panel => new { panel.Status, panel.NextAttemptAtUtc });
        });

        model.Entity<ChunkAnalysis>(entity =>
        {
            entity.HasKey(analysis => analysis.Id);
            entity.Property(analysis => analysis.Status).HasConversion<int>();
            entity.Property(analysis => analysis.NextAttemptAtUtc).HasConversion(NullableTicksConverter);
            entity.HasIndex(analysis => new { analysis.NovelId, analysis.Ordinal });
            entity.HasIndex(analysis => new { analysis.Status, analysis.NextAttemptAtUtc });
        });

        model.Entity<BookAnalysis>(entity =>
        {
            entity.HasKey(analysis => analysis.Id);
            entity.Property(analysis => analysis.PdfStatus).HasConversion<int>();
            entity.HasIndex(analysis => analysis.NovelId).IsUnique();
        });
    }
}
