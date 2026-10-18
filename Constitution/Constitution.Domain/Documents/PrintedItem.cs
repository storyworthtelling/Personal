namespace Constitution.Domain.Documents;

/// <summary>The type roles a document prints in; each is a rung of the registered ladder.</summary>
public enum PrintedRole
{
    Dose,
    Body,
    Label,
    Rule,
}

public enum PrintedAlign
{
    Left,
    Centre,
}

/// <summary>
/// Something printed on a page that is not a target: a word, a dose, a divider or a ruled line.
/// <paramref name="OwnerId"/> is the target (or lane group) it belongs to, if any.
/// </summary>
public sealed record PrintedItem(
    string Name, PrintedRole Role, InkRect Bounds, string? Text, string? OwnerId, PrintedAlign Align = PrintedAlign.Left);
