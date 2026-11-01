using System.Globalization;
using Pulp.Domain.Calendar;
using Pulp.Domain.LockedOpen;

namespace Pulp.Domain.Diagnostics;

/// <summary>
/// Pulp's lines for the log loop (LOG-LOOP-PLAN.md §4.5): what became of a hand-off, by service; what
/// Flyleaf answered to a locked hand-off; and the nightly calendar write as counts. Never what anyone
/// watched — no title, no game, no name, no address — which is the promise the Diagnostics group makes.
/// Fixed wording, because the reader's digest groups on it.
/// </summary>
public static class PulpLogLines
{
    public const string Started = "started";
    public const string NotInstalled = "not installed";

    public const string CalendarSkipped = "CALENDAR not run: no household or account yet";

    public static string Refused(string why) => $"refused ({why})";

    public static string HandOff(string service, string outcome) => $"HAND-OFF {service}: {outcome}";

    public static string LockedOpen(int? answer) => answer switch
    {
        LockedOpenHandOff.ResultStarted => "LOCKED OPEN started by Flyleaf",
        null => "LOCKED OPEN no answer from Flyleaf; opened plainly",
        { } code => string.Create(CultureInfo.InvariantCulture, $"LOCKED OPEN refused by Flyleaf (answer {code}); opened plainly"),
    };

    public static string LockedOpenNotSent(string why) => $"LOCKED OPEN could not be asked ({why}); opened plainly";

    public static string Calendar(CalendarNightlyOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.CalendarId is null)
        {
            return "CALENDAR not written: no calendar yet";
        }
        if (outcome.Result is not { } result)
        {
            return "CALENDAR not written: this install only reads it";
        }
        var line = string.Create(CultureInfo.InvariantCulture,
            $"CALENDAR {result.Created} created · {result.Moved} moved · {result.Deleted} deleted · {result.Left} left");
        return outcome.ShareFailures.Count == 0
            ? line
            : string.Create(CultureInfo.InvariantCulture, $"{line} · {outcome.ShareFailures.Count} share failed");
    }
}
