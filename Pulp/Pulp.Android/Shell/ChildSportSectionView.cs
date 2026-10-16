using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Components;
using Pulp.Domain.Children;
using Pulp.Domain.ChildView;
using Pulp.Domain.Core;
using Pulp.Domain.HandOffs;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Services;
using Pulp.Domain.Sports;
using Pulp.Library;
using Pulp.Sports;

namespace Pulp.Shell;

/// <summary>
/// The quiet "Sport" section below a child's shows in Watching (D325). Drawn from the same Paper pieces as
/// the Sports screen's rows (<see cref="SportsRows.Rule"/>, the league heading, a game's body and secondary
/// lines) but with none of its machinery: no fold, no Spoilers tag (a child cannot peek), no chips, no
/// highlights, no marks. A tap opens a card with the game and its channel; where the section decided Play,
/// the tap goes through the one hand-off door as the children watching (G13) with the game's league on its facts, so the
/// planner reads the same parent allowance the section did (D326), and the sitting is the
/// children's own, usage only (D312, D23). Nothing here reads a feed: it draws a <see cref="ChildSportSection"/>.
/// </summary>
internal sealed class ChildSportSectionView
{
    private readonly Activity host;
    private readonly Func<FrameLayout> overlay;
    private readonly PulpServices services;
    private readonly ChildAudience audience;
    private readonly LibraryWrites writes;

    public ChildSportSectionView(Activity host, Func<FrameLayout> overlay, PulpServices services, ChildAudience audience, Action changed)
    {
        this.host = host;
        this.overlay = overlay;
        this.services = services;
        this.audience = audience;
        writes = new LibraryWrites(services, () => host.RunOnUiThread(changed), "child");
    }

    public View Build(ChildSportSection section)
    {
        var column = new LinearLayout(host) { Orientation = Orientation.Vertical };
        var gap = PaperTheme.Dp(host, DesignTokens.Space.X4);
        column.SetPadding(0, gap, 0, gap);
        column.AddView(SportsRows.Rule(host, strong: false));
        var heading = PaperUi.Heading(host, section.Heading);
        heading.SetPadding(0, PaperTheme.Dp(host, DesignTokens.Space.X3), 0, 0);
        column.AddView(heading);
        foreach (var league in section.Leagues)
        {
            var name = PaperUi.Body(host, league.Name);
            name.SetTypeface(PaperTheme.SansSemibold(host), TypefaceStyle.Normal);
            name.SetPadding(0, PaperTheme.Dp(host, DesignTokens.Space.X3), 0, 0);
            column.AddView(name);
            foreach (var game in league.Games)
            {
                column.AddView(Row(game, league.Id));
            }
        }

        return column;
    }

    private View Row(ChildSportGame game, LeagueId league)
    {
        var row = new LinearLayout(host) { Orientation = Orientation.Vertical, Clickable = true };
        var pad = PaperTheme.Dp(host, DesignTokens.Space.X2);
        row.SetPadding(0, pad, 0, pad);
        row.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp));
        row.AddView(PaperUi.Body(host, game.Title));
        row.AddView(PaperUi.Secondary(host, game.Day + " · " + game.Time));
        if (game.ChannelLine is { } channel)
        {
            row.AddView(PaperUi.Secondary(host, channel));
        }

        row.Click += (_, _) => Tapped(game, league);
        return row;
    }

    private void Tapped(ChildSportGame game, LeagueId league)
    {
        if (game.Play is not { Opens: true } play)
        {
            ActionCard.Show(host, overlay(), game.Title, SportsScreenText.PlayNote(game.ChannelLine));
            return;
        }

        var service = ServiceCatalogue.Public.ById(play.Service!.Value)!;
        var shape = GamePlay.Request(audience.Watching[0], game.Id, service, play.Home!, game.Title, league);
        var request = new HandOffRequest(audience.Watching, shape.Title, shape.Link, shape.SessionNames);
        _ = Task.Run(async () =>
        {
            try
            {
                var executor = await services.HandOffsAsync(CancellationToken.None)
                    ?? throw new InvalidOperationException("The household has no roster, so no hand-off can be checked.");
                host.RunOnUiThread(() => Start(executor, request, game, service.Name));
            }
            catch (Exception exception) when (!CancellationVerdict.IsUserCancel(exception, CancellationToken.None))
            {
                services.Diagnostics.Warn("child", "A child's game Play could not reach the hand-off door.", exception);
            }
        });
    }

    private void Start(Pulp.HandOffs.HandOffExecutor executor, HandOffRequest request, ChildSportGame game, string serviceName)
    {
        var at = DateTimeOffset.UtcNow;
        var outcome = executor.Start(host, request, at);
        PulpNotices.AfterHandOff(host, overlay(), outcome, request, serviceName, game.Title, at, new PulpNotices.Buttons());
        if (outcome.Recorded is not { } pending)
        {
            return;
        }

        // Usage only, and each child's own: the sitting never says the game was watched (D312) and never reaches an adult's taste (D23).
        foreach (var child in audience.Watching)
        {
            var sitting = GamePlay.UsageSitting(child, game.Id, pending);
            writes.Enqueue("record a child's game hand-off", token => services.PutAsync(HouseholdMaps.Watched, sitting, token));
        }
    }
}
