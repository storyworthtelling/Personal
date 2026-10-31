namespace Constitution.Domain.Meals.Templates;

/// <summary>That a template was printed on a sheet: the week, the line (its frozen YES-box id), and the template that stood on it.</summary>
public sealed record TemplatePrint(DateOnly Week, string ItemId, string TemplateId);

/// <summary>
/// Every template the household has been shown or asked about, and where each was printed. Immutable: every change returns a new
/// library. Proposed templates are held out of the rotation (<see cref="Approved"/> is all the rotation reads) until the sheet's
/// answers say what they are.
/// </summary>
public sealed class TemplateLibrary
{
    public static TemplateLibrary Empty { get; } = new([], []);

    public TemplateLibrary(IReadOnlyList<MealTemplate> templates, IReadOnlyList<TemplatePrint> prints)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(prints);
        Templates = templates.GroupBy(template => template.Id, StringComparer.Ordinal).Select(group => group.Last()).OrderBy(template => template.Id, StringComparer.Ordinal).ToList();
        Prints = prints.Distinct().OrderBy(print => print.Week).ThenBy(print => print.ItemId, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<MealTemplate> Templates { get; }

    public IReadOnlyList<TemplatePrint> Prints { get; }

    public bool IsEmpty => Templates.Count == 0;

    /// <summary>In the rotation: id order, so the same library always rotates the same way.</summary>
    public IReadOnlyList<MealTemplate> Approved => Of(TemplateStatus.Approved);

    public IReadOnlyList<MealTemplate> Rejected => Of(TemplateStatus.Rejected);

    /// <summary>Proposed and not yet answered: printed once, never in the rotation.</summary>
    public IReadOnlyList<MealTemplate> Proposed => Of(TemplateStatus.Proposed);

    public MealTemplate? Find(string id) => Templates.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.Ordinal));

    /// <summary>The week of the latest sheet that printed this template, or null if none did.</summary>
    public DateOnly? LastPrintedWeek(string id) =>
        Prints.Where(print => string.Equals(print.TemplateId, id, StringComparison.Ordinal)).Select(print => (DateOnly?)print.Week).Max();

    public TemplateLibrary With(MealTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        template.EnsureValid();
        return new TemplateLibrary([.. Templates.Where(existing => existing.Id != template.Id), template], Prints);
    }

    public TemplateLibrary WithPrints(IEnumerable<TemplatePrint> prints)
    {
        ArgumentNullException.ThrowIfNull(prints);
        return new TemplateLibrary(Templates, [.. Prints, .. prints]);
    }

    private List<MealTemplate> Of(TemplateStatus status) => Templates.Where(template => template.Status == status).ToList();
}
