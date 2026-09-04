using Microsoft.Extensions.Options;

namespace Storyworthtelling.Voice;

// Adapts the Drive-backed shared config into the IOptions<VoiceOptions> that VoiceClient reads, so an app
// that keeps its key in Drive (rather than local settings) needs no key screen. Because the key lives in
// Drive — an async read — the value is loaded explicitly via RefreshAsync and then cached; Value returns
// the cache (empty until the first refresh). Apps call RefreshAsync when a screen appears, just before
// composing, so the compose sees the current key; a refresh failure leaves the cache as-is (empty →
// VoicedMessenger falls back to neutral copy).
public sealed class DriveVoiceOptions : IOptions<VoiceOptions>
{
    private readonly IVoiceConfigStore configStore;
    private volatile VoiceOptions cached = new();

    public DriveVoiceOptions(IVoiceConfigStore configStore)
    {
        this.configStore = configStore;
    }

    public VoiceOptions Value => cached;

    // Loads the shared config from Drive and updates the cache. Returns the loaded options.
    public async Task<VoiceOptions> RefreshAsync(CancellationToken cancellationToken)
    {
        var config = await configStore.LoadAsync(cancellationToken);
        var options = new VoiceOptions
        {
            ApiKey = config.ApiKey,
            Model = config.Model,
            FastModel = config.FastModel
        };
        cached = options;
        return options;
    }
}
