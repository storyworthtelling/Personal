using System.Collections.Immutable;

namespace Codex.Domain.Plate;

/// <summary>
/// Someone the owner knows, and the faces that have been confirmed as theirs.
///
/// <para>The id is their contact in the phone's address book — the Rolodex — so this never
/// becomes a second, competing list of people.</para>
/// </summary>
public sealed record KnownPerson(string ContactId, string Name, IReadOnlyList<float[]> Faces);

/// <summary>Who a face belongs to, or nobody.</summary>
public readonly record struct FaceMatch(string ContactId, string Name)
{
    public static readonly FaceMatch Unknown = new(string.Empty, string.Empty);

    public bool IsSomeone => ContactId.Length > 0;
}

/// <summary>
/// Knowing who is in a picture, on the phone.
///
/// <para><b>Why local.</b> Claude will not name a person in an image — it refuses as a matter
/// of policy — and a Darkroom picture has not been uploaded anywhere to be asked about in the
/// first place. So faces are matched here, against people the owner already knows: the
/// Rolodex is the phone's own address book, and a contact with a photo is a labelled face
/// that already exists.</para>
///
/// <para><b>It suggests; the owner decides.</b> Every match is a question — "is this
/// Jordan?" — and a confirmation teaches it, which is what makes the forgiving threshold the
/// right one: a wrong suggestion costs a tap, a missed one costs a name nobody offered. A
/// correction is permanent, exactly as Post's sender promotions are.</para>
///
/// <para>The matching is cosine distance because face embedders are trained for it, and
/// because it ignores how brightly the crop happened to be lit.</para>
/// </summary>
public sealed record FaceBook
{
    /// <summary>
    /// How close counts as the same person, as a cosine distance on L2-normalised
    /// embeddings. Generous on purpose: the answer is a question the owner confirms, not a
    /// filing decision made behind their back.
    ///
    /// <para><b>This number is assumed, not measured, and it is the phase's one un-evidenced
    /// constant.</b> It is the usual operating point for ArcFace-family models on real
    /// faces, which EdgeFace is. Synthetic inputs proved uninformative for setting it —
    /// the model has never seen anything like them and maps them all to nearly one point,
    /// so distances there sit around 0.001 rather than anywhere near this — which is
    /// precisely why it has to be measured against the owner's own contact photos on a real
    /// phone. The on-device test plan carries that as an explicit step.</para>
    ///
    /// <para>If it is wrong it will be wrong in a visible way: too high and everyone is
    /// suggested as the same person, too low and nobody is ever recognised. Both are obvious
    /// within a few pictures, and neither files anything without being asked.</para>
    /// </summary>
    public const double SameFace = 0.62;

    /// <summary>
    /// How many faces are kept for one person. Every stored face is compared on every
    /// picture, on a phone, while the owner waits — and the newest are the ones kept, because
    /// a face changes over time.
    /// </summary>
    public const int FacesPerPerson = 8;

    public static readonly FaceBook Empty = new([]);

    private FaceBook(ImmutableList<KnownPerson> people) => People = people;

    public IReadOnlyList<KnownPerson> People { get; }

    /// <summary>
    /// Who this face looks most like, or nobody. Nobody is the important answer: handing back
    /// the closest person in the book for a stranger is how a picture of someone else gets
    /// filed under a friend's name.
    /// </summary>
    public FaceMatch Nearest(IReadOnlyList<float> face)
    {
        if (!Usable(face))
        {
            return FaceMatch.Unknown;
        }

        var best = FaceMatch.Unknown;
        var bestDistance = double.MaxValue;

        foreach (var person in People)
        {
            foreach (var known in person.Faces)
            {
                // A model swap changes the vector length. Comparing across it would produce
                // confident nonsense rather than an obvious failure.
                if (known.Length != face.Count)
                {
                    continue;
                }

                var distance = CosineDistance(known, face);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = new FaceMatch(person.ContactId, person.Name);
                }
            }
        }

        return bestDistance <= SameFace ? best : FaceMatch.Unknown;
    }

    /// <summary>
    /// Records that this face is theirs. Learning the same person twice does not split them,
    /// and the name is refreshed from the address book, which is where names really live.
    /// </summary>
    public FaceBook Learn(string contactId, string name, IReadOnlyList<float> face)
    {
        if (!Usable(face) || string.IsNullOrEmpty(contactId))
        {
            return this;
        }

        var stored = face.ToArray();
        var existing = People.FirstOrDefault(person => person.ContactId == contactId);
        if (existing is null)
        {
            return new FaceBook([.. People.Append(new KnownPerson(contactId, name, [stored]))]);
        }

        var faces = existing.Faces.Append(stored).TakeLast(FacesPerPerson).ToList();
        return new FaceBook([.. People.Select(person => person.ContactId == contactId
            ? person with { Name = name, Faces = faces }
            : person)]);
    }

    /// <summary>
    /// The owner says it is somebody else. The face is taken off whoever it was wrongly on
    /// first — leaving it behind would pull the old name back the next time, and they would
    /// have to correct it again and again.
    /// </summary>
    public FaceBook Correct(string contactId, string name, IReadOnlyList<float> face)
    {
        if (!Usable(face))
        {
            return this;
        }

        var stored = face.ToArray();
        var cleaned = People
            .Select(person => person with
            {
                Faces = [.. person.Faces.Where(known => !known.SequenceEqual(stored))],
            })
            .ToImmutableList();

        return new FaceBook(cleaned).Learn(contactId, name, stored);
    }

    /// <summary>Removes someone entirely, rather than leaving their faces under no name.</summary>
    public FaceBook Forget(string contactId) =>
        new([.. People.Where(person => person.ContactId != contactId)]);

    // A detector that found nothing, or a crop that came out black. Either would otherwise be
    // "distance zero from everything".
    private static bool Usable(IReadOnlyList<float> face) =>
        face.Count > 0 && face.Any(value => value != 0);

    private static double CosineDistance(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double dot = 0, leftLength = 0, rightLength = 0;
        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            leftLength += left[i] * left[i];
            rightLength += right[i] * right[i];
        }

        if (leftLength == 0 || rightLength == 0)
        {
            return double.MaxValue;
        }

        return 1 - (dot / (Math.Sqrt(leftLength) * Math.Sqrt(rightLength)));
    }
}
