using Constitution.Domain.Notices;

namespace Constitution.Domain.Settings;

/// <summary>How a settings row is operated: a switch for a pillar, a §7 checkbox for equipment.</summary>
public enum SettingControl
{
    Switch,
    Checkbox,
}

/// <summary>One pillar's switch. <see cref="Caveat"/> is the muted line under it, if it has one.</summary>
public sealed record PillarRow(string Id, string Name, bool On, string? Caveat)
{
    public SettingControl Control => SettingControl.Switch;
}

/// <summary>One equipment checkbox row.</summary>
public sealed record EquipmentRow(string Id, string Name, bool Picked)
{
    public SettingControl Control => SettingControl.Checkbox;
}

/// <summary>
/// The vault passphrase field. The one secret not in the vault, so it is shown in the clear and in
/// mono: it has to be read back and typed identically into each app (suite rule).
/// </summary>
public sealed record PassphraseFieldSpec(bool Masked, bool Mono, string Helper);

/// <summary>What a connection says about itself, in Domain's words.</summary>
public enum ConnectionStatus
{
    /// <summary>Set, and the last live call was not rejected: collapses to "Working ✓".</summary>
    Working,

    /// <summary>Set, never exercised.</summary>
    Saved,

    /// <summary>Never entered or restored.</summary>
    NotSet,

    /// <summary>A live call with it was refused: says what to do, in Problem-rose.</summary>
    Problem,
}

/// <summary>What the head knows about one connection: whether it holds a value, and what the health report says.</summary>
public sealed record ConnectionInput(string CredentialId, bool IsSet, ConnectionHealth Health);

/// <summary>
/// One connection as the screen draws it. <see cref="ButtonLabel"/> is the single secondary button,
/// present only when the connection needs the owner; <see cref="RaisesNotice"/> is the dog-ear notice
/// a broken one lights.
/// </summary>
public sealed record ConnectionRow(
    string CredentialId,
    string Name,
    string Detail,
    ConnectionStatus Status,
    string StatusText,
    bool EntryWanted,
    string? ButtonLabel,
    NoticeKind? RaisesNotice)
{
    public bool IsProblem => Status == ConnectionStatus.Problem;
}

/// <summary>
/// The "Overnight reads" line under the Health Connect row (review S10). The overnight permission is declared but the
/// platform only grants it from Health Connect's own page, so the line says so in one sentence and offers the button
/// that opens that page.
/// </summary>
public sealed record OvernightReadsRow(string Name, string Sentence, string ButtonLabel);
