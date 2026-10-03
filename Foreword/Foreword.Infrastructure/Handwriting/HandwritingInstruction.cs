using Foreword.Domain.Models;

namespace Foreword.Infrastructure.Handwriting;

// Foreword's own prompt for the shared Storyworthtelling.Handwriting extractor: which region of
// the printed planner page to read and how a note names its column.
//
// Pulled out of ClaudeVisionHandwritingExtractor when that type moved to the shared library
// (Index-P11 Release F), just supplied from here instead of hardcoded inside the extractor.
// HandwritingInstructionTests pins the text so a future edit here is a decision, not a drift.
//
// The column names are each account's own data and never appear in code: the cached instruction
// says only "one of the column names given with the page", and Message supplies the names beside
// each page, so the instruction stays one cached prefix whatever anybody's columns are called.
public static class HandwritingInstruction
{
    public const string Text =
        "You are reading a daily planner page that was printed and then hand-written on with a " +
        "stylus. Ignore all printed/typed text and the printed grid. Look ONLY at the hand-written " +
        "ink in the 'write below' region near the bottom of the page. Transcribe each hand-written " +
        "future event or task. Respond with ONLY a JSON array (no prose). Each element: " +
        "{\"text\": string, \"when\": ISO-8601 date or date-time or null, " +
        "\"column\": one of the column names given with the page, exactly as written, or null, " +
        "\"isTask\": boolean (true for a to-do, false for a calendar event), " +
        "\"confidence\": number 0..1 for how confident you are in the transcription}. " +
        "If the region is empty, return []. Use low confidence when the writing is hard to read.";

    // The user message that rides beside the page: this account's printed columns, in print order.
    public static string Message(IEnumerable<PersonColumn> printedColumns) =>
        $"Transcribe the hand-written notes. The columns are: {string.Join(", ", printedColumns)}.";
}
