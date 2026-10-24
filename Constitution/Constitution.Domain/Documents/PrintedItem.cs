namespace Constitution.Domain.Documents;

/// <summary>The type roles a document prints in; each is a rung of the registered ladder.</summary>
public enum PrintedRole
{
    Dose,
    Body,
    Label,
    Rule,

    /// <summary>A serif semibold heading: the load under the dose.</summary>
    Heading,

    /// <summary>The secondary sans: rest, hints, footers, the cover's notes.</summary>
    Small,

    /// <summary>The body size in the semibold: names in a list, "Done", "Still sore?".</summary>
    Strong,

    /// <summary>The owner's voice (Pinyon Script). Only the encouragement seat prints in it.</summary>
    Voice,

    /// <summary>A circled word's face: the rating and the mobility choices.</summary>
    Choice,

    /// <summary>A shaded ground behind a group of items (the "Still sore?" band). Not print: no clearance is asked of it.</summary>
    Band,

    /// <summary>A dotted hairline: the rules of the notes column.</summary>
    Dotted,

    /// <summary>Room kept free for a figure. Draws nothing and asks no clearance.</summary>
    Slot,

    /// <summary>Cascadia Code on the Mono rung: quantities and clock times, set so a column of them lines up.</summary>
    Mono,
}

public enum PrintedAlign
{
    Left,
    Centre,
    Right,
}

/// <summary>
/// Something printed on a page that is not a target: a word, a dose, a divider or a ruled line.
/// <paramref name="OwnerId"/> is the target (or lane group) it belongs to, if any.
/// </summary>
public sealed record PrintedItem(
    string Name, PrintedRole Role, InkRect Bounds, string? Text, string? OwnerId, PrintedAlign Align = PrintedAlign.Left);
