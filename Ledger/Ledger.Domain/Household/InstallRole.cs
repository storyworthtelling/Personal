namespace Index.Domain.Household;

// D09/D10/D25/D26/D29/D35/D36/D90 (index-design-book.html): the two shapes an Index install can take.
// Owner is the one that ever talks to Plaid — connects, refreshes, edits — and publishes the household
// snapshot. Hers never talks to Plaid at all (D09: "no Plaid keys in Settings — hidden, not shown
// disabled") and reads the published snapshot instead, with Snowball's editing controls hidden too
// (D90) and a Settings screen holding only the vault passphrase and her Google account (D36).
public enum InstallRole
{
    Owner,
    Hers
}
