using Constitution.Domain.Meals.Templates;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household.Records;

/// <summary>
/// The keys of the template records. Templates (D110) ride the committed <c>week-plan</c> kind rather than a kind of their own, each behind a prefix
/// that no other <c>week-plan</c> record uses: <c>template</c> for a template, <c>template-print</c> for where one was printed. The meal answers keep
/// <c>answer</c> and the plan itself is a later record of the same kind; none can share a key with these, and none is read as the other.
/// </summary>
public static class TemplateKeys
{
    internal const string TemplatePart = "template";
    internal const string PrintPart = "template-print";

    public static RecordKey KeyOf(MealTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new RecordKey(ConstitutionKinds.WeekPlan, RecordIds.Join(TemplatePart, template.Id));
    }

    public static RecordKey KeyOf(TemplatePrint print)
    {
        ArgumentNullException.ThrowIfNull(print);
        return new RecordKey(ConstitutionKinds.WeekPlan, RecordIds.Join(PrintPart, RecordDates.Write(print.Week), print.ItemId));
    }

    /// <summary>Whether the key is a template's. A record of the same kind that is not one is left alone, neither read nor reported.</summary>
    public static bool IsTemplate(RecordKey key) => Has(key, TemplatePart);

    public static bool IsPrint(RecordKey key) => Has(key, PrintPart);

    private static bool Has(RecordKey key, string part) =>
        key.Kind == ConstitutionKinds.WeekPlan && key.Id.StartsWith(part + RecordIds.Separator, StringComparison.Ordinal);
}

/// <summary>
/// The <c>week-plan</c> template record: id, slot, words, status, approval, the household's reason and the week of the latest answers taken.
/// <b>Every field name and word is frozen</b> (<c>TemplateMapTests</c> holds each as a literal).
/// </summary>
internal sealed class TemplateMap : RecordMap<MealTemplate>
{
    public override string Kind => ConstitutionKinds.WeekPlan;

    protected override string IdOf(MealTemplate value) => TemplateKeys.KeyOf(value).Id;

    public override IReadOnlyDictionary<string, string> FieldsOf(MealTemplate value)
    {
        value.EnsureValid();
        return new FieldWriter()
            .Text("id", value.Id)
            .Word("slot", MealTemplateWords.Slots, value.Slot)
            .Text("words", value.Words)
            .Word("status", MealTemplateWords.Statuses, value.Status)
            .Word("approval", MealTemplateWords.Approvals, value.Approval)
            .Text("reason", value.Reason)
            .Date("feedback", value.FeedbackWeek)
            .Fields;
    }

    public override MealTemplate Read(RecordReader record)
    {
        MealTemplate value;
        try
        {
            value = new MealTemplate(record.Text("id"), record.Word("slot", MealTemplateWords.Slots), record.Text("words"))
            {
                Status = record.Word("status", MealTemplateWords.Statuses),
                Approval = record.Word("approval", MealTemplateWords.Approvals),
                Reason = record.OptionalText("reason"),
                FeedbackWeek = record.OptionalDate("feedback"),
            };
            value.EnsureValid();
        }
        catch (ArgumentException exception)
        {
            // A template the rules refuse (a status and approval that disagree, words too long) is an unreadable record, not a crash.
            throw new HouseholdRecordException(record.Key, exception.Message);
        }

        return TemplateKeys.KeyOf(value) == record.Key ? value : throw record.Fail("id", "does not belong to this record's key");
    }
}

/// <summary>The <c>week-plan</c> record of one printing: the week, the line, and the template that stood on it.</summary>
internal sealed class TemplatePrintMap : RecordMap<TemplatePrint>
{
    public override string Kind => ConstitutionKinds.WeekPlan;

    protected override string IdOf(TemplatePrint value) => TemplateKeys.KeyOf(value).Id;

    public override IReadOnlyDictionary<string, string> FieldsOf(TemplatePrint value) =>
        new FieldWriter()
            .Date("week", value.Week)
            .Text("item", value.ItemId)
            .Text("template", value.TemplateId)
            .Fields;

    public override TemplatePrint Read(RecordReader record)
    {
        var value = new TemplatePrint(
            record.OptionalDate("week") ?? throw record.Fail("week", "is missing"),
            record.Text("item"),
            record.Text("template"));
        return TemplateKeys.KeyOf(value) == record.Key ? value : throw record.Fail("item", "does not belong to this record's key");
    }
}

/// <summary>The maps for templates and where they were printed, beside <see cref="MealAnswerMaps"/>.</summary>
public static class TemplateMaps
{
    public static RecordMap<MealTemplate> Template { get; } = new TemplateMap();

    public static RecordMap<TemplatePrint> Print { get; } = new TemplatePrintMap();
}

/// <summary>The library a household document holds, and the template records it holds that could not be read.</summary>
public sealed record TemplateLibrarySet(TemplateLibrary Library, IReadOnlyList<UnreadableRecord> Unreadable);

/// <summary>
/// Saves and reads the template library in the household file. <b>Idempotent</b>: a record whose value is already what is being written is not
/// touched, so saving twice changes nothing. A record this build cannot read is left exactly as it is (it may be a newer writer's), and fields a
/// newer writer added to a readable one survive an edit. Other <c>week-plan</c> records are never read or written. Pure: the clock and time come
/// from the caller.
/// </summary>
public static class TemplateRecords
{
    public static HouseholdDocument Save(HouseholdDocument document, HybridLogicalClock clock, string install, TemplateLibrary library, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(install);
        ArgumentNullException.ThrowIfNull(library);
        foreach (var template in library.Templates)
        {
            document = SaveOne(document, clock, install, TemplateMaps.Template, template, now);
        }

        foreach (var print in library.Prints)
        {
            document = SaveOne(document, clock, install, TemplateMaps.Print, print, now);
        }

        return document;
    }

    public static TemplateLibrarySet Read(HouseholdDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var templates = new List<MealTemplate>();
        var prints = new List<TemplatePrint>();
        var unreadable = new List<UnreadableRecord>();
        foreach (var record in document.LiveRecords)
        {
            try
            {
                if (TemplateKeys.IsTemplate(record.Key))
                {
                    templates.Add(TemplateMaps.Template.Read(new RecordReader(record)));
                }
                else if (TemplateKeys.IsPrint(record.Key))
                {
                    prints.Add(TemplateMaps.Print.Read(new RecordReader(record)));
                }
            }
            catch (HouseholdRecordException exception)
            {
                unreadable.Add(new UnreadableRecord(record.Key, exception.Message));
            }
            catch (ArgumentException exception)
            {
                unreadable.Add(new UnreadableRecord(record.Key, exception.Message));
            }
        }

        return new TemplateLibrarySet(new TemplateLibrary(templates, prints), unreadable);
    }

    private static HouseholdDocument SaveOne<T>(HouseholdDocument document, HybridLogicalClock clock, string install, RecordMap<T> map, T value, DateTimeOffset now)
        where T : notnull
    {
        if (document.Live(map.KeyOf(value)) is { } existing)
        {
            T current;
            try
            {
                current = map.Read(new RecordReader(existing));
            }
            catch (HouseholdRecordException)
            {
                // A record this build cannot read may be a newer writer's; writing fields over it could clobber what it means.
                return document;
            }

            if (EqualityComparer<T>.Default.Equals(current, value))
            {
                return document;
            }
        }

        var stamp = clock.Witness(document.MaxStamp, now.ToUnixTimeMilliseconds());
        return HouseholdRecords.Put(document, map, value, stamp, install);
    }
}
