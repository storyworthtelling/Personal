using Android.Content;
using Android.OS;
using Android.Speech;
using Watch.Domain.Dash;
using RemoteInput = Android.App.RemoteInput;

namespace Dash.Wear;

/// <summary>
/// The system's own input for a reply (W-5.1.2, A10): its dictation (<see cref="RecognizerIntent"/>), and its input
/// screen, where the keyboard is (the wearable input action androidx.wear.input's <c>RemoteInputIntentHelper</c> builds;
/// its action and extra are written out here, as the platform reads them, so Dash takes no package for two strings).
/// Each is offered only when the watch has an activity for it; neither keeps or logs a word.
/// </summary>
internal static class SystemInput
{
    private const string KeyboardAction = "android.support.wearable.input.action.REMOTE_INPUT"; // not copy: the wearable input screen's action

    private const string InputsExtra = "android.support.wearable.input.extra.REMOTE_INPUTS"; // not copy: the wearable input screen's extra

    private const string ResultKey = "reply"; // not copy: the key the typed reply comes back under

    /// <summary>The system's dictation, prompted with who the reply is for; null when the watch has none.</summary>
    public static Intent? Dictation(Context context, string prompt)
    {
        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech)
            .PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm)
            .PutExtra(RecognizerIntent.ExtraPrompt, prompt)
            .PutExtra(RecognizerIntent.ExtraMaxResults, 1);
        return intent.ResolveActivity(context.PackageManager!) is null ? null : intent;
    }

    /// <summary>The system's input screen with its keyboard; null when the watch has none.</summary>
    public static Intent? Keyboard(Context context, string label)
    {
        var input = new RemoteInput.Builder(ResultKey).SetLabel(label)!.Build()!;
        var intent = new Intent(KeyboardAction).PutExtra(InputsExtra, new IParcelable[] { input });
        return intent.ResolveActivity(context.PackageManager!) is null ? null : intent;
    }

    /// <summary>The words dictated.</summary>
    public static string? Dictated(Intent? data) => data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults)?.FirstOrDefault();

    /// <summary>The words typed.</summary>
    public static string? Typed(Intent? data) =>
        data is null ? null : RemoteInput.GetResultsFromIntent(data)?.GetCharSequence(ResultKey)?.ToString();
}

/// <summary>The extras Dash's own screens hand each other, and keep across a restart of the screen.</summary>
internal static class DashExtras
{
    public const string Key = "key"; // not copy: an extra's name
    public const string Title = "title"; // not copy: an extra's name
    public const string Group = "group"; // not copy: an extra's name
    public const string Words = "words"; // not copy: an extra's name
    public const string Send = "send"; // not copy: an extra's name
    public const string Heard = "heard"; // not copy: an extra's name
    public const string At = "at"; // not copy: an extra's name
    public const string Act = "act"; // not copy: an extra's name
    public const string Left = "left"; // not copy: an extra's name
    public const string Query = "query"; // not copy: an extra's name
    public const string Call = "call"; // not copy: an extra's name
    public const string Length = "length"; // not copy: an extra's name
    public const string Ended = "ended"; // not copy: an extra's name
}

/// <summary>
/// The replies this wrist sent and Post answered as sent, per conversation, shown until Post's own copy of each comes
/// (<see cref="Conversation.StillOwed"/>). Kept for the process only: once Post has them they are the
/// phone's to show, and a watch that restarts asks the phone again.
/// </summary>
internal static class SentHereBook
{
    private static readonly Dictionary<string, List<SentHere>> Book = new(StringComparer.Ordinal);

    private static readonly object Gate = new();

    public static IReadOnlyList<SentHere> For(string key)
    {
        lock (Gate)
        {
            return Book.TryGetValue(key, out var sent) ? [.. sent] : [];
        }
    }

    public static void Add(string key, SentHere sent)
    {
        lock (Gate)
        {
            if (!Book.TryGetValue(key, out var list))
            {
                Book[key] = list = [];
            }
            list.Add(sent);
        }
    }

    /// <summary>Keeps only those still owed, once an answer has shown which Post now has.</summary>
    public static void Keep(string key, IReadOnlyList<SentHere> owed)
    {
        lock (Gate)
        {
            if (owed.Count == 0)
            {
                Book.Remove(key);
            }
            else
            {
                Book[key] = [.. owed];
            }
        }
    }
}
