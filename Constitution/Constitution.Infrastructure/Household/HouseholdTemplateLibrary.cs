using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// The rotating templates' library over the household file: the <c>week-plan/template:*</c> and <c>week-plan/template-print:*</c> records read and written
/// through <see cref="TemplateRecords"/>. The phone's own copy is the working copy, as for the recipe box; Drive is reached by the service's pass.
///
/// <para><b>Never an empty library for one that could not be read.</b> A file that will not open, or a damaged copy, is <see cref="TemplateLibraryRead.Unreadable"/> with
/// the file's own sentence; a phone that has not met a household file (and holds nothing) is <see cref="TemplateLibraryRead.NoneYet"/>. A template record this build cannot read is
/// counted, left exactly as it is, and never written over.</para>
///
/// <para><b>Reading is for any phone; saving is the planner's.</b> The weekly run reaches <see cref="Save"/> only after the lease says this phone plans the week, so a partner's
/// phone never proposes or prints. (Her answers to a sheet are a different record and a later job.)</para>
/// </summary>
public sealed class HouseholdTemplateLibrary(HouseholdFileService service) : ITemplateLibraryStore
{
    public TemplateLibraryRead Load()
    {
        if (service.BlockedSentence is { } blocked)
        {
            return new TemplateLibraryRead.Unreadable(blocked);
        }

        HouseholdDocument document;
        try
        {
            document = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdFormatException)
        {
            return new TemplateLibraryRead.Unreadable(HouseholdSentences.LocalDamaged);
        }

        var set = TemplateRecords.Read(document);
        service.NoteUnreadable(set.Unreadable.Count);
        var standing = service.Standing;
        var unmet = standing.State is HouseholdFileState.NotCompared or HouseholdFileState.LocalOnly or HouseholdFileState.WaitingForShare;
        if (document.Records.Count == 0 && unmet)
        {
            return new TemplateLibraryRead.NoneYet(standing.Sentence);
        }

        return new TemplateLibraryRead.Held(set.Library, set.Unreadable.Count);
    }

    public TemplateSave Save(TemplateLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (service.BlockedSentence is { } blocked)
        {
            return new TemplateSave.Refused(blocked);
        }

        try
        {
            service.EditAsync(
                (HouseholdDocument document, HybridLogicalClock clock, string writer) => TemplateRecords.Save(document, clock, writer, library, service.Now),
                CancellationToken.None).GetAwaiter().GetResult();
            return new TemplateSave.Saved();
        }
        catch (HouseholdUnreadableException exception)
        {
            return new TemplateSave.Refused(exception.Message);
        }
        catch (HouseholdFormatException)
        {
            return new TemplateSave.Refused(HouseholdSentences.LocalDamaged);
        }
    }
}
