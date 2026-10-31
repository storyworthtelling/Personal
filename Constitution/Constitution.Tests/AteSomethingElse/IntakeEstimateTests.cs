using System.Reflection;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using static Constitution.Tests.AteSomethingElse.AteFixture;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>C-6.6.2: the estimate is about this install's own person, for the meal the time says, and the photo is not kept.</summary>
public class IntakeEstimateTests
{
    [Theory]
    [InlineData(6, 0, AteMeal.Breakfast)]
    [InlineData(10, 59, AteMeal.Breakfast)]
    [InlineData(11, 0, AteMeal.Lunch)]
    [InlineData(15, 59, AteMeal.Lunch)]
    [InlineData(16, 0, AteMeal.Dinner)]
    [InlineData(19, 5, AteMeal.Dinner)]
    [InlineData(23, 59, AteMeal.Dinner)]
    public void MealComesFromTheTime(int hour, int minute, AteMeal expected)
    {
        Assert.Equal(expected, AteMeals.At(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void TheMealIsReadOffTheCaptureClockNotTheServer()
    {
        var model = new AteSomethingElseModel();

        model.StartCapture(new DateTimeOffset(2026, 11, 17, 8, 30, 0, TimeSpan.FromHours(-5)));

        Assert.Equal(AteMeal.Breakfast, model.Meal);
        Assert.Equal(new DateOnly(2026, 11, 17), model.Day);
    }

    [Fact]
    public void OnlyThisInstallsPerson()
    {
        // No person, no face, no picker (D65): nothing in the estimate's types or the model's surface can name anyone.
        var smells = new[] { "person", "who", "name", "face", "role", "participant", "member", "picker", "owner", "partner" };
        var types = new[] { typeof(AteEstimate), typeof(AteReading), typeof(AteSomethingElseModel), typeof(IAteEstimator), typeof(IAteEstimates) };

        foreach (var type in types)
        {
            var names = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(member => member.Name)
                .Concat(type.GetMethods().SelectMany(method => method.GetParameters().Select(parameter => parameter.Name ?? string.Empty)));
            foreach (var name in names)
            {
                Assert.DoesNotContain(smells, smell => name.Contains(smell, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void TheModelStartsWithNoPickerStep()
    {
        Assert.DoesNotContain(Enum.GetNames<AtePhase>(), phase => phase.Contains("Pick", StringComparison.OrdinalIgnoreCase)
            || phase.Contains("Who", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PhotoIsDiscarded()
    {
        using var rig = new PhotoRig();
        var estimator = new FakeEstimator(_ => Task.FromResult<AteOutcome>(new AteOutcome.Read(Reading())));
        var forgotten = new List<bool>();

        var outcome = await new AteSomethingElseModel().EstimateAsync(estimator, rig.CacheRoot, rig.PhotoPath, rig.Delete, forgotten.Add);

        Assert.IsType<AteOutcome.Read>(outcome);
        Assert.False(rig.Exists);
        Assert.Equal([true], forgotten);
    }

    [Fact]
    public async Task PhotoIsDiscardedWhenTheEstimateFails()
    {
        using var rig = new PhotoRig();
        var estimator = new FakeEstimator(_ => Task.FromResult<AteOutcome>(new AteOutcome.Unreadable()));

        var outcome = await new AteSomethingElseModel().EstimateAsync(estimator, rig.CacheRoot, rig.PhotoPath, rig.Delete);

        Assert.IsType<AteOutcome.Unreadable>(outcome);
        Assert.False(rig.Exists);
    }

    [Fact]
    public async Task PhotoIsDiscardedWhenTheServiceIsUnreachable()
    {
        using var rig = new PhotoRig();
        var estimator = new FakeEstimator(_ => throw new HttpRequestException("no route"));

        var outcome = await new AteSomethingElseModel().EstimateAsync(estimator, rig.CacheRoot, rig.PhotoPath, rig.Delete);

        Assert.IsType<AteOutcome.Unavailable>(outcome);
        Assert.False(rig.Exists);
    }

    [Fact]
    public async Task PhotoIsDiscardedWhenTheEstimatorThrowsAnythingElse()
    {
        using var rig = new PhotoRig();
        var estimator = new FakeEstimator(_ => throw new NotSupportedException("surprise"));

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            new AteSomethingElseModel().EstimateAsync(estimator, rig.CacheRoot, rig.PhotoPath, rig.Delete));

        Assert.False(rig.Exists);
    }

    [Fact]
    public async Task APhotoOutsideTheCaptureFolderIsNeitherReadNorDeleted()
    {
        using var rig = new PhotoRig();
        var elsewhere = Path.Combine(rig.CacheRoot, "other.jpg");
        File.WriteAllBytes(elsewhere, [1, 2, 3]);
        var estimator = new FakeEstimator(_ => Task.FromResult<AteOutcome>(new AteOutcome.Read(Reading())));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new AteSomethingElseModel().EstimateAsync(estimator, rig.CacheRoot, elsewhere, rig.Delete));

        Assert.Equal(0, estimator.Calls);
        Assert.True(File.Exists(elsewhere));
    }

    [Fact]
    public void ADeleteThatFailsIsToldNotHidden()
    {
        using var rig = new PhotoRig();
        var told = new List<bool>();
        var estimator = new FakeEstimator(_ => Task.FromResult<AteOutcome>(new AteOutcome.Read(Reading())));

        new AteSomethingElseModel().EstimateAsync(estimator, rig.CacheRoot, rig.PhotoPath, _ => false, told.Add).GetAwaiter().GetResult();

        Assert.Equal([false], told);
    }

    [Fact]
    public void NeverEntersTheHouseholdFile()
    {
        // The kinds are frozen: the estimate is not one of them, and no kind was added for it.
        Assert.Equal(7, ConstitutionKinds.All.Count);
        Assert.DoesNotContain(ConstitutionKinds.All, kind => kind.Contains("ate-", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("intake", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("estimate", StringComparison.OrdinalIgnoreCase));

        // And no file that handles the estimate has a way into the household file, the vault, Drive or a backup.
        var forbidden = new[] { "HouseholdRecord", "HouseholdFile", "IDriveTextFile", "DriveTextFile", "SecretsVault", "BodyBackup", "ConstitutionKinds" };
        var files = ConstitutionSource.Files(".cs")
            .Where(file => Path.GetFileName(file).StartsWith("Ate", StringComparison.Ordinal)
                || Path.GetFileName(file).Contains("AteEstimat", StringComparison.Ordinal)
                || Path.GetFileName(file).Contains("ClaudeAte", StringComparison.Ordinal)
                || Path.GetFileName(file).Contains("FileAte", StringComparison.Ordinal))
            .Where(file => !ConstitutionSource.Relative(file).StartsWith("Constitution.Tests/", StringComparison.Ordinal))
            .ToList();
        Assert.True(files.Count >= 8, "The guard found only " + files.Count + " Ate files.");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(forbidden, word => text.Contains(word, StringComparison.Ordinal));
        }
    }
}
