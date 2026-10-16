using System.Text;

namespace Codex.Domain.Post;

/// <summary>What Google Messages' install looks like from Post.</summary>
public enum MessagesAppState
{
    NotInstalled,
    Disabled,
    Suspended,
    Installed,
}

/// <summary>
/// Which routes a text has into Post on this phone right now, printed at the head of the
/// texts trail in the diagnostics export. The reading underneath is the point: the routes
/// interact in a way that is not visible from inside the app, and the sentence that
/// explains a lost RCS message belongs next to the evidence.
/// </summary>
public sealed record TextsStatus(
    bool HoldsSmsRole,
    bool BridgePaired,
    bool BridgeConnected,
    bool ListenerEnabled,
    MessagesAppState Messages)
{
    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Default SMS app: {(HoldsSmsRole ? "Post" : "not Post")}");
        builder.AppendLine("Messages-for-Web bridge: " + (BridgePaired
            ? (BridgeConnected ? "paired, connected" : "paired, not connected")
            : "not paired"));
        builder.AppendLine($"Notification capture: {(ListenerEnabled ? "on" : "off")}");
        builder.AppendLine("Google Messages: " + Messages switch
        {
            MessagesAppState.NotInstalled => "not installed",
            MessagesAppState.Disabled => "disabled",
            MessagesAppState.Suspended => "suspended",
            _ => "installed",
        });

        builder.AppendLine();
        foreach (var line in Reading())
        {
            builder.AppendLine(line);
        }
        return builder.ToString().TrimEnd();
    }

    private IEnumerable<string> Reading()
    {
        if (HoldsSmsRole && Messages != MessagesAppState.NotInstalled)
        {
            yield return "Google Messages is not the default SMS app, so it cannot keep RCS (\"chat features\") " +
                "turned on. Other phones go on sending RCS to this number until they notice, then fall back to " +
                "SMS: those texts arrive late, a group's fall back as separate texts from each sender, and some " +
                "never fall back at all.";
        }
        if (!BridgePaired && ListenerEnabled)
        {
            yield return "The bridge is not paired, so Post has no route for RCS except Google Messages' own " +
                "notifications, which name only the members who have spoken.";
        }
        if (!BridgePaired && !ListenerEnabled)
        {
            yield return "With no bridge and no notification capture, RCS messages cannot reach Post at all.";
        }
        if (Messages is MessagesAppState.Suspended or MessagesAppState.Disabled)
        {
            yield return "Google Messages cannot run, so it neither receives RCS nor posts the notifications " +
                "Post reads it from.";
        }
    }
}
