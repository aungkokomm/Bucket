namespace Bucket.Models;

/// <summary>How a bucket renders its staged items.</summary>
public enum ViewMode
{
    MiniStrip,
    CompactList,
    DetailedList,
    Gallery
}

/// <summary>The window size mode: a small square puck or the mid working size.</summary>
public enum WindowMode
{
    Compact,
    Mid
}

/// <summary>Accent color a bucket can be tagged with.</summary>
public enum BucketColor
{
    Blue,
    Green,
    Orange,
    Purple,
    Red
}
