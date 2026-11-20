using Foreword.Infrastructure.Calendar;
using Storyworthtelling.Household;
using Storyworthtelling.Maui.Diagnostics;
using Storyworthtelling.People;

namespace Foreword.Android;

/// <summary>
/// The household (<see cref="PeopleFile.FileName"/>): who lives here, their accounts, pickup and calendars, and the family calendar,
/// dinner and bedtime. <b>Foreword owns it</b>: the owner's phone edits it here and every other app (and the partner's phone) reads it.
///
/// <para><see cref="RefreshAsync"/> is one round: read this phone's copy, meet Drive, then — on the owner's phone — bring across the
/// columns this phone had before the household was shared (once, linked by name) and share the calendars Foreword made for people
/// without an account, and finally make this phone's columns the household's people (<see cref="HouseholdColumns.Apply"/>), keeping this
/// phone's order and routing words. Never throws: the standing says what happened.</para>
/// </summary>
public static class ForewordHousehold
{
    private static readonly SemaphoreSlim RoundGate = new(1, 1);

    /// <summary>Whose phone this is, from the shared vault's owner (cached by <see cref="VaultSync"/>).</summary>
    public static HouseholdFileRole Role
    {
        get
        {
            var owner = CredentialStore.HouseholdAccount.Trim();
            var me = CredentialStore.GoogleAccountEmail.Trim();
            if (owner.Length == 0 || me.Length == 0)
            {
                return HouseholdFileRole.Unknown;
            }

            return string.Equals(owner, me, StringComparison.OrdinalIgnoreCase) ? HouseholdFileRole.Owner : HouseholdFileRole.SharedWith;
        }
    }

    public static string Folder => Path.Combine(FileSystem.AppDataDirectory, "household");

    /// <summary>The household file, from the app's services (a page) or the ones a worker was handed.</summary>
    public static HouseholdPeopleFile FileFrom(IServiceProvider services) => services.GetRequiredService<HouseholdPeopleFile>();

    public static HouseholdPeopleFile File => FileFrom(IPlatformApplication.Current!.Services);

    public static async Task<PeopleStanding> RefreshAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var file = FileFrom(services);
        await RoundGate.WaitAsync(cancellationToken);
        try
        {
            await file.ReadAsync(cancellationToken);
            var standing = await file.SyncAsync(cancellationToken);
            if (file.CanEdit && standing.State is PeopleFileState.Synced && await ShareWhatThisPhoneKnowsAsync(file, cancellationToken))
            {
                standing = await file.SyncAsync(cancellationToken);
            }

            Project(file.People);
            MauiLog.Current?.Write("Household", Microsoft.Extensions.Logging.LogLevel.Information,
                $"Household round: {standing.State}, {file.People.People.Count} people", null);
            return standing;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A damaged copy on this phone, or a page asking before the app is up: the round is skipped and said, never fatal.
            MauiLog.Current?.Write("Household", Microsoft.Extensions.Logging.LogLevel.Warning, "Household round didn’t complete", exception);
            return file.Standing;
        }
        finally
        {
            RoundGate.Release();
        }
    }

    /// <summary>Makes this phone's columns the household's people. A phone that hasn't met the household keeps its own columns.</summary>
    public static void Project(HouseholdPeople people)
    {
        var settings = new CalendarSettings();
        settings.Reload();
        if (HouseholdColumns.Apply(settings.Mapping, settings.Work, people))
        {
            settings.SaveMapping();
            settings.SaveWork();
        }
    }

    // The owner's phone, once: its own columns and family calendar join the household, and a calendar Foreword made for somebody
    // without an account becomes that person's calendar, so the other apps can read their days.
    private static async Task<bool> ShareWhatThisPhoneKnowsAsync(HouseholdPeopleFile file, CancellationToken cancellationToken)
    {
        var settings = new CalendarSettings();
        settings.Reload();
        var unshared = HouseholdColumns.Unshared(settings.Mapping);
        var known = HouseholdColumns.Rhythm(settings.Mapping);

        var wrote = false;
        var edit = await file.EditAsync((editor, document, next, writer) =>
        {
            var changed = editor.Import(document, PeopleApps.Foreword, unshared, next, writer).Document;
            changed = editor.FillRhythm(changed, known, next(), writer).Document;
            var people = HouseholdPeople.From(changed);
            foreach (var (id, calendar) in HouseholdColumns.CalendarsToShare(settings.Mapping, people))
            {
                if (people.Find(id) is { } person)
                {
                    changed = editor.Put(changed, person with { CalendarId = calendar }, next(), writer).Document;
                }
            }

            wrote = !changed.ContentEquals(document);
            return new PeopleEdit(changed, null);
        }, cancellationToken);
        return edit.Accepted && wrote;
    }
}
