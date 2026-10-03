using System.Text;
using Index.Domain.Model;
using Index.Infrastructure.Snowball;

namespace IndexHistorySeal;

/// <summary>
/// <c>dotnet run --project Tools/IndexHistorySeal -- &lt;input.json&gt; &lt;output.json&gt; [--force]</c>
///
/// <para>Seals the owner's Debt Snowball history (the sheet's "Loans Over Time" tab, exported as JSON in the
/// shape <see cref="SnowballHistoryCodec"/> documents) with the vault passphrase, ready to upload to the
/// Drive root as <see cref="DriveFileName"/>. The history is the owner's finances and the repository is
/// public: this is how it reaches the app without ever being in the source.</para>
///
/// <para><b>The passphrase is never an argument</b> — a command line lands in shell history and in the process
/// list. It comes from the <see cref="PassphraseVariable"/> environment variable or a no-echo prompt, and an
/// argument that looks like an attempt to pass one is refused without being echoed. <b>It prints counts only</b>
/// and writes only the encrypted output: no plaintext copy and no temporary file. It overwrites nothing
/// without <c>--force</c>, and will not write over its own input.</para>
///
/// <para>Exit codes: 0 sealed; 1 usage or passphrase problem; 2 the input is not a history; 3 a file could
/// not be read or written.</para>
/// </summary>
public static class SealCommand
{
    public const string PassphraseVariable = "INDEX_VAULT_PASSPHRASE";

    public const string DriveFileName = DriveSnowballHistorySource.FileName;

    public static int Run(
        string[] args,
        Func<string, string?> environment,
        Func<string?> promptForPassphrase,
        TextWriter stdout,
        TextWriter stderr)
    {
        var paths = new List<string>();
        var force = false;
        foreach (var arg in args)
        {
            if (arg is "--help" or "-h")
            {
                stdout.WriteLine(Usage);
                return 0;
            }
            if (arg == "--force")
            {
                force = true;
            }
            else if (arg.StartsWith('-'))
            {
                // Never echoed: it may be a passphrase someone tried to pass on the command line.
                stderr.WriteLine("An argument was not understood and was ignored for safety. The passphrase is never "
                    + $"an argument; set {PassphraseVariable} or type it at the prompt.");
                stderr.WriteLine(Usage);
                return 1;
            }
            else
            {
                paths.Add(arg);
            }
        }

        if (paths.Count != 2)
        {
            stderr.WriteLine(paths.Count > 2
                ? "Too many arguments: expected an input file and an output file only."
                : "Expected an input file and an output file.");
            stderr.WriteLine(Usage);
            return 1;
        }

        var input = Path.GetFullPath(paths[0]);
        var output = Path.GetFullPath(paths[1]);
        if (string.Equals(input, output, StringComparison.Ordinal))
        {
            stderr.WriteLine("The output file must not be the input file: the plaintext would be overwritten.");
            return 1;
        }
        if (File.Exists(output) && !force)
        {
            stderr.WriteLine("The output file already exists. Nothing was written; pass --force to replace it.");
            return 1;
        }

        string plaintext;
        try
        {
            plaintext = File.ReadAllText(input);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"The input file could not be read ({ex.GetType().Name}).");
            return 3;
        }

        // Validate before asking for a passphrase: a document the app could not read is not worth a prompt.
        var parsed = SnowballHistoryCodec.Parse(plaintext);
        if (!parsed.Ok)
        {
            stderr.WriteLine(parsed.Error);
            stderr.WriteLine("Nothing was written.");
            return 2;
        }

        var passphrase = environment(PassphraseVariable);
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            passphrase = promptForPassphrase();
        }
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            stderr.WriteLine($"No passphrase. Set {PassphraseVariable} to the vault passphrase, or run this in a terminal to be asked.");
            return 1;
        }

        string envelope;
        try
        {
            envelope = SnowballHistorySealer.Seal(plaintext, passphrase);
            // Prove the app's own reader opens what is about to be written, before anything is written.
            var proof = SnowballHistorySealer.Open(envelope, passphrase);
            if (proof.Status != SnowballHistoryStatus.Loaded)
            {
                stderr.WriteLine("The sealed file did not open again, so it was not written.");
                return 3;
            }
        }
        catch (SnowballHistoryFormatException ex)
        {
            stderr.WriteLine(ex.Message);
            stderr.WriteLine("Nothing was written.");
            return 2;
        }

        try
        {
            // CreateNew is atomic about "does not exist yet", so without --force two runs cannot both win.
            using var stream = new FileStream(output, force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
            stream.Write(new UTF8Encoding(false).GetBytes(envelope));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"The output file could not be written ({ex.GetType().Name}).");
            return 3;
        }

        var data = parsed.Data!;
        stdout.WriteLine($"Sealed {data.Weeks.Count} weeks, {data.Loans.Count} loans ({data.Rows.Count} rows) -> {output}");
        stdout.WriteLine($"Upload it to the root of your Google Drive as {DriveFileName}, then open Index.");
        return 0;
    }

    private static readonly string Usage =
        "Usage: IndexHistorySeal <input.json> <output.json> [--force]" + Environment.NewLine
        + $"  Seals your Debt Snowball history with the vault passphrase (env {PassphraseVariable}, or a no-echo prompt)." + Environment.NewLine
        + $"  Upload the output to the root of your Drive as {DriveFileName}." + Environment.NewLine
        + "  Prints counts only; writes only the encrypted output; refuses to overwrite without --force.";
}
