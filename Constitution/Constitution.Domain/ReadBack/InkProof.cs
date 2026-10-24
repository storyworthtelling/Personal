namespace Constitution.Domain.ReadBack;

/// <summary>One document in which pen ink was seen, and the morning it was read.</summary>
public sealed record InkSighting
{
    public InkSighting(string documentName, DateOnly morning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentName);
        DocumentName = documentName;
        Morning = morning;
    }

    public string DocumentName { get; }

    public DateOnly Morning { get; }
}

/// <summary>
/// Whether the read-back believes pen ink comes through the cloud's PDF at all (R3). Once it does, a page with no ink is the
/// owner's silence and may be recorded as a skip; until it does, a page with no ink is a missing round trip and is kept.
///
/// <para><b>One read's word is not proof.</b> The flag used to be set by a single document, on the word of a model that said it
/// saw ink or of one confident mark, and was never doubted again, so one wrong "seen" turned every later page the cloud had
/// not caught up with into a skip. It now takes two <see cref="InkSighting"/>s: two different documents, read on two
/// different mornings. Establishing it also records the morning (<see cref="EstablishedOn"/>), so the service can hold back
/// the first blank pages it meets on that morning for a day.</para>
///
/// <para>Immutable; <see cref="Record"/> returns what is now known.</para>
/// </summary>
public sealed class InkProof
{
    /// <summary>How many sightings are kept while the proof is still being gathered. Once it is established none are kept.</summary>
    public const int MostSightings = 16;

    private InkProof(IReadOnlyList<InkSighting> sightings, bool isEstablished, DateOnly? establishedOn)
    {
        Sightings = sightings;
        IsEstablished = isEstablished;
        EstablishedOn = establishedOn;
    }

    /// <summary>Nothing seen yet.</summary>
    public static InkProof None { get; } = new([], false, null);

    /// <summary>Established, on a morning nobody kept (a file written before the morning was recorded).</summary>
    public static InkProof EstablishedEarlier { get; } = new([], true, null);

    public static InkProof EstablishedOnMorning(DateOnly morning) => new([], true, morning);

    /// <summary>What a file held: the sightings kept so far and, when it says so, that the proof is established.</summary>
    public static InkProof Restore(IEnumerable<InkSighting> sightings, bool established, DateOnly? establishedOn)
    {
        ArgumentNullException.ThrowIfNull(sightings);
        return established
            ? new InkProof([], true, establishedOn)
            : new InkProof(sightings.Distinct().TakeLast(MostSightings).ToList(), false, null);
    }

    public IReadOnlyList<InkSighting> Sightings { get; }

    public bool IsEstablished { get; }

    /// <summary>The morning the proof was established; null when it is not, or was established before mornings were kept.</summary>
    public DateOnly? EstablishedOn { get; }

    /// <summary>Remembers a sighting. The same sighting twice, or any sighting once established, changes nothing.</summary>
    public InkProof Record(InkSighting sighting)
    {
        ArgumentNullException.ThrowIfNull(sighting);
        if (IsEstablished || Sightings.Contains(sighting))
        {
            return this;
        }

        // Proved by a second sighting of a different document on a different morning than one already kept.
        if (Sightings.Any(kept => kept.DocumentName != sighting.DocumentName && kept.Morning != sighting.Morning))
        {
            return new InkProof([], true, sighting.Morning);
        }

        return new InkProof(Sightings.Append(sighting).TakeLast(MostSightings).ToList(), false, null);
    }
}
