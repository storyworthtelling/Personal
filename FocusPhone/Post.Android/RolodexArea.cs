using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Timeline;
using Codex.Ui;

namespace Post;

/// <summary>
/// The Rolodex: the address book, and where a new correspondence starts. Post is
/// person-first — you find who, then choose how to reach them (§D18/D19) — which is why
/// there is no cold compose button on the Mailbox.
/// </summary>
public sealed class RolodexArea(Activity host) : PostArea(host)
{
    private IReadOnlyList<ContactPicker.Person> people = [];
    private string query = string.Empty;
    private FilterField? filter;

    public override BinderArea Tab =>
        new("Rolodex", Resource.Drawable.area_rolodex, PaperTheme.PositiveMark, PaperTheme.PositiveFill);

    public override void Resume()
    {
        try
        {
            people = ContactPicker.People(Host);
        }
        catch (Java.Lang.SecurityException)
        {
            people = []; // READ_CONTACTS not granted yet; manual entry still works.
        }
    }

    public override void OnPermissionsResult()
    {
        Resume();
        Invalidate();
    }

    public override View BuildView()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };

        filter = new FilterField(Host, "Find a person", PaperTheme.PositiveMark);
        filter.SetQuery(query);
        filter.QueryChanged += text =>
        {
            query = text;
            Invalidate();
        };
        filter.Visibility = FilterMatch.IsWarranted(people.Count) ? ViewStates.Visible : ViewStates.Gone;
        column.AddView(filter, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var shown = query.Trim().Length == 0
            ? people
            : people.Where(person =>
                person.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        var frame = new FrameLayout(Host);
        var list = new ListView(Host);
        PaperUi.StylePaperList(Host, list);
        list.Adapter = new PaperListAdapter(Host, shown
            .Select(person => new PaperListRow(person.Name, Reachable(person)))
            .ToList());
        list.ItemClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < shown.Count)
            {
                ShowChannelChooser(shown[e.Position]);
            }
        };
        frame.AddView(list, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var empty = PaperUi.EmptyState(Host, people.Count == 0
            ? "No contacts yet.\n\nGrant contacts access, or reach someone by typing their number below."
            : "No one by that name.");
        frame.AddView(empty, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        list.EmptyView = empty;

        column.AddView(frame, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });
        return column;
    }

    // The Rolodex is the one area with a pinned primary action, because reaching someone
    // who is not in the book is a thing you do to the screen rather than to a row.
    public override View? BuildBottomBar() =>
        PaperUi.BottomBar(Host, PaperUi.PrimaryButton(Host, "Reach someone new", ShowManualEntry));

    private static string Reachable(ContactPicker.Person person)
    {
        var parts = new List<string>();
        if (person.Phones.Count > 0)
        {
            parts.Add(person.Phones[0]);
        }
        if (person.Emails.Count > 0)
        {
            parts.Add(person.Emails[0]);
        }
        return string.Join(" · ", parts);
    }

    private enum Channel { Call, Text, Email }

    private void ShowChannelChooser(ContactPicker.Person person)
    {
        var options = new List<(string Label, Channel Kind, string Address)>();
        if (person.Phones.Count > 0)
        {
            options.Add(("Call", Channel.Call, person.Phones[0]));
            options.Add(("Text", Channel.Text, person.Phones[0]));
        }
        if (person.Emails.Count > 0)
        {
            options.Add(("Email", Channel.Email, person.Emails[0]));
        }
        if (options.Count == 0)
        {
            return;
        }

        ActionCard.Show(Host, Overlay, person.Name, subtitle: null, options
            .Select(option => new CardAction(
                option.Label, () => Dispatch(option.Kind, option.Address), Lead: option.Kind == Channel.Text))
            .ToArray());
    }

    private void ShowManualEntry()
    {
        var input = PaperUi.MonoField(Host, "Number or email");
        var dialog = new AlertDialog.Builder(Host)
            .SetTitle("Reach someone new")!
            .SetView(input)!
            .SetPositiveButton("Go", (IDialogInterfaceOnClickListener?)null)!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Create()!;

        dialog.Show();
        // Wired after Show so invalid input keeps the dialog open.
        dialog.GetButton((int)DialogButtonType.Positive)!.Click += (_, _) =>
        {
            var raw = input.Text ?? string.Empty;
            if (raw.Contains('@') && RecipientInput.TryCreateEmail(raw, out var email))
            {
                dialog.Dismiss();
                Dispatch(Channel.Email, email);
            }
            else if (RecipientInput.TryCreatePhone(raw, out var phone))
            {
                dialog.Dismiss();
                ShowChannelChooser(new ContactPicker.Person(phone, [phone], []));
            }
            else
            {
                input.Error = "Enter a phone number or email.";
            }
        };
    }

    private void Dispatch(Channel kind, string address)
    {
        switch (kind)
        {
            case Channel.Call:
                if (RecipientInput.TryCreatePhone(address, out var callNumber))
                {
                    Host.StartActivity(new Intent(Intent.ActionCall, Android.Net.Uri.Parse($"tel:{callNumber}")));
                }
                break;
            case Channel.Text:
                if (RecipientInput.TryCreatePhone(address, out var textNumber))
                {
                    Host.StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{textNumber}")));
                }
                break;
            case Channel.Email:
                EmailComposeDialog.Show(Host, address, ContactNameResolver.DisplayName(Host, address));
                break;
        }
    }
}
