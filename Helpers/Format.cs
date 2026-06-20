namespace Bucket.Helpers;

/// <summary>
/// Static formatting helpers. Exposed for use from <c>x:Bind</c> function bindings
/// in XAML as well as from code.
/// </summary>
public static class Format
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Human-readable size. Folders (size &lt; 0) render as an em dash.</summary>
    public static string Size(long bytes)
    {
        if (bytes < 0)
            return "—";
        if (bytes == 0)
            return "0 B";

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {Units[unit]}"
            : $"{value:0.#} {Units[unit]}";
    }

    /// <summary>Short local date/time, or empty when unknown.</summary>
    public static string Modified(DateTime when)
        => when == DateTime.MinValue ? string.Empty : when.ToString("g");

    /// <summary>Pluralization helper, e.g. <c>Count(3, "item")</c> → "3 items".</summary>
    public static string Count(int n, string noun)
        => $"{n} {noun}{(n == 1 ? string.Empty : "s")}";
}
