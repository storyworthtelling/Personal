namespace Constitution.Domain.Health;

/// <summary>
/// What the permissions-rationale page says (C-7.1.1). Plain constants, so a test can read them and
/// the Android page is only a painter. Health Connect opens this page when the owner asks why
/// Constitution wants a permission, so it must say what is read, and that nothing is written.
///
/// <para>No digits: a count or a date typed into a sentence is a number that can drift from the
/// permissions it describes. No clinical or judging words either; Constitution charts a person
/// against their own past and carries no norm (brief, Body).</para>
/// </summary>
public static class HealthRationale
{
    /// <summary>The page's Serif header.</summary>
    public const string Title = "Health Connect";

    /// <summary>The one sentence that matters: what is read, from when, and that it is never changed.</summary>
    public const string Sentence =
        "Constitution only reads your weight, body fat, resting heart rate, heart rate variability, "
        + "steps, exercise and sleep from Health Connect, starting from the day you installed it. "
        + "It never writes to Health Connect or changes anything in it.";

    /// <summary>
    /// Said beneath the sentence: the one permission that is not a signal. Honest about who decides: the overnight read
    /// happens only if the owner allows it in Health Connect, and Constitution does not ask for it by itself.
    /// </summary>
    public const string BackgroundSentence =
        "Constitution reads overnight only if you allow it in Health Connect, so the morning check is current. "
        + "Otherwise it reads whenever you open the app.";

    /// <summary>The Settings line beside the button that opens Health Connect's own page for this app.</summary>
    public const string OvernightLine =
        "Allow overnight reads in Health Connect so the morning check is current; otherwise values refresh when you open the app.";
}
