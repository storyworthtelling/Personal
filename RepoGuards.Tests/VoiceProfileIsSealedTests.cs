namespace RepoGuards.Tests;

/// <summary>
/// Privacy report §5: the voice profile carries verbatim passages of the owner's manuscripts. Every app builds
/// it sealed (<c>DriveVoiceProfileStore.Sealed</c>); the plaintext constructor is for tests of the JSON only.
/// </summary>
public class VoiceProfileIsSealedTests
{
    [Fact]
    public void NoAppBuildsThePlaintextVoiceProfileStore()
    {
        var root = GuardScan.RepoRoot();
        var offenders = GuardScan.Files(root)
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(relative => !relative.Split('/').Any(part => part.EndsWith(".Tests", StringComparison.Ordinal)))
            .Where(relative => File.ReadAllText(Path.Combine(root, relative)).Contains("new DriveVoiceProfileStore(", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "These build the plaintext voice profile store:\n" + string.Join("\n", offenders));
    }
}
