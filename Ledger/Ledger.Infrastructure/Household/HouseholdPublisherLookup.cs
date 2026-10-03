using Index.Domain.Household;

namespace Index.Infrastructure.Household;

/// <summary>
/// <see cref="IHouseholdPublisherLookup"/> over the real household file: the same read her pages used
/// to make on every appearance, made once by <see cref="InstallRoleService"/> instead.
/// </summary>
public sealed class HouseholdPublisherLookup : IHouseholdPublisherLookup
{
    private readonly HouseholdSnapshotReader reader;

    public HouseholdPublisherLookup(HouseholdSnapshotReader reader)
    {
        this.reader = reader;
    }

    public async Task<HouseholdFilePublisher> LookAsync(string passphrase, CancellationToken cancellationToken)
    {
        HouseholdSnapshotReadResult result;
        try
        {
            result = await reader.ReadAsync(passphrase, cancellationToken);
        }
        catch (Exception ex)
        {
            // Drive being unreachable is "cannot tell right now" — the reason travels in the result so
            // the caller can log it; nothing is thrown past the seam.
            return HouseholdFilePublisher.Unreadable($"{ex.GetType().Name}: {ex.Message}");
        }

        return result.Status switch
        {
            HouseholdSnapshotStatus.Loaded => HouseholdFilePublisher.PublishedBy(result.Snapshot!.PublishedByAccountEmail),
            // A file newer than this build can read still names who published it, and that is all role
            // resolution needs — "unreadable" here would leave her install's role undecided on the very
            // day the owner's phone updated first.
            HouseholdSnapshotStatus.NewerVersion when !string.IsNullOrWhiteSpace(result.NewerFilePublisherEmail) =>
                HouseholdFilePublisher.PublishedBy(result.NewerFilePublisherEmail),
            HouseholdSnapshotStatus.NewerVersion => HouseholdFilePublisher.Unreadable("The household file is from a newer version of Index."),
            HouseholdSnapshotStatus.Empty => HouseholdFilePublisher.Absent,
            HouseholdSnapshotStatus.WrongPassphrase => HouseholdFilePublisher.Unreadable("The passphrase does not open the household file."),
            _ => HouseholdFilePublisher.Unreadable("The household file is damaged."),
        };
    }
}
