using Constitution.Domain.Meals.Templates;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.Templates;

/// <summary>Invented templates and a library to rotate: every dish is made up.</summary>
internal static class TemplateData
{
    public static readonly TemplateRotationFacts Facts = new(["Sam", "Kids"], ["Sam", "Kids"]);

    public static DateOnly WeekAfter(int weeks) => Week.AddDays(7 * weeks);

    public static MealTemplate Approved(string id, MealTemplateSlot slot, string words, TemplateApproval how = TemplateApproval.Explicit) =>
        new(id, slot, words) { Status = TemplateStatus.Approved, Approval = how };

    public static MealTemplate Rejected(string id, MealTemplateSlot slot, string words, string? reason = null) =>
        new(id, slot, words) { Status = TemplateStatus.Rejected, Reason = reason };

    public static MealTemplate Proposed(string id, MealTemplateSlot slot, string words) => new(id, slot, words);

    public static TemplateLibrary Lib(params MealTemplate[] templates) => new(templates, []);

    /// <summary>Three approved templates for every slot.</summary>
    public static TemplateLibrary Full()
    {
        var templates = new List<MealTemplate>();
        foreach (var slot in Enum.GetValues<MealTemplateSlot>())
        {
            var word = MealTemplateWords.Slots.Word(slot);
            foreach (var flavour in new[] { "oats", "toast", "yoghurt" })
            {
                templates.Add(Approved($"tpl-{word}-{flavour}", slot, $"{flavour} for {word}"));
            }
        }

        return Lib([.. templates]);
    }
}
