using Xunit;

namespace Folio.Tests.Guard;

// Efficiency audit §6: the 15-minute worker started a foreground service, with its "reading" notification, before it
// asked whether there was any book to read, 96 times a day on an idle phone. It asks first now.
public class IdleWorkerStaysInTheBackgroundTests
{
    [Fact]
    public void TheWorkerAsksForWorkBeforeGoingToTheForeground()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
            root = Path.GetDirectoryName(root);
        var worker = File.ReadAllText(Path.Combine(root!, "Storyboarder", "Folio.Android", "PipelineWorker.cs"));
        var work = worker[worker.IndexOf("private Result Work()", StringComparison.Ordinal)..];

        var asks = work.IndexOf("HasActiveWorkAsync", StringComparison.Ordinal);
        var promotes = work.IndexOf("GoToForeground(", StringComparison.Ordinal);

        Assert.True(asks >= 0 && asks < promotes, "The worker must check for active work before GoToForeground.");
    }
}
