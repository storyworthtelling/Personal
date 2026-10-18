namespace Constitution.Domain.Diagnostics;

/// <summary>
/// Everything a Diagnostics export may say, and nothing else (C-8.3.2): the app version, whose
/// install this is, counts, the last outcome of each part, the documents delivered and where they
/// went, and each connection's credential state.
///
/// <para><b>A closed type, not a filtered one.</b> The way to keep a health value out of a shared
/// file is for the model to have nowhere to put one, so every member is an integer count (capped), an
/// enum, or one of four validated identifiers (<see cref="AppVersion"/>, <see cref="DocumentFile"/>,
/// <see cref="ConnectionId"/>, and since review S8 <see cref="TrailLine"/>, which only <see cref="TrailLines.Filter"/> can make). There is no string constructor parameter anywhere in the graph and no
/// double, date or free-text field; <c>DiagnosticsSnapshotShapeTests</c> walks the graph and fails the
/// day one appears. Notices go in as kinds and counts (<see cref="NoticeCount.Tally"/>), never as
/// their cause keys. The counts of workouts and recipes are counts: whatever those are made of stays
/// where it is.</para>
///
/// <para>Notices are not a separate list because the notice <i>kinds</i> already are the failure
/// states the owner sees on the Waiting page; the credential and outcome entries say which part is
/// behind each.</para>
/// </summary>
public sealed class DiagnosticsSnapshot
{
    public DiagnosticsSnapshot(
        AppVersion version,
        InstallRole role,
        DiagnosticsCounts counts,
        IEnumerable<ComponentOutcome> outcomes,
        IEnumerable<DocumentDelivery> documents,
        IEnumerable<CredentialStatus> credentials,
        IEnumerable<TrailSection>? trails = null)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(counts);
        ArgumentNullException.ThrowIfNull(outcomes);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(credentials);

        Version = version;
        Role = ComponentOutcome.Defined(role, nameof(role));
        Counts = counts;
        Outcomes = outcomes.ToList();
        Documents = documents.ToList();
        Credentials = credentials.ToList();
        Trails = (trails ?? []).ToList();

        if (Outcomes.Select(entry => entry.Component).Distinct().Count() != Outcomes.Count)
        {
            throw new ArgumentException("Each part may report one outcome.", nameof(outcomes));
        }
        if (Trails.Select(section => section.Kind).Distinct().Count() != Trails.Count)
        {
            throw new ArgumentException("Each trail may appear once.", nameof(trails));
        }
        if (Credentials.Select(entry => entry.Connection.Value).Distinct(StringComparer.Ordinal).Count() != Credentials.Count)
        {
            throw new ArgumentException("Each connection may report one state.", nameof(credentials));
        }
    }

    public AppVersion Version { get; }

    public InstallRole Role { get; }

    public DiagnosticsCounts Counts { get; }

    public IReadOnlyList<ComponentOutcome> Outcomes { get; }

    public IReadOnlyList<DocumentDelivery> Documents { get; }

    public IReadOnlyList<CredentialStatus> Credentials { get; }

    /// <summary>The newest lines of the Body and vault trails, each through <see cref="TrailLines.Filter"/>.</summary>
    public IReadOnlyList<TrailSection> Trails { get; }
}
