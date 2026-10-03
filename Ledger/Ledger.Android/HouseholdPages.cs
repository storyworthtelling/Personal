using Index.Domain.Household;
using Index.Infrastructure.Household;
using Storyworthtelling.Maui;

namespace Index.Android;

/// <summary>What a page learns, in one step, about which install this is and what her side can show.</summary>
/// <param name="IsHers">The remembered role (no Drive read to learn it).</param>
/// <param name="Snapshot">Her data, when this is her install and the household file could be read now.</param>
/// <param name="FileIsNewer">Her install, and the file was written by a newer Index than this build can read
/// (remediation 3.6). Distinct from "nothing to show": the pages say so plainly
/// (<see cref="HouseholdChartCopy.NewerFile"/>) instead of reading as an empty phone.</param>
internal readonly record struct HouseholdView(bool IsHers, HouseholdSnapshot? Snapshot, bool FileIsNewer = false);

/// <summary>
/// The one place the five tabs ask "is this her install, and what do I render?". The role comes from
/// <see cref="InstallRoleService"/> — settled once and remembered, so no tab re-derives it from Drive on
/// every appearance — and the household file is read only when it is her install, because the file is
/// then the DATA she renders, not the question of who she is.
/// </summary>
internal static class HouseholdPages
{
    /// <summary>The one sentence every page shows, in the kit's empty-page voice, when the household file
    /// was written by a newer Index than this build can read (remediation 3.6). Drawing nothing, or falling
    /// back to her empty local database, would read as "nothing is connected" — which is false.</summary>
    public static View NewerFileNotice() => new Label
    {
        Text = HouseholdChartCopy.NewerFile,
        Style = PaperResources.Token<Style>("EmptyStateLabel"),
        HorizontalTextAlignment = TextAlignment.Center,
        VerticalOptions = LayoutOptions.Center,
        Margin = new Thickness(34, 120, 34, 0),
    };

    public static async Task<HouseholdView> LoadAsync(InstallRoleService roles, HouseholdSnapshotReader reader)
    {
        if (await roles.ResolveAsync(CancellationToken.None) != InstallRole.Hers)
        {
            return new HouseholdView(false, null);
        }

        var passphrase = SettingsStore.VaultPassphrase;
        if (string.IsNullOrEmpty(passphrase))
        {
            return new HouseholdView(true, null);
        }

        try
        {
            var result = await reader.ReadAsync(passphrase, CancellationToken.None);
            return new HouseholdView(
                true,
                result.Status == HouseholdSnapshotStatus.Loaded ? result.Snapshot : null,
                FileIsNewer: result.Status == HouseholdSnapshotStatus.NewerVersion);
        }
        catch
        {
            // Best-effort: Drive being unreachable just means her tab has nothing newer to show right
            // now. The role is already settled, so this cannot turn her install into the owner's.
            return new HouseholdView(true, null);
        }
    }
}
