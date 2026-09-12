using System.Text.RegularExpressions;
using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// Prayer content is isolated by <b>type</b>, not by discipline (D15, guard G8).
///
/// <para>The three public composers — the brief's opening, the calendar's narrator, the checklist's
/// coach — must be structurally unable to receive it, and the writers that push the morning out to
/// Google must never see it either. A rule would hold until the morning somebody forgot, and the
/// failure would be silent: a prompt carrying one extra paragraph returns a perfectly good
/// sentence, and nothing on the page would look wrong.</para>
/// </summary>
public class PrayerIsolationTests
{
    /// <summary>
    /// The one hole a type cannot close. Interpolate anything into a prompt and C# calls
    /// <c>ToString</c> for you, with no cast, no warning and nothing to review. So the answer is
    /// that the string form is not the text.
    /// </summary>
    [Fact]
    public void TheTextIsNeverWhatInterpolationPutsInAPrompt()
    {
        var prayer = PrayerText.Of("Dad — the scan on the 14th");

        var prompt = $"Write one line for this morning. Context: {prayer}";

        Assert.DoesNotContain("scan", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dad", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PrayerText.Redacted, prompt);
    }

    /// <summary>String.Format and ToString() reach the same method, so both are pinned.</summary>
    [Fact]
    public void NorWhatStringFormatPutsInOne()
    {
        var prayer = PrayerText.Of("Still turning over whether to say something to Dan.");

        Assert.DoesNotContain("Dan", string.Format("{0}", prayer));
        Assert.DoesNotContain("Dan", prayer.ToString());
    }

    /// <summary>
    /// The text comes out only for a declared reader. A <c>null</c> reader is the shape a leak
    /// takes when somebody wants the string and has nothing to pass — so it throws rather than
    /// obliging.
    /// </summary>
    [Fact]
    public void TheTextComesOutOnlyForADeclaredReader()
    {
        var prayer = PrayerText.Of("The Kellers, moving");

        Assert.Equal("The Kellers, moving", prayer.ReadBy(new PermittedReader()));
        Assert.Throws<ArgumentNullException>(() => prayer.ReadBy(null!));
    }

    /// <summary>
    /// And the list of readers is enumerated here rather than trusted. A fifth implementation is
    /// not a bug in itself — it is a decision, and this is what makes it one.
    /// </summary>
    [Fact]
    public void OnlyTheDeclaredFewReadIt()
    {
        var readers = new[]
            {
                typeof(PrayerText).Assembly,
                typeof(Infrastructure.Pdf.PdfComposer).Assembly,
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IPrayerReader).IsAssignableFrom(type))
            .Where(type => type is { IsInterface: false, IsAbstract: false })
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "ClaudeJournalTranscriber",
                "ClaudePastoralComposer",
                "DrivePrayerArchive",
                "PrayerPages",
            },
            readers);
    }

    /// <summary>
    /// The public composers and the Google writers do not so much as name a prayer type. This is
    /// the structural half: the runtime guards above say a leak would carry nothing, and this says
    /// there is no wire to leak down.
    /// </summary>
    [Fact]
    public void NoPublicComposerOrGoogleWriterNamesAPrayerType()
    {
        string[] publicSurfaces =
        [
            "Foreword.Infrastructure/Pdf/PdfComposer.cs",
            "Foreword.Infrastructure/Pdf/ForewordPage.cs",
            "Foreword.Infrastructure/Pdf/DayPdfComposer.cs",
            "Foreword.Infrastructure/Pdf/ChecklistPage.cs",
            "Foreword.Infrastructure/Tasks/ClaudeTaskReader.cs",
            "Foreword.Infrastructure/Handwriting/ClaudeVisionHandwritingExtractor.cs",
            "Foreword.Infrastructure/Google/GoogleTasksClient.cs",
            "Foreword.Infrastructure/Google/GoogleCalendarClient.cs",
        ];

        var offenders = new List<string>();

        foreach (var relative in publicSurfaces)
        {
            var path = Path.Combine(Root(), relative);
            Assert.True(File.Exists(path), $"{relative} has moved; this guard is now watching nothing.");

            var source = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(source, @"\b(PrayerText|PrayerDocument|JournalEntry|IPrayerReader|PrayerRequest)\b"))
            {
                offenders.Add($"{relative}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These compose a public prompt or write to Google, and a prayer type has reached them:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// The pastoral composer does not go through the shared voice stack.
    ///
    /// <para>The voice profile is one file every app in the suite reads. A composer that reached the
    /// journal <i>through</i> it would be a path from the most private thing the owner writes into
    /// the most widely shared file they have — and it would look, in code, exactly like reuse.</para>
    /// </summary>
    [Fact]
    public void ThePastoralComposerDoesNotGoThroughTheSharedVoiceStack()
    {
        var source = File.ReadAllText(Path.Combine(
            Root(), "Foreword.Infrastructure/Prayer/ClaudePastoralComposer.cs"));

        foreach (Match match in Regex.Matches(
            source, @"^\s*(?:using|.*\bnew\s+)?.*\b(VoicedMessenger|IVoiceProfileStore|IVoiceClient|VoiceProfile)\b",
            RegexOptions.Multiline))
        {
            // Its own doc comment names them to say it does not use them, which is the opposite of
            // a leak — so only real code counts.
            var line = match.Value.TrimStart();
            Assert.True(line.StartsWith("///", StringComparison.Ordinal),
                $"the pastoral composer reaches the shared voice stack: {line}");
        }
    }

    /// <summary>
    /// Every place in the app that reads prayer content, enumerated by <b>source</b> rather than by
    /// reflection.
    ///
    /// <para>The reflective check above can only see the two assemblies a test can load, and the
    /// phone is not one of them — the rotations editor reads the names on a list, so there is a
    /// fifth reader that the reflective test is structurally unable to notice. This one reads the
    /// repository, which is the only way to see all of them.</para>
    ///
    /// <para>A sixth entry here is not a bug in itself. It is a decision, and this is what makes it
    /// one instead of a diff nobody looked twice at — which is how "while we are here, let us also
    /// send it to X" gets written.</para>
    /// </summary>
    [Fact]
    public void EveryPlaceThatReadsPrayerContentIsOneOfTheDeclaredFew()
    {
        string[] allowed =
        [
            // The four in Domain and Infrastructure.
            "DrivePrayerArchive.cs",
            "PrayerArchiveCodec.cs",
            "ClaudePastoralComposer.cs",
            "ClaudeJournalTranscriber.cs",
            "PrayerPages.cs",

            // And the one on the phone: the screen that edits the standing lists.
            "RotationEditPage.xaml.cs",
        ];

        var offenders = new List<string>();

        foreach (var project in new[] { "Foreword.Infrastructure", "Foreword.Android", "Foreword.Domain" })
        {
            foreach (var file in Directory.EnumerateFiles(
                Path.Combine(Root(), project), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = Path.GetFileName(file);

                // PrayerText itself is where ReadBy is defined.
                if (allowed.Contains(name) || name == "PrayerText.cs")
                {
                    continue;
                }

                if (Regex.IsMatch(File.ReadAllText(file), @"\.ReadBy\s*\("))
                {
                    offenders.Add($"{project}/{name}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These read prayer content and are not one of the declared readers:\n"
            + string.Join("\n", offenders));
    }

    private sealed class PermittedReader : IPrayerReader
    {
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Foreword.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
