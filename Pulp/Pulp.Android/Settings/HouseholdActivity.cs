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

    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<ViewerId, ChildRules> rules = [];
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
    private SyncWatch syncWatch = null!;

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
        syncWatch = new SyncWatch(this, services, Reload);
        writes = new LibraryWrites(services, () => RunOnUiThread(() =>
        {
            pending--;
            if (pending == 0)
            {
                Reload();
            }
        }), "household");

        column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var scroll = new ScrollView(this) { FillViewport = true };
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        var (spread, pageOverlay) = SettingsViews.Frame(this, PageTitle, scroll);
        overlay = pageOverlay;
        SetContentView(spread);

        ShowStatus(Loading);
        Reload();
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

    private void Reload()
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

                    view = read;
                    phone = answer;
                    rules.Clear();
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
        column.AddView(PaperUi.Secondary(this, HouseholdSyncText.Line(services.SyncStatus, DateTimeOffset.UtcNow)), SettingsViews.Spaced(this, DesignTokens.Space.X2));
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

        var field = PaperUi.Field(this, "Name");
        field.Text = name;
        editor.AddView(field);
        editor.AddView(PaperUi.SecondaryButton(this, "Rename", () =>
        {
            if (ViewerNaming.Clean(field.Text) is { } clean && clean != name)
            {
                Enqueue($"rename {viewer.Id}", token => services.PutAsync(HouseholdMaps.ViewerName, new ViewerNameEntry(viewer.Id, clean), token));
            }
        }), SettingsViews.Spaced(this, DesignTokens.Space.X2));

        if (!mayChangeWho)
        {
            return editor;
        }

        editor.AddView(SettingsViews.CheckRow(this, "A child", viewer.IsChild, isChild => SetChild(viewer, name, isChild)));
        editor.AddView(PaperUi.QuietButton(this, $"Remove {name}", () => Remove(viewer, name)), SettingsViews.Spaced(this, DesignTokens.Space.X1));
        return editor;
    }

    private View AddPerson()
    {
        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(0, PaperTheme.Dp(this, DesignTokens.Space.X3), 0, 0);
        var field = PaperUi.Field(this, "Add someone: their name");
        form.AddView(field);
        var child = false;
        form.AddView(SettingsViews.CheckRow(this, "A child", child, picked => child = picked));
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

            var isChild = child;
            Enqueue($"add {name}", async token =>
            {
                // Minted against the household as it stands when the write lands, not the copy this page read.
                var now = await services.HouseholdAsync(token);
                var id = ViewerNaming.Mint(name, now.Roster?.Viewers.Select(viewer => viewer.Id) ?? []);
                await services.PutAsync(HouseholdMaps.Viewer, new HouseholdViewer(id, isChild), token);
                await services.PutAsync(HouseholdMaps.ViewerName, new ViewerNameEntry(id, name), token);
                if (isChild)
                {
                    await services.PutAsync(HouseholdMaps.ChildRules, ChildRuleEdits.NewChild(id), token);
                }
            });
            field.Text = string.Empty;
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
            Enqueue($"make {viewer.Id} a child", async token =>
            {
                await services.PutAsync(HouseholdMaps.Viewer, viewer with { IsChild = true }, token);
                await services.PutAsync(HouseholdMaps.ChildRules, fresh, token);
            });
            return;
        }

        // An adult keeps no rules — a stray rules record would list them as a child in the picker — so
        // taking the flag off drops the rules, and the dog-ear can put them back (§13).
        rules.Remove(viewer.Id);
        Enqueue($"make {viewer.Id} an adult", async token =>
        {
            await services.PutAsync(HouseholdMaps.Viewer, viewer with { IsChild = false }, token);
            if (before is not null)
            {
                await services.RemoveAsync(HouseholdMaps.ChildRules, before, token);
            }
        });
        DogEar.Show(this, overlay, HouseholdText.Undo("change", name), () =>
        {
            Enqueue($"make {viewer.Id} a child again", async token =>
            {
                await services.PutAsync(HouseholdMaps.Viewer, viewer with { IsChild = true }, token);
                await services.PutAsync(HouseholdMaps.ChildRules, before ?? ChildRuleEdits.NewChild(viewer.Id), token);
            });
        });
    }

    /// <summary>Undo-first (§13): the person, their name and a child's rules go at once; the dog-ear puts all three back.</summary>
    private void Remove(HouseholdViewer viewer, string name)
    {
        var named = view?.NameOf(viewer.Id) == viewer.Id.Value ? null : new ViewerNameEntry(viewer.Id, name);
        var childRules = rules.GetValueOrDefault(viewer.Id);
        editing = null;
        Enqueue($"remove {viewer.Id}", async token =>
        {
            await services.RemoveAsync(HouseholdMaps.Viewer, viewer, token);
            if (named is not null)
            {
                await services.RemoveAsync(HouseholdMaps.ViewerName, named, token);
            }

            if (childRules is not null)
            {
                await services.RemoveAsync(HouseholdMaps.ChildRules, childRules, token);
            }
        });
        DogEar.Show(this, overlay, HouseholdText.RemoveUndo(name), () =>
            Enqueue($"put {viewer.Id} back", async token =>
            {
                await services.PutAsync(HouseholdMaps.Viewer, viewer, token);
                if (named is not null)
                {
                    await services.PutAsync(HouseholdMaps.ViewerName, named, token);
                }

                if (childRules is not null)
                {
                    await services.PutAsync(HouseholdMaps.ChildRules, childRules, token);
                }
            }));
    }

    private void SetThisPhone(ViewerId viewer)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await services.SetThisPhoneAsync(viewer, lifetime.Token);
            }
            catch (Exception exception) when (exception is not System.OperationCanceledException)
            {
                services.Diagnostics.Warn("household", $"This phone could not be recorded as {viewer}'s.", exception);
            }

            RunOnUiThread(Reload);
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

        section.AddView(TitleSearch(childRules.Child, name), SettingsViews.Spaced(this, DesignTokens.Space.X3));
        section.AddView(SettingsViews.Rule(this, PaperTheme.Hairline), SettingsViews.Spaced(this, DesignTokens.Space.X3));
        return section;
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
        var field = PaperUi.Field(this, $"Find a title to allow or block for {name}");
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
