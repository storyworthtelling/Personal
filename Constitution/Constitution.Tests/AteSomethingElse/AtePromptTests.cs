using System.Reflection;
using Constitution.Domain.Meals;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>What is asked of the model: a fixed question and the photo, with nothing about anyone in it (D98, D65).</summary>
public class AtePromptTests
{
    private static readonly string[] PersonalWords =
    [
        "sleep", "heart", "bpm", "steps", "weight", "workout", "calendar", "recovery", "pillar", "household", "owner", "partner",
        "wife", "husband", "email", "address", "@", "http", "password", "token", "passphrase", "body reading",
    ];

    [Fact]
    public void ThePromptHoldsNoPersonalData()
    {
        var text = (AtePrompt.System + " " + AtePrompt.Question).ToLowerInvariant();

        Assert.DoesNotContain(PersonalWords, word => text.Contains(word, StringComparison.Ordinal));
    }

    [Fact]
    public void ThePromptHasNoDigitsToCarryAFigureIn()
    {
        Assert.DoesNotContain((AtePrompt.System + AtePrompt.Question), char.IsDigit);
    }

    [Fact]
    public void ThePromptIsFixedTextNotABuilder()
    {
        // Nothing can be interpolated into it: every member is a constant string and there is no method that takes a value.
        var members = typeof(AtePrompt).GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is not MemberTypes.Constructor)
            .ToList();

        Assert.NotEmpty(members);
        Assert.All(members, member =>
        {
            var field = Assert.IsAssignableFrom<FieldInfo>(member);
            Assert.True(field.IsLiteral, field.Name + " is not a constant");
            Assert.Equal(typeof(string), field.FieldType);
        });
    }

    [Fact]
    public void ThePromptAsksForRangesAndIgnoresPeople()
    {
        var text = AtePrompt.System.ToLowerInvariant();

        Assert.Contains("range", text, StringComparison.Ordinal);
        Assert.Contains("people", text, StringComparison.Ordinal);
        Assert.Contains("ignore", text, StringComparison.Ordinal);
        Assert.Contains("is_meal", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptNamesNoModel()
    {
        var text = (AtePrompt.System + AtePrompt.Question).ToLowerInvariant();

        Assert.DoesNotContain("claude-", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sonnet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("opus", text, StringComparison.Ordinal);
        Assert.DoesNotContain("haiku", text, StringComparison.Ordinal);
    }
}
