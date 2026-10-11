using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain.Availability;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Services;
using Pulp.Domain.Settings;
using Pulp.Library;

namespace Pulp.Settings;

/// <summary>
/// A service's own page (D272: no mockup, built from the Paper anatomy and the recorded decisions).
/// From the top: whether the household has it (D1); its plan — the tier, no ads or with ads (D29), and
/// the best picture it streams at (D86) — which the row in Settings reads back as "4K · no ads"; the
/// PIN-lock attestation with the plain sentence of what ticking it means (D210 — children may start
/// titles only on attested services; a kids-only app needs none); and whether Flyleaf keeps the app on
/// its tray (D8 — off by default; stored now, read by Flyleaf when the cross-app contract lands).
///
/// <para>Every change is an inline state change written at once (§5) through the household's one
/// write path, queued so a quick second change never lands ahead of the first. Not built here, and
/// reported: profiles mapped to viewers (D86, D11) and a live service's channels.</para>
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class ServiceActivity : Activity
{
    public const string ExtraServiceId = "serviceId";

    private const string Loading = "Reading the household…";

    private readonly CancellationTokenSource lifetime = new();
    private PulpServices services = null!;
    private LibraryWrites writes = null!;
    private LinearLayout column = null!;
    private StreamingService? service;
    private ServiceId id;

    // What this page last wrote, so a second change builds on the first even before it lands.
    private ServicePlanEntry plan = null!;
    private bool had;
    private bool attested;
    private bool loaded;
    private int pending;
    private SyncWatch syncWatch = null!;

    public static Intent For(Context context, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(ServiceActivity));
        intent.PutExtra(ExtraServiceId, service.Value);
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        services = PulpServices.For(this);
        // Re-read only once every queued write has landed: a read taken between two writes would put
        // the first one's plan back on the page and the next change would build on it.
        writes = new LibraryWrites(services, () => RunOnUiThread(() =>
        {
            pending--;
            if (pending == 0)
            {
                Reload();
            }
        }), "settings");

        var raw = Intent?.GetStringExtra(ExtraServiceId);
        if (string.IsNullOrWhiteSpace(raw))
        {
            services.Diagnostics.Warn("settings", "A service page was opened with no service id.");
            Finish();
            return;
        }

        syncWatch = new SyncWatch(this, services, Reload);
        id = new ServiceId(raw);
        service = services.Catalogue.ById(id);
        plan = new ServicePlanEntry(id);

        column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        var (spread, _) = SettingsViews.Frame(this, service?.Name ?? id.Value, scroll);
        SetContentView(spread);

        ShowStatus(Loading);
        Reload();
    }

    protected override void OnResume()
    {
        base.OnResume();
        syncWatch?.Resume();
    }

    protected override void OnPause()
    {
        base.OnPause();
        syncWatch?.Pause();
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }

    private void Reload()
    {
        var token = lifetime.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var view = await services.HouseholdAsync(token);
                RunOnUiThread(() =>
                {
                    if (IsFinishing || IsDestroyed || pending > 0)
                    {
                        return;
                    }

                    plan = view.PlanOf(id) ?? new ServicePlanEntry(id);
                    had = view.Services.Contains(id);
                    attested = view.Attested.Covers(id, services.Catalogue) && service is { KidsOnly: false };
                    loaded = true;
                    Render();
                });
            }
            catch (System.OperationCanceledException)
            {
                // The page closed while it was reading; nothing is left to paint.
                return;
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("settings", $"The page for service {id} could not read the household.", exception);
                RunOnUiThread(() => ShowStatus("This service couldn't be read just now."));
            }
        }, token);
    }

    private void Render()
    {
        column.RemoveAllViews();
        if (service is null)
        {
            // A record names a service this build's catalogue does not carry (a newer writer's).
            column.AddView(PaperUi.Secondary(this, "Pulp doesn't know this service yet, so it can't be set up here."));
            return;
        }

        var name = service.Name;
        if (!services.Rights.CanEditServices)
        {
            RenderReadOnly(name);
            return;
        }

        column.AddView(SettingsViews.CheckRow(this, $"The household has {name}", had, SetHad));
        if (!loaded || !had)
        {
            column.AddView(PaperUi.Secondary(this, $"Tick it to open titles on {name} from Pulp."), SettingsViews.Spaced(this, DesignTokens.Space.X1));
            return;
        }

        // ---- the plan (D29, D86) ----
        column.AddView(SettingsViews.Subheading(this, "Plan"));
        column.AddView(PaperUi.Segmented(
            this,
            [ServiceRowText.TierChoice(PlanTier.AdFree), ServiceRowText.TierChoice(PlanTier.WithAds)],
            index => SetPlan(plan with { Tier = index == 0 ? PlanTier.AdFree : PlanTier.WithAds }),
            plan.Tier switch { PlanTier.AdFree => 0, PlanTier.WithAds => 1, _ => -1 }));

        var pictures = new[] { VideoQuality.Sd, VideoQuality.Hd, VideoQuality.Uhd };
        column.AddView(PaperUi.Segmented(
            this,
            pictures.Select(ServiceRowText.Picture).ToList(),
            index => SetPlan(plan with { Picture = pictures[index] }),
            plan.Picture is { } picture ? Array.IndexOf(pictures, picture) : -1),
            SettingsViews.Spaced(this, DesignTokens.Space.X2));
        column.AddView(Muted(ServiceRowText.Tier(plan)), SettingsViews.Spaced(this, DesignTokens.Space.X1));

        // ---- children (D210) ----
        column.AddView(SettingsViews.Subheading(this, "Children"));
        if (service.KidsOnly)
        {
            column.AddView(PaperUi.Secondary(this, AttestationText.KidsOnly(name)));
        }
        else
        {
            column.AddView(SettingsViews.CheckRow(this, AttestationText.Toggle, attested, SetAttested));
            column.AddView(Muted(AttestationText.Statement(name)));
        }

        // ---- Flyleaf (D8) ----
        column.AddView(SettingsViews.Subheading(this, "Flyleaf"));
        column.AddView(SettingsViews.CheckRow(this, "Hide from Flyleaf", !plan.ShownInFlyleaf, hide => SetPlan(plan with { ShownInFlyleaf = !hide })));
        column.AddView(Muted($"Off Flyleaf's tray, {name} opens only through Pulp, on a title."));
    }

    /// <summary>D166: on the phone the household is shared with the service is read, not changed.</summary>
    private void RenderReadOnly(string name)
    {
        column.AddView(PaperUi.Body(this, had ? $"The household has {name}." : $"The household doesn't have {name}."));
        if (had)
        {
            column.AddView(Muted(ServiceRowText.Tier(plan)), SettingsViews.Spaced(this, DesignTokens.Space.X1));
            column.AddView(Muted(service is { KidsOnly: true } ? AttestationText.KidsOnly(name) : AttestationText.ReadOnly(name, attested)));
        }

        column.AddView(Muted(HouseholdPermissions.OwnerOnlyNote), SettingsViews.Spaced(this, DesignTokens.Space.X3));
    }

    private void SetHad(bool has)
    {
        had = has;
        var entry = new HouseholdServiceEntry(id);
        pending++;
        writes.Enqueue(has ? $"add {id}" : $"remove {id}", token => has
            ? services.PutAsync(HouseholdMaps.HouseholdService, entry, token)
            : services.RemoveAsync(HouseholdMaps.HouseholdService, entry, token));
        Render();
    }

    private void SetPlan(ServicePlanEntry changed)
    {
        plan = changed;
        pending++;
        writes.Enqueue($"record the plan for {id}", token => services.PutAsync(HouseholdMaps.ServicePlan, changed, token));
        Render();
    }

    private void SetAttested(bool locked)
    {
        attested = locked;
        var entry = new AttestedServiceEntry(id);
        pending++;
        writes.Enqueue(locked ? $"attest {id}" : $"withdraw the attestation for {id}", token => locked
            ? services.PutAsync(HouseholdMaps.AttestedService, entry, token)
            : services.RemoveAsync(HouseholdMaps.AttestedService, entry, token));
    }

    private TextView Muted(string text)
    {
        var line = PaperUi.Secondary(this, text);
        line.SetTextColor(PaperTheme.InkSoft);
        return line;
    }

    private void ShowStatus(string text)
    {
        column.RemoveAllViews();
        column.AddView(PaperUi.Secondary(this, text));
    }
}
