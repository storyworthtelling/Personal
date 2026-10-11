using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Diagnostics;
using Codex.Ui;
using Codex.Ui.Voice;
using Pulp.App;
using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Services;
using Pulp.Domain.Settings;
using Pulp.Domain.Shell;
using Pulp.Infrastructure.Catalogue;
using Pulp.Settings;
using Pulp.Vault;
using Storyworthtelling.Voice.Health;

namespace Pulp.Shell;

/// <summary>
/// Settings, one tab of Pulp's binder (mockup 09, D162, D250). Six groups in the drawn order —
/// Services · Household · Discover · Vault · About · Diagnostics (TV waits for its phase, D187) — each
/// a Serif heading with its quiet line, each folding; Services is open as drawn. Services is a short
/// row per service, name and tier, with one neutral "Adults only" badge on a service no child may
/// start titles on (D248, D249); a tap opens the service's own page. Household opens its own page
/// (D272). Discover sets the stack's size on this phone (D106). Vault opens the vault page. About is
/// the version and the data credits (D140). Diagnostics exports the log (D125).
///
/// <para>Settings is the neutral grey area (§9): nothing here wears the identity or a role, bar the
/// pine tick a service the household has carries, as drawn. The page ends with the suite's build line
/// (<see cref="PaperUi.BuildNumber"/>), which every Settings screen in the suite ends with.</para>
/// </summary>
public sealed class SettingsArea(Activity host) : PulpAreaPage(host, PulpArea.Settings)
{
    public const string ReadingStatus = "Reading the household…";
    public const string UnreadableStatus = "The household couldn't be read just now.";

    private readonly HashSet<string> open = [SettingsGroups.Services];
    private CancellationTokenSource? loading;
    private Snapshot? snapshot;
    private bool unreadable;
    private string? diagnosticsStatus;

    private sealed record Snapshot(HouseholdView Household, IReadOnlyList<bool> KeysNeedingEntry);

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        var services = PulpServices.For(Host);

        if (snapshot is { } read)
        {
            column.AddView(ServicesGroup(services, read.Household));
            column.AddView(HouseholdGroup(read.Household));
        }
        else
        {
            column.AddView(PaperUi.Secondary(Host, unreadable ? UnreadableStatus : ReadingStatus));
        }

        column.AddView(DiscoverGroup());
        column.AddView(VaultGroup());
        column.AddView(AboutGroup());
        column.AddView(DiagnosticsGroup(services));

        // The quiet foot line: which build is actually on the phone. The channel installs
        // silently and most updates change nothing visible, so without it "did that land?" has
        // no answer on the device.
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.TopMargin = PaperTheme.Dp(Host, DesignTokens.Space.X8);
        column.AddView(PaperUi.BuildNumber(Host, "Pulp"), lp);

        var scroll = new ScrollView(Host) { FillViewport = true };
        scroll.AddView(column);
        return scroll;
    }

    public override void Resume() => Reload();

    public override void Pause()
    {
        loading?.Cancel();
        loading = null;
    }

    // ---- Services (D1, D86, D248, D249) ----

    private View ServicesGroup(PulpServices services, HouseholdView household)
    {
        var had = household.Services.Distinct().ToList();
        return Group(SettingsGroups.Services, ServiceRowText.Count(had.Count), body =>
        {
            foreach (var id in had)
            {
                var entry = services.Catalogue.ById(id);
                var name = entry?.Name ?? id.Value;
                var badge = ServiceRowText.AdultsOnly(id, household.Attested, services.Catalogue) ? ServiceRowText.AdultsOnlyBadge : null;
                body.AddView(SettingsViews.Rule(Host, PaperTheme.Hairline));
                body.AddView(SettingsViews.Row(
                    Host, name, ServiceRowText.Tier(household.PlanOf(id)), ticked: true, badge: badge,
                    onTap: () => Host.StartActivity(ServiceActivity.For(Host, id))));
            }

            body.AddView(SettingsViews.Rule(Host, PaperTheme.Hairline));
            if (!services.Rights.CanEditServices)
            {
                // D166: on the phone the household is shared with, services are the owner's to change.
                body.AddView(PaperUi.Secondary(Host, HouseholdPermissions.OwnerOnlyNote), SettingsViews.Spaced(Host, DesignTokens.Space.X2));
                return;
            }

            // As drawn: the one action in the group, in the sans semibold and the ink.
            var add = PaperUi.Body(Host, ServiceRowText.AddService);
            add.Typeface = PaperTheme.SansSemibold(Host);
            add.Gravity = GravityFlags.CenterVertical;
            add.SetMinimumHeight(PaperTheme.Dp(Host, DesignTokens.Shape.TouchTargetDp));
            add.SetPadding(PaperTheme.Dp(Host, DesignTokens.Space.X1), 0, 0, 0);
            add.Clickable = true;
            add.Click += (_, _) => OfferServices(services, had);
            body.AddView(add);
        });
    }

    /// <summary>"Add a service": the catalogue's services the household does not have; a pick opens that service's page to tick it.</summary>
    private void OfferServices(PulpServices services, IReadOnlyList<ServiceId> had)
    {
        var others = services.Catalogue.All.Where(service => !had.Contains(service.Id)).OrderBy(service => service.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var actions = others
            .Select(service => new CardAction(service.Name, () => Host.StartActivity(ServiceActivity.For(Host, service.Id))))
            .ToArray();
        ActionCard.Show(Host, Overlay, ServiceRowText.AddService, others.Count == 0 ? "Every service Pulp knows is already yours." : null, actions);
    }

    // ---- Household (D88, D166, D272) ----

    private View HouseholdGroup(HouseholdView household)
    {
        var people = household.Roster?.Viewers ?? [];
        var names = people.Select(viewer => household.NameOf(viewer.Id)).ToList();
        return Group(SettingsGroups.Household, SettingsGroups.HouseholdMeta(names), body =>
        {
            // D166, mockup 09: where the household stands against Drive, as a quiet line.
            body.AddView(PaperUi.Secondary(Host, HouseholdSyncText.Line(PulpServices.For(Host).SyncStatus, DateTimeOffset.UtcNow)),
                SettingsViews.Spaced(Host, DesignTokens.Space.X1));
            foreach (var viewer in people)
            {
                var ceiling = household.ChildRules.FirstOrDefault(rules => rules.Child == viewer.Id)?.Ceiling;
                body.AddView(SettingsViews.Rule(Host, PaperTheme.Hairline));
                body.AddView(SettingsViews.Row(
                    Host, household.NameOf(viewer.Id), HouseholdText.Person(viewer.IsChild, ceiling),
                    onTap: () => Host.StartActivity(HouseholdActivity.For(Host))));
            }

            body.AddView(PaperUi.QuietButton(Host, "People, children's rules and this phone", () => Host.StartActivity(HouseholdActivity.For(Host))),
                SettingsViews.Spaced(Host, DesignTokens.Space.X2));
        });
    }

    // ---- Discover (D106) ----

    private View DiscoverGroup()
    {
        var store = new StackSizeStore(Host);
        var sizes = store.Load();
        return Group(SettingsGroups.Discover, StackSizeChoice.Summary(sizes), body =>
        {
            body.AddView(PaperUi.Secondary(Host, "How many the stack deals each Friday, on this phone."));
            body.AddView(Stepper(store, sizes, TitleKind.Film), SettingsViews.Spaced(Host, DesignTokens.Space.X2));
            body.AddView(Stepper(store, sizes, TitleKind.Show), SettingsViews.Spaced(Host, DesignTokens.Space.X2));
        });
    }

    private View Stepper(StackSizeStore store, StackSizes sizes, TitleKind kind)
    {
        var row = new LinearLayout(Host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(PaperTheme.Dp(Host, DesignTokens.Shape.TouchTargetDp));
        row.AddView(PaperUi.Body(Host, StackSizeChoice.Pile(kind, sizes.For(kind))), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        foreach (var (label, delta) in new[] { ("Fewer", -1), ("More", +1) })
        {
            var button = PaperUi.SecondaryButton(Host, label, () =>
            {
                if (StackSizeChoice.CanStep(store.Load(), kind, delta))
                {
                    store.Save(StackSizeChoice.Step(store.Load(), kind, delta));
                    Invalidate();
                }
            });
            button.Enabled = StackSizeChoice.CanStep(sizes, kind, delta);
            button.ContentDescription = $"{label} {(kind == TitleKind.Film ? "films" : "shows")}";
            row.AddView(button, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                MarginStart = PaperTheme.Dp(Host, DesignTokens.Space.X2),
            });
        }

        return row;
    }

    // ---- Vault (credential health) ----

    private View VaultGroup()
    {
        var meta = snapshot is { } read ? SettingsGroups.VaultMeta(read.KeysNeedingEntry) : null;
        return Group(SettingsGroups.Vault, meta, body =>
        {
            body.AddView(PaperUi.Secondary(Host, "The passphrase, and the keys for availability, ratings and trailers."));
            body.AddView(PaperUi.QuietButton(Host, "Open the vault", () => Host.StartActivity(VaultActivity.For(Host))),
                SettingsViews.Spaced(Host, DesignTokens.Space.X2));
        });
    }

    // ---- About (D140) ----

    private View AboutGroup() =>
        Group(SettingsGroups.About, SettingsGroups.AboutMeta(VersionName()), body =>
        {
            foreach (var credit in SettingsGroups.Credits)
            {
                var line = PaperUi.Secondary(Host, credit);
                line.SetTextColor(PaperTheme.InkSoft);
                body.AddView(line, SettingsViews.Spaced(Host, DesignTokens.Space.X1));
            }
        });

    // ---- Diagnostics (D125) ----

    private View DiagnosticsGroup(PulpServices services) =>
        Group(SettingsGroups.Diagnostics, SettingsGroups.DiagnosticsMeta, body =>
        {
            body.AddView(PaperUi.Secondary(Host, diagnosticsStatus ?? "What went wrong quietly on this phone — ids, never what anyone watched."));
            body.AddView(PaperUi.SecondaryButton(Host, SettingsGroups.DiagnosticsMeta, () => ExportLog(services)),
                SettingsViews.Spaced(Host, DesignTokens.Space.X2));
        });

    private void ExportLog(PulpServices services)
    {
        var directory = Host.FilesDir?.AbsolutePath;
        string text;
        try
        {
            text = directory is null ? string.Empty : new DiagnosticLog(directory).ReadAll().TrimEnd();
        }
        catch (IOException exception)
        {
            services.Diagnostics.Warn("settings", "The diagnostics log could not be read for export.", exception);
            diagnosticsStatus = "The log couldn't be read just now.";
            Invalidate();
            return;
        }

        if (text.Length == 0)
        {
            diagnosticsStatus = "Nothing has been logged on this phone.";
            Invalidate();
            return;
        }

        var send = new Intent(Intent.ActionSend);
        send.SetType("text/plain");
        send.PutExtra(Intent.ExtraSubject, "Pulp diagnostics");
        send.PutExtra(Intent.ExtraText, text);
        Host.StartActivity(Intent.CreateChooser(send, SettingsGroups.DiagnosticsMeta));
    }

    // ---- reading ----

    private View Group(string title, string? meta, Action<LinearLayout> fill) =>
        SettingsViews.Group(Host, title, meta, open.Contains(title), () =>
        {
            if (!open.Remove(title))
            {
                open.Add(title);
            }

            Invalidate();
        }, fill);

    private string? VersionName()
    {
        try
        {
            return Host.PackageManager?.GetPackageInfo(Host.PackageName!, (PackageInfoFlags)0)?.VersionName;
        }
        catch (PackageManager.NameNotFoundException exception)
        {
            PulpServices.For(Host).Diagnostics.Warn("settings", "The package manager did not know Pulp's own package.", exception);
            return null;
        }
    }

    private void Reload()
    {
        loading?.Cancel();
        var cancel = new CancellationTokenSource();
        loading = cancel;
        var services = PulpServices.For(Host);

        _ = Task.Run(async () =>
        {
            try
            {
                var household = await services.HouseholdAsync(cancel.Token);
                var keys = KeysNeedingEntry(services);
                Host.RunOnUiThread(() =>
                {
                    if (cancel.IsCancellationRequested)
                    {
                        return;
                    }

                    unreadable = false;
                    snapshot = new Snapshot(household, keys);
                    Invalidate();
                });
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // A newer read, or the screen going away, superseded this one.
            }
            catch (Exception exception)
            {
                services.Diagnostics.Warn("settings", "Settings could not read the household.", exception);
                Host.RunOnUiThread(() =>
                {
                    unreadable = snapshot is null;
                    Invalidate();
                });
            }
        });
    }

    /// <summary>For each of Pulp's three keys, whether its field would show (missing, or just rejected) — credential health's own rule.</summary>
    private static IReadOnlyList<bool> KeysNeedingEntry(PulpServices services)
    {
        var keys = services.Keys.Keys;
        var health = new CredentialHealthStore().Load();
        return
        [
            CredentialFieldState.Decide(!string.IsNullOrWhiteSpace(keys.StreamingAvailabilityKey), health.Get(PulpCredentialIds.StreamingAvailability)).ShowEntry,
            CredentialFieldState.Decide(!string.IsNullOrWhiteSpace(keys.OmdbKey), health.Get(PulpCredentialIds.Omdb)).ShowEntry,
            CredentialFieldState.Decide(!string.IsNullOrWhiteSpace(keys.YouTubeKey), health.Get(PulpCredentialIds.YouTube)).ShowEntry,
        ];
    }
}
