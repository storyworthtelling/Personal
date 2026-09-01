namespace Storyboarder.Infrastructure.External;

// Raised when Claude stops generating because it hit its max_tokens output ceiling. The accumulated
// text is a partial response (typically unterminated JSON), so we fail loudly here with an actionable
// message rather than returning the truncated body and letting it surface downstream as an opaque JSON
// parse error (e.g. System.Text.Json's "ZeroDepthAtEnd").
public sealed class ResponseTruncatedException : Exception
{
    public ResponseTruncatedException(string message) : base(message)
    {
    }
}
