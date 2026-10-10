using Android.Content;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Voice;

namespace Pulp.Components;

/// <summary>
/// An empty page (mockup 13, D38, D264): a centred invitation on plain paper, plus the primary action
/// where the page has one in-page (§7). The invitation is a voiced seat — in the owner's hand when a
/// profile and key exist — and falls back to the neutral Serif italic line when they do not. Only one
/// ever shows. While a line is being composed the seat stays empty rather than flashing the neutral
/// line first (hidden until composed, as the greeting is, D118).
///
/// <para>The neutral lines are written here, one per empty-page seat on <see cref="VoicedOccasions"/>;
/// <c>EmptyStateNeutralLineTests</c> holds that every such seat has one.</para>
/// </summary>
public sealed class EmptyState : LinearLayout
{
    private readonly TextView voiced;
    private readonly TextView neutral;

    /// <param name="primary">
    /// The in-page primary action, for the seats that draw one ("Make one from a prompt", "Find a team").
    /// Home's own buttons live in its bar and never change (D263), so most pages pass none.
    /// </param>
    public EmptyState(Context context, VoiceSeat seat, Action? primary = null) : base(context)
    {
        var occasion = VoicedOccasions.For(seat);
        if (occasion.Fallback != VoiceFallback.NeutralLine)
        {
            throw new ArgumentException(
                $"{seat} hides when it has no voice; an empty page needs a seat with a neutral line (D264).", nameof(seat));
        }

        Seat = seat;
        Orientation = Orientation.Vertical;
        SetGravity(GravityFlags.Center);
        var edge = PaperTheme.Dp(context, DesignTokens.Shape.ScreenEdgeDp);
        SetPadding(edge, edge, edge, edge);

        voiced = PaperUi.Voice(context, string.Empty);
        voiced.Gravity = GravityFlags.Center;
        voiced.Visibility = ViewStates.Gone;
        AddView(voiced, Centred());

        neutral = PaperUi.EmptyState(context, NeutralLine(seat));
        neutral.Visibility = ViewStates.Gone;
        AddView(neutral, Centred());

        if (primary is not null)
        {
            var label = PrimaryLabel(seat)
                ?? throw new ArgumentException($"The {seat} page draws no in-page action, so it takes none.", nameof(primary));
            var button = PaperUi.PrimaryButton(context, label, primary);
            var layout = new LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                Gravity = GravityFlags.CenterHorizontal,
                TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X6),
            };
            AddView(button, layout);
        }
    }

    public VoiceSeat Seat { get; }

    /// <summary>
    /// What <see cref="VoiceLinePlan.Decide"/> said for this paint. <see cref="VoiceLineAction.Compose"/>
    /// leaves the seat empty until <see cref="Composed"/> arrives; <see cref="VoiceLineAction.Reuse"/>
    /// shows <paramref name="line"/>; anything else is the neutral line.
    /// </summary>
    public void Present(VoiceLineAction action, string? line)
    {
        switch (action)
        {
            case VoiceLineAction.Compose:
                voiced.Visibility = ViewStates.Gone;
                neutral.Visibility = ViewStates.Gone;
                break;
            case VoiceLineAction.Reuse:
                Composed(line);
                break;
            case VoiceLineAction.ShowNeutral:
            case VoiceLineAction.Hide:
                ShowNeutral();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, "A voice line action the empty page does not know.");
        }
    }

    /// <summary>The composer answered. A blank answer (no profile, a failed call) is the neutral line.</summary>
    public void Composed(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            ShowNeutral();
            return;
        }

        voiced.Text = line.Trim();
        voiced.Visibility = ViewStates.Visible;
        neutral.Visibility = ViewStates.Gone;
    }

    /// <summary>
    /// The neutral Serif italic line for each empty page (D264), from mockup 13's drawn copy. A child's
    /// two later pages (D294) are written in the plain, short voice of the child's Favorites line.
    /// </summary>
    public static string NeutralLine(VoiceSeat seat) => seat switch
    {
        VoiceSeat.EmptyContinueWatching => "Nothing started yet. Discover has this week's stack.",
        VoiceSeat.EmptyPlaylists => "No playlists yet.",
        VoiceSeat.EmptyPlaylist => "Nothing on this playlist yet.",
        VoiceSeat.EmptyRecentlyWatched => "Nothing watched yet.",
        VoiceSeat.EmptyChildFavorites => "No favorites yet.",
        VoiceSeat.EmptyChildWatching => "Nothing started yet.",
        VoiceSeat.EmptyChildPlaylists => "No playlists yet.",
        VoiceSeat.EmptyCalendar => "Nothing in the next two weeks.",
        VoiceSeat.EmptySports => "No teams followed yet.",
        VoiceSeat.StackUsedUp => "That's the week. More on Friday.",
        _ => throw new ArgumentOutOfRangeException(nameof(seat), seat, "This seat is not an empty page."),
    };

    /// <summary>The in-page primary action's label, for the two pages mockup 13 names one on.</summary>
    public static string? PrimaryLabel(VoiceSeat seat) => seat switch
    {
        VoiceSeat.EmptyPlaylists => MakeOneFromAPrompt,
        VoiceSeat.EmptySports => FindATeam,
        _ => null,
    };

    private const string MakeOneFromAPrompt = "Make one from a prompt";
    private const string FindATeam = "Find a team";

    private void ShowNeutral()
    {
        voiced.Visibility = ViewStates.Gone;
        neutral.Visibility = ViewStates.Visible;
    }

    private static LayoutParams Centred() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { Gravity = GravityFlags.Center };
}
