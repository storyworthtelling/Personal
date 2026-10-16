using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.People;
using Pulp.Domain.Settings;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;
using Pulp.Library;

namespace Pulp.Settings;

/// <summary>
/// Household (D272: no mockup, built from the Paper anatomy and the recorded decisions). Three parts,
/// top to bottom: whose phone this is (D97 — only an adult on the roster can be recorded, the rule
/// <see cref="ThisPhone"/> keeps); the people — add someone with a name and whether they are a child
/// (D88), rename, change the child flag, remove (undo-first on the dog-ear, §13); and, for each child,
/// their rules (D12, D127–D129): the age band that is their ceiling, the services blocked whole, the
/// titles allowed above the band and the titles and series blocked under it, with a title search to
/// add either (D209). A parent's pick from the picker allow-lists too (D255); those live on the child's
/// Favorites and are not listed here.
///
/// <para>Every change is written at once through the household's one write path and queued; the page
/// re-reads only when every queued write has landed. Not built, and reported: each adult's PIN (D126,
/// no PIN store exists yet) and D166's "only the owner adds or removes viewers" (the file role is not
/// exposed to the screens yet).</para>
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class HouseholdActivity : Activity
{
    private const string PageTitle = "Household";
    private const string Loading = "Reading the household…";
    private const int MostResults = 8;
    private const string AddDraftKey = "add";

    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<ViewerId, ChildRules> rules = [];
    private readonly Dictionary<ViewerId, HashSet<LeagueId>> leaguesAllowed = [];
    private readonly Dictionary<TitleId, string> titleNames = [];
    private readonly Dictionary<ViewerId, IReadOnlyList<CatalogueTitle>> results = [];
    private readonly Dictionary<ViewerId, string> searchStatus = [];
    private PulpServices services = null!;
    private LibraryWrites writes = null!;
    private FrameLayout overlay = null!;
    private LinearLayout column = null!;
    private HouseholdView? view;
    private ThisPhoneAnswer phone = new(ThisPhoneStatus.NoRoster, null);
    private ViewerId? editing;
    private int pending;
    private bool addAsChild;
    private SyncWatch syncWatch = null!;
    private readonly PageDrafts drafts = new();
    private bool fieldFocused;
    private bool syncWaiting;

    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Intent(context, typeof(HouseholdActivity));
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        services = PulpServices.For(this);
        syncWatch = new SyncWatch(this, services, () => Reload(ReloadCause.Sync));
        writes = new LibraryWrites(services, () => RunOnUiThread(() =>
        {
            pending--;
            if (pending == 0)
            {
                Reload(ReloadCause.OwnWrites);
            }
        }), "household");

        column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        var (spread, pageOverlay) = SettingsViews.Frame(this, PageTitle, scroll);
        overlay = pageOverlay;
        SetContentView(spread);

        ShowStatus(Loading);
        Reload(ReloadCause.Opened);
    }

    protected override void OnResume()
    {
        base.OnResume();
        syncWatch.Resume();
    }

    protected override void OnPause()
    {
        base.OnPause();
        syncWatch.Pause();
    }

    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }

    // ---- reading ----

    private void Reload(ReloadCause cause)
    {
        var token = lifetime.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var read = await services.HouseholdAsync(token);
                var answer = services.ThisPhoneOf(read);
                RunOnUiThread(() =>
                {
                    // A read taken between two queued writes would put the first one's state back.
                    if (IsFinishing || IsDestroyed || pending > 0)
                    {
                        return;
                    }

                    // A sync pass rebuilds nothing under someone who is typing; it runs once they leave the field.
                    if (ReloadPolicy.ShouldWait(cause, fieldFocused, drafts.Any))
                    {
                        syncWaiting = true;
                        return;
                    }

                    syncWaiting = false;

                    view = read;
                    phone = answer;
                    rules.Clear();
                    leaguesAllowed.Clear();
                    foreach (var allowance in read.ChildLeagueAllowances)
                    {
                        if (!leaguesAllowed.TryAdd(allowance.Child, [allowance.League]))
                        {
                            leaguesAllowed[allowance.Child].Add(allowance.League);
                        }
                    }

                    foreach (var child in read.ChildRules)
                    {
                        rules[child.Child] = child;
                    }

                    // A child with no rules record yet (added before this page existed) is shown at the
                    // strictest band; the record is written with the first change made here.
                    foreach (var child in read.Roster?.Children ?? new HashSet<ViewerId>())
                    {
                        rules.TryAdd(child, ChildRuleEdits.NewChild(child));
                    }

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
                services.Diagnostics.Warn("household", "The Household page could not read the household.", exception);
                RunOnUiThread(() => ShowStatus("The household couldn't be read just now."));
            }
        }, token);
    }

    private void Render()
    {
        if (view is not { } household)
        {
            return;
        }

        column.RemoveAllViews();
        var people = household.Roster?.Viewers ?? [];

        // ---- whose phone this is (D97) ----
        column.AddView(PaperUi.Body(this, HouseholdText.ThisPhone(phone, household.NameOf)));
        var adults = people.Where(viewer => ThisPhone.CanBeRecorded(household.Roster, viewer.Id)).ToList();
        if (adults.Count > 0)
        {
            var current = phone.Viewer is { } mine ? adults.FindIndex(adult => adult.Id == mine) : -1;
            column.AddView(PaperUi.Segmented(
                this,
                adults.Select(adult => household.NameOf(adult.Id)).ToList(),
                index => SetThisPhone(adults[index].Id),
                current),
                SettingsViews.Spaced(this, DesignTokens.Space.X2));
        }

        // D166, mockup 09: where the household stands against Drive, and what this phone may change of it.
        var rights = services.Rights;
        column.AddView(SyncLine.Build(this, services, lifetime.Token), SettingsViews.Spaced(this, DesignTokens.Space.X2));
        if (rights.Note is { } note)
        {
            column.AddView(PaperUi.Secondary(this, note), SettingsViews.Spaced(this, DesignTokens.Space.X1));
        }

        // ---- the people (D88) ----
        column.AddView(SettingsViews.Subheading(this, "People"));
        foreach (var viewer in people)
        {
            var name = household.NameOf(viewer.Id);
            var ceiling = rules.TryGetValue(viewer.Id, out var childRules) ? childRules.Ceiling : (AgeBand?)null;
            column.AddView(SettingsViews.Rule(this, PaperTheme.Hairline));
            // On the phone the household is shared with, only her own viewer can be opened, and only to rename (D166).
            var editable = rights.CanAddOrRemoveViewers || (rights.CanRenameOwnViewer && phone.Viewer == viewer.Id);
            column.AddView(SettingsViews.Row(this, name, HouseholdText.Person(viewer.IsChild, ceiling), onTap: editable ? () =>
            {
                editing = editing == viewer.Id ? null : viewer.Id;
                Render();
            } : null));

            if (editing == viewer.Id && editable)
            {
                column.AddView(PersonEditor(viewer, name, rights.CanAddOrRemoveViewers));
            }
        }

        column.AddView(SettingsViews.Rule(this, PaperTheme.Hairline));
        if (rights.CanAddOrRemoveViewers)
        {
            column.AddView(AddPerson());
        }

        // ---- each child's rules (D12, D127–D129) ----
        foreach (var child in people.Where(viewer => viewer.IsChild))
        {
            if (rules.TryGetValue(child.Id, out var childRules))
            {
                column.AddView(ChildSection(household, household.NameOf(child.Id), childRules));
            }
        }
    }

    // ---- people ----

    private View PersonEditor(HouseholdViewer viewer, string name, bool mayChangeWho)
    {
        var editor = new LinearLayout(this) { Orientation = Orientation.Vertical };
        editor.SetPadding(PaperTheme.Dp(this, DesignTokens.Space.X4), 0, 0, PaperTheme.Dp(this, DesignTokens.Space.X3));

        var draftKey = $"rename:{viewer.Id}";
        var field = DraftField(draftKey, "Name", name);
        editor.AddView(field);
        editor.AddView(PaperUi.SecondaryButton(this, "Rename", () =>
        {
            if (ViewerNaming.Clean(field.Text) is { } clean && clean != name)
            {
                drafts.Clear(draftKey);
                Enqueue($"rename {viewer.Id}", token => RunSteps(PersonEdits.Rename(viewer.Id, clean), token));
            }
        }), SettingsViews.Spaced(this, DesignTokens.Space.X2));

        if (!mayChangeWho)
        {
            return editor;
        }

        if (viewer.IsChild == false)
        {
            editor.AddView(AccountEditor(viewer, name));
        }

        editor.AddView(SettingsViews.CheckRow(this, "A child", viewer.IsChild, isChild => SetChild(viewer, name, isChild)));
        editor.AddView(PaperUi.QuietButton(this, $"Remove {name}", () => Remove(viewer, name)), SettingsViews.Spaced(this, DesignTokens.Space.X1));
        return editor;
    }

    /// <summary>
    /// An adult's Google address (D304): the owner's install shares the Pulp calendar with it, read-only, on its
    /// next nightly write. It is kept only in the encrypted household file. Blank clears it, undo-first (§13).
    /// </summary>
    private View AccountEditor(HouseholdViewer viewer, string name)
    {
        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X3), 0, 0);
        var current = view?.GoogleAccountOf(viewer.Id);
        var draftKey = $"account:{viewer.Id}";
        form.AddView(Muted("Google account, to share the Pulp calendar with"));
        var field = DraftField(draftKey, "name@example.com", current ?? string.Empty);
        field.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationEmailAddress;
        form.AddView(field);
        var refusal = Muted(string.Empty);
        refusal.Visibility = ViewStates.Gone;
        form.AddView(refusal);
        form.AddView(PaperUi.SecondaryButton(this, "Save account", () =>
        {
            var typed = GoogleAddress.Clean(field.Text);
            if (typed is null)
            {
                if (current is not null)
                {
                    drafts.Clear(draftKey);
                    ClearAccount(viewer, name, current);
                }

                return;
            }

            var problem = GoogleAddress.Check(typed);
            refusal.Text = problem ?? string.Empty;
            refusal.Visibility = problem is null ? ViewStates.Gone : ViewStates.Visible;
            if (problem is not null || typed == current)
            {
                return;
            }

            drafts.Clear(draftKey);
            Enqueue($"record {viewer.Id}'s Google account", token =>
                services.PutAsync(HouseholdMaps.AdultAccount, new AdultAccountEntry(viewer.Id, typed), token));
        }), SettingsViews.Spaced(this, DesignTokens.Space.X2));
        return form;
    }

    private void ClearAccount(HouseholdViewer viewer, string name, string address)
    {
        var entry = new AdultAccountEntry(viewer.Id, address);
        Enqueue($"clear {viewer.Id}'s Google account", token => services.RemoveAsync(HouseholdMaps.AdultAccount, entry, token));
        DogEar.Show(this, overlay, HouseholdText.Undo("clear account", name), () =>
            Enqueue($"put {viewer.Id}'s Google account back", token => services.PutAsync(HouseholdMaps.AdultAccount, entry, token)));
    }

    private View AddPerson()
    {
        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X3), 0, 0);
        var field = DraftField(AddDraftKey, "Add someone: their name", string.Empty);
        form.AddView(field);
        form.AddView(SettingsViews.CheckRow(this, "A child", addAsChild, picked => addAsChild = picked));
        var refusal = Muted(string.Empty);
        refusal.Visibility = ViewStates.Gone;
        form.AddView(refusal);
        form.AddView(PaperUi.SecondaryButton(this, "Add", () =>
        {
            // Checked against the whole roster as it stands now, the one first run checks against.
            var roster = view?.Roster?.Viewers.Select(viewer => viewer.Id) ?? [];
            var problem = ViewerNaming.Check(field.Text, roster);
            refusal.Text = problem ?? string.Empty;
            refusal.Visibility = problem is null ? ViewStates.Gone : ViewStates.Visible;
            if (problem is not null || ViewerNaming.Clean(field.Text) is not { } name)
            {
                return;
            }

            var isChild = addAsChild;
            Enqueue($"add {name}", async token =>
            {
                // Minted against the household as it stands when the write lands, not the copy this page read.
                var now = await services.HouseholdAsync(token);
                var id = ViewerNaming.Mint(name, now.Roster?.Viewers.Select(viewer => viewer.Id) ?? []);
                await RunSteps(
                    PersonEdits.Add(new HouseholdViewer(id, isChild), new ViewerNameEntry(id, name), isChild ? ChildRuleEdits.NewChild(id) : null),
                    token);
            });
            field.Text = string.Empty;
            drafts.Clear(AddDraftKey);
            addAsChild = false;
        }));
        return form;
    }

    private void SetChild(HouseholdViewer viewer, string name, bool isChild)
    {
        var before = rules.GetValueOrDefault(viewer.Id);
        if (isChild)
        {
            var fresh = before ?? ChildRuleEdits.NewChild(viewer.Id);
            rules[viewer.Id] = fresh;
            Enqueue($"make {viewer.Id} a child", token => RunSteps(PersonEdits.MakeChild(viewer, fresh), token));
            return;
        }

        // An adult keeps no rules — a stray rules record would list them as a child in the picker — so
        // taking the flag off drops the rules, and the dog-ear can put them back (§13). PersonEdits
        // decides the order the records are written in.
        rules.Remove(viewer.Id);
        var allowancesOf = AllowancesOf(viewer.Id);
        Enqueue($"make {viewer.Id} an adult", token => RunSteps(PersonEdits.MakeAdult(viewer, before, allowancesOf), token));
        DogEar.Show(this, overlay, HouseholdText.Undo("change", name), () =>
            Enqueue($"make {viewer.Id} a child again", token => RunSteps(PersonEdits.MakeChild(viewer, before ?? ChildRuleEdits.NewChild(viewer.Id), allowancesOf), token)));
    }

    /// <summary>The leagues ticked for one child, as read; they are tombstoned and restored with the person (D362).</summary>
    private IReadOnlyList<ChildLeagueAllowance> AllowancesOf(ViewerId child) =>
        view?.ChildLeagueAllowances.Where(entry => entry.Child == child).ToList() ?? [];

    /// <summary>Undo-first (§13): the person, their name and a child's rules go at once; the dog-ear puts all three back.</summary>
    private void Remove(HouseholdViewer viewer, string name)
    {
        var named = view?.NameOf(viewer.Id) == viewer.Id.Value ? null : new ViewerNameEntry(viewer.Id, name);
        var childRules = rules.GetValueOrDefault(viewer.Id);
        var allowancesOf = AllowancesOf(viewer.Id);
        editing = null;
        var address = view?.GoogleAccountOf(viewer.Id);
        Enqueue($"remove {viewer.Id}", async token =>
        {
            await RunSteps(PersonEdits.Remove(viewer, named, childRules, allowancesOf), token);
            if (address is not null)
            {
                await services.RemoveAsync(HouseholdMaps.AdultAccount, new AdultAccountEntry(viewer.Id, address), token);
            }
        });
        DogEar.Show(this, overlay, HouseholdText.RemoveUndo(name), () =>
            Enqueue($"put {viewer.Id} back", async token =>
            {
                await RunSteps(PersonEdits.Add(viewer, named ?? new ViewerNameEntry(viewer.Id, viewer.Id.Value), childRules ?? (viewer.IsChild ? ChildRuleEdits.NewChild(viewer.Id) : null), allowancesOf), token);
                if (address is not null)
                {
                    await services.PutAsync(HouseholdMaps.AdultAccount, new AdultAccountEntry(viewer.Id, address), token);
                }
            }));
    }

    /// <summary>
    /// Writes every record of a person's change in ONE household edit (<see cref="PersonEdits.ApplyAll"/>),
    /// so a sync pass can never land between a child and their rules.
    /// </summary>
    private Task RunSteps(PersonEdit edit, CancellationToken token) =>
        services.EditHouseholdAsync((document, stamp, writer) => PersonEdits.ApplyAll(document, edit, stamp, writer), token);

    private void SetThisPhone(ViewerId viewer)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await services.SetThisPhoneAsync(viewer, lifetime.Token);
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, lifetime.Token))
            {
                services.Diagnostics.Warn("household", "This phone could not be recorded as a viewer's.", exception);
            }

            RunOnUiThread(() => Reload(ReloadCause.OwnWrites));
        });
    }

    // ---- a child's rules ----

    private View ChildSection(HouseholdView household, string name, ChildRules childRules)
    {
        var section = new LinearLayout(this) { Orientation = Orientation.Vertical };
        section.AddView(SettingsViews.Subheading(this, $"{name}'s rules"));

        // The ceiling (D127): one age band, read against both rating systems.
        var bands = Enum.GetValues<AgeBand>();
        var picker = new AccordionPicker(
            this,
            "Age band",
            bands.Select(band => new PickerOption(ChildRuleEdits.BandLabel(band), ChildRuleEdits.BandDetail(band))),
            Array.IndexOf(bands, childRules.Ceiling));
        picker.Chosen += index => Edit(childRules.Child, $"set {childRules.Child}'s band", current => ChildRuleEdits.Ceiling(current, bands[index]));
        section.AddView(picker);

        // Whole services (D129). A blocked service hides a title only when it is the title's only home (D282).
        var had = household.Services.Distinct().ToList();
        if (had.Count > 0)
        {
            section.AddView(Muted("Blocked services"), SettingsViews.Spaced(this, DesignTokens.Space.X3));
            foreach (var id in had)
            {
                var serviceName = services.Catalogue.ById(id)?.Name ?? id.Value;
                section.AddView(SettingsViews.CheckRow(this, serviceName, childRules.BlockedServices.Contains(id), blocked =>
                    Edit(childRules.Child, $"{(blocked ? "block" : "unblock")} {id} for {childRules.Child}", current => blocked
                        ? ChildRuleEdits.BlockService(current, id)
                        : ChildRuleEdits.UnblockService(current, id))));
            }
        }

        // Titles allowed above the band (D12), blocked under it, and blocked series (D129).
        TitleList(section, childRules.Child, "Allowed above the band", childRules.Allowed, allowList: true);
        TitleList(section, childRules.Child, "Blocked titles", childRules.BlockedTitles, allowList: false);
        if (childRules.BlockedFranchises.Count > 0)
        {
            section.AddView(Muted("Blocked series"), SettingsViews.Spaced(this, DesignTokens.Space.X3));
            foreach (var franchise in childRules.BlockedFranchises.OrderBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase))
            {
                section.AddView(SettingsViews.Row(this, franchise.Value, "Tap to unblock", onTap: () =>
                {
                    Edit(childRules.Child, $"unblock series {franchise} for {childRules.Child}", current => ChildRuleEdits.UnblockFranchise(current, franchise));
                    DogEar.Show(this, overlay, HouseholdText.Undo("unblock", franchise.Value), () =>
                        Edit(childRules.Child, $"block series {franchise} again", current => ChildRuleEdits.BlockFranchise(current, franchise)));
                }));
            }
        }

        LeagueSection(section, childRules.Child, name);

        section.AddView(TitleSearch(childRules.Child, name), SettingsViews.Spaced(this, DesignTokens.Space.X3));
        section.AddView(SettingsViews.Rule(this, PaperTheme.Hairline), SettingsViews.Spaced(this, DesignTokens.Space.X3));
        return section;
    }

    /// <summary>
    /// The leagues a parent allows this child (D59, D72, D317): sport is allow-list only, so every league
    /// starts unticked. Ticking one is a record of its own, written by the adult whose phone this is.
    /// </summary>
    private void LeagueSection(LinearLayout section, ViewerId child, string name)
    {
        section.AddView(Muted("Leagues"), SettingsViews.Spaced(this, DesignTokens.Space.X3));
        if (phone.Viewer is not { } parent)
        {
            section.AddView(PaperUi.Secondary(this, "Say whose phone this is above to choose " + name + "'s leagues."));
            return;
        }

        var allowed = leaguesAllowed.TryGetValue(child, out var current) ? current : [];
        foreach (var row in ChildSports.Rows(services.SportsSources, allowed))
        {
            section.AddView(SettingsViews.CheckRow(this, row.Name, row.Allowed, ticked =>
            {
                var allowance = new ChildLeagueAllowance(child, row.League, parent, DateTimeOffset.UtcNow);
                if (!leaguesAllowed.TryAdd(child, ticked ? [row.League] : []) && ticked)
                {
                    leaguesAllowed[child].Add(row.League);
                }
                else if (!ticked)
                {
                    leaguesAllowed[child].Remove(row.League);
                }

                Enqueue($"{(ticked ? "allow" : "stop allowing")} {row.League} for {child}", token => ticked
                    ? services.PutAsync(HouseholdMaps.ChildLeague, allowance, token)
                    : services.RemoveAsync(HouseholdMaps.ChildLeague, allowance, token));
            }));
        }
    }

    /// <summary>One list of titles on a child's rules; a tap takes a title off it at once, and the dog-ear puts it back.</summary>
    private void TitleList(LinearLayout section, ViewerId child, string heading, IReadOnlySet<TitleId> titles, bool allowList)
    {
        if (titles.Count == 0)
        {
            return;
        }

        section.AddView(Muted(heading), SettingsViews.Spaced(this, DesignTokens.Space.X3));
        foreach (var title in titles.OrderBy(TitleName, StringComparer.CurrentCultureIgnoreCase))
        {
            var label = TitleName(title);
            var hint = allowList ? "Tap to take off the allow-list" : "Tap to unblock";
            section.AddView(SettingsViews.Row(this, label, hint, onTap: () =>
            {
                Edit(child, $"take {title} off {child}'s {(allowList ? "allow" : "block")}-list", current => allowList
                    ? ChildRuleEdits.Unallow(current, title)
                    : ChildRuleEdits.Unblock(current, title));
                DogEar.Show(this, overlay, HouseholdText.Undo(allowList ? "remove" : "unblock", label), () =>
                    Edit(child, $"put {title} back on {child}'s list", current => allowList
                        ? ChildRuleEdits.Allow(current, title)
                        : ChildRuleEdits.Block(current, title)));
            }));
        }
    }

    /// <summary>A title search (D209) for this child; a result opens a card to allow it above the band or block it.</summary>
    private View TitleSearch(ViewerId child, string name)
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var field = DraftField($"search:{child}", $"Find a title to allow or block for {name}", string.Empty);
        box.AddView(field);
        box.AddView(PaperUi.SecondaryButton(this, "Find", () => Search(child, field.Text ?? string.Empty)), SettingsViews.Spaced(this, DesignTokens.Space.X2));

        if (searchStatus.TryGetValue(child, out var status))
        {
            box.AddView(Muted(status), SettingsViews.Spaced(this, DesignTokens.Space.X1));
        }

        foreach (var title in results.GetValueOrDefault(child) ?? [])
        {
            var detail = string.Join(" · ", new[] { title.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture), title.Kind == TitleKind.Film ? "Film" : "Show" }.OfType<string>());
            box.AddView(SettingsViews.Row(this, title.Name, detail, onTap: () => ActionCard.Show(this, overlay, title.Name, name,
                new CardAction($"Allow for {name}", () =>
                {
                    titleNames[title.Id] = title.Name;
                    Edit(child, $"allow {title.Id} for {child}", current => ChildRuleEdits.Allow(current, title.Id));
                }),
                new CardAction($"Block for {name}", () =>
                {
                    titleNames[title.Id] = title.Name;
                    Edit(child, $"block {title.Id} for {child}", current => ChildRuleEdits.Block(current, title.Id));
                }))));
        }

        return box;
    }

    private void Search(ViewerId child, string text)
    {
        var query = text.Trim();
        if (query.Length == 0)
        {
            return;
        }

        searchStatus[child] = "Looking…";
        results.Remove(child);
        Render();
        _ = Task.Run(async () =>
        {
            IReadOnlyList<CatalogueTitle> found;
            string? status = null;
            try
            {
                found = (await services.SearchAsync(query, lifetime.Token)).Take(MostResults).ToList();
                if (found.Count == 0)
                {
                    status = "Nothing by that name.";
                }
            }
            catch (CatalogueLookupException exception)
            {
                services.Diagnostics.Warn("household", $"A title search for a child's rules failed ({exception.Failure}).", exception);
                found = [];
                status = "The search couldn't be reached just now.";
            }
            catch (System.OperationCanceledException)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                if (IsFinishing || IsDestroyed)
                {
                    return;
                }

                results[child] = found;
                if (status is null)
                {
                    searchStatus.Remove(child);
                }
                else
                {
                    searchStatus[child] = status;
                }

                Render();
            });
        });
    }

    // ---- writing ----

    /// <summary>Changes a child's rules as this page last wrote them, shows it at once, and queues the write.</summary>
    private void Edit(ViewerId child, string what, Func<ChildRules, ChildRules> change)
    {
        if (!rules.TryGetValue(child, out var current))
        {
            return;
        }

        var changed = change(current);
        rules[child] = changed;
        Enqueue(what, token => services.PutAsync(HouseholdMaps.ChildRules, changed, token));
    }

    private void Enqueue(string what, Func<CancellationToken, Task> write)
    {
        pending++;
        writes.Enqueue(what, write);
        Render();
    }

    /// <summary>
    /// A text field whose text outlives the page's rebuilds: what is typed is kept under <paramref name="key"/>
    /// and put back whenever the views are made again, and focus is tracked so a sync pass can wait.
    /// </summary>
    private EditText DraftField(string key, string hint, string fallback)
    {
        var field = PaperUi.Field(this, hint);
        field.Text = drafts.Get(key, fallback);
        field.TextChanged += (_, _) => drafts.Set(key, field.Text ?? string.Empty);
        field.FocusChange += (_, args) =>
        {
            fieldFocused = args.HasFocus;
            if (!args.HasFocus && syncWaiting)
            {
                Reload(ReloadCause.Opened);
            }
        };
        return field;
    }

    private string TitleName(TitleId title) =>
        titleNames.GetValueOrDefault(title) ?? services.CachedCopy(title)?.Name ?? title.Value;

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
