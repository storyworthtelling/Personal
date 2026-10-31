using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.Domain.Plate;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Plate's recipe read as a frozen contract with Constitution (PBI C-11.2.1): the wire names, the
/// component table, the signature permission, and the rule that nothing is kept.
///
/// <para>Constitution holds the same words as literals in its own project and
/// <c>Constitution.Tests/Contracts/RecipeReadContractTests</c> reads this side by content, so
/// neither can drift without a test failing. Every recipe here is invented.</para>
/// </summary>
public class RecipeErrandTests
{
    private static readonly RecipeReading Sample = new(
        "Marigold Lentil Stew",
        ["1 cup red lentils", "½ cup cream"],
        ["Warm the oil.", "Add the onion."]);

    [Fact]
    public void RecipeIsAReadAction()
    {
        Assert.Equal("com.storyworthtelling.plate.action.READ_RECIPE", ReadRequest.ReadRecipe);
        Assert.NotEqual(ReadRequest.ReadRecipe, ReadRequest.ReadCard);
        Assert.NotEqual(ReadRequest.ReadRecipe, ReadRequest.ReadStatement);
        Assert.Equal(Errand.ReadRecipe, Errands.For(ReadRequest.ReadRecipe, null));
        Assert.True(Errands.HasAWaitingCaller(Errand.ReadRecipe));
    }

    [Fact]
    public void TheComponentNamesAreFrozenLiterals()
    {
        Assert.Equal(PolicyContext.PlatePackage + ".RecipeReadActivity", ReadRequest.RecipeActivity);
        Assert.Equal(PolicyContext.PlatePackage + ".READ_RECIPE", ReadRequest.RecipePermission);
        Assert.Equal("title", ReadRequest.TitleExtra);
        Assert.Equal("ingredients", ReadRequest.IngredientsExtra);
        Assert.Equal("steps", ReadRequest.StepsExtra);
    }

    [Fact]
    public void RecipeCrossesAsThreeStrings()
    {
        var extras = ReadRequest.RecipeExtras(Sample);

        Assert.NotNull(extras);
        Assert.Equal(
            new[] { "ingredients", "steps", "title" },
            extras!.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Marigold Lentil Stew", extras[ReadRequest.TitleExtra]);
        Assert.Equal(Sample.Ingredients, JsonSerializer.Deserialize<string[]>(extras[ReadRequest.IngredientsExtra]));
        Assert.Equal(Sample.Steps, JsonSerializer.Deserialize<string[]>(extras[ReadRequest.StepsExtra]));
    }

    [Fact]
    public void NoExtraCanHoldAnImageOrAUri()
    {
        // The recipe photo is not kept by Plate or by Constitution: only the words cross.
        var extras = ReadRequest.RecipeExtras(Sample)!;

        Assert.All(extras.Values, value =>
        {
            Assert.DoesNotContain("content://", value, StringComparison.Ordinal);
            Assert.DoesNotContain("file://", value, StringComparison.Ordinal);
            Assert.DoesNotContain(".jpg", value, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void EmptyTitleIsLeftOut()
    {
        var extras = ReadRequest.RecipeExtras(Sample with { Title = string.Empty });

        Assert.NotNull(extras);
        Assert.False(extras!.ContainsKey(ReadRequest.TitleExtra));
        Assert.True(extras.ContainsKey(ReadRequest.IngredientsExtra));
    }

    [Fact]
    public void NoIngredientsOrStepsSendsNothing()
    {
        // A title alone is not a recipe. Errs toward sending nothing the owner has to throw away.
        Assert.Null(ReadRequest.RecipeExtras(new RecipeReading("Marigold Lentil Stew", [], [])));
        Assert.Null(ReadRequest.RecipeExtras(new RecipeReading(string.Empty, [], [])));
    }

    [Fact]
    public void WordsKeepTheirCharactersOnTheWire()
    {
        var extras = ReadRequest.RecipeExtras(Sample)!;

        Assert.Contains("½ cup cream", extras[ReadRequest.IngredientsExtra], StringComparison.Ordinal);
    }

    [Fact]
    public void RecipeIsDeclared()
    {
        var declaring = PlateSources()
            .Where(file => IntentFilters(File.ReadAllText(file)).Any(filter =>
                filter.Contains("ReadRequest.ReadRecipe", StringComparison.Ordinal)
                || filter.Contains(ReadRequest.ReadRecipe, StringComparison.Ordinal)))
            .ToList();

        // Android silently omits a component that is missing, so one declaration, and exactly one.
        var file = Assert.Single(declaring);
        var source = File.ReadAllText(file);

        Assert.Contains("Name = ReadRequest.RecipeActivity", source, StringComparison.Ordinal);
        Assert.Contains("Permission = ReadRequest.RecipePermission", source, StringComparison.Ordinal);
        Assert.Contains("Exported = true", source, StringComparison.Ordinal);
        Assert.Contains("NoHistory = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenErrandDoesNotAnswerRecipe()
    {
        // The camera filters on ErrandActivity must stay public, so the recipe read lives on its
        // own protected component rather than as one more action on the open one.
        var errand = File.ReadAllText(Path.Combine(PlateRoot(), "ErrandActivity.cs"));

        Assert.DoesNotContain(IntentFilters(errand), filter =>
            filter.Contains("ReadRecipe", StringComparison.Ordinal));
    }

    [Fact]
    public void PermissionIsSignatureLevel()
    {
        var declared = PlateSources().Select(File.ReadAllText).Any(source =>
            Regex.IsMatch(
                source,
                @"\[assembly:\s*Permission\(\s*Name\s*=\s*ReadRequest\.RecipePermission\s*,\s*ProtectionLevel\s*=\s*Protection\.Signature\s*\)\]",
                RegexOptions.None, TimeSpan.FromSeconds(2)));

        Assert.True(declared,
            "Plate does not declare the READ_RECIPE permission at signature level, so any app on the phone could ask it to read.");
    }

    [Fact]
    public void RecipeReadKeepsNothing()
    {
        // The Darkroom spine says nothing leaves Plate unchosen and nothing stays once chosen; a
        // recipe read is neither a capture nor a keeper. No MediaStore row, no Darkroom entry, no
        // image or URI handed back, and the frame it recognised is deleted afterwards.
        var errand = File.ReadAllText(Path.Combine(PlateRoot(), "ErrandActivity.cs"));
        var recipe = CodeOnly(File.ReadAllText(Path.Combine(PlateRoot(), "RecipeReadActivity.cs")));

        Assert.DoesNotContain("Darkroom", recipe, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaStore", recipe, StringComparison.Ordinal);
        Assert.DoesNotContain("DarkroomStore", errand, StringComparison.Ordinal);

        // MediaStore is used in exactly one place in the errand: the Photo path's Delivered().
        var delivered = MethodBody(errand, "private void Delivered(");
        Assert.Equal(
            Regex.Matches(errand, "MediaStore", RegexOptions.None, TimeSpan.FromSeconds(2)).Count,
            Regex.Matches(delivered, "MediaStore", RegexOptions.None, TimeSpan.FromSeconds(2)).Count);

        var send = MethodBody(errand, "private void SendReading(");
        Assert.DoesNotContain("SetData(", send, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClipData(", send, StringComparison.Ordinal);
        Assert.DoesNotContain("GrantReadUriPermission", send, StringComparison.Ordinal);
        Assert.DoesNotContain("Parcelable", send, StringComparison.Ordinal);
        Assert.DoesNotContain("File", send, StringComparison.Ordinal);

        // Declaration, plus the success and failure paths that finish with the recognised frame.
        Assert.True(
            Regex.Matches(errand, @"DiscardFrame\(", RegexOptions.None, TimeSpan.FromSeconds(2)).Count >= 3,
            "The frame a read recognised must be deleted on every path out of the read.");
    }

    /// <summary>The source without its comment lines, so a guard reads what the code does and not what it says.</summary>
    private static string CodeOnly(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string PlateRoot() => Path.Combine(RepoRoot(), "FocusPhone", "Plate.Android");

    private static IEnumerable<string> PlateSources() =>
        Directory.EnumerateFiles(PlateRoot(), "*.cs", SearchOption.TopDirectoryOnly);

    private static IEnumerable<string> IntentFilters(string source)
    {
        const string Attribute = "[IntentFilter(";
        var index = source.IndexOf(Attribute, StringComparison.Ordinal);
        while (index >= 0)
        {
            var depth = 0;
            var end = index + Attribute.Length - 1;
            for (; end < source.Length; end++)
            {
                if (source[end] == '(')
                {
                    depth++;
                }
                else if (source[end] == ')' && --depth == 0)
                {
                    break;
                }
            }

            yield return source[index..Math.Min(end + 1, source.Length)];
            index = source.IndexOf(Attribute, Math.Min(end + 1, source.Length), StringComparison.Ordinal);
        }
    }

    /// <summary>The text from the signature to the end of the matching brace, so a guard names one method.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} was not found.");
        var open = source.IndexOf('{', start);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"{signature} has no closing brace.");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repo root (.github) above " + AppContext.BaseDirectory);
    }
}
