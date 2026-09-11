using Xunit;

namespace Foreword.Tests.Guard;

/// <summary>
/// The task list asks for completed tasks, and asks the way the API actually requires.
///
/// <para><c>showCompleted</c> alone is not enough. The published reference says it in as many words:
/// <i>"Note that showHidden must also be True to show tasks completed in first party clients, such
/// as the web UI and Google's mobile apps."</i> — which is precisely the case T3 is about, a task
/// the owner closed in Google's own app.</para>
///
/// <para>Set one and not the other and nothing fails: the request succeeds, the list comes back, and
/// the closed task is simply absent. Foreword would then draw it as though it had never existed,
/// which reads as a task Foreword lost rather than one the owner finished. So both flags are pinned
/// here, in the one place the call is made — it runs through the Google SDK against a live service,
/// so there is nothing else to test it with.</para>
/// </summary>
public class CompletedTasksAreAskedForTests
{
    [Fact]
    public void TheTaskListRequestAsksForCompletedAndHiddenTasks()
    {
        var source = File.ReadAllText(Path.Combine(
            Root(), "Foreword.Infrastructure", "Google", "GoogleTasksClient.cs"));

        Assert.Contains("ShowCompleted = true", source, StringComparison.Ordinal);
        Assert.Contains("ShowHidden = true", source, StringComparison.Ordinal);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Foreword.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
