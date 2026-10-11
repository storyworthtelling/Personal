using Pulp.Infrastructure.Catalogue;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.Tests.Infrastructure;

/// <summary>Invented keys, changeable between calls so a test can prove each call reads the key live.</summary>
public sealed class FakeKeys : IPulpKeySource
{
    public PulpSecrets Keys { get; set; } = new("invented-sa-key", "invented-omdb-key", "invented-yt-key");
}

public sealed class RecordingHealthSink : ICredentialHealthSink
{
    public List<string> Ok { get; } = [];

    public List<string> Failing { get; } = [];

    public void MarkOk(string credentialId) => Ok.Add(credentialId);

    public void MarkFailing(string credentialId, string message) => Failing.Add(credentialId);
}
