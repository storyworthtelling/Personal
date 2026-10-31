using Constitution.Domain.Meals;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>Invented figures and an in-memory stand-in for the estimator, shared by the I-ate-something-else tests.</summary>
internal static class AteFixture
{
    public static readonly DateTimeOffset Evening = new(2026, 11, 17, 19, 5, 0, TimeSpan.FromHours(-5));

    public static AteReading Reading(string what = "pasta with tomato sauce", int low = 750, int high = 1050, AteConfidence confidence = AteConfidence.Fair)
    {
        Assert.True(AteReading.TryCreate(what, low, high, 25, 45, confidence, out var reading));
        return reading!;
    }

    public static AteEstimate Estimate(AteMeal meal = AteMeal.Dinner, int low = 750, int high = 1050) =>
        new(new DateOnly(2026, 11, 17), meal, Reading(low: low, high: high));

    /// <summary>A capture folder in a temp directory with one photo in it, and its cleanup.</summary>
    internal sealed class PhotoRig : IDisposable
    {
        public PhotoRig()
        {
            CacheRoot = Path.Combine(Path.GetTempPath(), "ate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Constitution.Domain.Recipes.Screens.CameraCaptureCache.FolderIn(CacheRoot));
            PhotoPath = Constitution.Domain.Recipes.Screens.CameraCaptureCache.PathIn(CacheRoot);
            File.WriteAllBytes(PhotoPath, [0xFF, 0xD8, 0xFF, 0xD9]);
        }

        public string CacheRoot { get; }

        public string PhotoPath { get; }

        public bool Exists => File.Exists(PhotoPath);

        public bool Delete(string path)
        {
            File.Delete(path);
            return true;
        }

        public void Dispose()
        {
            if (Directory.Exists(CacheRoot))
            {
                Directory.Delete(CacheRoot, recursive: true);
            }
        }
    }

    internal sealed class FakeEstimator(Func<string, Task<AteOutcome>> respond) : IAteEstimator
    {
        public int Calls { get; private set; }

        public Task<AteOutcome> EstimateAsync(string photoPath, CancellationToken cancellation)
        {
            Calls++;
            return respond(photoPath);
        }
    }

    internal sealed class MemoryEstimates : IAteEstimates
    {
        public List<AteEstimate> Items { get; } = [];

        public AteStoreRead Read() => new(Items.ToList(), false);

        public AteEstimate? Record(AteEstimate estimate)
        {
            var previous = Items.FirstOrDefault(item => item.Day == estimate.Day && item.Meal == estimate.Meal);
            Items.RemoveAll(item => item.Day == estimate.Day && item.Meal == estimate.Meal);
            Items.Add(estimate);
            return previous;
        }

        public void Withdraw(AteEstimate recorded, AteEstimate? previous)
        {
            Items.RemoveAll(item => item.Day == recorded.Day && item.Meal == recorded.Meal);
            if (previous is not null)
            {
                Items.Add(previous);
            }
        }
    }
}
