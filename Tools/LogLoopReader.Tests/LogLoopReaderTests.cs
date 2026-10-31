using Storyworthtelling.Diagnostics;

namespace LogLoopReader.Tests;

// fetch and digest (plan §5.2): every file decrypted or named as WrongKey/Corrupt — never misread — and
// the digest grouping lines by a content-free fingerprint so the session reads a summary first.
public class LogLoopReaderTests
{
    [Fact]
    public async Task FetchDecryptsEachFileAndWritesAManifest()
    {
        var output = Support.TempOutside();
        var source = new Support.FakeSource(new()
        {
            ["swt-log.post.a.000001-000002.json"] = Support.Sealed(Support.Batch("post", "owner", Support.Entry("WARN", "Sync", "failed")), Support.Key),
        });

        var fetched = await new FetchCommand(source, Support.Key).RunAsync(output, Support.RepoRoot(), CancellationToken.None);

        var file = Assert.Single(fetched);
        Assert.Equal(FetchStatus.Decrypted, file.Status);
        Assert.True(File.Exists(file.PlainPath));
        Assert.Contains("swt-log.post.a.000001-000002.json", File.ReadAllText(Path.Combine(output, Manifest.FileName)));
        Assert.Equal("post", LogBatchCodec.Decode(LogBatchCodec.Encode(FetchCommand.ReadPlain(file.PlainPath!))).Header.App);
    }

    [Fact]
    public async Task AWrongKeyAndACorruptFileAreNamedNotMisread()
    {
        var output = Support.TempOutside();
        var source = new Support.FakeSource(new()
        {
            ["swt-log.post.a.000001-000001.json"] = Support.Sealed(Support.Batch("post", "owner", "x"), LogKey.Derive("another invented passphrase", 1)),
            ["swt-log.post.a.000002-000002.json"] = "{ not an envelope",
        });

        var fetched = await new FetchCommand(source, Support.Key).RunAsync(output, Support.RepoRoot(), CancellationToken.None);

        Assert.Equal(FetchStatus.WrongKey, fetched.Single(f => f.Name.Contains("000001-")).Status);
        Assert.Equal(FetchStatus.Corrupt, fetched.Single(f => f.Name.Contains("000002-")).Status);
        Assert.All(fetched, f => Assert.Null(f.PlainPath));
    }

    [Fact]
    public async Task OnlyLogBatchesAreFetched()
    {
        var output = Support.TempOutside();
        var source = new Support.FakeSource(new()
        {
            ["swt-log-processed.json"] = "{\"names\":[]}",
            ["notes.txt"] = "hello",
        });

        Assert.Empty(await new FetchCommand(source, Support.Key).RunAsync(output, Support.RepoRoot(), CancellationToken.None));
    }

    [Fact]
    public void TheSameExceptionFromTheSamePlaceIsOneGroupAcrossPhonesAndDays()
    {
        const string stack = "System.InvalidOperationException: The row 4471 was gone\n"
            + "   at Post.MailboxArea.LoadOpenThread() in /src/MailboxArea.cs:line 812\n"
            + "   at Post.MailboxArea.Resume()\n"
            + "   at Codex.Ui.DogEar.Show(Activity host)\n"
            + "   at Android.App.Activity.n_OnResume(IntPtr jnienv)";
        var other = stack.Replace("4471", "9902").Replace("line 812", "line 815");

        var groups = Digest.Build([
            Support.Batch("post", "owner", Support.Entry("CRASH", "Post", "Unhandled exception", stack), day: 24),
            Support.Batch("post", "owner", Support.Entry("CRASH", "Post", "Unhandled exception", other), day: 30),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal("exception", group.Kind);
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero), group.FirstSeen);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.Zero), group.LastSeen);
        Assert.Contains("System.InvalidOperationException", group.Key);
        Assert.Contains("Post.MailboxArea.LoadOpenThread", group.Key);
        Assert.DoesNotContain("4471", group.Key);
    }

    [Fact]
    public void ADifferentTopFrameIsADifferentGroup()
    {
        const string one = "System.InvalidOperationException: x\n   at Post.A.One()\n   at Post.A.Two()";
        const string two = "System.InvalidOperationException: x\n   at Post.B.Three()\n   at Post.A.Two()";

        var groups = Digest.Build([Support.Batch("post", "owner", Support.Entry("WARN", "Sync", "failed", one) + Support.Entry("WARN", "Sync", "failed", two))]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void TapVerdictsGroupByScreenControlAndVerdictNotByCount()
    {
        var three = new TapVerdict(TapVerdictKind.Repeated, 0, "Mailbox", "Send", 3, null, []).ToLogText();
        var five = new TapVerdict(TapVerdictKind.Repeated, 0, "Mailbox", "Send", 5, null, []).ToLogText();
        var dead = new TapVerdict(TapVerdictKind.Dead, 0, "Mailbox", "Send", 1, null, []).ToLogText();

        var groups = Digest.Build([Support.Batch("post", "owner",
            Support.Entry("WARN", "Tap", three) + Support.Entry("WARN", "Tap", five) + Support.Entry("WARN", "Tap", dead))]);

        Assert.Equal(2, groups.Count);
        var repeated = groups.Single(g => g.Key.Contains("REPEATED TAP"));
        Assert.Equal(2, repeated.Count);
        Assert.Equal("tap", repeated.Kind);
        Assert.Contains("Mailbox", repeated.Key);
    }

    [Fact]
    public void SignInsGroupByFlowKindAndDiagnosis()
    {
        var t0 = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);
        SignInTrail Cancelled(string flowId) => SignInTrail
            .Start(SignInFlowKinds.GmailApi, flowId, t0, null, null, ["gmail.modify"], SignInBrowserPaths.AccountPicker)
            .AccountCancelled(t0.AddSeconds(3));

        var groups = Digest.Build([Support.Batch("post", "owner",
            Support.Entry("WARN", "SignIn", Cancelled("flow-a").ToLogText()) + Support.Entry("WARN", "SignIn", Cancelled("flow-b").ToLogText()))]);

        var group = Assert.Single(groups);
        Assert.Equal("sign-in", group.Kind);
        Assert.Equal(2, group.Count);
        Assert.Contains("gmail-api", group.Key);
        Assert.Contains("Google account picker cancelled.", group.Key);
    }

    [Fact]
    public void PlainMessagesGroupByTheirTemplate()
    {
        var groups = Digest.Build([Support.Batch("post", "owner",
            Support.Entry("WARN", "Mms", "MMS download did not complete (result 4).")
            + Support.Entry("WARN", "Mms", "MMS download did not complete (result 8).")
            + Support.Entry("WARN", "Filing", "Owed delete of msg-77812@example.com failed"))]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups.Single(g => g.Key.Contains("MMS download")).Count);
    }

    [Fact]
    public void PhonesAreCountedAndAppsKeptApart()
    {
        var groups = Digest.Build([
            Support.Batch("post", "owner", Support.Entry("WARN", "Sync", "failed")),
            Support.Batch("post", "partner", Support.Entry("WARN", "Sync", "failed")),
            Support.Batch("score", "owner", Support.Entry("WARN", "Sync", "failed")),
        ]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(["owner", "partner"], groups.Single(g => g.App == "post").Phones);
    }

    [Fact]
    public void TheFingerprintIsAStableHashOfTheContentFreeKey()
    {
        var a = Digest.Build([Support.Batch("post", "owner", Support.Entry("WARN", "Sync", "failed after 3 tries"))]).Single();
        var b = Digest.Build([Support.Batch("post", "owner", Support.Entry("WARN", "Sync", "failed after 9 tries"))]).Single();

        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Matches("^[0-9a-f]{64}$", a.Fingerprint);
    }

    [Fact]
    public void TheRenderedDigestListsBiggestFirstAndHoldsNoRawLine()
    {
        var groups = Digest.Build([Support.Batch("post", "owner",
            Support.Entry("WARN", "Sync", "failed for ada.example@example.com") + Support.Entry("WARN", "Sync", "failed for ada.example@example.com")
            + Support.Entry("WARN", "Bridge", "Bridge refresh during sync failed"))]);

        var rendered = Digest.Render(groups, new LogExcerptSanitizer(new HashSet<string>(StringComparer.Ordinal) { "Sync", "Bridge" }));

        Assert.True(rendered.IndexOf("failed for", StringComparison.Ordinal) < rendered.IndexOf("Bridge refresh", StringComparison.Ordinal));
        Assert.DoesNotContain("ada.example@example.com", rendered);
    }

    [Fact]
    public void EntriesAreSplitTheWayDiagnosticLogWritesThem()
    {
        var text = Support.Entry("WARN", "Sync", "failed", "System.IO.IOException: x\n   at Post.A.B()")
            + Support.Entry("INFO", "Launch", "LAUNCH Post 1.0.42 (200042) on Android 15 (API 35)")
            + "DROPPED 2 segments (oldest 2026-10-20T08:00:00Z)\n";

        var entries = LogEntries.Parse(text);

        Assert.Equal(3, entries.Count);
        Assert.Equal("WARN", entries[0].Level);
        Assert.Equal("Sync", entries[0].Source);
        Assert.Contains("IOException", entries[0].Detail);
        Assert.Equal("DROPPED", entries[2].Level);
    }
}
