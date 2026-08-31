using global::Google.Apis.Calendar.v3;
using global::Google.Apis.Tasks.v1;

namespace DailyLoader.Infrastructure.Google;

// Builds authenticated Google API services from the stored OAuth refresh token.
public interface IGoogleClientFactory
{
    CalendarService CreateCalendarService();
    TasksService CreateTasksService();
}
