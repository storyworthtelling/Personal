using Xunit;

namespace Storyworthtelling.Diagnostics.Tests;

/// <summary>
/// Audit 11 §22: every app put a raw <c>ex.Message</c> on screen. Folio's desk alone said what a person can act on, in
/// words; that rule is now shared, with the service a refusal came from named by the caller.
/// </summary>
public class PlainErrorTests
{
    [Theory]
    [InlineData(
        "Reading pass 'story-integrity': ResponseTruncatedException: Claude response was truncated at the 8000-token output limit (stop_reason=max_tokens)",
        "the reply was cut off at the output limit")]
    [InlineData(
        "ConsolidateCraft: HttpRequestException: Anthropic credit balance is too low: 400 (BadRequest)",
        "your Anthropic credit balance is too low")]
    [InlineData(
        "Reading pass 'x': HttpRequestException: Connection failure ← IOException: Unable to resolve host \"api.anthropic.com\": No address associated with hostname",
        "the phone has no network")]
    [InlineData(
        "Reading pass 'x': IOException: Software caused connection abort ← SocketException: Software caused connection abort",
        "the connection dropped part-way through")]
    [InlineData(
        "ConsolidateAnalysis: RateLimitException: Anthropic rate limited: 429 (TooManyRequests)",
        "Claude is rate-limiting requests")]
    [InlineData(
        "Could not deliver to reMarkable: Failed to allocate a 8208 byte allocation with 203360 free bytes and 198KB until OOM",
        "the phone ran out of memory")]
    [InlineData(
        "Could not deliver to reMarkable: java.lang.OutOfMemoryError: Java heap space",
        "the phone ran out of memory")]
    [InlineData(
        "ConsolidateAnalysis: HttpRequestException: Anthropic request failed: 401 (Unauthorized): invalid x-api-key",
        "the Claude key was rejected")]
    public void FolioCasesKeepTheirWords(string recorded, string expected) =>
        Assert.Equal(expected, PlainError.Describe(recorded, "Claude"));

    [Theory]
    [InlineData("Google.GoogleApiException: The service drive has thrown an exception. HttpStatusCode is Forbidden. 403", "Google Drive refused the request")]
    [InlineData("java.io.IOException: write failed: ENOSPC (No space left on device)", "the phone is out of storage space")]
    [InlineData("TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", "the request timed out")]
    public void OtherServicesAndCausesAreNamed(string recorded, string expected) =>
        Assert.Equal(expected, PlainError.Describe(recorded, recorded.Contains("drive") ? "Google Drive" : null));

    [Fact]
    public void ARefusalWithNoServiceNamedSaysTheRequestWasTurnedAway() =>
        Assert.Equal("the request was turned away", PlainError.Describe("HttpRequestException: 401 (Unauthorized)"));

    [Fact]
    public void AnExceptionIsReadThroughItsWholeChain()
    {
        var failure = new HttpRequestException("Connection failure",
            new IOException("Unable to resolve host \"www.googleapis.com\": No address associated with hostname"));

        Assert.Equal("the phone has no network", PlainError.Describe(failure));
    }

    [Fact]
    public void AnOutOfMemoryIsKnownByItsType() =>
        Assert.Equal("the phone ran out of memory", PlainError.Describe(new OutOfMemoryException()));

    [Fact]
    public void AnUnknownCauseKeepsItsOwnWords_WithoutTheCallOrTheTypeInFront() =>
        Assert.Equal("the model replied with a haiku",
            PlainError.Describe("ProposeExpansion: InvalidOperationException: the model replied with a haiku"));

    [Fact]
    public void ALongUnknownCauseIsShortened()
    {
        var plain = PlainError.Describe("X: Exception: " + new string('a', 500));

        Assert.True(plain.Length <= 140, $"{plain.Length} characters");
        Assert.EndsWith("…", plain);
    }

    [Fact]
    public void NothingRecordedIsNothingToSay()
    {
        Assert.Equal(string.Empty, PlainError.Describe((string?)null));
        Assert.Equal(string.Empty, PlainError.Describe("  "));
    }
}
