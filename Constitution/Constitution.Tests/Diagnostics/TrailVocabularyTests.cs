using System.Text;
using System.Text.RegularExpressions;
using Constitution.Domain.Diagnostics;
using Constitution.Domain.Body;
using Constitution.Infrastructure.SleepNumber;
using Constitution.Infrastructure.Vault;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// Review S8: the trail filter keeps only words the app itself writes, so it is only as good as its list. This ties the
/// list to the writers: every word in a phrase handed to a trail's <c>Outcome(</c> anywhere in Constitution, and every
/// enum name a phrase prints, must be in <see cref="TrailLines.Vocabulary"/>. A new word in a trail phrase fails here
/// until somebody decides it may leave the phone.
/// </summary>
public class TrailVocabularyTests
{
    private static readonly Regex Letters = new("[A-Za-z]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void EveryWordAPhraseWritesIsInTheVocabulary()
    {
        var phrases = TrailPhrases().ToList();
        Assert.True(phrases.Count > 40, "The scan found only " + phrases.Count + " trail phrases; it should read every writer.");

        var missing = phrases
            .SelectMany(phrase => Letters.Matches(phrase).Select(match => match.Value))
            .Where(word => !TrailLines.Vocabulary.Contains(word.ToLowerInvariant()) && !IsTypeNameOrConstant(word))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, "Trail phrases use words the vocabulary lacks:\n" + string.Join(", ", missing));
    }

    [Fact]
    public void EveryEnumNameAPhrasePrintsIsInTheVocabulary()
    {
        // Printed lower-cased by the writers: ToString().ToLowerInvariant().
        var printed = new[]
        {
            typeof(DiagnosticsOutcome), typeof(SleepIqFailure), typeof(HealthReadOutcome), typeof(VaultRunStatus), typeof(BedSide),
        }.SelectMany(type => Enum.GetNames(type)).Select(name => name.ToLowerInvariant()).Distinct().ToList();

        var missing = printed.Where(name => !TrailLines.Vocabulary.Contains(name)).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, "Enum names a trail prints are missing from the vocabulary:\n" + string.Join(", ", missing));
    }

    [Fact]
    public void NoPhrasePrintsAnEnumInItsCapitalisedSpelling()
    {
        // The filter reads a capital as a possible name; the writers lower-case the enums they print.
        var offences = new List<string>();
        foreach (var file in ConstitutionSource.Files(".cs"))
        {
            var relative = ConstitutionSource.Relative(file);
            if (relative.StartsWith("Constitution.Tests/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match call in Regex.Matches(text, @"(?<!void )\bOutcome\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            {
                var region = CallRegion(text, call.Index + call.Length);
                if (region is not null
                    && Regex.IsMatch(region, @"\.ToString\(\)(?!\.ToLowerInvariant\(\))", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
                {
                    offences.Add(relative);
                }
            }
        }

        Assert.True(offences.Count == 0, "Lower-case the enum a trail phrase prints in:\n" + string.Join("\n", offences.Distinct()));
    }

    [Fact]
    public void ThePlatformsErrorReasonIsPrintedLowerCase()
    {
        var text = File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file).EndsWith("Health/HealthConnectSource.cs", StringComparison.Ordinal)));

        Assert.Contains("reason.ToString().ToLowerInvariant()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySignalIdIsInTheVocabulary()
    {
        foreach (var signal in BodySignals.All)
        {
            foreach (var part in BodySignals.Id(signal).Split('-'))
            {
                Assert.Contains(part, TrailLines.Vocabulary);
            }
        }
    }

    [Fact]
    public void TheVocabularyHoldsNoNumberNoCapitalAndNoPrivateWord()
    {
        foreach (var word in TrailLines.Vocabulary)
        {
            Assert.Matches("^[a-z]+$", word);
        }

        // Words that would only ever come from a person or a reading. Assembled so this file is not what holds them.
        string[] never = ["kg", "lb", "lbs", "bpm", "kcal", "email", "name", "password", "birthday", "calorie", "sam", "lee", "robin", "juniper"];
        Assert.Empty(TrailLines.Vocabulary.Intersect(never));
    }

    [Fact]
    public void TheWithheldMarkerIsMadeOfVocabulary()
    {
        foreach (var word in Letters.Matches(TrailLines.Withheld).Select(match => match.Value))
        {
            Assert.Contains(word, TrailLines.Vocabulary);
        }
    }

    // A type name the filter allows by pattern, or an exact constant it allows.
    private static bool IsTypeNameOrConstant(string word) =>
        word.EndsWith("Exception", StringComparison.Ordinal) || word.EndsWith("Error", StringComparison.Ordinal)
        || word is "NoError" or "NoResponse" or "sleepData" or "Info" or "Warn";

    // ---- reading the writers ----

    private static IEnumerable<string> TrailPhrases()
    {
        foreach (var file in ConstitutionSource.Files(".cs"))
        {
            var relative = ConstitutionSource.Relative(file);
            if (relative.StartsWith("Constitution.Tests/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            foreach (Match call in Regex.Matches(text, @"(?<!void )\bOutcome\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            {
                var region = CallRegion(text, call.Index + call.Length);
                if (region is not null)
                {
                    yield return LiteralText(region);
                }
            }

            // The reasons the bed client gives for a reply in the wrong shape, and the ids of the voiced seats.
            foreach (Match reason in Regex.Matches(text, "reason = \"([^\"]*)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            {
                yield return reason.Groups[1].Value;
            }

            if (relative.EndsWith("Voice/VoicedOccasions.cs", StringComparison.Ordinal))
            {
                foreach (Match id in Regex.Matches(text, "new\\(VoiceSeat\\.\\w+, \"([a-z-]+)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
                {
                    yield return id.Groups[1].Value;
                }
            }

            // The helpers that turn an outcome into a phrase return their literals from a switch.
            foreach (Match helper in Regex.Matches(text, @"static string (?:Words|LogPhrase)\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            {
                foreach (Match literal in Regex.Matches(text[helper.Index..], "return \\$?\"([^\"]*)\";", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
                {
                    yield return LiteralText("\"" + Regex.Replace(literal.Groups[1].Value, @"\{[^}]*\}", " ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)) + "\"");
                }
            }
        }
    }

    /// <summary>The text between an opening parenthesis already consumed and its match, strings respected.</summary>
    private static string? CallRegion(string text, int start)
    {
        var depth = 1;
        for (var index = start; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '"':
                    index = SkipString(text, index);
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        return text[start..index];
                    }

                    break;
            }
        }

        return null;
    }

    private static int SkipString(string text, int quote)
    {
        for (var index = quote + 1; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
            }
            else if (text[index] == '{' && index + 1 < text.Length && text[index + 1] != '{' && IsInterpolated(text, quote))
            {
                index = SkipBraces(text, index);
            }
            else if (text[index] == '"')
            {
                return index;
            }
        }

        return text.Length - 1;
    }

    private static bool IsInterpolated(string text, int quote) => quote > 0 && text[quote - 1] == '$';

    private static int SkipBraces(string text, int open)
    {
        var depth = 0;
        for (var index = open; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
            else if (text[index] == '"')
            {
                index = SkipString(text, index);
            }
        }

        return text.Length - 1;
    }

    /// <summary>The literal words of a call's arguments: text outside interpolations, plus any string literal inside one.</summary>
    private static string LiteralText(string region)
    {
        var words = new StringBuilder();
        for (var index = 0; index < region.Length; index++)
        {
            if (region[index] != '"')
            {
                continue;
            }

            var interpolated = index > 0 && region[index - 1] == '$';
            for (index++; index < region.Length && region[index] != '"'; index++)
            {
                if (region[index] == '\\')
                {
                    index++;
                }
                else if (interpolated && region[index] == '{' && index + 1 < region.Length && region[index + 1] != '{')
                {
                    var end = SkipBraces(region, index);
                    words.Append(' ').Append(LiteralText(region[index..Math.Min(end + 1, region.Length)]));
                    index = end;
                }
                else
                {
                    words.Append(region[index]);
                }
            }

            words.Append(' ');
        }

        return words.ToString();
    }
}
