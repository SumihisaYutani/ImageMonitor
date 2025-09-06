namespace ImageMonitor.Models;

public interface IDisplayItem
{
    string Id { get; }
    string FilePath { get; }
    string FileName { get; }
    string DisplayName { get; }
    long FileSize { get; }
    string FormattedFileSize { get; }
    DateTime CreatedAt { get; }
    DateTime ModifiedAt { get; }
    DateTime ScanDate { get; }
    string? ThumbnailPath { get; }
    bool IsDeleted { get; }
    bool IsArchived { get; }
}