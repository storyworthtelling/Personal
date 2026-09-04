using Android.Content;
using Codex.Domain.Audio;
using Codex.Infrastructure.Podcasts;

namespace Score;

/// <summary>
/// What is downloaded and when it arrived. A thin <see cref="ISharedPreferences"/>
/// adapter over <see cref="DownloadBudgetCodec"/>.
///
/// The files themselves live in ExoPlayer's download cache, which is the authority on
/// what is actually on disk. What this holds is the part the cache does not know: how
/// long each thing has been there, which is what the ten-day sweep runs on, and how
/// much playback time it costs, which is what the budget is measured in.
/// </summary>
public class DownloadStore
{
    private const string PreferencesName = "downloads";
    private const string BudgetKey = "budget";

    private readonly ISharedPreferences preferences;

    public DownloadStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public DownloadBudget Load() => DownloadBudgetCodec.Decode(preferences.GetString(BudgetKey, null));

    public void Save(DownloadBudget budget) =>
        preferences.Edit()!.PutString(BudgetKey, DownloadBudgetCodec.Encode(budget))!.Apply();
}
