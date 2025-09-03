namespace ImageMonitor.Models;

public class ArchiveItem
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    
    public string FilePath { get; set; } = string.Empty;
    
    public string FileName { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime ModifiedAt { get; set; }
    
    public DateTime ScanDate { get; set; }
    
    public string ArchiveType { get; set; } = string.Empty;
    
    public int TotalFiles { get; set; }
    
    public int ImageFiles { get; set; }
    
    public decimal ImageRatio { get; set; }
    
    public string? AssociatedApp { get; set; }
    
    public List<ImageInArchive> Images { get; set; } = new();
    
    public bool IsDeleted { get; set; }

    // Computed properties
    public string FormattedFileSize => FormatFileSize(FileSize);
    
    public string ImageRatioPercentage => $"{ImageRatio:P0}";

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
    
    public static string GenerateArchiveId(string filePath)
    {
        return Path.GetFullPath(filePath).GetHashCode().ToString("x8");
    }
}

public class ImageInArchive
{
    public string InternalPath { get; set; } = string.Empty;
    
    public string FileName { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    public int Width { get; set; }
    
    public int Height { get; set; }
    
    public string? ThumbnailData { get; set; }

    // Computed properties
    public string Resolution => $"{Width}x{Height}";
    
    public string FormattedFileSize => FormatFileSize(FileSize);

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
}