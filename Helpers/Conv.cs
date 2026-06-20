using Microsoft.UI.Xaml;

namespace Bucket.Helpers;

/// <summary>Static converters for <c>x:Bind</c> function bindings.</summary>
public static class Conv
{
    public static Visibility Vis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisNot(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VisAnd(bool a, bool b) => a && b ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Fallback Segoe Fluent glyph (folder / generic file) shown behind a thumbnail.</summary>
    public static string Glyph(bool isFolder) => isFolder ? "" : "";
}
