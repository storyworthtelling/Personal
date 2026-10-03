using System.Text;

namespace IndexHistorySeal;

/// <summary>Reads the passphrase from the keyboard without echoing it. Returns null when there is no
/// keyboard to read (input is redirected), so a script that forgot the environment variable fails with a
/// message instead of hanging on a prompt nobody will answer.</summary>
internal static class PassphrasePrompt
{
    public static string? ReadWithoutEcho()
    {
        if (Console.IsInputRedirected)
        {
            return null;
        }

        Console.Error.Write("Vault passphrase (not shown): ");
        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }

        Console.Error.WriteLine();
        return typed.ToString();
    }
}
