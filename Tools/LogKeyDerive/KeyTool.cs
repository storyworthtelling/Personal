using System.Globalization;
using Storyworthtelling.Diagnostics;

namespace LogKeyDerive;

/// <summary>
/// Reads the vault passphrase from stdin, prints <see cref="LogKey.Derive"/>'s hex and nothing else, and
/// never echoes what it read — not on success, not in an error (LOG-LOOP-PLAN.md §6.2).
/// Usage: <c>dotnet run --project Tools/LogKeyDerive [-- --epoch N]</c>, passphrase on stdin.
/// </summary>
public static class KeyTool
{
    public static int Run(TextReader input, TextWriter output, TextWriter errors, string[] args)
    {
        var epoch = LogKey.Epoch;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--epoch" && i + 1 < args.Length
                && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                epoch = parsed;
                i++;
            }
            else
            {
                errors.WriteLine("Usage: LogKeyDerive [--epoch N] < passphrase");
                return 2;
            }
        }

        var passphrase = input.ReadLine()?.TrimEnd('\r', '\n');
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            errors.WriteLine("No passphrase on stdin; nothing derived.");
            return 1;
        }
        output.WriteLine(LogKey.Derive(passphrase, epoch));
        return 0;
    }
}
