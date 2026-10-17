using System.Net;
using System.Text.Json;
using Constitution.Domain.Settings;

namespace Constitution.Tests.Settings;

/// <summary>
/// S3 — pairing and signing in always leave the row in a defined state with a sentence. The failures are
/// told apart by kind (a refused code, a timeout, an unreadable reply, a dropped network), each in
/// plain words, and none repeats a message that could carry a value.
/// </summary>
public class ConnectionFailureTests
{
    private const string Echo = "example-one-time-code-echo";

    [Fact]
    public void ARefusedCodeNamesTheStatus()
    {
        var failure = new HttpRequestException(Echo, null, HttpStatusCode.BadRequest);

        var sentence = ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, failure, callerCancelled: false);

        Assert.Equal("The tablet did not accept that code (BadRequest).", sentence);
    }

    [Fact]
    public void ANetworkThatNeverAnsweredIsNotACodeRefusal()
    {
        var failure = new HttpRequestException(Echo);

        var sentence = ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, failure, callerCancelled: false);

        Assert.Contains("could not be reached", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("did not accept", sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(TimeoutException))]
    public void ATimeoutThePersonDidNotAskForSaysItTimedOut(Type type)
    {
        var failure = (Exception)Activator.CreateInstance(type)!;

        var pairing = ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, failure, callerCancelled: false);
        var signIn = ConnectionFailure.Sentence(ConnectionAct.SignIn, failure, callerCancelled: false);

        Assert.Contains("timed out", pairing, StringComparison.Ordinal);
        Assert.Contains("timed out", signIn, StringComparison.Ordinal);
    }

    [Fact]
    public void ACancellationTheCallerAskedForIsNotATimeout()
    {
        var sentence = ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, new TaskCanceledException(), callerCancelled: true);

        Assert.DoesNotContain("timed out", sentence, StringComparison.Ordinal);
        Assert.Contains("stopped", sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(FormatException))]
    public void AReplyThatCannotBeReadIsSaidSo(Type type)
    {
        var failure = (Exception)Activator.CreateInstance(type)!;

        var sentence = ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, failure, callerCancelled: false);

        Assert.Contains("could not read", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ADroppedConnectionIsANetworkProblem()
    {
        var sentence = ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, new IOException(Echo), callerCancelled: false);

        Assert.Contains("connection", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignInThatDidNotCompleteKeepsItsOwnExplanation()
    {
        // These come from the shared sign-in code ("Account selection was cancelled."), written for the person.
        var sentence = ConnectionFailure.Sentence(
            ConnectionAct.SignIn, new InvalidOperationException("Account selection was cancelled."), callerCancelled: false);

        Assert.Equal("Sign-in did not complete. Account selection was cancelled.", sentence);
    }

    [Fact]
    public void AnythingElseNamesOnlyItsType()
    {
        var sentence = ConnectionFailure.Sentence(ConnectionAct.PairRemarkable, new ArithmeticException(Echo), callerCancelled: false);

        Assert.Contains("ArithmeticException", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain(Echo, sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TaskCanceledException))]
    [InlineData(typeof(JsonException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(ArithmeticException))]
    public void NoSentenceEchoesAMessageAndNoneIsEmpty(Type type)
    {
        var failure = (Exception)Activator.CreateInstance(type, Echo)!;

        foreach (var act in Enum.GetValues<ConnectionAct>())
        {
            var sentence = ConnectionFailure.Sentence(act, failure, callerCancelled: false);

            Assert.False(string.IsNullOrWhiteSpace(sentence));
            Assert.EndsWith(".", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain(Echo, sentence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheTrailPhraseKeepsTheTypeNameAndLosesTheMessage()
    {
        var phrase = ConnectionFailure.LogPhrase(ConnectionAct.PairRemarkable, new JsonException(Echo));

        Assert.Equal("remarkable pairing failed: JsonException", phrase);
        Assert.Equal(
            "sign-in failed: TaskCanceledException",
            ConnectionFailure.LogPhrase(ConnectionAct.SignIn, new TaskCanceledException(Echo)));
    }

    [Fact]
    public void EveryActHasItsOwnPhrase()
    {
        var phrases = Enum.GetValues<ConnectionAct>()
            .Select(act => ConnectionFailure.LogPhrase(act, new IOException()))
            .ToList();

        Assert.Equal(phrases.Count, phrases.Distinct().Count());
    }
}
