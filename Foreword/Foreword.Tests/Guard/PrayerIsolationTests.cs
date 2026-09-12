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
    /// And the archive is the only thing that writes prayer content anywhere. A second writer is
    /// the shape "it also syncs to X" takes when somebody is being helpful.
    /// </summary>
    [Fact]
    public void OnlyTheArchiveWritesPrayerContentOut()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(Root(), "Foreword.Infrastructure"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains("/obj/", StringComparison.Ordinal)
                || file.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            var name = Path.GetFileName(file);
            if (name is "DrivePrayerArchive.cs" or "PrayerArchiveCodec.cs"
                or "ClaudePastoralComposer.cs" or "ClaudeJournalTranscriber.cs" or "PrayerPages.cs")
            {
                continue;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\.ReadBy\s*\("))
            {
                offenders.Add($"{name}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These read prayer content and are not one of the four declared readers:\n"
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
