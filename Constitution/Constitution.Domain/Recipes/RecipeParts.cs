using System.Text.RegularExpressions;

namespace Constitution.Domain.Recipes;

/// <summary>
/// A recipe's stable id: lower-case words and dashes, never the title, so renaming a card moves no
/// record. It is the id half of the record's key, which the library lower-cases; allowing only what
/// survives that unchanged keeps one card one key.
/// </summary>
public readonly record struct RecipeId
{
    public const int MaxLength = 64;

    private static readonly Regex Shape = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public RecipeId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaxLength || !Shape.IsMatch(value))
        {
            throw new ArgumentException("A recipe id is lower-case letters and digits joined by single dashes, at most " + MaxLength + " long.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// One ingredient line as the card says it: a quantity and unit kept as the words they were written in
/// ("¾", "1 1/2", "cans"), and the name, which is what search reads. Quantity and unit may be absent
/// ("salt").
/// </summary>
public sealed record Ingredient
{
    public const int MaxPartLength = 40;
    public const int MaxNameLength = 300;

    public Ingredient(string? quantity, string? unit, string name)
    {
        Quantity = Clean(quantity);
        Unit = Clean(unit);
        Name = name?.Trim() ?? string.Empty;
        EnsureValid();
    }

    public string? Quantity { get; init; }

    public string? Unit { get; init; }

    public string Name { get; init; }

    /// <summary>Throws <see cref="ArgumentException"/> naming what is wrong. Run again before writing, since <c>with</c> skips the constructor.</summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
        {
            throw new ArgumentException("An ingredient needs a name of 1 to " + MaxNameLength + " characters.", nameof(Name));
        }

        if (Quantity is { Length: > MaxPartLength } || Unit is { Length: > MaxPartLength })
        {
            throw new ArgumentException("An ingredient's quantity and unit are at most " + MaxPartLength + " characters each.", nameof(Quantity));
        }
    }

    private static string? Clean(string? part) => string.IsNullOrWhiteSpace(part) ? null : part.Trim();
}

/// <summary>
/// One step: its words, optionally how long it takes, and whether the cook is busy for that time
/// (<see cref="Active"/>: chopping) or waiting (baking). The two add up differently on the card
/// ("45 min hands-on").
/// </summary>
public sealed record RecipeStep
{
    public const int MaxTextLength = 2000;
    public const int MaxMinutes = 24 * 60;

    public RecipeStep(string text, int? minutes, bool active)
    {
        Text = text?.Trim() ?? string.Empty;
        Minutes = minutes;
        Active = active;
        EnsureValid();
    }

    public string Text { get; init; }

    public int? Minutes { get; init; }

    public bool Active { get; init; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Text) || Text.Length > MaxTextLength)
        {
            throw new ArgumentException("A step needs words of 1 to " + MaxTextLength + " characters.", nameof(Text));
        }

        if (Minutes is { } minutes && (minutes < 1 || minutes > MaxMinutes))
        {
            throw new ArgumentException("A step's minutes are 1 to " + MaxMinutes + ", or left out.", nameof(Minutes));
        }
    }
}

/// <summary>
/// Where a card came from, as the household words it. Only a short label is ever kept — never an
/// address. A link to a publisher's page is not stored content (NYT Cooking's terms bar keeping the
/// recipe, D96), so a label that looks like one is refused rather than quietly kept.
/// </summary>
public static class RecipeSource
{
    /// <summary>Typed in by hand.</summary>
    public const string Typed = "typed";

    /// <summary>Read off a card through Plate; only the words were kept, never the picture.</summary>
    public const string Scanned = "scanned";

    private static readonly Regex Address = new(
        @"://|^\s*www\.|[a-z0-9-]+\.(com|net|org|io|co|uk|edu|gov|app)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>Whether <paramref name="label"/> reads as a web address rather than a few words of the owner's.</summary>
    public static bool LooksLikeAddress(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Address.IsMatch(label);
    }
}
