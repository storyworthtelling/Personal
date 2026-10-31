namespace Constitution.Domain.Groceries;

/// <summary>What sharing the list to Keep comes to.</summary>
public enum GroceryShareOutcome
{
    /// <summary>Send <see cref="GroceryShareDecision.Body"/> to <see cref="GroceryShareDecision.Package"/>.</summary>
    Send,

    /// <summary>The list is empty, so there is nothing to send.</summary>
    NothingToSend,

    /// <summary>Keep is not on this phone; nothing is sent and no chooser is offered.</summary>
    KeepNotInstalled,
}

/// <summary>The share intent as a decision: the text, and the one app it is aimed at.</summary>
public sealed record GroceryShareDecision(GroceryShareOutcome Outcome, string Package, string Title, string Body, string Notice);

/// <summary>
/// The grocery list handed to Google Keep (C-6.4.2, C-10.1.4). Pure: the Android sender only carries the decision out.
/// </summary>
public static class GroceryShare
{
    /// <summary>Keep's package id; the same string as Flyleaf's allowlist, held equal by a parity test.</summary>
    public const string KeepPackage = "com.google.android.keep";

    /// <summary>
    /// Decides the share. The package is always Keep's: under the lockdown a chooser would offer only whatever survived it,
    /// so a phone without Keep is told so rather than handed another app. The body is aisle headers over "quantity name"
    /// lines, in shopping order, with nothing else in it (no money, no names).
    /// </summary>
    public static GroceryShareDecision Plan(GroceryList list, bool keepIsInstalled)
    {
        ArgumentNullException.ThrowIfNull(list);
        const string title = "Groceries";
        if (list.IsEmpty)
        {
            return new GroceryShareDecision(GroceryShareOutcome.NothingToSend, KeepPackage, title, string.Empty, "There is nothing on the list to send.");
        }

        if (!keepIsInstalled)
        {
            return new GroceryShareDecision(GroceryShareOutcome.KeepNotInstalled, KeepPackage, title, string.Empty, "Keep is not on this phone, so the list was not sent.");
        }

        var blocks = list.Sections.Select(section =>
            Aisles.Label(section.Aisle) + "\n" + string.Join("\n", section.Lines.Select(Text))).ToList();
        if (list.ExtraLines.Count > 0)
        {
            blocks.Add(GroceryList.ExtraHeading + "\n" + string.Join("\n", list.ExtraLines));
        }

        return new GroceryShareDecision(GroceryShareOutcome.Send, KeepPackage, title, string.Join("\n\n", blocks), string.Empty);
    }

    private static string Text(GroceryLine line) => line.Quantity.Length == 0 ? line.Name : line.Quantity + " " + line.Name;
}
