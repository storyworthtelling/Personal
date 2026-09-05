namespace Codex.Domain.Plate;

/// <summary>
/// The owner's voice in Plate, which gets exactly one seat: the empty Darkroom — as Post's
/// gets the empty Mailbox. Set in Pinyon Script in ink-blue, the one place in the whole
/// suite that hand appears.
///
/// <para>The lines never nag. The owner was offered auto-send, expiry and a fold that
/// refuses to flatten, and chose none of them; a line that scolded about what was waiting
/// would be the nag arriving through the back door.</para>
/// </summary>
public static class DarkroomVoice
{
    /// <summary>
    /// Drawn from a pool, because a line that repeats every time stops being a voice and
    /// becomes a label.
    /// </summary>
    public static IReadOnlyList<string> EmptyLines { get; } =
    [
        "Nothing hanging on the line. Go and take something.",
        "Empty trays, clean tongs, no excuses.",
        "The light is on and there is nothing under it.",
        "Not a frame in the place. Suits me.",
        "All quiet. The world is out there being photogenic.",
        "Nothing developing. Enviable, really.",
    ];

    /// <summary>
    /// Picks a line. The roll comes from a random draw that can be anything at all,
    /// including negative, so it is folded into range rather than trusted.
    /// </summary>
    public static string Empty(int roll) =>
        EmptyLines[(int)(((long)roll % EmptyLines.Count + EmptyLines.Count) % EmptyLines.Count)];

    /// <summary>
    /// What the vision call is asked for. It lives here rather than in the head so its shape
    /// can be read — and its plural agreement checked — without a phone or an API key.
    /// </summary>
    public static string CaptionPrompt(int pictures) =>
        pictures == 1
            ? "Look at this one picture waiting to be chosen. Write one short line about what "
              + "is in it, in the owner's voice. No more than a sentence, and never an instruction."
            : $"Look at these {pictures} pictures waiting to be chosen. Write one short line "
              + "about what they have in common, in the owner's voice. No more than a sentence, "
              + "and never an instruction.";
}
