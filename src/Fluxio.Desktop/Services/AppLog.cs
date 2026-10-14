using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Fluxio.Desktop.Services;

/// <summary>
/// Minimal local log (%LOCALAPPDATA%\Fluxio\logs, kept 7 days). Fluxio never logs tokens,
/// QR secrets, file contents or full paths; the engine only logs events and error kinds.
/// Nothing is sent anywhere (no telemetry).
/// </summary>
public static class AppLog
{
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 10_000);
    private static readonly Thread Writer;

    static AppLog()
    {
        Writer = new Thread(WriteLoop) { IsBackground = true, Name = "Fluxio log writer" };
        Writer.Start();
        try
        {
            Directory.CreateDirectory(AppPaths.LogFolder);
            foreach (var old in Directory.EnumerateFiles(AppPaths.LogFolder, "fluxio-*.log"))
            {
                if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-7))
                {
                    File.Delete(old);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static void Info(string message) => Write("INF", message, null);

    public static void Warn(string message) => Write("WRN", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERR", message, exception);

    public static void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(message);
        if (exception is not null)
        {
            // Type and message only: stack traces can contain user paths.
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        }

        Queue.TryAdd(line.ToString());
    }

    public static void Flush()
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (Queue.Count > 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }
    }

    private static void WriteLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                var file = Path.Combine(AppPaths.LogFolder, $"fluxio-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>Bridges Microsoft.Extensions.Logging (used by the engine) to <see cref="AppLog"/>.</summary>
public sealed class AppLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new AppLogger(categoryName);

    public void Dispose()
    {
    }

    private sealed class AppLogger(string category) : ILogger
    {
        private readonly string _category = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= (_category.StartsWith("Fluxio", StringComparison.Ordinal) || !category.StartsWith("Microsoft", StringComparison.Ordinal)
                ? LogLevel.Information
                : LogLevel.Warning);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var level = logLevel switch
            {
                LogLevel.Warning => "WRN",
                LogLevel.Error or LogLevel.Critical => "ERR",
                _ => "INF",
            };
            AppLog.Write(level, $"[{_category}] {formatter(state, exception)}", exception);
        }
    }
}
