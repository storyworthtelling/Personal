using System.Globalization;
using Codex.Domain.Music;

namespace Constitution.Domain.Documents;

/// <summary>
/// The workout document (book p.5, p.6): a cover that lists the session and states the three rules,
/// then one page per exercise with the dose at the top and the rating and Done tick fixed at the foot.
/// It takes a plain model in and returns laid-out pages; every target comes from <see cref="InkTargets"/>
/// and the ids from <see cref="WorkoutIds"/>, so the ink reader reads the very instances the painter drew.
/// </summary>
public static class WorkoutDocument
{
    /// <summary>The Tick / circle / skipped rule, as the book words it (p.5).</summary>
    public const string TickRule = "Tick each page as you finish it, and circle how it felt.";

    public const string SkippedRule = "A page left unticked counts as skipped.";

    /// <summary>The stopped-early rule (D24), which moved to the cover when the last page went.</summary>
    public const string StoppedEarlyRule = "Stopped early? Tick only the pages you finished; the plan eases from where you stopped.";

    private const float DoseBaseline = 148f;

    /// <summary>The "Still sore?" band is 44 points tall at the top of the body; the page's content starts this far lower under it.</summary>
    private const float SoreShift = 56f;

    public static IReadOnlyList<InkPageLayout> Build(WorkoutDocumentModel model)
    {
        Validate(model);
        var pages = model.Exercises.Count + 1;
        var layouts = new List<InkPageLayout> { Cover(model, pages) };

        for (var i = 0; i < model.Exercises.Count; i++)
        {
            layouts.Add(ExercisePage(model, i, pages));
        }

        return layouts;
    }

    /// <summary>The text of an exercise's load in pounds: "25 lb kettlebell", or just its words.</summary>
    public static string LoadText(ExerciseLoad load) =>
        load.Pounds is { } pounds ? $"{pounds.ToString(CultureInfo.InvariantCulture)} lb {load.Description}" : load.Description;

    /// <summary>"Score · 132 bpm · Steady": the setting and its band, the band from <see cref="TempoBands.Of"/> (D25).</summary>
    public static string ScoreLine(int bpm) => $"Score · {bpm.ToString(CultureInfo.InvariantCulture)} bpm · {TempoBands.Names[(int)TempoBands.Of(bpm)]}";

    private static InkPageLayout Cover(WorkoutDocumentModel model, int pages)
    {
        var sore = model.Soreness is not null;
        var shift = sore ? SoreShift : 0f;
        var draft = new PageDraft(model.Title);

        draft.Kicker($"WORKOUT · {PageDraft.DayAndTime(model.Start)}");
        draft.Footer(model.Title, model.Start, 1, pages);
        if (model.Soreness is { } soreness)
        {
            StillSoreBand(draft, soreness);
        }

        var minutes = model.TotalMinutes.ToString(CultureInfo.InvariantCulture);
        draft.Line(PrintedRole.Dose, PageDraft.Left, DoseBaseline + shift, 120f, minutes);
        draft.Line(PrintedRole.Heading, PageDraft.Left + 30f * minutes.Length + 2f, DoseBaseline + shift, 60f, "min");
        draft.Line(PrintedRole.Body, 160f, 130f + shift, 249f, $"{model.Exercises.Count} exercises · 1 page each");
        draft.Line(PrintedRole.Small, 160f, 146f + shift, 249f, model.EquipmentLine);

        var rowTop = 178f + shift;
        draft.Rule("list-top", PageDraft.Left, rowTop, PageDraft.FullWidth, 0f);

        for (var i = 0; i < model.Exercises.Count; i++)
        {
            var exercise = model.Exercises[i];
            var baseline = rowTop + 18f + 34f * i;
            var nameLines = CoverCell(exercise.Name, PageText.CoverNameChars, "name");
            var loadLines = CoverCell(LoadText(exercise.Load), PageText.CoverLoadChars, "load");
            draft.Line(PrintedRole.Small, PageDraft.Left, baseline, 20f, (i + 1).ToString(CultureInfo.InvariantCulture));
            CoverLines(draft, PrintedRole.Strong, 58f, rowTop + 34f * i, baseline, 170f, nameLines);
            draft.Line(PrintedRole.Body, 230f, baseline, 60f, exercise.Dose);
            CoverLines(draft, PrintedRole.Small, 292f, rowTop + 34f * i, baseline, 70f, loadLines);
            draft.Line(PrintedRole.Small, PageDraft.RightEdge - 40f, baseline, 40f, $"p. {i + 2}", PrintedAlign.Right);
            draft.Rule($"list-row-{i + 1}", PageDraft.Left, rowTop + 34f * (i + 1), PageDraft.FullWidth, 0f);
        }

        var rulesBaseline = rowTop + 34f * model.Exercises.Count + 32f;
        var lines = new List<string>();
        lines.AddRange(PageText.Wrap(TickRule, PageText.CoverBodyChars));
        lines.AddRange(PageText.Wrap(SkippedRule + " " + StoppedEarlyRule, PageText.CoverBodyChars + 2));
        PageDraft.RequireRoom(
            rulesBaseline + 16f * (lines.Count - 1), PageDraft.CoverContentLimit,
            $"A cover for {model.Exercises.Count} exercises");

        foreach (var line in lines)
        {
            draft.Line(PrintedRole.Small, PageDraft.Left, rulesBaseline, PageDraft.FullWidth, line);
            rulesBaseline += 16f;
        }

        return draft.ToLayout();
    }

    /// <summary>A cover row is 34 points between its rules and holds two lines of a cell at most.</summary>
    private const int CoverCellLines = 2;

    /// <summary>Where the first and second line of a wrapped cell sit below the row's top rule.</summary>
    private const float WrappedFirstBaseline = 14f;

    private const float WrappedLeading = 14f;

    private static IReadOnlyList<string> CoverCell(string text, int chars, string what)
    {
        var lines = PageText.Wrap(text, chars);
        if (lines.Count > CoverCellLines)
        {
            throw new ArgumentException($"The {what} '{text}' takes {lines.Count} lines in its column and a cover row holds {CoverCellLines}.", nameof(text));
        }

        return lines;
    }

    // One line sits on the row's own baseline; two share the row, the first a little above and the second a little below it.
    private static void CoverLines(PageDraft draft, PrintedRole role, float left, float rowTop, float baseline, float width, IReadOnlyList<string> lines)
    {
        for (var line = 0; line < lines.Count; line++)
        {
            var at = lines.Count == 1 ? baseline : rowTop + WrappedFirstBaseline + (WrappedLeading * line);
            draft.Line(role, left, at, width, lines[line]);
        }
    }

    private static InkPageLayout ExercisePage(WorkoutDocumentModel model, int index, int pages)
    {
        var exercise = model.Exercises[index];
        var sore = model.Soreness;
        var draft = new PageDraft(exercise.Name);

        var kicker = $"EXERCISE {index + 1} OF {model.Exercises.Count} · {model.Title.ToUpperInvariant()}";
        if (sore is not null)
        {
            kicker += $" · WORKING AROUND YOUR {sore.Place.ToUpperInvariant()}";
        }

        draft.Kicker(kicker);
        draft.Footer(model.Title, model.Start, index + 2, pages);
        draft.NotesColumn();
        if (sore is not null)
        {
            StillSoreBand(draft, sore);
        }

        var top = DoseBaseline + (sore is null ? 0f : SoreShift);
        draft.Line(PrintedRole.Dose, PageDraft.Left, top, PageDraft.ColumnWidth, exercise.Dose);
        draft.Line(PrintedRole.Heading, PageDraft.Left, top + 22f, PageDraft.ColumnWidth, LoadText(exercise.Load));
        var last = top + 22f;

        if (exercise.RestSeconds is { } rest)
        {
            last += 18f;
            draft.Line(PrintedRole.Small, PageDraft.Left, last, PageDraft.ColumnWidth, $"Rest {rest.ToString(CultureInfo.InvariantCulture)} seconds between sets");
        }

        if (!string.IsNullOrWhiteSpace(exercise.Substitution))
        {
            last += 18f;
            draft.Line(PrintedRole.Small, PageDraft.Left, last, PageDraft.ColumnWidth, exercise.Substitution);
        }

        var labelBaseline = last + 26f;
        var voiceLines = PageDraft.VoiceLines(exercise.Encouragement, exercise.EncouragementVoiced).Count;
        var doctor = sore is { DoctorNoteDue: true } && index == 0;
        if (exercise.ShowFigure)
        {
            // The figure gives way before anything the page must say: on a crowded page (the "Still sore?"
            // band and a long voiced line) it shrinks, and below a useful height it is not kept at all.
            var slotTop = last + 10f;
            var tail = (voiceLines == 0 ? 20f : 48f + 20f * (voiceLines - 1)) + (doctor ? DoctorNoteHeight : 0f);
            var height = Math.Min(FigureHeight, PageDraft.ColumnContentLimit - (slotTop + 16f + tail));
            if (height >= FigureMinimumHeight)
            {
                draft.Add(new PrintedItem(
                    WorkoutIds.FigureSlot, PrintedRole.Slot, new InkRect(PageDraft.Left, slotTop, PageDraft.ColumnWidth, height), null, null));
                labelBaseline = slotTop + height + 16f;
            }
        }

        var bpm = sore is not null && TempoBands.Of(exercise.ScoreBpm) != TempoBand.Easy
            ? TempoBands.Default(TempoBand.Easy)
            : exercise.ScoreBpm;
        draft.Line(PrintedRole.Label, PageDraft.Left, labelBaseline, PageDraft.ColumnWidth, "SET SCORE TO");
        draft.Line(PrintedRole.Strong, PageDraft.Left, labelBaseline + 20f, PageDraft.ColumnWidth, ScoreLine(bpm));
        var end = draft.Voice(exercise.Encouragement, labelBaseline + 48f, labelBaseline + 20f, exercise.EncouragementVoiced);

        if (doctor)
        {
            end = DoctorNote(draft, sore, end);
        }

        PageDraft.RequireRoom(end, PageDraft.ColumnContentLimit, $"The page for {exercise.Name}");
        draft.Rating();
        draft.DoneTick();
        return draft.ToLayout();
    }

    private const float FigureHeight = 150f;

    /// <summary>Below this a figure is too small to learn a form from, and the page prints without one.</summary>
    private const float FigureMinimumHeight = 96f;

    private const float DoctorNoteHeight = 74f;

    /// <summary>
    /// The shaded band at the top of the body (p.6): a white tick, "Still sore?", and where and since when.
    /// A tick means still sore and blank means better, so the question stops by itself (D28).
    /// </summary>
    private static void StillSoreBand(PageDraft draft, Soreness sore)
    {
        var tick = InkTargets.LargeTick(WorkoutIds.StillSore, 46f, 108f, "still sore: a mark means yes, blank means better");
        draft.Add(new PrintedItem("sore-band", PrintedRole.Band, new InkRect(PageDraft.Left, 100f, PageDraft.ColumnWidth, 44f), null, null));
        draft.Target(tick);
        draft.Line(PrintedRole.Strong, 84f, 119f, 222f, "Still sore?", owner: tick.Id, name: "sore-question");
        draft.Line(
            PrintedRole.Small, 84f, 134f, 222f,
            $"{PageText.Capitalise(sore.Place)}, since {sore.Since}. Leave blank if it’s better.", owner: tick.Id, name: "sore-where");
    }

    /// <summary>
    /// The calm doctor suggestion, once, in plain neutral type under a hairline. It is a safety note and
    /// not encouragement, so it is never set in the owner's voice.
    /// </summary>
    private static float DoctorNote(PageDraft draft, Soreness sore, float after)
    {
        var rule = after + 26f;
        draft.Rule("doctor-rule", PageDraft.Left, rule, PageDraft.ColumnWidth, 0f);
        draft.Line(PrintedRole.Strong, PageDraft.Left, rule + 20f, PageDraft.ColumnWidth, $"Sore for {PageText.Word(sore.DaysSore)} days now.");
        draft.Line(PrintedRole.Small, PageDraft.Left, rule + 34f, PageDraft.ColumnWidth, "If it isn’t easing, it may be worth having a doctor");
        draft.Line(PrintedRole.Small, PageDraft.Left, rule + 48f, PageDraft.ColumnWidth, "look at it. This note shows once.");
        return rule + 48f;
    }

    private static void Validate(WorkoutDocumentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Require(model.Title, "title", "the workout");
        if (model.Exercises.Count == 0)
        {
            throw new ArgumentException("A workout needs at least one exercise; there would be nothing for a cover to list.", nameof(model));
        }

        if (model.TotalMinutes <= 0)
        {
            throw new ArgumentException($"A workout of {model.TotalMinutes} minutes has no total time to print.", nameof(model));
        }

        foreach (var exercise in model.Exercises)
        {
            Require(exercise.Name, "name", "an exercise");
            Require(exercise.Dose, "dose", $"'{exercise.Name}'");
            Require(exercise.Load.Description, "load words", $"'{exercise.Name}'");
            if (exercise.Load.Pounds is <= 0)
            {
                throw new ArgumentException($"'{exercise.Name}' has a load of {exercise.Load.Pounds} lb; weights are a positive number of pounds.", nameof(model));
            }

            if (exercise.RestSeconds is <= 0)
            {
                throw new ArgumentException($"'{exercise.Name}' has a rest of {exercise.RestSeconds} seconds; leave the rest out instead.", nameof(model));
            }

            if (exercise.ScoreBpm < TempoBands.Slowest || exercise.ScoreBpm > TempoBands.Fastest)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(model), exercise.ScoreBpm,
                    $"'{exercise.Name}' sets Score to {exercise.ScoreBpm} bpm; the dial runs {TempoBands.Slowest} to {TempoBands.Fastest}.");
            }
        }

        if (model.Soreness is { } sore)
        {
            Require(sore.Place, "place", "the soreness note");
            Require(sore.Since, "start", "the soreness note");
            if (sore.DaysSore < 1)
            {
                throw new ArgumentException($"A soreness of {sore.DaysSore} days is not open; the band is only printed while it is.", nameof(model));
            }
        }
    }

    private static void Require(string? value, string what, string owner)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{owner} needs a {what} to print.");
        }
    }
}
