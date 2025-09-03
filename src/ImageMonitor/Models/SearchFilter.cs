namespace ImageMonitor.Models;

public class SearchFilter
{
    public string Query { get; set; } = string.Empty;
    
    public long? MinFileSize { get; set; }
    
    public long? MaxFileSize { get; set; }
    
    public DateTime? DateFrom { get; set; }
    
    public DateTime? DateTo { get; set; }
    
    public int? MinWidth { get; set; }
    
    public int? MaxWidth { get; set; }
    
    public int? MinHeight { get; set; }
    
    public int? MaxHeight { get; set; }
    
    // Image-specific filters
    public bool IncludeArchives { get; set; } = true;
    
    public decimal ImageRatioThreshold { get; set; } = 0.5m;
    
    public List<string> ImageFormats { get; set; } = new();
    
    public bool? HasExifData { get; set; }
    
    public DateTime? DateTakenFrom { get; set; }
    
    public DateTime? DateTakenTo { get; set; }
    
    // Sorting and paging
    public SortBy SortBy { get; set; } = SortBy.FileName;
    
    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
    
    public int PageSize { get; set; } = 50;
    
    public int PageNumber { get; set; } = 1;
}

public enum SortBy
{
    FileName,
    FileSize,
    CreatedAt,
    ModifiedAt,
    DateTaken,
    Width,
    Height
}

public enum SortDirection
{
    Ascending,
    Descending
}