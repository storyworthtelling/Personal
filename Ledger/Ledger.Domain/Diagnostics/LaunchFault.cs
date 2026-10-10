namespace Index.Domain.Diagnostics;

/// <summary>
/// The text of a fault that stopped or interrupted the app, written so the owner can read it on the
/// next launch. The detail is the whole exception — type, message, stack and every inner exception —
/// because a message alone cannot say where the fault came from.
/// </summary>
public static class LaunchFault
{
    public static string Describe(DateTimeOffset when, string where, Exception exception) =>
        Describe(when, where, exception.ToString());

    public static string Describe(DateTimeOffset when, string where, string detail) =>
        $"{when:yyyy-MM-dd HH:mm:ss zzz}\nWhere: {where}\n\n{detail}";
}
