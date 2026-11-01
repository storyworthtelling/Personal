using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The native heads on the log loop (LOG-LOOP-PLAN.md §4, Phase 2), with what each must declare. One table,
/// so every guard asks the same question of the same heads; a head joins the loop by a row here.
/// </summary>
internal static class LogLoopHeads
{
    public sealed record Head(string App, string Folder, string KeyActionsFile, string KeyActionsClass, string[] KeyActions);

    public const string SharedKit = "FocusPhone/Codex.Ui.Android";

    public static readonly Head[] All =
    [
        new("post", "FocusPhone/Post.Android", "FocusPhone/Codex.Domain/Post/PostKeyActions.cs", "PostKeyActions",
        [
            "post.send-sms", "post.send-mms", "post.file", "post.delete", "post.request-sms-role",
            "post.gmail-sign-in", "post.open-attachment", "post.send-logs-now",
        ]),
        new("flyleaf", "FocusPhone/Flyleaf.Android", "FocusPhone/Codex.Domain/Diagnostics/FlyleafKeyActions.cs", "FlyleafKeyActions",
        [
            "flyleaf.install-from-github", "flyleaf.install-apk", "flyleaf.admin-unlock", "flyleaf.move-to-shelf",
            "flyleaf.feature", "flyleaf.block", "flyleaf.uninstall", "flyleaf.connectivity", "flyleaf.answer-call",
            "flyleaf.send-logs-now",
        ]),
        new("score", "FocusPhone/Score.Android", "FocusPhone/Codex.Domain/Diagnostics/ScoreKeyActions.cs", "ScoreKeyActions",
        [
            "score.tidal-sign-in", "score.rescan", "score.play", "score.mark-played", "score.discovery-playlist",
            "score.send-logs-now",
        ]),
        new("plate", "FocusPhone/Plate.Android", "FocusPhone/Codex.Domain/Diagnostics/PlateKeyActions.cs", "PlateKeyActions",
        [
            "plate.shutter", "plate.send", "plate.scan", "plate.deliver", "plate.recognise", "plate.send-logs-now",
        ]),
        new("pulp", "Pulp/Pulp.Android", "Pulp/Pulp.Domain/Diagnostics/PulpKeyActions.cs", "PulpKeyActions",
        [
            "pulp.hand-off", "pulp.locked-open", "pulp.plan", "pulp.send-logs-now",
        ]),
        new("constitution", "Constitution/Constitution.Android", "Constitution/Constitution.Domain/Diagnostics/ConstitutionKeyActions.cs", "ConstitutionKeyActions",
        [
            "constitution.google-sign-in", "constitution.sleep-number-login", "constitution.health-connect-permission",
            "constitution.scan-recipe", "constitution.cook-soon", "constitution.send-logs-now",
        ]),
    ];

    public static TheoryData<string> Folders() => [.. All.Select(h => h.Folder)];

    public static TheoryData<string> Apps() => [.. All.Select(h => h.App)];

    public static Head For(string app) => All.Single(h => h.App == app);
}
