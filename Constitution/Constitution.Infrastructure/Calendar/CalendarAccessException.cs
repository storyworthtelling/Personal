namespace Constitution.Infrastructure.Calendar;

/// <summary>
/// Google Calendar cannot be reached because the phone has no usable sign-in: nobody has signed in, or the account would not give a token.
/// The calendar adapters turn it into the plain sentence <see cref="Exception.Message"/> (a fixed sentence the head chose, never a token or
/// an address) on every calendar they would have read, and keep this exception's type and inner exception for the trail.
/// </summary>
public sealed class CalendarAccessException : Exception
{
    public CalendarAccessException(string reason)
        : base(reason)
    {
    }

    public CalendarAccessException(string reason, Exception inner)
        : base(reason, inner)
    {
    }
}
