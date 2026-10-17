namespace Constitution.Domain.Equipment;

/// <summary>
/// Which equipment this install has ticked. Immutable. The planner never prescribes a tool that is
/// unticked (book p.17), so the set is what it reads.
///
/// <para>Stored as one comma-joined string. <b>Nothing stored means everything ticked</b>: an
/// install that has never been asked must not plan as though it owned nothing. A deliberate "none"
/// is stored as a dash so it is told apart from "never asked".</para>
/// </summary>
public sealed class EquipmentSet
{
    private const string NoneMarker = "-";

    private readonly HashSet<string> owned;

    private EquipmentSet(IEnumerable<string> ids)
    {
        var known = EquipmentCatalogue.All.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        owned = ids.Where(known.Contains).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The set of these ids; an id the catalogue does not know is ignored.</summary>
    public static EquipmentSet Of(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return new EquipmentSet(ids);
    }

    /// <summary>Everything ticked: what an install that was never asked has.</summary>
    public static EquipmentSet Everything() => new(EquipmentCatalogue.All.Select(item => item.Id));

    /// <summary>How many of the six are ticked.</summary>
    public int CountOn => owned.Count;

    /// <summary>How many there are to tick.</summary>
    public static int Total => EquipmentCatalogue.All.Count;

    public bool Has(string id) => owned.Contains(id);

    /// <summary>A copy with one item ticked or unticked.</summary>
    public EquipmentSet With(string id, bool on) =>
        new(on ? owned.Append(id) : owned.Where(existing => existing != id));

    /// <summary>The stored form: ids in catalogue order, or a dash for none.</summary>
    public string Encode() =>
        owned.Count == 0
            ? NoneMarker
            : string.Join(',', EquipmentCatalogue.All.Where(item => owned.Contains(item.Id)).Select(item => item.Id));

    /// <summary>Reads the stored form. Null or empty (never stored) is everything.</summary>
    public static EquipmentSet Decode(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return Everything();
        }

        return stored == NoneMarker
            ? Of([])
            : Of(stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
