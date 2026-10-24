using Folio.Domain.Pipeline;
using Xunit;

namespace Folio.Tests.Pipeline;

// The recorded error is built for a diagnostics file: the call, the exception type, the inner chain.
// The desk needs the one thing a person can act on, in words.
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
        "the connection dropped part-way through the reply")]
    [InlineData(
        "ConsolidateAnalysis: RateLimitException: Anthropic rate limited: 429 (TooManyRequests)",
        "Anthropic is rate-limiting requests")]
    [InlineData(
        "Could not deliver to reMarkable: Failed to allocate a 8208 byte allocation with 203360 free bytes and 198KB until OOM",
        "the phone ran out of memory")]
    [InlineData(
        "Could not deliver to reMarkable: java.lang.OutOfMemoryError: Java heap space",
        "the phone ran out of memory")]
    [InlineData(
        "ConsolidateAnalysis: HttpRequestException: Anthropic request failed: 401 (Unauthorized): invalid x-api-key",
        "the Claude key was rejected")]
    public void KnownCausesBecomeASentenceFragment(string recorded, string expected)
    {
        Assert.Equal(expected, PlainError.Describe(recorded));
    }

    [Fact]
    public void AnUnknownCauseKeepsItsOwnWords_WithoutTheCallOrTheTypeInFront()
    {
        var plain = PlainError.Describe("ProposeExpansion: InvalidOperationException: the model replied with a haiku");

        Assert.Equal("the model replied with a haiku", plain);
    }

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
        Assert.Equal(string.Empty, PlainError.Describe(null));
        Assert.Equal(string.Empty, PlainError.Describe("  "));
    }
}
