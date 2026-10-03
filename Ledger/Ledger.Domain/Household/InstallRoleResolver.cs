namespace Index.Domain.Household;

/// <summary>
/// Which install this is — mirroring <c>Foreword.Domain.Install.OwnerColumn</c>'s exact shape (the
/// finding's own precedent for "which install is this", read in full before writing this type):
/// proposed from context rather than asked for, an explicit choice always winning over the proposal,
/// and a safe default for when nothing is known yet.
///
/// <para><b>D44</b> rules out a "whose install is this" picker anywhere in the UI ("no separate label
/// — the signed-in Google account is the only signal"), so nothing in this app ever supplies
/// <paramref name="chosen"/> today; it exists only so <see cref="Resolve"/> keeps <c>OwnerColumn</c>'s
/// full shape rather than silently dropping the one part of the precedent Index doesn't currently use
/// — a later release that does need an override changes nothing about this type's contract.</para>
///
/// <para>Foreword's version proposes a person from a household address book
/// (<c>PersonIdentity.WithMailboxes</c>) it already has. Index has no such book — there are only ever
/// two installs, and the fact Index actually has is the household snapshot file itself
/// (<c>HouseholdSnapshot.PublishedByAccountEmail</c>), which records which Google account published
/// it. An install whose own signed-in account differs from the file's publisher is the reading
/// install; nothing else needs to ask, and nothing is asked.</para>
/// </summary>
public static class InstallRoleResolver
{
    /// <summary>
    /// What the two account addresses suggest, or nothing when either is unknown — no signed-in
    /// account yet, or no household snapshot has ever been read (a fresh owner install, or an install
    /// that has never reached the household file). A matching address is the OWNER's own install
    /// (it published what it is reading back, or would); anything else is Hers.
    /// </summary>
    public static InstallRole? Propose(string? signedInAccountEmail, string? snapshotPublishedByAccountEmail)
    {
        if (string.IsNullOrWhiteSpace(signedInAccountEmail) || string.IsNullOrWhiteSpace(snapshotPublishedByAccountEmail))
        {
            return null;
        }

        return string.Equals(
                signedInAccountEmail.Trim(), snapshotPublishedByAccountEmail.Trim(), StringComparison.OrdinalIgnoreCase)
            ? InstallRole.Owner
            : InstallRole.Hers;
    }

    /// <summary>
    /// The role this install plays: <paramref name="chosen"/> if anything ever sets it, else what the
    /// two accounts propose, else <see cref="InstallRole.Owner"/> — the answer for every install
    /// before this release existed, and for the brief window on a genuinely fresh install (hers
    /// included) before a household snapshot has ever been read. That fallback matters the same way
    /// <c>OwnerColumn</c>'s does: a phone that has been connecting Plaid and publishing balances for
    /// months must not decide it is the read-only one just because nothing was read yet this launch.
    /// </summary>
    public static InstallRole Resolve(
        InstallRole? chosen, string? signedInAccountEmail, string? snapshotPublishedByAccountEmail) =>
        chosen
            ?? Propose(signedInAccountEmail, snapshotPublishedByAccountEmail)
            ?? InstallRole.Owner;
}
