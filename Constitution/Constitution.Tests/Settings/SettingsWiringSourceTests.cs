using System.Text.RegularExpressions;

namespace Constitution.Tests.Settings;

/// <summary>
/// The Settings screen and first run are Android views the suite cannot load, so what they must do is
/// read from their source. The decisions themselves (what to restore, what to say, what failed) are
/// pure and tested elsewhere; these pin that the screen actually asks them (Release A review, S1, S2,
/// S3, S8).
/// </summary>
public class SettingsWiringSourceTests
{
    private static string Head(string relative)
    {
        var file = ConstitutionSource.Files(".cs")
            .SingleOrDefault(candidate => ConstitutionSource.Relative(candidate) == "Constitution.Android/" + relative);
        Assert.True(file is not null, $"Constitution.Android/{relative} was not found.");
        return File.ReadAllText(file!);
    }

    /// <summary>The text of the named method's braces, or null when it is not in the source.</summary>
    internal static string? BodyOf(string source, string method)
    {
        var declaration = Regex.Match(source, @"\b" + Regex.Escape(method) + @"\s*\([^)]*\)\s*(?:where [^{]*)?\{", RegexOptions.CultureInvariant);
        if (!declaration.Success)
        {
            return null;
        }

        var depth = 0;
        for (var i = declaration.Index + declaration.Length - 1; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[(declaration.Index + declaration.Length)..i];
            }
        }

        return null;
    }

    // ---- BodyOf itself ----

    [Fact]
    public void BodyOfFindsABodyAndNotAMissingOne()
    {
        const string source = "class A { void One() { if (x) { y(); } } void Two() { z(); } }";

        Assert.Equal(" if (x) { y(); } ", BodyOf(source, "One"));
        Assert.Equal(" z(); ", BodyOf(source, "Two"));
        Assert.Null(BodyOf(source, "Three"));
    }

    // ---- S1: a typed passphrase restores before it publishes ----

    [Fact]
    public void ATypedPassphraseRestoresThenPublishes()
    {
        var body = BodyOf(Head("SettingsArea.cs"), "CommitPassphrase");

        Assert.NotNull(body);
        Assert.Contains("RestoreThenPublishAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NoScreenPublishesWithoutSayingWhatChanged()
    {
        // The old fire-and-forget publish carried no changed set and no result; nothing may call it.
        var offenders = ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("PublishAfterChange", StringComparison.Ordinal))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "PublishAfterChange is gone; use the runner: " + string.Join(", ", offenders));
    }

    [Fact]
    public void FirstRunRestoresAfterThePassphraseIsSaved()
    {
        // Verified in the review: first run already re-runs the restore once the passphrase is stored.
        var page = Head("FirstRun/FirstRunPage.cs");
        var save = BodyOf(page, "SavePassphrase");

        Assert.NotNull(save);
        Assert.Contains("VaultPassphraseStore.Passphrase = typed", save, StringComparison.Ordinal);
        Assert.Contains("RestoreThenRefreshAsync", save, StringComparison.Ordinal);
        Assert.Contains("RestoreAsync", BodyOf(page, "RestoreThenRefreshAsync") ?? string.Empty, StringComparison.Ordinal);
    }

    // ---- S2: the note says what the vault did ----

    [Fact]
    public void TheSavedNotesComeFromTheVaultsOwnResult()
    {
        var text = Head("SettingsArea.cs");

        Assert.Contains("VaultNote.For(", text, StringComparison.Ordinal);
        Assert.Contains("VaultNoteSubject.SleepNumber", text, StringComparison.Ordinal);
        Assert.Contains("VaultNoteSubject.Remarkable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Saved to the vault", text, StringComparison.Ordinal);
        Assert.DoesNotContain("saved to the vault", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatChangedIsSaidToThePublish()
    {
        var text = Head("SettingsArea.cs");

        Assert.Contains("VaultField.SleepNumberLogin", text, StringComparison.Ordinal);
        Assert.Contains("VaultField.SleepNumberPassword", text, StringComparison.Ordinal);
        Assert.Contains("VaultField.RemarkableDeviceToken", text, StringComparison.Ordinal);
    }

    // ---- S3: a failure leaves a defined row and a sentence ----

    [Fact]
    public void PairingAndSignInWordTheirFailuresThroughTheSharedWording()
    {
        var text = Head("SettingsArea.cs");

        Assert.Contains("ConnectionFailure.Sentence(", text, StringComparison.Ordinal);
        Assert.Contains("ConnectionAct.PairRemarkable", text, StringComparison.Ordinal);
        Assert.Contains("ConnectionAct.SignIn", text, StringComparison.Ordinal);
        Assert.Contains("ConnectionFailure.Sentence(", Head("FirstRun/FirstRunPage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PairingCatchesMoreThanTheStatusCodeFailure()
    {
        var body = BodyOf(Head("SettingsArea.cs"), "PairRemarkableAsync");

        Assert.NotNull(body);
        foreach (var kind in new[] { "HttpRequestException", "OperationCanceledException", "JsonException", "FormatException", "IOException" })
        {
            Assert.Contains(kind, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryFailureIsRecordedByTypeName()
    {
        // The trail phrase is the type name alone; the screens hand the exception to it, never its message.
        Assert.Contains("ConnectionFailure.LogPhrase(", Head("Services/ConnectionTrail.cs"), StringComparison.Ordinal);
        Assert.Contains("ConnectionTrail.Expecting(", Head("SettingsArea.cs"), StringComparison.Ordinal);
        Assert.Contains("ConnectionTrail.Expecting(", Head("FirstRun/FirstRunPage.cs"), StringComparison.Ordinal);
    }

    // ---- S8: a replaced credential is not judged by the old one ----

    [Fact]
    public void SavingTheLoginAndPairingTheTabletBothReplaceTheirCredential()
    {
        var text = Head("SettingsArea.cs");

        Assert.Matches(@"CredentialReplaced\([^)]*CredentialIds\.SleepNumber", text);
        Assert.Matches(@"CredentialReplaced\([^)]*CredentialIds\.Remarkable", text);
    }

    [Fact]
    public void EveryReadOfStoredHealthIsJudgedAgainstTheReplacement()
    {
        // Settings and the diagnostics export both ask; neither may read a raw stored status.
        var settings = Head("SettingsArea.cs");
        var export = Head("Settings/DiagnosticsExport.cs");

        Assert.DoesNotContain("report.Get(", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("report.Get(", export, StringComparison.Ordinal);
        Assert.Contains("ConstitutionHealthReport.HealthOf(", settings, StringComparison.Ordinal);
        Assert.Contains("ConstitutionHealthReport.HealthOf(", export, StringComparison.Ordinal);
    }
}
