using Constitution.Domain.Meals;
using Storyworthtelling.Household;
using static Constitution.Tests.MealAnswers.MealAnswerData;

namespace Constitution.Tests.MealAnswers;

/// <summary>
/// Two installs, each reading its own adult's tablet, each reading a line one to three times at seeded moments from twenty hours before
/// the deadline to ten hours after it, each with a clock a few minutes off. The planner's install is then handed
/// every state either file was ever in, in a seeded order with repeats, and must reach the same verdict whatever the order. A
/// failure prints the seed.
/// </summary>
internal sealed class MealAnswerSimulation
{
    public const int SeedCount = 150;

    private static readonly string[] Items =
    [
        "dinner-sun-yes", "dinner-mon-yes", "dinner-tue-yes", "dinner-wed-yes", "dinner-thu-yes", "dinner-fri-yes", "dinner-sat-yes",
        "weekend-breakfast-yes", "weekend-lunch-yes", "box-sandwich-yes",
    ];

    private static readonly string[] Reasons = ["Robin has swim", "we had it Monday", "no fish", "Juniper is away"];

    private readonly Random random;
    private readonly List<HouseholdDocument> states = [];

    private MealAnswerSimulation(int seed)
    {
        Seed = seed;
        random = new Random(seed);
    }

    public int Seed { get; }

    /// <summary>Both files joined: what the planner holds once everything has arrived.</summary>
    public HouseholdDocument Truth { get; private set; } = HouseholdDocument.Empty;

    /// <summary>Every reading either tablet gave, as given (before the writer refused any).</summary>
    public List<MealAnswer> Readings { get; } = [];

    public IReadOnlyList<HouseholdDocument> States => states;

    public static MealAnswerSimulation Run(int seed)
    {
        var simulation = new MealAnswerSimulation(seed);
        simulation.Play();
        return simulation;
    }

    /// <summary>The planner's file after being handed every state in a seeded order, repeats included.</summary>
    public HouseholdDocument Arrive(int arrivalSeed)
    {
        var arrival = new Random(Seed * 31 + arrivalSeed);
        var planner = HouseholdDocument.Empty;
        var order = states.Concat(states.Where(_ => arrival.Next(3) == 0)).OrderBy(_ => arrival.Next()).ToList();
        foreach (var state in order)
        {
            planner = HouseholdMerge.Merge(planner, state);
        }

        return HouseholdMerge.Merge(planner, Truth);
    }

    private void Play()
    {
        var phones = new[]
        {
            new Phone(Sam, "install-sam", random.Next(-6, 7)),
            new Phone(Lee, "install-lee", random.Next(-6, 7)),
        };

        foreach (var phone in phones)
        {
            foreach (var item in Items)
            {
                for (var reading = random.Next(1, 4); reading > 0; reading--)
                {
                    var at = Deadline.AddMinutes(random.Next(-20 * 60, 10 * 60));
                    Readings.Add(Make(phone.Person, item, at));
                    phone.Pending.Add(Readings[^1]);
                }
            }
        }

        // The tablets are read in the order the moments fall, whichever phone read them.
        foreach (var reading in Readings.OrderBy(reading => reading.ReadAt))
        {
            var phone = phones.Single(candidate => candidate.Person == reading.Person);
            var written = MealAnswerRecords.Write(phone.File, phone.Clock, phone.Install, reading, Deadline, reading.ReadAt.AddMinutes(phone.SkewMinutes));
            phone.File = written.Document;
            states.Add(phone.File);

            // Now and then a phone syncs: it takes in the other's file as it stands.
            if (random.Next(4) == 0)
            {
                var other = phones.Single(candidate => candidate != phone);
                phone.File = HouseholdMerge.Merge(phone.File, other.File);
                states.Add(phone.File);
            }
        }

        Truth = phones.Aggregate(HouseholdDocument.Empty, (joined, phone) => HouseholdMerge.Merge(joined, phone.File));
    }

    private MealAnswer Make(Constitution.Domain.Household.PersonId person, string item, DateTimeOffset at)
    {
        var roll = random.Next(100);
        return roll < 50
            ? Silent(person, item, at)
            : roll < 75
                ? Tick(person, item, at)
                : Deny(person, item, random.Next(3) == 0 ? null : Reasons[random.Next(Reasons.Length)], at);
    }

    private sealed class Phone(Constitution.Domain.Household.PersonId person, string install, int skewMinutes)
    {
        public Constitution.Domain.Household.PersonId Person { get; } = person;

        public string Install { get; } = install;

        public int SkewMinutes { get; } = skewMinutes;

        public HybridLogicalClock Clock { get; } = new();

        public HouseholdDocument File { get; set; } = HouseholdDocument.Empty;

        public List<MealAnswer> Pending { get; } = [];
    }
}
