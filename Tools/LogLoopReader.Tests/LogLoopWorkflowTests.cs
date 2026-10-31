namespace LogLoopReader.Tests;

// The two workflows the loop depends on, read by content (plan §6.1, §6.3). What must not move: the guard
// runs on every claude/** push and on PRs to main under the job name branch protection requires; the
// auto-merge gate runs main's code only, never the PR's, and never merges by itself.
public class LogLoopWorkflowTests
{
    private static string Workflow(string name) =>
        File.ReadAllText(Path.Combine(Support.RepoRoot(), ".github", "workflows", name));

    [Fact]
    public void TheGuardRunsOnEveryClaudePushAndEveryPrToMain()
    {
        var guard = Workflow("log-loop-guard.yml");

        Assert.Contains("branches: ['claude/**']", guard);
        Assert.Contains("pull_request:\n    branches: [main]", guard);
        Assert.DoesNotContain("paths:", guard);
        Assert.Contains("name: log-loop-guard", guard);
        Assert.Contains("leakscan --ci", guard);
        Assert.Contains("dotnet test Tools/LogLoopReader.Tests/LogLoopReader.Tests.csproj", guard);
    }

    [Fact]
    public void TheGateRunsMainsCodeNeverThePrs()
    {
        var gate = Workflow("log-loop-automerge.yml");

        Assert.Contains("pull_request_target:", gate);
        Assert.DoesNotContain("\n  pull_request:", gate);
        Assert.Contains("ref: ${{ github.event.repository.default_branch }}", gate);
        Assert.DoesNotContain("github.event.pull_request.head.sha }}", gate);
        Assert.DoesNotContain("github.event.pull_request.head.ref }}", gate);
    }

    [Fact]
    public void TheGateOnlyEnablesOrDisablesAutoMergeAndNeverMergesItself()
    {
        var gate = Workflow("log-loop-automerge.yml");

        Assert.Contains("gh pr merge \"$n\" --repo \"$REPO\" --auto --merge", gate);
        Assert.Contains("--disable-auto", gate);
        Assert.Equal(2, gate.Split("gh pr merge").Length - 1);
        Assert.Contains("LogLoopReader --no-build -- gate --input", gate);
    }

    [Fact]
    public void TheGateWaitsForEveryAppWorkflowThatHasATestJob()
    {
        var gate = Workflow("log-loop-automerge.yml");
        var folder = Path.Combine(Support.RepoRoot(), ".github", "workflows");

        foreach (var file in Directory.EnumerateFiles(folder, "build-*.yml"))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("\n  test:", StringComparison.Ordinal) && !text.Contains("dotnet test", StringComparison.Ordinal))
            {
                continue;
            }
            var name = text.Split('\n').First(l => l.StartsWith("name: ", StringComparison.Ordinal))["name: ".Length..].Trim();
            Assert.True(gate.Contains("      - " + name + "\n", StringComparison.Ordinal),
                $"{Path.GetFileName(file)} ({name}) finishing does not wake the gate, so a PR it tests would wait for another event.");
        }
        Assert.Contains("      - Log loop guard\n", gate);
    }

    [Fact]
    public void TheGateExcludesItsOwnRunFromTheChecksItWaitsFor()
    {
        Assert.Contains("select(.name != \"log-loop-gate\")", Workflow("log-loop-automerge.yml"));
    }
}
