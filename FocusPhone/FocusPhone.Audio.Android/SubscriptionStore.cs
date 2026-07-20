using Android.Content;
using FocusPhone.Domain.Audio;
using FocusPhone.Infrastructure.Podcasts;

namespace FocusPhone.Audio;

/// <summary>
/// Persists the ordered, titled subscription list in SharedPreferences via the
/// unit-tested <see cref="SubscriptionListCodec"/>. Replaces the old unordered
/// feed-URL set in <see cref="PodcastStore"/>.
/// </summary>
public class SubscriptionStore
{
    private const string PreferencesName = "podcastSubscriptions";
    private const string SubscriptionsKey = "subscriptions";

    private readonly ISharedPreferences preferences;

    public SubscriptionStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public IReadOnlyList<PodcastSubscription> Load() =>
        SubscriptionListCodec.Decode(preferences.GetString(SubscriptionsKey, null));

    public void Save(IReadOnlyList<PodcastSubscription> subscriptions) =>
        preferences.Edit()!.PutString(SubscriptionsKey, SubscriptionListCodec.Encode(subscriptions))!.Apply();
}
