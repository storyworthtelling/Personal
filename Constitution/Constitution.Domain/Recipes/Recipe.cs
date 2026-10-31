namespace Constitution.Domain.Recipes;

/// <summary>
/// One card in the recipe box: the household's own words for a dish. It holds <b>words only</b> — a
/// title, ingredient lines, step lines — never an image, a file or an address to a publisher's page
/// (<see cref="RecipeSource"/>). Created time and the rating summary are deliberately not here yet;
/// ratings are records of their own (<c>rating</c>), and a field added later is carried through edits by
/// the household library until this build learns it.
///
/// <para>Equality compares the lists by content, since a record's own equality would compare the list
/// objects and a card read back from the file would never equal the one written.</para>
/// </summary>
public sealed record Recipe
{
    public const int MaxTitleLength = 200;
    public const int MaxLines = 200;
    public const int MaxServings = 100;
    public const int MaxSourceLabelLength = 80;

    public Recipe(
        RecipeId id,
        string title,
        Cuisine cuisine,
        IEnumerable<Ingredient> ingredients,
        IEnumerable<RecipeStep> steps,
        int? servings,
        string? sourceLabel,
        bool forKids)
    {
        ArgumentNullException.ThrowIfNull(ingredients);
        ArgumentNullException.ThrowIfNull(steps);
        Id = id;
        Title = title?.Trim() ?? string.Empty;
        Cuisine = cuisine;
        Ingredients = ingredients.ToArray();
        Steps = steps.ToArray();
        Servings = servings;
        SourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? null : sourceLabel.Trim();
        ForKids = forKids;
        EnsureValid();
    }

    public RecipeId Id { get; init; }

    public string Title { get; init; }

    public Cuisine Cuisine { get; init; }

    public IReadOnlyList<Ingredient> Ingredients { get; init; }

    public IReadOnlyList<RecipeStep> Steps { get; init; }

    public int? Servings { get; init; }

    /// <summary>"typed", "scanned" or a few words the owner typed ("Grandma's card"); never an address.</summary>
    public string? SourceLabel { get; init; }

    /// <summary>Whether the children eat this one (the planner's kids filter reads it).</summary>
    public bool ForKids { get; init; }

    /// <summary>Minutes the cook is busy for: the timed steps marked active.</summary>
    public int HandsOnMinutes => Steps.Where(step => step.Active && step.Minutes is not null).Sum(step => step.Minutes!.Value);

    /// <summary>
    /// Throws <see cref="ArgumentException"/> naming what is wrong. Run by the constructor and again before
    /// anything is written, because a <c>with</c> expression builds a card without passing through it.
    /// </summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Length > MaxTitleLength)
        {
            throw new ArgumentException("A recipe needs a title of 1 to " + MaxTitleLength + " characters.", nameof(Title));
        }

        if (Ingredients is null || Ingredients.Count > MaxLines)
        {
            throw new ArgumentException("A recipe holds at most " + MaxLines + " ingredient lines.", nameof(Ingredients));
        }

        if (Steps is null || Steps.Count > MaxLines)
        {
            throw new ArgumentException("A recipe holds at most " + MaxLines + " steps.", nameof(Steps));
        }

        if (Servings is { } servings && (servings < 1 || servings > MaxServings))
        {
            throw new ArgumentException("Servings are 1 to " + MaxServings + ", or left out.", nameof(Servings));
        }

        if (SourceLabel is { } label)
        {
            if (label.Length > MaxSourceLabelLength)
            {
                throw new ArgumentException("A source label is at most " + MaxSourceLabelLength + " characters.", nameof(SourceLabel));
            }

            if (RecipeSource.LooksLikeAddress(label))
            {
                throw new ArgumentException("A source label is a few words, never a web address.", nameof(SourceLabel));
            }
        }

        foreach (var ingredient in Ingredients)
        {
            ingredient.EnsureValid();
        }

        foreach (var step in Steps)
        {
            step.EnsureValid();
        }
    }

    public bool Equals(Recipe? other) =>
        other is not null
        && Id == other.Id
        && Title == other.Title
        && Cuisine == other.Cuisine
        && Servings == other.Servings
        && SourceLabel == other.SourceLabel
        && ForKids == other.ForKids
        && Ingredients.SequenceEqual(other.Ingredients)
        && Steps.SequenceEqual(other.Steps);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Title);
        hash.Add(Cuisine);
        hash.Add(Servings);
        hash.Add(SourceLabel);
        hash.Add(ForKids);
        foreach (var ingredient in Ingredients)
        {
            hash.Add(ingredient);
        }

        foreach (var step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => nameof(Recipe) + "(" + Id.Value + ")";
}
