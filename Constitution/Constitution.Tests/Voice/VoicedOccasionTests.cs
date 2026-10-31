using System.Reflection;
using System.Text.RegularExpressions;
using Constitution.Domain.Voice;
using Constitution.Infrastructure.Voice;

namespace Constitution.Tests.Voice;

/// <summary>
/// C-9.2.1 — the owner's voice in exactly six places, and never on an error (DESIGN.md §14).
///
/// <para><b>The error rule.</b> A voice is for the app's own remarks, not its bad news. A failed
/// sync, a refused permission, a bed that will not answer: these are facts to act on, and dressing
/// them in the owner's wit makes them read as banter about their health, and puts a model call on
/// the path of reporting that something failed. The seats are therefore a closed list rather than a
/// convention, and the composer's whole surface is built so an error has nowhere to go in.</para>
///
/// <para>The ids are stored (the daily cache of C-9.2.3 will key on them), so the expected list is
/// typed out here as literals: a guard that derived it from the code could not notice it moving.</para>
/// </summary>
public class VoicedOccasionTests
{
    private static readonly string[] ExpectedIds =
    [
        "today-top-line",
        "rest-day-line",
        "body-monthly",
        "signal-sentence",
        "workout-encouragement",
        "meal-sheet-intro",
    ];

    private static readonly string[] Trouble =
        ["error", "fail", "failed", "failure", "problem", "sorry", "unable", "denied", "invalid", "expired", "crash", "exception"];

    // ---- the closed list ----

    [Fact]
    public void ExactlySixSeats()
    {
        Assert.Equal(6, Enum.GetValues<VoiceSeat>().Length);
        Assert.Equal(6, VoicedOccasions.All.Count);
        Assert.Equal(ExpectedIds, VoicedOccasions.All.Select(occasion => occasion.Id).ToArray());
    }

    [Fact]
    public void EverySeatHasExactlyOneOccasionAndTheOccasionNamesItsSeat()
    {
        foreach (var seat in Enum.GetValues<VoiceSeat>())
        {
            var occasion = Assert.Single(VoicedOccasions.All, candidate => candidate.Seat == seat);
            Assert.Same(occasion, VoicedOccasions.For(seat));
            Assert.Same(occasion, VoicedOccasions.TryGetById(occasion.Id));
        }
    }

    [Fact]
    public void TheSeatsAreTheSixTheBookNames()
    {
        Assert.Equal(
            ["TodayTopLine", "RestDayLine", "BodyMonthly", "SignalSentence", "WorkoutEncouragement", "MealSheetIntro"],
            Enum.GetNames<VoiceSeat>());
    }

    [Fact]
    public void AnIdFromAnOlderOrNewerBuildIsUnknownNotAGuess()
    {
        Assert.Null(VoicedOccasions.TryGetById("welcome"));
        Assert.Null(VoicedOccasions.TryGetById(""));
        Assert.Null(VoicedOccasions.TryGetById("TODAY-TOP-LINE"));
    }

    [Fact]
    public void ASeatOutsideTheListIsRefusedNotInvented()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => VoicedOccasions.For((VoiceSeat)99));
        Assert.Contains("closed list", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EachOccasionHasWhatTheComposerNeeds()
    {
        foreach (var occasion in VoicedOccasions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(occasion.Purpose), occasion.Id);
            Assert.InRange(occasion.MaxWords, 10, 40);
            Assert.Matches("^[a-z]+(-[a-z]+)*$", occasion.Id);
        }

        Assert.Equal(VoicedOccasions.All.Count, VoicedOccasions.All.Select(occasion => occasion.Id).Distinct().Count());
        Assert.Equal(VoicedOccasions.All.Count, VoicedOccasions.All.Select(occasion => occasion.Purpose).Distinct().Count());
    }

    /// <summary>
    /// The ids are stored, so each is a string literal. Deriving one from <c>nameof</c> or an enum's
    /// name would let a harmless-looking rename change what is stored, and nothing would notice.
    /// </summary>
    [Fact]
    public void TheIdsAreLiteralsNotDerivedFromNames()
    {
        var source = File.ReadAllText(DomainVoiceFile("VoicedOccasions.cs"));

        // Each entry is written `new(VoiceSeat.X, "the-id", ...)`: the id is the second argument and a literal.
        foreach (var id in ExpectedIds)
        {
            Assert.Matches($@"new\(VoiceSeat\.\w+,\s*""{Regex.Escape(id)}"",", source);
        }

        Assert.Equal(ExpectedIds.Length, Regex.Matches(source, @"new\(VoiceSeat\.").Count);
    }

    // ---- errors are never voiced ----

    [Fact]
    public void ErrorsAreNeverVoiced()
    {
        // No seat, id or purpose reads as trouble: there is no seat for any error.
        foreach (var occasion in VoicedOccasions.All)
        {
            var text = $"{occasion.Seat} {occasion.Id} {occasion.Purpose}";
            var offending = Trouble.FirstOrDefault(word => text.Contains(word, StringComparison.OrdinalIgnoreCase));
            Assert.True(offending is null,
                $"The occasion \"{occasion.Id}\" reads as trouble (\"{offending}\"). Errors are told in plain "
                + "copy and folded into the dog-ear; a voiced failure reads as banter and puts a model call "
                + "on the path of reporting that one failed.");
        }

        // Nothing a seat takes or gives can carry an error: no Exception anywhere on the voice surface.
        var offenders = VoiceSurface()
            .SelectMany(type => ExceptionExposure.Find(type).Select(where => $"{type.FullName}: {where}"))
            .ToList();
        Assert.True(offenders.Count == 0,
            "These voice types accept or expose an error, so an error could reach a seat:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheExceptionCheckSeesAnErrorOnAnySurface()
    {
        Assert.NotEmpty(ExceptionExposure.Find(typeof(LeaksAnError)));
        Assert.NotEmpty(ExceptionExposure.Find(typeof(TakesAnError)));
        Assert.NotEmpty(ExceptionExposure.Find(typeof(ReturnsAnError)));
        Assert.NotEmpty(ExceptionExposure.Find(typeof(HidesAnErrorInAList)));
        Assert.NotEmpty(ExceptionExposure.Find(typeof(AnErrorItself)));
        Assert.Empty(ExceptionExposure.Find(typeof(Clean)));
    }

    [Fact]
    public void TheVoiceSurfaceIsActuallyScanned()
    {
        var names = VoiceSurface().Select(type => type.Name).ToList();
        Assert.Contains(nameof(VoicedOccasions), names);
        Assert.Contains(nameof(SeatComposer), names);
        Assert.Contains(nameof(SeatRequest), names);
        Assert.Contains(nameof(SeatLine), names);
    }

    /// <summary>
    /// Only the composer builds a model request, and it takes the purpose from the closed list.
    /// Source-level, because the next voiced surface will be written by someone reaching for the
    /// nearest constructor, and a bare <c>new MessageRequest(...)</c> there is how an error message
    /// quietly becomes voiced.
    /// </summary>
    [Fact]
    public void OnlyTheSeatComposerBuildsAModelRequest()
    {
        var offenders = RequestBuilders(ConstitutionSource.Root(), "SeatComposer.cs");
        Assert.True(offenders.Count == 0,
            "These build a model request outside SeatComposer, so they voice something without naming a seat:\n  "
            + string.Join("\n  ", offenders));

        var composer = File.ReadAllText(InfrastructureVoiceFile("SeatComposer.cs"));
        Assert.Single(StrayRequest.Matches(composer));
        Assert.Contains("new MessageRequest(occasion.Purpose", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestScanFindsAStrayRequestInAPlantedFile()
    {
        var root = Directory.CreateTempSubdirectory("constitution-voice-");
        try
        {
            var path = Path.Combine(root.FullName, "Stray.cs");
            File.WriteAllText(path, "var request = new MessageRequest(\"a failed sync\");\n");
            var found = Assert.Single(RequestBuilders(root.FullName, "SeatComposer.cs"));
            Assert.StartsWith("Stray.cs", found, StringComparison.Ordinal);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // ---- fallbacks never hide ----

    [Fact]
    public async Task FallbackNeverHides()
    {
        // With no profile and no key, every one of the six answers with its plain sentence: none is
        // hidden, none is blank, and none reaches the model.
        foreach (var occasion in VoicedOccasions.All)
        {
            var client = new RecordingVoiceClient();
            var composer = new SeatComposer(
                new Storyworthtelling.Voice.VoicedMessenger(
                    new FakeProfileStore(Storyworthtelling.Voice.VoiceProfile.Empty), client),
                new Storyworthtelling.Voice.DriveVoiceOptions(
                    new FakeConfigStore(Storyworthtelling.Voice.VoiceConfig.Empty)));

            var line = await composer.ComposeAsync(
                new SeatRequest(occasion.Seat, ["Strength at 5:30", "lentil soup tonight"], "Strength at 5:30. Lentil soup tonight."),
                default);

            Assert.False(string.IsNullOrWhiteSpace(line.Text), occasion.Id);
            Assert.Equal("Strength at 5:30. Lentil soup tonight.", line.Text);
            Assert.False(line.IsVoiced, occasion.Id);
            Assert.Empty(client.Requests);
        }
    }

    [Fact]
    public void TheListCarriesNoHideOption()
    {
        Assert.DoesNotContain(
            typeof(VoicedOccasion).GetProperties().Select(property => property.Name),
            name => name.Contains("Hide", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Fallback", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            Enum.GetNames<VoiceSeat>(), name => name.Contains("Hide", StringComparison.OrdinalIgnoreCase));
    }

    // ---- helpers ----

    private static IReadOnlyList<Type> VoiceSurface() =>
        typeof(VoicedOccasions).Assembly.GetTypes()
            .Concat(typeof(SeatComposer).Assembly.GetTypes())
            .Where(type => type.Namespace is "Constitution.Domain.Voice" or "Constitution.Infrastructure.Voice")
            .Where(type => type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null)
            .ToList();

    private static readonly Regex StrayRequest = new(@"new\s+([\w.]+\.)?MessageRequest\s*\(");

    private static List<string> RequestBuilders(string root, string allowedFileName) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or "Constitution.Tests"))
            .Where(file => !Path.GetFileName(file).Equals(allowedFileName, StringComparison.Ordinal))
            .SelectMany(file => File.ReadAllLines(file)
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    && !line.TrimStart().StartsWith("///", StringComparison.Ordinal)
                    && StrayRequest.IsMatch(line))
                .Select(line => $"{Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')}: {line.Trim()}"))
            .ToList();

    private static string DomainVoiceFile(string name) =>
        Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "Voice", name);

    private static string InfrastructureVoiceFile(string name) =>
        Path.Combine(ConstitutionSource.Root(), "Constitution.Infrastructure", "Voice", name);

    // Deliberately bad (and one clean) shapes for the self-test of the exception check.
    private sealed class LeaksAnError { public Exception? Last { get; set; } }

    private sealed class TakesAnError { public void Voice(string text, InvalidOperationException error) { } }

    private sealed class ReturnsAnError { public Task<Exception> Take() => throw new NotSupportedException(); }

    private sealed class HidesAnErrorInAList { public IReadOnlyList<ArgumentException> Errors { get; } = []; }

    private sealed class AnErrorItself : Exception;

    private sealed class Clean { public string Say(string text, int words) => text; }

    /// <summary>Finds an <see cref="Exception"/> anywhere a type's public or constructor surface touches one.</summary>
    private static class ExceptionExposure
    {
        private const BindingFlags Surface = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static IEnumerable<string> Find(Type type)
        {
            if (typeof(Exception).IsAssignableFrom(type))
            {
                yield return "the type is itself an Exception";
            }

            foreach (var field in type.GetFields(Surface).Where(field => !IsCompilerGenerated(field)))
            {
                if (Mentions(field.FieldType))
                {
                    yield return $"field {field.Name}";
                }
            }

            foreach (var property in type.GetProperties(Surface))
            {
                if (Mentions(property.PropertyType))
                {
                    yield return $"property {property.Name}";
                }
            }

            foreach (var method in type.GetMethods(Surface).Cast<MethodBase>().Concat(type.GetConstructors(Surface)))
            {
                foreach (var parameter in method.GetParameters().Where(parameter => Mentions(parameter.ParameterType)))
                {
                    yield return $"{method.Name}({parameter.Name})";
                }

                if (method is MethodInfo info && Mentions(info.ReturnType))
                {
                    yield return $"{method.Name} returns it";
                }
            }
        }

        private static bool IsCompilerGenerated(FieldInfo field) =>
            field.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;

        private static bool Mentions(Type type) =>
            typeof(Exception).IsAssignableFrom(type)
            || (type.HasElementType && type.GetElementType() is { } element && Mentions(element))
            || (type.IsGenericType && type.GetGenericArguments().Any(Mentions));
    }
}
