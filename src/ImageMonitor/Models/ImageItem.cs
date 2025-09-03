namespace ImageMonitor.Models;

public class ImageItem
{
    [BsonId]
    public string Id { get; set; } = string.Empty;
    
    [BsonField("filePath")]
    public string FilePath { get; set; } = string.Empty;
    
    public string FileName { get; set; } = string.Empty;
    
    public long FileSize { get; set; }
    
    public int Width { get; set; }
    
    public int Height { get; set; }
    
    public string? ThumbnailPath { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime ModifiedAt { get; set; }
    
    public DateTime ScanDate { get; set; }
    
    public bool IsDeleted { get; set; }
    
    // Image-specific properties
    public bool IsArchived { get; set; }
    
    public string? ArchivePath { get; set; }
    
    public string? InternalPath { get; set; }
    
    public decimal? ArchiveImageRatio { get; set; }
    
    public string? ImageFormat { get; set; }
    
    public int? ColorDepth { get; set; }
    
    public bool HasExifData { get; set; }
    
    public DateTime? DateTaken { get; set; }

    // Computed properties
    public string Resolution => $"{Width}x{Height}";
    
    public string FormattedFileSize => FormatFileSize(FileSize);
    
    public string ArchiveInfo => IsArchived && !string.IsNullOrEmpty(ArchivePath) 
        ? $"Archive: {Path.GetFileName(ArchivePath)}" 
        : string.Empty;

    public string DisplayName => IsArchived && !string.IsNullOrEmpty(ArchivePath) 
        ? Path.GetFileNameWithoutExtension(ArchivePath)
        : Path.GetFileNameWithoutExtension(FileName);

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
    
    public static string GenerateFileId(string filePath)
    {
        return Path.GetFullPath(filePath).GetHashCode().ToString("x8");
    }
}