using System.Text;

namespace Bucket.Services;

/// <summary>
/// Best-effort crash/diagnostic logger. Writes unhandled exceptions to
/// <c>crash.log</c> next to the app's other state so users can share it when
/// reporting a problem. Never throws.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    /// <summary>Full path to the log file (shown to users in error reports).</summary>
    public static string FilePath => Storage.PathTo("crash.log");

    public static void Write(string source, Exception? ex, string? note = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
              .Append(source);
            if (!string.IsNullOrEmpty(note))
                sb.Append(" — ").Append(note);
            sb.AppendLine();
            sb.AppendLine(ex?.ToString() ?? "(no exception object)");
            sb.AppendLine(new string('-', 60));

            lock (Gate)
                File.AppendAllText(FilePath, sb.ToString());
        }
        catch { /* logging must never crash the app */ }
    }
}
