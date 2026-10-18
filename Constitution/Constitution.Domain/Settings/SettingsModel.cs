using Constitution.Domain.Areas;
using Constitution.Domain.Credentials;
using Constitution.Domain.Equipment;
using Constitution.Domain.Health;
using Constitution.Domain.Notices;

namespace Constitution.Domain.Settings;

/// <summary>
/// What the Settings page says (book p.17), as data: pillar switches, equipment checkboxes, the
/// connection rows, the passphrase field and the export button. Pure, so the screen is a thin painter.
///
/// <para>Connection states follow the suite's <c>CredentialFieldState.Decide</c> rule (a working key is a
/// done state; only a missing or just-rejected one asks for anything). Domain does not reference the
/// library that holds it, so the rule is restated here and pinned equal by
/// <c>ConnectionsTests.AgreesWithTheSharedCredentialFieldState</c>.</para>
///
/// <para>NYT Cooking is deliberately absent (spike D96: no official route).</para>
/// </summary>
public sealed class SettingsModel
{
    /// <summary>The id of the Health Connect row; it has no credential, so it has no notice.</summary>
    public const string HealthConnectId = "health-connect";

    private const string MealsCaveat = "Stays a household plan even when off here";
    private const string PassphraseHelper = "Shown in the clear so it can be read back into each app.";

    private SettingsModel()
    {
    }

    public string PillarsHeader => Header("Pillars", PillarsSummary);

    /// <summary>The muted half of the closed group's header, "3 on".</summary>
    public string PillarsSummary { get; private init; } = string.Empty;

    public IReadOnlyList<PillarRow> PillarRows { get; private init; } = [];

    public string EquipmentHeader => Header("Equipment", EquipmentSummary);

    /// <summary>The muted half of the closed group's header, "5 of 6".</summary>
    public string EquipmentSummary { get; private init; } = string.Empty;

    public IReadOnlyList<EquipmentRow> EquipmentRows { get; private init; } = [];

    public string ConnectionsHeader => Header("Connections", ConnectionsSummary);

    /// <summary>The muted half of the closed group's header; empty when nothing needs the owner.</summary>
    public string ConnectionsSummary { get; private init; } = string.Empty;

    public IReadOnlyList<ConnectionRow> Connections { get; private init; } = [];

    /// <summary>The line under the Health Connect row that asks for the overnight permission (review S10).</summary>
    public OvernightReadsRow OvernightReads { get; } = new("Overnight reads", HealthRationale.OvernightLine, "Open Health Connect");

    public PassphraseFieldSpec Passphrase { get; } = new(Masked: false, Mono: true, PassphraseHelper);

    public string ExportLabel => "Export diagnostics";

    /// <summary>
    /// Builds the page. <paramref name="connections"/> states what the head knows; a connection it says
    /// nothing about is shown as not set.
    /// </summary>
    public static SettingsModel Build(Pillars pillars, EquipmentSet equipment, IEnumerable<ConnectionInput> connections)
    {
        ArgumentNullException.ThrowIfNull(pillars);
        ArgumentNullException.ThrowIfNull(equipment);
        ArgumentNullException.ThrowIfNull(connections);

        PillarRow[] pillarRows =
        [
            new("workouts", "Workouts", pillars.Workouts, null),
            new("meals", "Meals", pillars.Meals, MealsCaveat),
            new("body", "Body", pillars.Body, null),
        ];

        var equipmentRows = EquipmentCatalogue.All
            .Select(item => new EquipmentRow(item.Id, item.Name, equipment.Has(item.Id)))
            .ToList();

        var stated = connections.ToDictionary(input => input.CredentialId, StringComparer.Ordinal);
        var rows = ConnectionOrder
            .Select(id => RowFor(id, stated.TryGetValue(id, out var input) ? input : new ConnectionInput(id, false, ConnectionHealth.Unknown)))
            .ToList();
        // Counted by what the owner can act on: a row with a button.
        var needing = rows.Count(row => row.ButtonLabel is not null);

        return new SettingsModel
        {
            PillarsSummary = $"{pillarRows.Count(row => row.On)} on",
            PillarRows = pillarRows,
            EquipmentSummary = $"{equipment.CountOn} of {EquipmentSet.Total}",
            EquipmentRows = equipmentRows,
            ConnectionsSummary = needing switch
            {
                0 => string.Empty,
                1 => "1 needs you",
                _ => $"{needing} need you",
            },
            Connections = rows,
        };
    }

    /// <summary>
    /// States every connection to the board: a refused one raises its notice, a working one takes it
    /// away. Returns whether the board changed.
    /// </summary>
    public static bool ApplyNotices(NoticeBoard board, IEnumerable<ConnectionInput> connections, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(connections);

        var changed = false;
        foreach (var input in connections)
        {
            changed |= ConnectionNotices.Apply(board, input.CredentialId, input.Health, now);
        }

        return changed;
    }

    private static string Header(string title, string summary) =>
        summary.Length == 0 ? title : $"{title} {summary}";

    private static readonly string[] ConnectionOrder =
        [HealthConnectId, CredentialIds.SleepNumber, CredentialIds.Remarkable];

    private static ConnectionRow RowFor(string id, ConnectionInput input)
    {
        var (name, detail, problemText, button) = id switch
        {
            HealthConnectId => ("Health Connect", "Choices kept in the vault",
                "Health Connect is not reachable. Open it and allow Constitution to read again.", (string?)null),
            CredentialIds.SleepNumber => ("Sleep Number", "The household login · in the vault",
                "The bed would not accept the login. Sign in again; the household login is saved to the vault.", "Sign in"),
            _ => ("reMarkable", "From the vault · the same pairing Foreword uses",
                "The tablet refused the pairing. Pair again with a new code; the pairing is saved to the vault.", "Pair again"),
        };

        // CredentialFieldState.Decide, restated (see the class summary).
        if (input.Health == ConnectionHealth.Rejected)
        {
            return new ConnectionRow(id, name, detail, ConnectionStatus.Problem, problemText, EntryWanted: true,
                button, ConnectionNotices.KindFor(id));
        }

        if (!input.IsSet)
        {
            return new ConnectionRow(id, name, detail, ConnectionStatus.NotSet, "Not set.", EntryWanted: true,
                button, null);
        }

        return input.Health == ConnectionHealth.Working
            ? new ConnectionRow(id, name, detail, ConnectionStatus.Working, "Working ✓", EntryWanted: false, null, null)
            : new ConnectionRow(id, name, detail, ConnectionStatus.Saved, "Saved.", EntryWanted: false, null, null);
    }
}
