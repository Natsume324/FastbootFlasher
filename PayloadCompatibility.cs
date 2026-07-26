namespace FastbootFlasher;

public sealed class Partition
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
}

internal static class ImageFile
{
    public static string FormatImageSize(long size)
    {
        if (size < 1024) return $"{size}B";
        if (size < 1024L * 1024) return $"{size / 1024.0:F2}KB";
        if (size < 1024L * 1024 * 1024) return $"{size / (1024.0 * 1024):F2}MB";
        return $"{size / (1024.0 * 1024 * 1024):F2}GB";
    }
}
