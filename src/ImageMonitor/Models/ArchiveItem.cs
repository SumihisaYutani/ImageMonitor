namespace ImageMonitor.Models;

public class ArchiveItem : IDisplayItem
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
    
    public string? ThumbnailPath { get; set; }

    // Computed properties
    public string DisplayName => Path.GetFileNameWithoutExtension(FileName);
    public bool IsArchived => true; // ArchiveItemは常にアーカイブ
    public string FormattedFileSize => FormatFileSize(FileSize);
    
    private string? _cachedResolution;
    public string Resolution 
    { 
        get 
        {
            if (_cachedResolution != null) 
                return _cachedResolution;
                
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            if (Images != null && Images.Count > 0)
            {
                var firstImage = Images[0];
                _cachedResolution = firstImage.Resolution;
            }
            else
            {
                _cachedResolution = "Unknown";
            }
            
            sw.Stop();
            if (sw.ElapsedMilliseconds > 10)
            {
                System.Diagnostics.Debug.WriteLine($"[PERF-UI] Slow Resolution property for {DisplayName}: {sw.ElapsedMilliseconds}ms");
            }
            
            return _cachedResolution;
        }
    }
    
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