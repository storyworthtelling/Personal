using Android.App;
using Android.Content;
using Android.Widget;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain.Household;

namespace Pulp.Settings;

/// <summary>
/// The household page's sync line with its quiet "Sync now" button (S5): the line says where the household
/// stands against Drive (<see cref="HouseholdSyncText"/>), the button asks for a pass at once — which also
/// releases a file that was refused until someone asked — and the line is the state change when it lands.
/// The page adds one call to <see cref="Build"/>; it owns nothing of the sync.
/// </summary>
public static class SyncLine
{
    public const string ButtonLabel = "Sync now";

    /// <summary>
    /// The line and its button as one view. <paramref name="lifetime"/> is the page's token, so a pass the
    /// page is no longer there to show is not waited on. The line reads the status when built; the page
    /// builds it again when it repaints, as it already does when the sync status moves.
    /// </summary>
    public static LinearLayout Build(Activity host, PulpServices services, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(services);
        Context context = host;

        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(Android.Views.GravityFlags.CenterVertical);
        var line = PaperUi.Secondary(context, HouseholdSyncText.Line(services.SyncStatus, DateTimeOffset.UtcNow, TimeZoneInfo.Local));
        row.AddView(line, new LinearLayout.LayoutParams(0, Android.Views.ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var busy = false;
        Button? button = null;
        button = PaperUi.QuietButton(context, ButtonLabel, () =>
        {
            if (busy)
            {
                return;
            }

            busy = true;
            button!.Enabled = false;
            _ = Task.Run(async () =>
            {
                var status = await services.SyncNowAsync(lifetime);
                host.RunOnUiThread(() =>
                {
                    busy = false;
                    button.Enabled = true;
                    line.Text = HouseholdSyncText.Line(status, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
                });
            }, lifetime);
        });
        row.AddView(button);
        return row;
    }
}
