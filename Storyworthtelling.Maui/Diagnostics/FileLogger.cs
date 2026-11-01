using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Storyworthtelling.Diagnostics;

namespace Storyworthtelling.Maui.Diagnostics;

/// <summary>
/// What one MAUI head's file log is.
/// </summary>
/// <param name="Directory">Where the live file and the log loop's outbox live (the app data directory).</param>
/// <param name="FileName">The file's name, which is also its name in the outbox and the upload: refresh.log, pipeline.log, delivery.log.</param>
/// <param name="MaxBytes">Past this the live file is sealed into the outbox and a new one begun.</param>
/// <param name="IsOwnCategory">The head's own categories, logged from Information; everything else (EF Core's per-command SQL, MAUI) from Warning.</param>
/// <param name="QuietCategories">
/// Categories (a namespace or a type's full name) whose lines keep only the message template, the level, the
/// exception's type and its stack — never a formatted argument or an exception message. Foreword's prayer
/// pipeline is the reason (A2): what the owner writes on the prayer page must never reach a log.
/// </param>
/// <param name="MaxMessageChars">A message longer than this is cut, saying how long it was. Folio's error bodies can quote the manuscript.</param>
public sealed record FileLoggerOptions(
    string Directory,
    string FileName,
    long MaxBytes,
    Func<string, bool> IsOwnCategory,
    IReadOnlyList<string>? QuietCategories = null,
    int? MaxMessageChars = null);

/// <summary>
/// The one file logger the MAUI heads share (LOG-LOOP-PLAN.md §4.4), replacing Index's and Folio's
/// <c>PipelineFileLogger</c> and Foreword's <c>DeliveryFileLogger</c>. Each head keeps its file name; a full file
/// is <b>sealed</b> into the log loop's <see cref="LogOutbox"/> rather than rolled over a backup, so a busy week
/// loses nothing before the Saturday upload; and Foreword, whose log grew forever, now has a cap. A logging
/// failure never reaches the caller: the log is diagnostics, and it must not break what it describes.
/// </summary>
public sealed class FileLogger : ILoggerProvider
{
    private const string OriginalFormat = "{OriginalFormat}";

    private readonly FileLoggerOptions options;
    private readonly Func<DateTimeOffset> clock;
    private readonly Lock gate = new();
    private bool backupChecked;

    public FileLogger(FileLoggerOptions options, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        this.clock = clock ?? (() => DateTimeOffset.Now);
        LivePath = Path.Combine(options.Directory, options.FileName);
        Outbox = new LogOutbox(options.Directory);
    }

    /// <summary>The live file, which the upload seals with everything else before it reads the outbox.</summary>
    public string LivePath { get; }

    public LogOutbox Outbox { get; }

    public string FileName => options.FileName;

    public bool HasEntries
    {
        get
        {
            try
            {
                return (File.Exists(LivePath) && new FileInfo(LivePath).Length > 0) || Outbox.SegmentsFor(options.FileName).Count > 0;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        var quiet = options.QuietCategories?.Any(prefix => IsUnder(categoryName, prefix)) == true;
        var minimum = options.IsOwnCategory(categoryName) ? LogLevel.Information : LogLevel.Warning;
        return new CategoryLogger(this, categoryName, minimum, quiet);
    }

    /// <summary>Writes one line outside the logging pipeline: the head's unhandled-exception handler.</summary>
    public void Write(string category, LogLevel level, string message, Exception? exception) =>
        Append(Line(category, level, message, exception, quiet: false));

    /// <summary>Everything since the last confirmed upload, oldest first: the sealed segments, then the live file.</summary>
    public string ReadAll()
    {
        try
        {
            lock (gate)
            {
                SealOldBackup();
                var builder = new StringBuilder(Outbox.ReadSealed(options.FileName));
                if (File.Exists(LivePath))
                {
                    builder.Append(File.ReadAllText(LivePath));
                }
                return builder.ToString();
            }
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>Empties the live file and this log's sealed segments; other logs' segments are left alone.</summary>
    public void Clear()
    {
        try
        {
            lock (gate)
            {
                File.WriteAllText(LivePath, string.Empty);
                Outbox.ClearLog(options.FileName);
            }
        }
        catch (IOException)
        {
            // Nothing to clear is not a failure worth surfacing; the next write starts the file again.
        }
    }

    public void Dispose()
    {
    }

    private string Line(string category, LogLevel level, string message, Exception? exception, bool quiet)
    {
        var prefix = level switch
        {
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => "INFO",
        };
        var builder = new StringBuilder();
        builder.Append('[').Append(clock().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append("] [")
            .Append(prefix).Append("] ").Append(ShortCategory(category)).Append(": ").Append(Cut(message));
        if (exception is not null)
        {
            builder.Append("\n       ").Append(quiet ? TypeChain(exception) : Chain(exception));
            if (exception.StackTrace is { } stack)
            {
                builder.Append("\n       ").Append(stack.Trim());
            }
        }
        return builder.Append('\n').ToString();
    }

    private string Cut(string message)
    {
        if (options.MaxMessageChars is not { } cap || message.Length <= cap)
        {
            return message;
        }
        return string.Create(CultureInfo.InvariantCulture, $"{message[..cap]}… [cut: {message.Length} characters]");
    }

    private void Append(string line)
    {
        try
        {
            lock (gate)
            {
                SealOldBackup();
                if (File.Exists(LivePath) && new FileInfo(LivePath).Length + Encoding.UTF8.GetByteCount(line) > options.MaxBytes)
                {
                    Outbox.Seal(LivePath, options.FileName);
                }
                File.AppendAllText(LivePath, line);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // The disk is full or the path is wrong; a missing log line must never break the work it describes.
        }
    }

    // The roll this replaced kept one ".1" backup — the older half of the record. Seal it rather than leave it.
    private void SealOldBackup()
    {
        if (backupChecked)
        {
            return;
        }
        backupChecked = true;
        var backup = LivePath + ".1";
        if (File.Exists(backup))
        {
            Outbox.Seal(backup, options.FileName);
        }
    }

    private static bool IsUnder(string category, string prefix) =>
        category == prefix
        || (category.StartsWith(prefix, StringComparison.Ordinal) && category.Length > prefix.Length && category[prefix.Length] is '.' or '+');

    private static string Chain(Exception exception)
    {
        var parts = new List<string>();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            parts.Add($"{e.GetType().Name}: {e.Message}");
        }
        return string.Join(" ← ", parts);
    }

    private static string TypeChain(Exception exception)
    {
        var parts = new List<string>();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            parts.Add(e.GetType().Name);
        }
        return string.Join(" ← ", parts);
    }

    private static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        return lastDot >= 0 && lastDot < category.Length - 1 ? category[(lastDot + 1)..] : category;
    }

    private sealed class CategoryLogger(FileLogger owner, string category, LogLevel minimum, bool quiet) : ILogger
    {
        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            string message;
            try
            {
                message = quiet ? Template(state) : formatter(state, exception);
            }
            catch (FormatException)
            {
                message = "(the message could not be formatted)";
            }
            owner.Append(owner.Line(category, logLevel, message, exception, quiet));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // A quiet line is the template the caller wrote, never what was put into it. A message with no holes may be
        // an interpolated string that already carries the words, so it is kept only as a short hash: enough for the
        // reader to group repeats, nothing it could read back.
        private static string Template<TState>(TState state)
        {
            var template = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.FirstOrDefault(pair => pair.Key == OriginalFormat).Value as string
                : null;
            if (template is null || template == "[null]")
            {
                return "(no template)";
            }
            if (template.Contains('{', StringComparison.Ordinal) && template.Contains('}', StringComparison.Ordinal))
            {
                return template;
            }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(template)))[..8].ToLowerInvariant();
            return $"(no template · #{hash})";
        }
    }
}
