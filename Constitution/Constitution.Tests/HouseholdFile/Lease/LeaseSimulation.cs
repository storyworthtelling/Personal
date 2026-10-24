using Constitution.Domain.Household;
using Constitution.Domain.Household.Lease;
using Storyworthtelling.Household;
using static Constitution.Tests.HouseholdFile.LeaseRig;

namespace Constitution.Tests.HouseholdFile;

/// <summary>
/// Two installs, one Drive file, interleaved at the granularity of one read or one write. Drive v3 has no
/// conditional write, so a write replaces the whole file and a slow phone's write can erase a fast one's;
/// the library's publish cycle (<see cref="HouseholdPublish"/>) is what repairs that, and it is used here
/// exactly as the sync does. A seed fixes everything: when each phone passes, how long each step takes,
/// when a phone goes dark, and how far each phone's clock is off. A failure prints the seed.
///
/// <para><b>What the model assumes, stated.</b> An install that is up passes at least every
/// <see cref="MaxGapMinutes"/> (the heartbeat period), which is why the settle window is twice that: a claim
/// written from a stale view has been merged with its rival before anyone plans on it. Passes are made to
/// pile up where claims collide: on the half hour, at the minute a week opens, and an install coming back
/// from days dark at a week's opening (the owner returning as the partner decides it is silent). A phone
/// goes dark only when it has just left Drive holding everything it has and the other phone is not
/// mid-pass. The double fault excluded is "a write lost to a rival's stale write, then the loser vanishes
/// for longer than the settle window, or an up phone stops passing for days": a conditional write is the
/// only real fix, and Drive v3 has none. With the settle window at zero this model finds double-planned
/// weeks within the committed seeds; at its real length it finds none in 4000.</para>
/// </summary>
internal sealed class LeaseSimulation
{
    public const int SeedCount = 150;

    public const int MaxGapMinutes = 360;

    private const int HorizonMinutes = 6 * 7 * 24 * 60;
    private const int MaxStepMinutes = 3;
    private const int GridMinutes = 30;

    private readonly Random random;
    private readonly SimPhone[] phones;
    private readonly PriorityQueue<int, (int Minute, int Tie)> queue = new();
    private readonly DateTimeOffset start = Edt(10, 19, 6);
    private HouseholdDocument file = HouseholdDocument.Empty;

    private LeaseSimulation(int seed)
    {
        random = new Random(seed);
        phones =
        [
            new SimPhone(new LeasePhone(Sam, RosterRole.Owner), random.Next(-4, 5)),
            new SimPhone(new LeasePhone(Lee, RosterRole.Partner), random.Next(-4, 5)),
        ];
    }

    public Dictionary<long, HashSet<string>> Planners { get; } = [];

    public int PartnerTakes { get; private set; }

    public bool Converged { get; private set; }

    /// <summary>What each pass decided to do, in order, so a failing seed can be read.</summary>
    public List<string> Trace { get; } = [];

    public static LeaseSimulation Run(int seed)
    {
        var simulation = new LeaseSimulation(seed);
        simulation.Play();
        return simulation;
    }

    private DateTimeOffset At(int minute, SimPhone phone) => start.AddMinutes(minute + phone.SkewMinutes);

    private void Play()
    {
        for (var index = 0; index < phones.Length; index++)
        {
            Schedule(index, random.Next(0, 90));
        }

        while (queue.TryDequeue(out var index, out var due))
        {
            if (due.Minute > HorizonMinutes)
            {
                break;
            }

            Step(index, due.Minute);
        }

        Settle();
    }

    private void Schedule(int phone, int minute) => queue.Enqueue(phone, (minute, random.Next()));

    private void Step(int index, int minute)
    {
        var phone = phones[index];
        var local = phone.Phone;
        var now = At(minute, phone);
        switch (phone.Phase)
        {
            case Phase.Start:
                phone.Read = file;
                local.Local = HouseholdMerge.Merge(local.Local, phone.Read);
                phone.Phase = Phase.Compute;
                Schedule(index, minute + random.Next(0, MaxStepMinutes + 1));
                break;

            case Phase.Compute:
                Compute(phone, now);
                var first = HouseholdPublish.FirstWrite(local.Local, phone.Read);
                local.Local = first;
                if (HouseholdSync.NeedsWrite(first, phone.Read))
                {
                    phone.ToWrite = first;
                    phone.Phase = Phase.Write;
                    Schedule(index, minute + random.Next(0, MaxStepMinutes + 1));
                }
                else
                {
                    EndPass(index, minute);
                }

                break;

            case Phase.Write:
                file = phone.ToWrite;
                phone.Writes++;
                phone.Phase = Phase.Reread;
                Schedule(index, minute + random.Next(0, MaxStepMinutes + 1));
                break;

            case Phase.Reread:
                var next = HouseholdPublish.AfterReread(local.Local, file, phone.Writes);
                local.Local = next.Document;
                if (next.Action == PublishAction.WriteAgain)
                {
                    phone.ToWrite = next.Document;
                    phone.Phase = Phase.Write;
                    Schedule(index, minute + random.Next(0, MaxStepMinutes + 1));
                }
                else
                {
                    EndPass(index, minute);
                }

                break;
        }
    }

    private void Compute(SimPhone phone, DateTimeOffset now)
    {
        var local = phone.Phone;
        if (LeaseWriter.NeedsHeartbeat(local.Local, local.Install, now))
        {
            local.Beat(now);
        }

        var decision = local.Ask(now);
        if (decision.Verdict != LeaseVerdict.DoNotPlan)
        {
            var seen = LeaseView.Read(local.Local);
            var detail = string.Join(';', seen.Heartbeats.Values.Select(b => $"{b.Install}@{b.Beat:MM-dd HH:mm}")) + "|" + string.Join(';', seen.Claims.Values.Select(c => $"{c.Holder}@{c.Taken:MM-dd HH:mm}"));
            Trace.Add($"{now:MM-dd HH:mm} {local.Install} {decision.Verdict} week {decision.WeekIndex} [{detail}]");
        }

        switch (decision.Verdict)
        {
            case LeaseVerdict.Take:
            case LeaseVerdict.Release:
                if (decision.Verdict == LeaseVerdict.Take && local.Role == RosterRole.Partner)
                {
                    PartnerTakes++;
                }

                local.Local = LeaseWriter.Apply(local.Local, decision, local.Clock, local.Install, local.Role, now, Zone, Lease);
                break;
            case LeaseVerdict.Plan:
                if (!Planners.TryGetValue(decision.WeekIndex, out var who))
                {
                    who = [];
                    Planners[decision.WeekIndex] = who;
                }

                who.Add(local.Install);
                break;
        }
    }

    private void EndPass(int index, int minute)
    {
        var phone = phones[index];
        phone.Phase = Phase.Start;
        phone.Writes = 0;
        var other = phones[1 - index];
        var leftDriveWhole = HouseholdMerge.Merge(file, phone.Phone.Local).ContentEquals(file);
        var mayGoDark = leftDriveWhole && other.Phase == Phase.Start;
        var dark = mayGoDark && random.Next(100) < 12;
        var gap = dark ? random.Next(6 * 60, 5 * 24 * 60) : random.Next(10, MaxGapMinutes + 1);
        var next = minute + gap;

        // Passes cluster where claims collide. A phone that is up wakes as the week opens when that is within a
        // gap's reach; a phone that was dark comes back as a week opens half the time (the owner returning at the
        // minute the partner is deciding it is silent); and passes often land on the half hour.
        var nextMonday = MinutesToNextMonday(minute);
        if (!dark && nextMonday - minute <= MaxGapMinutes && random.Next(100) < 70)
        {
            next = nextMonday + random.Next(0, 3);
        }
        else if (dark && random.Next(2) == 0)
        {
            next = MinutesToNextMonday(next - 1) + random.Next(0, 3);
        }
        else if (random.Next(2) == 0)
        {
            next = ((next + GridMinutes - 1) / GridMinutes * GridMinutes) + random.Next(0, 3);
        }

        Schedule(index, next);
    }

    private int MinutesToNextMonday(int minute)
    {
        var week = LeaseWeek.IndexOf(start.AddMinutes(minute), Zone);
        return (int)(LeaseWeek.StartOf(week + 1, Zone) - start).TotalMinutes;
    }

    private void Settle()
    {
        // Quiet at last: each phone syncs, twice round, with nobody racing it.
        for (var round = 0; round < 2; round++)
        {
            foreach (var phone in phones)
            {
                phone.Phone.Local = HouseholdMerge.Merge(phone.Phone.Local, file);
                file = HouseholdMerge.Merge(file, phone.Phone.Local);
            }
        }

        Converged = phones[0].Phone.Local.ContentEquals(phones[1].Phone.Local) && phones[0].Phone.Local.ContentEquals(file);
    }

    private enum Phase
    {
        Start,
        Compute,
        Write,
        Reread,
    }

    private sealed class SimPhone(LeasePhone phone, int skewMinutes)
    {
        public LeasePhone Phone { get; } = phone;

        public int SkewMinutes { get; } = skewMinutes;

        public Phase Phase { get; set; } = Phase.Start;

        public HouseholdDocument Read { get; set; } = HouseholdDocument.Empty;

        public HouseholdDocument ToWrite { get; set; } = HouseholdDocument.Empty;

        public int Writes { get; set; }
    }
}
