using Storyworthtelling.Diagnostics;

namespace LogLoopReader.Tests;

// §6.2: the owner derives SWT_LOG_KEY on their own machine. The tool reads the passphrase from stdin,
// prints only the key and logs nothing; an empty passphrase is refused rather than turned into a key.
public class LogKeyDeriveToolTests
{
    [Fact]
    public void PrintsTheDerivedKeyAndNothingElse()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var code = LogKeyDerive.KeyTool.Run(new StringReader("an invented passphrase\n"), output, errors, []);

        Assert.Equal(0, code);
        Assert.Equal(LogKey.Derive("an invented passphrase", LogKey.Epoch) + Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, errors.ToString());
    }

    [Fact]
    public void TakesAnEpochForRotation()
    {
        var output = new StringWriter();

        LogKeyDerive.KeyTool.Run(new StringReader("an invented passphrase"), output, new StringWriter(), ["--epoch", "2"]);

        Assert.Equal(LogKey.Derive("an invented passphrase", 2), output.ToString().Trim());
    }

    [Fact]
    public void AnEmptyPassphraseIsRefusedAndTheErrorNeverEchoesInput()
    {
        var output = new StringWriter();
        var errors = new StringWriter();

        var code = LogKeyDerive.KeyTool.Run(new StringReader("   \n"), output, errors, []);

        Assert.NotEqual(0, code);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("passphrase", errors.ToString());
    }
}
