using Android.Content;
using Android.Content.PM;
using Android.Health.Connect;
using Android.Health.Connect.DataTypes;
using Android.OS;
using Android.Runtime;
using Constitution.Domain.Body;
using Constitution.Domain.Health;
using DomainPermissions = Constitution.Domain.Health.HealthPermissions;
using JavaClass = Java.Lang.Class;
using JavaInstant = Java.Time.Instant;
using JavaObject = Java.Lang.Object;

namespace Constitution.Health;

/// <summary>
/// Health Connect read through the platform's own <see cref="HealthConnectManager"/>: no binding package (TF §1.3), no
/// history permission, no write call anywhere. Every type and member below was read out of
/// <c>Microsoft.Android.Ref.36</c>'s <c>Mono.Android.dll</c> metadata before it was written; none is typed from memory.
///
/// <para><b>Nothing here has met a phone.</b> The device spike was skipped (D97). So every platform outcome is written to
/// the Body trail (<see cref="Body.BodyLog"/>): which signals were granted, how many records came back, and the type
/// name of anything that went wrong. Never a value, a package name, or a person's name. When the owner's device test
/// runs, that trail is what says what really happened.</para>
///
/// <para><b>Assumptions the trail will confirm or correct:</b></para>
/// <list type="bullet">
/// <item><c>Context.GetSystemService("healthconnect")</c> returns the manager on Android 14 and higher (the floor is 34).
/// A null answer is reported as unsupported, not thrown.</item>
/// <item><c>ReadRecords</c> with a <c>TimeInstantRangeFilter</c> matches interval records (steps, exercise, sleep) by their
/// start time. A night's sleep begins the evening before the day it belongs to, so the sleep window opens twelve hours
/// early; the reader then keeps only the days it asked for.</item>
/// <item>Paging: <c>ReadRecordsResponse.NextPageToken</c> is negative when there are no more pages. Capped at
/// <see cref="MaxPages"/> so a platform that never says so cannot hold the worker.</item>
/// <item>A day is the record's own local date (its zone offset, else this phone's zone). Sleep belongs to the day it ended.</item>
/// <item>Steps, active minutes and sleep come from several apps and are not de-duplicated by the platform when read as
/// records, so each sample carries its writer's package as an opaque origin and <see cref="DailyReadings"/> takes the
/// fuller source rather than adding two together.</item>
/// <item>Active minutes are the length of exercise sessions (<c>READ_EXERCISE</c>), the closest honest source (see
/// <see cref="DomainPermissions"/>). Sleep is the time in a sleeping stage, or the whole session when it has no stages.</item>
/// <item>Body composition reads body fat only, and asks for nothing else: the chart draws body-fat percent.</item>
/// </list>
/// </summary>
public sealed class HealthConnectSource : IHealthRecordSource
{
    private const int PageSize = 1000;
    private const int MaxPages = 100;
    private static readonly TimeSpan SleepLookBack = TimeSpan.FromHours(12);

    // One thread is plenty: reads are awaited one at a time, and the callbacks only complete a task.
    private static readonly Lazy<Java.Util.Concurrent.IExecutorService> Executor =
        new(Java.Util.Concurrent.Executors.NewSingleThreadExecutor()!);

    private static readonly Dictionary<BodySignal, bool> LastSaid = [];

    private readonly Context context;
    private readonly IBodyLog log;

    public HealthConnectSource(Context context, IBodyLog log)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(log);
        this.context = context;
        this.log = log;
    }

    public bool BackgroundReadsGranted => Holds(DomainPermissions.ReadInBackground);

    public bool IsGranted(BodySignal signal)
    {
        var granted = Holds(HealthSignalPermissions.Primary(signal));

        // Said once and again only when it changes, so a read on every open does not roll the trail over.
        lock (LastSaid)
        {
            if (!LastSaid.TryGetValue(signal, out var said) || said != granted)
            {
                LastSaid[signal] = granted;
                log.Outcome($"health permission {BodySignals.Id(signal)}: {(granted ? "granted" : "not granted")}");
            }
        }

        return granted;
    }

    public async Task<HealthRead> ReadAsync(BodySignal signal, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var id = BodySignals.Id(signal);
        if (context.GetSystemService(Context.HealthconnectService) is not HealthConnectManager manager)
        {
            log.Outcome($"health read {id}: unsupported (no health connect service)");
            return new HealthRead(HealthReadOutcome.Unsupported, []);
        }

        var recordClass = RecordClassOf(signal);
        var window = WindowFor(signal, from, to);
        var samples = new List<HealthSample>();
        long pageToken = -1;
        var records = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var answer = await ReadPageAsync(manager, recordClass, window, pageToken, cancellationToken).ConfigureAwait(false);
            if (answer.Failure is { } failure)
            {
                log.Outcome($"health read {id}: {failure.ToString().ToLowerInvariant()} ({answer.Detail})");
                return new HealthRead(failure, []);
            }

            foreach (var record in answer.Records)
            {
                records++;
                if (SampleOf(signal, record) is { } sample)
                {
                    samples.Add(sample);
                }
            }

            if (answer.NextPageToken < 0)
            {
                break;
            }

            pageToken = answer.NextPageToken;
        }

        log.Outcome($"health read {id}: {records} {(records == 1 ? "record" : "records")}, {samples.Count} usable");
        return HealthRead.Ok(samples);
    }

    // ---- the platform call ----

    private sealed record Page(IReadOnlyList<JavaObject> Records, long NextPageToken, HealthReadOutcome? Failure, string Detail);

    private static async Task<Page> ReadPageAsync(
        HealthConnectManager manager, JavaClass recordClass, TimeInstantRangeFilter window, long pageToken, CancellationToken cancellationToken)
    {
        var receiver = new PageReceiver();
        try
        {
            var builder = new ReadRecordsRequestUsingFilters.Builder(recordClass)
                .SetTimeRangeFilter(window)!
                .SetPageSize(PageSize)!;
            if (pageToken >= 0)
            {
                builder.SetPageToken(pageToken);
            }

            manager.ReadRecords(builder.Build()!, Executor.Value, receiver);
        }
        catch (Java.Lang.SecurityException e)
        {
            return new Page([], -1, HealthReadOutcome.Denied, e.GetType().Name);
        }
        catch (Java.Lang.Exception e)
        {
            return new Page([], -1, HealthReadOutcome.Failed, e.GetType().Name);
        }

        using (cancellationToken.Register(receiver.Cancel))
        {
            return await receiver.Done.ConfigureAwait(false);
        }
    }

    // The platform answers on the executor's thread; this only completes a task and never touches a value.
    private sealed class PageReceiver : JavaObject, IOutcomeReceiver
    {
        private readonly TaskCompletionSource<Page> done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Page> Done => done.Task;

        public void Cancel() => done.TrySetCanceled();

        public void OnResult(JavaObject? result)
        {
            var response = result is null ? null : As<ReadRecordsResponse>(result);
            if (response is null)
            {
                done.TrySetResult(new Page([], -1, HealthReadOutcome.Failed, result?.Class?.SimpleName ?? "NoResponse"));
                return;
            }

            var records = new List<JavaObject>();
            if (response.Records is { } list)
            {
                foreach (var item in list)
                {
                    if (item is JavaObject record)
                    {
                        records.Add(record);
                    }
                }
            }

            done.TrySetResult(new Page(records, response.NextPageToken, null, string.Empty));
        }

        public void OnError(JavaObject? error)
        {
            // The platform hands the Throwable over as a plain Java object, so it is cast by asking Java, not by a C# pattern.
            var kind = error is not null && JavaClass.FromType(typeof(HealthConnectException)).IsInstance(error)
                ? error.JavaCast<HealthConnectException>().ErrorCode
                : (HealthConnectExceptionErrorReason?)null;
            var outcome = kind switch
            {
                HealthConnectExceptionErrorReason.Security => HealthReadOutcome.Denied,
                HealthConnectExceptionErrorReason.UnsupportedOperation => HealthReadOutcome.Unsupported,
                _ => HealthReadOutcome.Failed,
            };

            // The exception's type, and the platform's error reason when it gave one: a name for a kind of failure, no text.
            var detail = (error?.Class?.SimpleName ?? "NoError") + (kind is { } reason ? $" {reason.ToString().ToLowerInvariant()}" : string.Empty);
            done.TrySetResult(new Page([], -1, outcome, detail));
        }
    }

    // ---- what each signal reads ----

    private static JavaClass RecordClassOf(BodySignal signal) => signal switch
    {
        BodySignal.Weight => JavaClass.FromType(typeof(WeightRecord)),
        BodySignal.BodyComposition => JavaClass.FromType(typeof(BodyFatRecord)),
        BodySignal.RestingHeartRate => JavaClass.FromType(typeof(RestingHeartRateRecord)),
        BodySignal.HeartRateVariability => JavaClass.FromType(typeof(HeartRateVariabilityRmssdRecord)),
        BodySignal.Steps => JavaClass.FromType(typeof(StepsRecord)),
        BodySignal.ActiveMinutes => JavaClass.FromType(typeof(ExerciseSessionRecord)),
        BodySignal.Sleep => JavaClass.FromType(typeof(SleepSessionRecord)),
        _ => throw new ArgumentException($"{signal} is not read from Health Connect.", nameof(signal)),
    };

    private static HealthSample? SampleOf(BodySignal signal, JavaObject record)
    {
        switch (signal)
        {
            case BodySignal.Weight when As<WeightRecord>(record) is { Weight: { } mass } weight:
                return Spot(weight, BodyUnits.PoundsFromGrams(mass.InGrams));
            case BodySignal.BodyComposition when As<BodyFatRecord>(record) is { Percentage: { } percentage } fat:
                return Spot(fat, percentage.Value);
            case BodySignal.RestingHeartRate when As<RestingHeartRateRecord>(record) is { } resting:
                return Spot(resting, resting.BeatsPerMinute);
            case BodySignal.HeartRateVariability when As<HeartRateVariabilityRmssdRecord>(record) is { } variability:
                return Spot(variability, variability.HeartRateVariabilityMillis);
            case BodySignal.Steps when As<StepsRecord>(record) is { } steps:
                return Total(steps, steps.StartTime, steps.StartZoneOffset, steps.Count);
            case BodySignal.ActiveMinutes when As<ExerciseSessionRecord>(record) is { StartTime: { } start, EndTime: { } end } exercise:
                return Total(exercise, start, exercise.StartZoneOffset, (end.ToEpochMilli() - start.ToEpochMilli()) / 60000.0);
            case BodySignal.Sleep when As<SleepSessionRecord>(record) is { StartTime: { } begins, EndTime: { } ends } sleep:
                return Total(sleep, ends, sleep.EndZoneOffset, BodyUnits.HoursFromSeconds(AsleepSeconds(sleep, begins, ends)));
            default:
                return null;
        }
    }

    // The platform hands records over as plain Java objects; a managed subclass is used when the runtime made one,
    // and otherwise the object is cast by asking Java whether it is an instance. Null when it is not the type.
    private static T? As<T>(JavaObject value) where T : JavaObject =>
        value as T ?? (JavaClass.FromType(typeof(T)).IsInstance(value) ? value.JavaCast<T>() : null);

    private static HealthSample? Spot(InstantRecord record, double amount) =>
        record.Time is { } time ? new HealthSample(DayOf(time, record.ZoneOffset), amount, OriginOf(record)) : null;

    private static HealthSample? Total(Record record, JavaInstant? instant, Java.Time.ZoneOffset? offset, double amount) =>
        instant is null ? null : new HealthSample(DayOf(instant, offset), amount, OriginOf(record));

    private static double AsleepSeconds(SleepSessionRecord sleep, JavaInstant begins, JavaInstant ends)
    {
        if (sleep.Stages is not { Count: > 0 } stages)
        {
            return (ends.ToEpochMilli() - begins.ToEpochMilli()) / 1000.0;
        }

        double seconds = 0;
        foreach (var stage in stages)
        {
            if (stage.Type is SleepStageType.Sleeping or SleepStageType.SleepingLight or SleepStageType.SleepingDeep or SleepStageType.SleepingRem
                && stage.StartTime is { } stageStart && stage.EndTime is { } stageEnd)
            {
                seconds += (stageEnd.ToEpochMilli() - stageStart.ToEpochMilli()) / 1000.0;
            }
        }

        return seconds;
    }

    // The writing app's package, kept only to tell two writers apart; never stored, shown or logged.
    private static string OriginOf(Record record) => record.Metadata?.DataOrigin?.PackageName ?? string.Empty;

    // ---- time ----

    private static DateOnly DayOf(JavaInstant instant, Java.Time.ZoneOffset? offset)
    {
        var moment = DateTimeOffset.FromUnixTimeMilliseconds(instant.ToEpochMilli());
        var local = offset is null
            ? TimeZoneInfo.ConvertTime(moment, TimeZoneInfo.Local)
            : moment.ToOffset(TimeSpan.FromSeconds(offset.TotalSeconds));
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static TimeInstantRangeFilter WindowFor(BodySignal signal, DateOnly from, DateOnly to)
    {
        var start = LocalMidnight(from);
        if (signal == BodySignal.Sleep)
        {
            start -= SleepLookBack;
        }

        var end = LocalMidnight(to.AddDays(1));
        return new TimeInstantRangeFilter.Builder()
            .SetStartTime(JavaInstant.OfEpochMilli(start.ToUnixTimeMilliseconds())!)!
            .SetEndTime(JavaInstant.OfEpochMilli(end.ToUnixTimeMilliseconds())!)!
            .Build()!;
    }

    private static DateTimeOffset LocalMidnight(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private bool Holds(string permission) => context.CheckSelfPermission(permission) == Permission.Granted;
}
