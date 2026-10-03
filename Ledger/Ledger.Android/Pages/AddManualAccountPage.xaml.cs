using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Shapes;
using Index.Android.Views;
using Index.Domain.Design;
using Index.Domain.Model;
using Index.Domain.Time;
using Index.Infrastructure.Services;
using Storyworthtelling.Maui;
using Storyworthtelling.Maui.Views;

namespace Index.Android.Pages;

/// <summary>
/// Mockup 4, frame 2 ("By hand"): the single form for a hand-entered account (D108, D130). Reached
/// through <see cref="Views.AddAccountChooserCard"/>'s "Add one by hand" choice.
///
/// <para>D131: the index card is the ground and the layout, nothing more — the fields and controls
/// below are the design system's own (boxed <c>Entry</c>s, <see cref="PaperSegmented"/> for Kind and
/// Every), laid onto <c>MaterialTokens</c>' note-card stock and rules rather than reusing
/// <see cref="Views.IndexCardView"/>, whose rows are fixed label+value text — not editable controls.</para>
///
/// <para><b>Validation is per field, on blur and then live</b> (mockup 4): a bad figure turns its own box
/// rose with a line saying how to fix it, and Add refuses to save while one is showing — a figure that
/// cannot be read is never saved as "no figure" (R-G16: "6.25%" used to save the account with no APR,
/// leaving it out of the Snowball without a word). The one sentence at the top of the card is the
/// required-field summary and nothing else. Name and Balance, the two required fields (D108), carry the
/// faint identity tint.</para>
///
/// <para><b>Not built 1:1 for one piece, flagged rather than silently substituted</b>: mockup 4's
/// "flip picker" for Next due (an in-place expanding calendar, DESIGN.md §7) has no equivalent
/// component in <c>Storyworthtelling.Maui</c> today, so <see cref="DayChooser"/> opens the system date
/// dialog instead. The boxed mono field material (<c>$</c> prefix, <c>%</c> suffix, radius, height) is the
/// cosmetic pass.</para>
/// </summary>
public partial class AddManualAccountPage : LedgerBinderPage
{
    // Mockup 4's `.ixg` pitch: a 22dp caption line, 11dp, the 44dp box, 11dp.
    private const double CaptionLineDp = 22;
    private const double FieldRowGapDp = 11;

    private readonly AccountService accounts;
    private readonly ILogger<AddManualAccountPage> logger;

    private readonly Label statusLabel = new();
    private readonly FieldBox nameBox = new(new Entry());
    private readonly FieldBox bankBox = new(new Entry());
    private readonly PaperSegmented kindSegmented = new(
        [ManualAccountForm.KindLoan, ManualAccountForm.KindCard, ManualAccountForm.KindBank],
        ManualAccountForm.KindLoan, fillWidth: true);
    private readonly FieldBox balanceBox = new(new Entry { Keyboard = Keyboard.Numeric }, mono: true);
    private readonly FieldBox limitBox = new(new Entry { Keyboard = Keyboard.Numeric }, mono: true);
    private readonly FieldBox aprBox = new(new Entry { Keyboard = Keyboard.Numeric }, mono: true);
    private readonly FieldBox paymentBox = new(new Entry { Keyboard = Keyboard.Numeric }, mono: true);
    private readonly PaperSegmented everySegmented = new(["Week", "Month"], "Month", fillWidth: true);
    private readonly DayChooser nextDue = new(initial: null);
    private readonly ScrollView formScroll = new();
    private readonly Button addButton = new() { Text = "Add account" };

    // The rows Kind shows and hides (mockup 4): toggled in place, never rebuilt, so what was typed survives a
    // change of Kind.
    private View limitRow = null!;
    private View aprPaymentRow = null!;
    private View everyRow = null!;
    private View dueRow = null!;

    // The Every control always has an option picked (Month, as drawn), so "picked" is not "chosen": only a tap
    // that changes it counts, and a cadence nobody chose is not stored (ManualAccountForm.CadenceToStore).
    private bool everyTouched;

    private readonly BlurThenLive nameLive = new();
    private readonly BlurThenLive balanceLive = new();
    private readonly BlurThenLive limitLive = new();
    private readonly BlurThenLive aprLive = new();
    private readonly BlurThenLive paymentLive = new();

    public AddManualAccountPage(AccountService accounts, ILogger<AddManualAccountPage> logger)
    {
        InitializeComponent();
        this.accounts = accounts;
        this.logger = logger;

        var cancelButton = new Button { Text = "Cancel", Style = PaperResources.Token<Style>("QuietButton") };
        cancelButton.Clicked += (_, _) => Navigation.PopAsync();
        addButton.Clicked += OnAddAccountClicked;
        SetBar(cancelButton, addButton);

        // The body is built BEFORE Wrap: Wrap puts the binder frame around whatever Content already is, so a
        // page that sets Content afterwards replaces the frame (header rule and binder strip) with the bare
        // form. This page used to do exactly that.
        BuildBody();
        Wrap(LedgerArea.Accounts, "By hand");
    }

    // Mockup 4: the keyboard pushes the form up and the pinned bar hides while it is open.
    protected override bool HidesBarForKeyboard => true;

    // Mockup 4 draws the graph paper running up behind "By hand" too.
    protected override bool PaperBehindHeader => true;

    private void BuildBody()
    {
        statusLabel.Style = PaperResources.Token<Style>("SecondaryLabel");
        statusLabel.TextColor = PaperResources.Colour("ProblemMark");
        statusLabel.IsVisible = false;

        // The hints are invented EXAMPLES (mockup 4 draws the same slots), shown as placeholders and never as defaults.
        nameBox.Entry.Placeholder = ManualAccountForm.NameExample;
        bankBox.Entry.Placeholder = ManualAccountForm.BankExample;
        balanceBox.Entry.Placeholder = ManualAccountForm.BalanceExample;
        limitBox.Entry.Placeholder = ManualAccountForm.LimitExample;
        aprBox.Entry.Placeholder = ManualAccountForm.AprExample;
        paymentBox.Entry.Placeholder = ManualAccountForm.PaymentExample;

        // Only Name and Balance are required (D108): their boxes carry the faint identity tint.
        nameBox.SetRequiredTint(true);
        balanceBox.SetRequiredTint(true);

        WireValidation(nameBox, nameLive, text => string.IsNullOrWhiteSpace(text) ? ManualAccountEditing.NameHint : null);
        WireValidation(balanceBox, balanceLive,
            text => ManualAccountEditing.FigureError(text, FigureKind.Money, required: true, allowNegative: true));
        WireValidation(limitBox, limitLive, text => ManualAccountEditing.FigureError(text, FigureKind.Money, required: false));
        WireValidation(aprBox, aprLive, text => ManualAccountEditing.FigureError(text, FigureKind.Percent, required: false));
        WireValidation(paymentBox, paymentLive, text => ManualAccountEditing.FigureError(text, FigureKind.Money, required: false));

        // Mockup 4: a valid figure is left in the shape the mockup draws ("$12,345.67", "6.25%").
        WireBlurFormat(balanceBox, FigureKind.Money, allowNegative: true);
        WireBlurFormat(limitBox, FigureKind.Money, allowNegative: false);
        WireBlurFormat(aprBox, FigureKind.Percent, allowNegative: false);
        WireBlurFormat(paymentBox, FigureKind.Money, allowNegative: false);

        foreach (var entry in new[] { nameBox.Entry, bankBox.Entry, balanceBox.Entry, limitBox.Entry, aprBox.Entry, paymentBox.Entry })
        {
            KeepVisibleWhenFocused(formScroll, entry);
        }

        // Mockup 4: APR takes 40% of the row and Payment the rest, sharing their rules.
        var aprPayment = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(2, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(3, GridUnitType.Star)),
            },
            ColumnSpacing = 14,
        };
        var aprColumn = FieldRow("APR", aprBox);
        var paymentColumn = FieldRow("Payment", paymentBox);
        aprPayment.Children.Add(aprColumn);
        Grid.SetColumn(aprColumn, 0);
        aprPayment.Children.Add(paymentColumn);
        Grid.SetColumn(paymentColumn, 1);

        limitRow = FieldRow("Credit limit", limitBox);
        aprPaymentRow = aprPayment;
        everyRow = FieldRow("Every", everySegmented);
        dueRow = FieldRow("Next due", nextDue);

        kindSegmented.Picked += _ => ApplyKind();
        everySegmented.Picked += _ => everyTouched = true;
        ApplyKind();

        // Mockup 4's rows are 88dp apart: a 22dp label line, the 44dp box with 11dp clear above it, and 11 below.
        var body = new VerticalStackLayout
        {
            Spacing = FieldRowGapDp,
            Children =
            {
                new Label { Text = "New account", Style = PaperResources.Token<Style>("HeadingLabel") },
                new BoxView { HeightRequest = 1.5, Color = Color.FromArgb(Index.Domain.Design.MaterialTokens.CardHeadRuleHex) },
                statusLabel,
                FieldRow("Name", nameBox),
                FieldRow("Bank or lender", bankBox, ManualAccountForm.BankHint),
                FieldRow("Kind", kindSegmented),
                FieldRow("Balance", balanceBox),
                limitRow,
                aprPaymentRow,
                everyRow,
                dueRow,
            },
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb(Index.Domain.Design.MaterialTokens.NoteCardHex),
            Stroke = PaperResources.Colour("Hairline"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) },
            Padding = new Thickness(20, 10, 20, 14),
            Shadow = null,
            Content = body,
        };

        formScroll.Content = new VerticalStackLayout
        {
            Padding = new Thickness(AccountsLayout.WellPaddingLeftDp, 8, AccountsLayout.WellPaddingRightDp, 24),
            Children = { card },
        };
        Content = formScroll;
    }

    // Mockup 4: "Validation happens on blur, then live." Leaving the box checks it and, if it is bad, marks it;
    // from then on every keystroke re-checks it, so the rose clears the moment the text becomes readable.
    private static void WireValidation(FieldBox box, BlurThenLive live, Func<string?, string?> errorOf)
    {
        box.Entry.Unfocused += (_, _) => box.ShowError(live.Blur(errorOf(box.Entry.Text)));
        box.Entry.TextChanged += (_, _) => box.ShowError(live.Typed(errorOf(box.Entry.Text)));
    }

    // Leaving a box that holds a valid figure rewrites it the way the mockup draws it. A bad or empty box is
    // left exactly as typed — the rose says what is wrong, and an empty one stays empty.
    private static void WireBlurFormat(FieldBox box, FigureKind kind, bool allowNegative)
    {
        box.Entry.Unfocused += (_, _) =>
        {
            var parsed = FigureParser.Parse(box.Entry.Text, kind, allowNegative);
            if (parsed.Status != FigureStatus.Valid)
            {
                return;
            }

            var shown = FigureParser.FormatForBox(parsed.Value, kind);
            if (box.Entry.Text != shown)
            {
                box.Entry.Text = shown;
            }
        };
    }

    // Kind decides which rows are on show (mockup 4). Only visibility changes: the boxes keep what was typed, so
    // switching Card to Bank account and back loses nothing, and OnAddAccountClicked saves only what is on show.
    private void ApplyKind()
    {
        var shown = ManualAccountForm.FieldsFor(ManualAccountForm.KindOf(kindSegmented.Value));
        limitRow.IsVisible = shown.HasFlag(ManualField.CreditLimit);
        aprPaymentRow.IsVisible = shown.HasFlag(ManualField.Apr) || shown.HasFlag(ManualField.Payment);
        everyRow.IsVisible = shown.HasFlag(ManualField.Every);
        dueRow.IsVisible = shown.HasFlag(ManualField.NextDue);
    }

    // The caption row: the field's label on the left and, where the mockup draws one, a hint right-aligned on the
    // same row. The hint is a label, not the Entry's placeholder, because it has to stay on show while the owner
    // types.
    private static View FieldRow(string label, View control, string? hint = null)
    {
        var caption = new Grid
        {
            HeightRequest = CaptionLineDp,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 8,
        };
        caption.Children.Add(new Label { Text = label, Style = PaperResources.Token<Style>("FieldLabel") });
        if (hint is not null)
        {
            var hintLabel = new Label
            {
                Text = hint,
                Style = PaperResources.Token<Style>("SecondaryLabel"),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.End,
                HorizontalTextAlignment = TextAlignment.End,
            };
            caption.Children.Add(hintLabel);
            Grid.SetColumn(hintLabel, 1);
        }

        return new VerticalStackLayout { Spacing = FieldRowGapDp, Children = { caption, control } };
    }

    private async void OnAddAccountClicked(object? sender, EventArgs e)
    {
        var kind = ManualAccountForm.KindOf(kindSegmented.Value);
        var shown = ManualAccountForm.FieldsFor(kind);

        var name = nameBox.Entry.Text?.Trim() ?? string.Empty;
        var balance = FigureParser.Parse(balanceBox.Entry.Text, FigureKind.Money, allowNegative: true);
        var limit = FigureParser.Parse(limitBox.Entry.Text, FigureKind.Money);
        var apr = FigureParser.Parse(aprBox.Entry.Text, FigureKind.Percent);
        var payment = FigureParser.Parse(paymentBox.Entry.Text, FigureKind.Money);

        // Every box ON SHOW is checked now, so none that is wrong can be left unmarked by a field never visited.
        // A box hidden by the Kind is not: a figure typed before switching Kind neither blocks Add nor is saved.
        nameBox.ShowError(nameLive.Blur(name.Length == 0 ? ManualAccountEditing.NameHint : null));
        balanceBox.ShowError(balanceLive.Blur(
            ManualAccountEditing.FigureError(balanceBox.Entry.Text, FigureKind.Money, required: true, allowNegative: true)));
        if (shown.HasFlag(ManualField.CreditLimit))
        {
            limitBox.ShowError(limitLive.Blur(
                ManualAccountEditing.FigureError(limitBox.Entry.Text, FigureKind.Money, required: false)));
        }

        if (shown.HasFlag(ManualField.Apr))
        {
            aprBox.ShowError(aprLive.Blur(ManualAccountEditing.FigureError(aprBox.Entry.Text, FigureKind.Percent, required: false)));
        }

        if (shown.HasFlag(ManualField.Payment))
        {
            paymentBox.ShowError(paymentLive.Blur(
                ManualAccountEditing.FigureError(paymentBox.Entry.Text, FigureKind.Money, required: false)));
        }

        // The one sentence at the top is the required-field summary and nothing else.
        var summary = ManualAccountEditing.RequiredSummary(
            nameMissing: name.Length == 0, balanceMissing: balance.Status == FigureStatus.Empty);
        if (summary is not null)
        {
            ShowError(summary);
            FocusFirstRoseBox(shown);
            return;
        }

        statusLabel.IsVisible = false;

        // A figure that cannot be read is NEVER saved as "no figure": the rose box says how to fix it.
        if (balance.Status == FigureStatus.Invalid
            || (shown.HasFlag(ManualField.CreditLimit) && limit.Status == FigureStatus.Invalid)
            || (shown.HasFlag(ManualField.Apr) && apr.Status == FigureStatus.Invalid)
            || (shown.HasFlag(ManualField.Payment) && payment.Status == FigureStatus.Invalid))
        {
            FocusFirstRoseBox(shown);
            return;
        }

        decimal? limitValue = limit.Status == FigureStatus.Valid ? limit.Value : null;
        decimal? aprValue = apr.Status == FigureStatus.Valid ? apr.Value : null;
        decimal? paymentValue = payment.Status == FigureStatus.Valid ? payment.Value : null;
        var pickedCadence = everySegmented.Value == nameof(PaymentCadence.Week) ? PaymentCadence.Week : PaymentCadence.Month;
        var cadence = ManualAccountForm.CadenceToStore(paymentValue, everyTouched, pickedCadence);
        DateTimeOffset? nextDueStored = nextDue.Day is { } chosenDay ? CalendarDate.ToStored(chosenDay) : null;

        addButton.IsEnabled = false; // one tap, one account
        try
        {
            // The service keeps only what the Kind shows, so a hidden field's value is dropped there as well.
            await accounts.AddManualAccountAsync(
                name, bankBox.Entry.Text?.Trim(), kind, balance.Value, aprValue, paymentValue, cadence, nextDueStored,
                CancellationToken.None, creditLimit: limitValue);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not add the hand-entered account {AccountName}.", name);
            ShowError(ManualAccountEditing.AddFailedSentence);
            addButton.IsEnabled = true;
            return;
        }

        // Success is the card appearing on Accounts, with no toast (§7).
        addButton.IsEnabled = true;
        await Navigation.PopToRootAsync();
    }

    private void FocusFirstRoseBox(ManualField shown)
    {
        foreach (var (box, field) in new[]
                 {
                     (nameBox, ManualField.Name), (balanceBox, ManualField.Balance), (limitBox, ManualField.CreditLimit),
                     (aprBox, ManualField.Apr), (paymentBox, ManualField.Payment),
                 })
        {
            if (shown.HasFlag(field) && box.IsInvalid)
            {
                box.Entry.Focus();
                return;
            }
        }
    }

    private void ShowError(string message)
    {
        statusLabel.Text = message;
        statusLabel.IsVisible = true;
    }
}
