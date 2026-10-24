using System.Text.RegularExpressions;

namespace Codex.Domain.Plate;

/// <summary>
/// What a recipe card gave up: a title, the ingredient lines and the step lines, all as words.
/// Any part may be empty; none of them may be a guess. There is no image here and no way to carry
/// one, which is the point (PBI C-11.2.1): Constitution keeps the words the owner confirms and
/// nothing of the photograph.
/// </summary>
public sealed record RecipeReading(string Title, IReadOnlyList<string> Ingredients, IReadOnlyList<string> Steps)
{
    public bool IsEmpty => Title.Length == 0 && Ingredients.Count == 0 && Steps.Count == 0;
}

/// <summary>
/// Reading a recipe card out of recognised text, across as many pages as it took.
///
/// <para>Like <see cref="BusinessCard"/> it <b>errs toward leaving a part empty</b>. The owner
/// confirms the words in Constitution before any are kept, and a wrong line that looks right is
/// the one nobody re-reads. So a title is taken only when something recognisable follows it, a
/// line is an ingredient only under an Ingredients heading or when it opens with a quantity, and a
/// step is only what sits under a Directions heading or reads as a sentence.</para>
///
/// <para>The lines of every page arrive in order as one list. A page that repeats a heading simply
/// switches section again; nothing already read is discarded.</para>
/// </summary>
public static partial class RecipeRead
{
    private enum Section
    {
        Preamble,
        Ingredients,
        Steps,
    }

    [GeneratedRegex(@"^ingredients?\s*:?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex IngredientsHeading();

    [GeneratedRegex(@"^(directions?|method|instructions?|preparation|steps?|procedure)\s*:?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex StepsHeading();

    // Servings and times. Needs a digit, so "Cook until soft" is never mistaken for one.
    [GeneratedRegex(@"^(serves|servings?|yield|makes|prep|cook|total|ready)\b.*\d",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex MetaLine();

    [GeneratedRegex(@"^\d{1,3}$", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex PageNumber();

    // "1." "2)" "Step 3:" — a digit then punctuation then a space, so "1.5 cups" is not a step.
    [GeneratedRegex(@"^(?:step\s*)?\d{1,2}\s*[.)\]:\-]\s+(?<text>\S.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex StepMarker();

    [GeneratedRegex(@"^(\d|[¼-¾⅐-⅞]|(a|one)\s+(pinch|dash|handful|knob|splash|drizzle)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex QuantityStart();

    [GeneratedRegex(@"^(from|by|adapted|source|copyright|recipe from)\b|@|^(https?://|www\.)|©",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex NotATitle();

    [GeneratedRegex(@"(,|-|\band|\bor|\bof|\bfor|\bwith|\bplus|\bto|\ba|\bthe)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex Trails();

    private const int LongestTitleWords = 8;
    private const int LongestTitleLetters = 80;
    private const int ShortestLooseStepWords = 5;

    public static RecipeReading Read(IReadOnlyList<string> lines)
    {
        var cleaned = Clean(lines);

        var section = Section.Preamble;
        var sawIngredients = false;
        var sawSteps = false;
        var preamble = new List<string>();
        var ingredientLines = new List<string>();
        var stepLines = new List<string>();

        foreach (var line in cleaned)
        {
            if (IngredientsHeading().IsMatch(line))
            {
                section = Section.Ingredients;
                sawIngredients = true;
                continue;
            }

            if (StepsHeading().IsMatch(line))
            {
                section = Section.Steps;
                sawSteps = true;
                continue;
            }

            switch (section)
            {
                case Section.Preamble:
                    preamble.Add(line);
                    break;
                case Section.Ingredients:
                    if (!MetaLine().IsMatch(line))
                    {
                        ingredientLines.Add(line);
                    }

                    break;
                default:
                    stepLines.Add(line);
                    break;
            }
        }

        var ingredients = JoinWrappedIngredients(ingredientLines);
        var steps = BuildSteps(stepLines);

        // No Ingredients heading: only a line that opens with a quantity can be one.
        if (!sawIngredients)
        {
            ingredients.AddRange(preamble.Where(IsQuantityLine));
        }

        // No Directions heading: only what reads as a whole sentence can be a step.
        if (!sawSteps)
        {
            var loose = preamble.Where(line => !IsQuantityLine(line) && !MetaLine().IsMatch(line)).Skip(1);
            steps.AddRange(BuildSteps(loose.ToList()).Where(IsLooseStep));
        }

        var title = ingredients.Count > 0 || steps.Count > 0 ? TitleFrom(preamble) : string.Empty;
        return new RecipeReading(title, ingredients, steps);
    }

    private static List<string> Clean(IReadOnlyList<string>? lines) =>
        lines is null
            ? []
            : lines.Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                // A lone number is a page number. The words around it are untouched.
                .Where(line => !PageNumber().IsMatch(line))
                .ToList();

    // Only the first line of the card is a candidate. Reaching further down for "the next line
    // that looks like a title" is how a wrong title gets chosen.
    private static string TitleFrom(List<string> preamble)
    {
        var first = preamble.FirstOrDefault(line => !MetaLine().IsMatch(line));
        if (first is null
            || NotATitle().IsMatch(first)
            || IsQuantityLine(first)
            || StepMarker().IsMatch(first)
            || first.Length > LongestTitleLetters
            || WordCount(first) > LongestTitleWords
            || first[^1] is '.' or '!' or '?' or ',' or ':' or ';')
        {
            return string.Empty;
        }

        return first;
    }

    private static bool IsQuantityLine(string line) =>
        QuantityStart().IsMatch(line) && !StepMarker().IsMatch(line);

    private static List<string> JoinWrappedIngredients(List<string> lines)
    {
        var joined = new List<string>();
        foreach (var line in lines)
        {
            var wrapped = joined.Count > 0
                && !IsQuantityLine(line)
                && (Trails().IsMatch(joined[^1]) || HasOpenBracket(joined[^1]));
            if (wrapped)
            {
                joined[^1] = joined[^1] + " " + line;
            }
            else
            {
                joined.Add(line);
            }
        }

        return joined;
    }

    private static bool HasOpenBracket(string line) => line.Count(c => c == '(') > line.Count(c => c == ')');

    private static List<string> BuildSteps(List<string> lines)
    {
        var numbered = lines.Any(line => StepMarker().IsMatch(line));
        var steps = new List<string>();
        var open = false;

        foreach (var line in lines)
        {
            var marker = StepMarker().Match(line);
            if (marker.Success)
            {
                steps.Add(marker.Groups["text"].Value.Trim());
                open = true;
                continue;
            }

            // A line with no number is the rest of the step above it. Without numbers, a step
            // ends where a sentence does.
            var continues = open && steps.Count > 0 && (numbered || !EndsASentence(steps[^1]));
            if (continues)
            {
                steps[^1] = steps[^1] + " " + line;
            }
            else
            {
                steps.Add(line);
                open = true;
            }
        }

        return steps;
    }

    private static bool EndsASentence(string text) => text.Length > 0 && text[^1] is '.' or '!' or '?';

    private static bool IsLooseStep(string step) =>
        EndsASentence(step) && WordCount(step) >= ShortestLooseStepWords;

    private static int WordCount(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
