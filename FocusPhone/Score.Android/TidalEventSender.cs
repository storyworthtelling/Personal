using Com.Tidal.Sdk.Eventproducer;
using Com.Tidal.Sdk.Eventproducer.Model;

namespace Score;

/// <summary>
/// TIDAL's own <c>EventSender</c> — playback telemetry the Player SDK reports as its
/// terms require (see <c>Codex.Tidal.Binding/README.md</c>: the telemetry model
/// classes are excluded from the binding, but the SDK still reports at runtime).
/// Neither method is a suspend function, so unlike <see cref="TidalCredentialsProvider"/>
/// this needed no coroutine bridge — just an implementation to hand the Player
/// constructor.
///
/// Score does not have its own consent/analytics pipeline for this today, so both
/// methods currently just log rather than doing anything with the event — enough to
/// satisfy the SDK's requirement to be handed a working sender without inventing
/// behavior nothing in the app calls for yet.
/// </summary>
public sealed class TidalEventSender : Java.Lang.Object, IEventSender
{
    private const string LogTag = "TidalEventSender";

    public void SendEvent(
        string eventName, ConsentCategory consentCategory, string payload, IDictionary<string, string> headers) =>
        Android.Util.Log.Debug(LogTag, $"SendEvent(name=\"{eventName}\", category={consentCategory})");

    public void SetBlockedConsentCategories(ICollection<ConsentCategory> blockedConsentCategories) =>
        Android.Util.Log.Debug(LogTag,
            $"SetBlockedConsentCategories([{string.Join(", ", blockedConsentCategories)}])");
}
