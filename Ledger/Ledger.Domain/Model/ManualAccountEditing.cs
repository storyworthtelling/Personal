using Index.Domain.Time;

namespace Index.Domain.Model;

public enum FigureEditOutcome
{
    /// <summary>Nothing to store: the box is empty, or holds what is already stored.</summary>
    NoChange,
    Write,
    /// <summary>The text is not a figure. Nothing is stored and the stored value stays exactly as it was.</summary>
    Invalid,
}

public readonly record struct FigureEdit(FigureEditOutcome Outcome, decimal Value)
{
    /// <summary>
    /// What leaving a figure box means for a stored value (R-G15/R-G16, D159).
    ///
    /// <para><b>Empty never clears.</b> Mockup 15 draws no way to take an APR or a payment back out — "Nothing
    /// to save", no clear control — so an emptied box is "no change": a stored value is kept (the page writes
    /// it back into the box), and an unset one stays unset. Clearing a figure is therefore not an edit the
    /// post-it can make at all, which is also why <see cref="ManualAccountPatch"/> can use null to mean
    /// "leave alone". An unreadable figure is <see cref="FigureEditOutcome.Invalid"/> and is never stored.
    /// Writing a value equal to the stored one is no change either, so tapping in and out of a box does not
    /// refresh a balance's "Updated" date.</para>
    /// </summary>
    public static FigureEdit Decide(string? text, FigureKind kind, decimal? stored, bool allowNegative = false)
    {
        var parsed = FigureParser.Parse(text, kind, allowNegative);
        return parsed.Status switch
        {
            FigureStatus.Invalid => new FigureEdit(FigureEditOutcome.Invalid, 0m),
            FigureStatus.Empty => new FigureEdit(FigureEditOutcome.NoChange, 0m),
            _ when stored == parsed.Value => new FigureEdit(FigureEditOutcome.NoChange, parsed.Value),
            _ => new FigureEdit(FigureEditOutcome.Write, parsed.Value),
        };
    }
}

/// <summary>
/// The fields of a hand-entered account the owner changed — and only those. Every member is "leave alone"
/// when null, so a patch for one field cannot, by construction, carry a default for another (R-G15). The
/// post-it used to write all five fields on every blur: opening it and editing Balance stored today's date
/// as Next due and "Month" as Every, neither of which the owner ever chose.
/// </summary>
public sealed record ManualAccountPatch
{
    public decimal? Balance { get; init; }
    public decimal? Apr { get; init; }
    public decimal? Payment { get; init; }
    public decimal? CreditLimit { get; init; }
    public PaymentCadence? Cadence { get; init; }
    public DateTimeOffset? NextDue { get; init; }

    public bool IsEmpty =>
        Balance is null && Apr is null && Payment is null && CreditLimit is null && Cadence is null && NextDue is null;

    public static ManualAccountPatch OfNextDue(DateOnly day) => new() { NextDue = CalendarDate.ToStored(day) };
}

/// <summary>The copy and the small decisions the hand-entered account's post-it and Add form share.</summary>
public static class ManualAccountEditing
{
    /// <summary>One plain sentence on the post-it when a write failed (the detail goes to Diagnostics).</summary>
    public const string SaveFailedSentence = "Couldn't save that. Try again.";

    public const string AddFailedSentence = "Couldn't add that. Try again.";

    /// <summary>The post-it's first line while the account is not yet in the Snowball.</summary>
    public const string NotInSnowballSentence = "Not in the Snowball until it has an APR and a payment.";

    public const string NameHint = "Give it a name.";

    /// <summary>
    /// APR and Payment carry the required tint until BOTH are there (mockup 15): a hand-entered account is
    /// not in the Snowball until it has both, so one filled box does not release the other. Present means
    /// "the owner has given one" — typed and readable, or already stored.
    /// </summary>
    public static bool RequiredTintApplies(bool aprPresent, bool paymentPresent) => !(aprPresent && paymentPresent);

    /// <summary>The Add form's one top sentence, for the required fields only; null when both are there.</summary>
    public static string? RequiredSummary(bool nameMissing, bool balanceMissing) => (nameMissing, balanceMissing) switch
    {
        (true, true) => "Name and balance are required.",
        (true, false) => "Name is required.",
        (false, true) => "Balance is required.",
        _ => null,
    };

    /// <summary>The error for a figure box on leaving it, or null when it is fine. A required box that is
    /// empty is an error; an optional empty one is not.</summary>
    public static string? FigureError(string? text, FigureKind kind, bool required, bool allowNegative = false)
    {
        var parsed = FigureParser.Parse(text, kind, allowNegative);
        return parsed.Status switch
        {
            FigureStatus.Invalid => FigureParser.FixHint(kind),
            FigureStatus.Empty when required => FigureParser.FixHint(kind),
            _ => null,
        };
    }
}

/// <summary>
/// Mockup 4: "Validation happens on blur, then live." A box says nothing while it is being typed into for
/// the first time; once leaving it has marked it invalid, every keystroke re-checks it, so the rose clears
/// the moment the text becomes readable. After it clears it is quiet again until the next blur — a box does
/// not start shouting halfway through a number.
/// </summary>
public sealed class BlurThenLive
{
    private bool marked;

    /// <summary>The field was left: <paramref name="error"/> is its current error (null if fine). Returns
    /// what to show.</summary>
    public string? Blur(string? error)
    {
        marked = error is not null;
        return error;
    }

    /// <summary>The text changed: returns what to show — the error if the field is marked, otherwise nothing.</summary>
    public string? Typed(string? error)
    {
        if (!marked)
        {
            return null;
        }

        marked = error is not null;
        return error;
    }
}

/// <summary>
/// What the post-it's facts row shows. Plaid reports no APR for a savings account, and mockup 15 frame 1
/// draws only AVAILABLE for one: the APR column (and the gap that separated it from the next) is omitted
/// rather than printed as a dash.
/// </summary>
public sealed record PostItFact(string Label, string Value);

public static class PostItFacts
{
    public static IReadOnlyList<PostItFact> For(string? aprText, string availableText)
    {
        var facts = new List<PostItFact>(2);
        if (!string.IsNullOrEmpty(aprText))
        {
            facts.Add(new PostItFact("APR", aprText));
        }

        facts.Add(new PostItFact("Available", availableText));
        return facts;
    }
}
