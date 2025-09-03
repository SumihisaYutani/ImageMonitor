namespace ImageMonitor.Models;

public class AppSettings
{
    public List<string> ScanDirectories { get; set; } = new();
    
    public int ThumbnailSize { get; set; } = 128;
    
    public string? DefaultArchiveViewer { get; set; }
    
    public decimal ImageRatioThreshold { get; set; } = 0.5m;
    
    public AppTheme Theme { get; set; } = AppTheme.Light;
    
    public List<string> SupportedImageFormats { get; set; } = new()
    {
        ".jpg", ".jpeg", ".png"
    };
    
    public List<string> SupportedArchiveFormats { get; set; } = new()
    {
        ".zip", ".rar"
    };
    
    public bool GenerateArchiveThumbnails { get; set; } = true;
    
    public int MaxArchiveEntries { get; set; } = 10000;
    
    // Window state
    public double WindowWidth { get; set; } = 1200;
    
    public double WindowHeight { get; set; } = 800;
    
    public bool WindowMaximized { get; set; } = false;
    
    // Performance settings
    public int MaxConcurrentScans { get; set; } = 4;
    
    public int ScanTimeout { get; set; } = 300;
    
    // Cache settings
    public int ThumbnailCacheSize { get; set; } = 1000;
    
    public int CacheCleanupInterval { get; set; } = 24;
    
    // Thumbnail settings
    public bool EnableThumbnailGeneration { get; set; } = true;
    
    // Logging settings
    public bool EnableLogging { get; set; } = true;
    
    public string LogLevel { get; set; } = "Information";
    
    public int MaxLogFiles { get; set; } = 7;
}

public enum AppTheme
{
    Light,
    Dark
}