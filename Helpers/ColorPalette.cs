using Bucket.Models;
using Microsoft.UI;
using Windows.UI;

namespace Bucket.Helpers;

/// <summary>Maps <see cref="BucketColor"/> values to accent colors.</summary>
public static class ColorPalette
{
    /// <summary>Strong accent color — used for the title text, + icon, and accent line.</summary>
    public static Color ToColor(BucketColor color) => color switch
    {
        BucketColor.Blue   => Color.FromArgb(255, 0x2F, 0x6F, 0xED),
        BucketColor.Green  => Color.FromArgb(255, 0x1F, 0xA8, 0x55),
        BucketColor.Orange => Color.FromArgb(255, 0xE8, 0x7A, 0x1B),
        BucketColor.Purple => Color.FromArgb(255, 0x8B, 0x3F, 0xD6),
        BucketColor.Red    => Color.FromArgb(255, 0xD6, 0x3C, 0x3C),
        _ => Colors.SteelBlue
    };

    /// <summary>Light tint of the color — used as the whole-window background.</summary>
    public static Color ToBackground(BucketColor color) => color switch
    {
        BucketColor.Blue   => Color.FromArgb(255, 0xDD, 0xEB, 0xFF),
        BucketColor.Green  => Color.FromArgb(255, 0xDD, 0xF3, 0xE3),
        BucketColor.Orange => Color.FromArgb(255, 0xFC, 0xEC, 0xD7),
        BucketColor.Purple => Color.FromArgb(255, 0xEC, 0xE2, 0xFB),
        BucketColor.Red    => Color.FromArgb(255, 0xFB, 0xE4, 0xE3),
        _ => Color.FromArgb(255, 0xDD, 0xEB, 0xFF)
    };

    /// <summary>A medium tint of the color, for the title bar gradient's deep end.</summary>
    public static Color ToHeader(BucketColor color) => Lerp(ToColor(color), Colors.White, 0.48);

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    public static string DisplayName(BucketColor color) => color.ToString();
}
