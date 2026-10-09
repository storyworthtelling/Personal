using Pulp.Domain.Household;

namespace Pulp.Tests.Household;

/// <summary>
/// G11 — two or three writers' interleaved edits converge to one document whatever order the
/// merges happen in. Property-style: a seeded <see cref="Random"/> drives each run, so a failure
/// names its seed and replays exactly. The writers, keys and values are invented.
///
/// <para>The bug it exists for is the silent one: a merge that depends on which side read first
/// leaves the two phones holding different households with nothing to say so, and the one that
/// writes last quietly decides what the other lost.</para>
/// </summary>
public class HouseholdConvergenceTests
{
    private const int Runs = 120;
    private const int StepsPerRun = 60;
    private const int OrdersPerRun = 12;
    private const long Hour = 3600000L;

    private static readonly RecordKey[] Keys =
    [
        new("playlist", "rainy-day"),
        new("playlist", "film-club"),
        new("watched", "show-17"),
        new("favorite", "film-4"),
    ];

    private sealed class Writer
    {
        public Writer(string id, long skew, int schemaVersion)
        {
            Id = id;
            Skew = skew;
            SchemaVersion = schemaVersion;
        }

        public string Id { get; }

        public long Skew { get; }

        public int SchemaVersion { get; }

        public HybridLogicalClock Clock { get; } = new();

        public HouseholdDocument Replica { get; set; } = HouseholdDocument.Empty;
    }

    private sealed class Tally
    {
        public int Operations;
        public int ContestedKeys;
        public int SameWallAcrossWriters;
        public int DeletesBeatenByNewerWrite;
        public int WritesBeatenByDelete;
        public int FinalTombstones;
        public int FinalLive;
        public int OrdersCompared;
    }

    [Fact]
    public void TwoWritersConvergeWhateverOrderTheirEditsMerge() => RunMany(writerCount: 2, firstSeed: 1000);

    [Fact]
    public void ThreeWritersConvergeWhateverOrderTheirEditsMerge() => RunMany(writerCount: 3, firstSeed: 5000);

    private static void RunMany(int writerCount, int firstSeed)
    {
        var tally = new Tally();
        for (var seed = firstSeed; seed < firstSeed + Runs; seed++)
        {
            RunOne(seed, writerCount, tally);
        }

        // Non-vacuity: the runs must actually have produced the conflicts the guard is about.
        Assert.True(tally.Operations >= Runs * StepsPerRun / 2, $"only {tally.Operations} operations were generated");
        Assert.True(tally.ContestedKeys > Runs, $"only {tally.ContestedKeys} keys were written by more than one writer");
        Assert.True(tally.SameWallAcrossWriters > 0, "no two writers ever shared a wall-clock millisecond; the tie-break went untested");
        Assert.True(tally.DeletesBeatenByNewerWrite > 0, "no delete was ever followed by a newer write; resurrection went untested");
        Assert.True(tally.WritesBeatenByDelete > 0, "no write was ever beaten by a delete; tombstones went untested");
        Assert.True(tally.FinalTombstones > 0 && tally.FinalLive > 0, $"final documents held {tally.FinalLive} live and {tally.FinalTombstones} tombstones");
        Assert.True(tally.OrdersCompared >= Runs * OrdersPerRun, $"only {tally.OrdersCompared} merge orders were compared");
    }

    private static void RunOne(int seed, int writerCount, Tally tally)
    {
        var random = new Random(seed);
        var writers = new List<Writer>
        {
            new("phone-a", 0, 1),
            new("phone-b", random.Next(2) == 0 ? 0 : -Hour, 1),
            new("tv-c", Hour / 2, 2),
        }.Take(writerCount).ToList();

        var operations = new List<HouseholdRecord>();
        var now = 10 * Hour;

        for (var step = 0; step < StepsPerRun; step++)
        {
            now += random.Next(3);
            var writer = writers[random.Next(writers.Count)];
            var physical = now + writer.Skew;
            var key = Keys[random.Next(Keys.Length)];
            var roll = random.Next(10);

            if (roll < 3)
            {
                var other = writers[random.Next(writers.Count)];
                writer.Clock.Witness(other.Replica.MaxStamp, physical);
                writer.Replica = HouseholdMerge.Merge(writer.Replica, other.Replica);
                continue;
            }

            var existing = writer.Replica.Find(key);
            HouseholdRecord record;
            if (roll < 6)
            {
                var stamp = writer.Clock.Now(physical);
                record = existing is null
                    ? HouseholdRecord.Tombstone(key, writer.SchemaVersion, stamp, writer.Id)
                    : existing.Delete(stamp, writer.Id);
            }
            else
            {
                var changes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["note"] = $"{writer.Id}-{step}",
                };
                if (writer.SchemaVersion > 1)
                {
                    changes["screen"] = "den";
                }

                var stamp = writer.Clock.Now(physical);
                record = existing is null
                    ? HouseholdRecord.Write(key, writer.SchemaVersion, changes, stamp, writer.Id)
                    : existing.Edit(changes, writer.SchemaVersion, stamp, writer.Id);
            }

            operations.Add(record);
            writer.Replica = writer.Replica.With(record);
        }

        tally.Operations += operations.Count;
        CountConflicts(operations, tally);

        var expected = Oracle(operations);

        // Every shuffled fold of the individual operations lands on the same document.
        for (var order = 0; order < OrdersPerRun; order++)
        {
            var shuffled = operations.OrderBy(item => random.Next()).ToList();
            var folded = shuffled.Aggregate(HouseholdDocument.Empty, (document, record) => document.With(record));
            Assert.True(folded.ContentEquals(expected), $"seed {seed}: shuffled fold {order} disagreed with the oracle");

            var grouped = RandomTree(shuffled.Select(record => HouseholdDocument.Empty.With(record)).ToList(), random);
            Assert.True(grouped.ContentEquals(expected), $"seed {seed}: grouping {order} disagreed (associativity)");
            tally.OrdersCompared++;
        }

        // The replicas, merged in any order, hold exactly what was written.
        var replicas = writers.Select(writer => writer.Replica).ToList();
        var fromReplicas = replicas.OrderBy(item => random.Next()).Aggregate(HouseholdDocument.Empty, HouseholdMerge.Merge);
        Assert.True(fromReplicas.ContentEquals(expected), $"seed {seed}: merged replicas disagreed with the oracle");

        // Commutative and idempotent, pairwise.
        foreach (var left in replicas)
        {
            foreach (var right in replicas)
            {
                Assert.True(HouseholdMerge.Merge(left, right).ContentEquals(HouseholdMerge.Merge(right, left)), $"seed {seed}: merge was not commutative");
            }

            Assert.True(HouseholdMerge.Merge(left, left).ContentEquals(left), $"seed {seed}: merge was not idempotent");
        }

        // One full round of reading each other, and every device holds the same household.
        foreach (var writer in writers)
        {
            writer.Replica = replicas.Aggregate(writer.Replica, HouseholdMerge.Merge);
        }

        Assert.All(writers, writer => Assert.True(writer.Replica.ContentEquals(expected), $"seed {seed}: {writer.Id} did not converge"));

        tally.FinalTombstones += expected.Records.Count(record => record.IsTombstone);
        tally.FinalLive += expected.Records.Count(record => !record.IsTombstone);
    }

    /// <summary>
    /// An independent statement of the rule, written without <see cref="HouseholdMerge"/>: per key,
    /// the latest stamp wins; then the larger writer id; then a delete over a write.
    /// </summary>
    private static HouseholdDocument Oracle(IReadOnlyList<HouseholdRecord> operations)
    {
        var winners = operations
            .GroupBy(record => record.Key)
            .Select(group => group
                .OrderByDescending(record => record.Stamp.WallMillis)
                .ThenByDescending(record => record.Stamp.Counter)
                .ThenByDescending(record => record.Writer, StringComparer.Ordinal)
                .ThenByDescending(record => record.IsTombstone)
                .First());

        return winners.Aggregate(HouseholdDocument.Empty, (document, record) => document.With(record));
    }

    private static HouseholdDocument RandomTree(List<HouseholdDocument> leaves, Random random)
    {
        if (leaves.Count == 0)
        {
            return HouseholdDocument.Empty;
        }

        while (leaves.Count > 1)
        {
            var index = random.Next(leaves.Count - 1);
            var joined = random.Next(2) == 0
                ? HouseholdMerge.Merge(leaves[index], leaves[index + 1])
                : HouseholdMerge.Merge(leaves[index + 1], leaves[index]);
            leaves[index] = joined;
            leaves.RemoveAt(index + 1);
        }

        return leaves[0];
    }

    private static void CountConflicts(IReadOnlyList<HouseholdRecord> operations, Tally tally)
    {
        foreach (var group in operations.GroupBy(record => record.Key))
        {
            var records = group.ToList();
            if (records.Select(record => record.Writer).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                tally.ContestedKeys++;
            }

            tally.SameWallAcrossWriters += records
                .GroupBy(record => record.Stamp.WallMillis)
                .Count(sameWall => sameWall.Select(record => record.Writer).Distinct(StringComparer.Ordinal).Count() > 1);

            foreach (var delete in records.Where(record => record.IsTombstone))
            {
                if (records.Any(record => !record.IsTombstone && record.Stamp > delete.Stamp))
                {
                    tally.DeletesBeatenByNewerWrite++;
                }

                if (records.Any(record => !record.IsTombstone && record.Stamp < delete.Stamp))
                {
                    tally.WritesBeatenByDelete++;
                }
            }
        }
    }
}
