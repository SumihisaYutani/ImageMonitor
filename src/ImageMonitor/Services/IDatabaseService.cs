using ImageMonitor.Models;

namespace ImageMonitor.Services;

public interface IDatabaseService : IDisposable
{
    Task InitializeAsync();
    
    // ImageItem operations
    Task<IEnumerable<ImageItem>> GetAllImageItemsAsync();
    
    Task<IEnumerable<ImageItem>> GetImageItemsAsync(int skip = 0, int take = 100);
    
    Task<ImageItem?> GetImageItemByIdAsync(string id);
    
    Task<ImageItem?> GetImageItemByPathAsync(string filePath);
    
    Task<bool> InsertImageItemAsync(ImageItem imageItem);
    
    Task<bool> UpdateImageItemAsync(ImageItem imageItem);
    
    Task<bool> DeleteImageItemAsync(string id);
    
    Task<int> BulkInsertImageItemsAsync(IEnumerable<ImageItem> imageItems);
    
    Task<int> StreamInsertImageItemsAsync(IAsyncEnumerable<ImageItem> imageItems, CancellationToken cancellationToken = default);
    
    Task<IEnumerable<ImageItem>> SearchImageItemsAsync(SearchFilter filter);
    
    Task<long> GetImageItemCountAsync();
    
    // ArchiveItem operations
    Task<IEnumerable<ArchiveItem>> GetAllArchiveItemsAsync();
    
    Task<ArchiveItem?> GetArchiveItemByIdAsync(string id);
    
    Task<ArchiveItem?> GetArchiveItemByPathAsync(string filePath);
    
    Task<bool> InsertArchiveItemAsync(ArchiveItem archiveItem);
    
    Task<bool> UpdateArchiveItemAsync(ArchiveItem archiveItem);
    
    Task<bool> DeleteArchiveItemAsync(string id);
    
    Task<long> GetArchiveItemCountAsync();
    Task<IEnumerable<ArchiveItem>> GetArchiveItemsAsync(int offset, int limit);
    Task<IEnumerable<ImageItem>> GetNonArchivedImageItemsAsync(int offset, int limit);
    Task<bool> UpsertArchiveItemAsync(ArchiveItem archiveItem);
    
    Task<IEnumerable<ArchiveItem>> SearchArchiveItemsAsync(SearchFilter filter);
    
    // ScanHistory operations
    Task<bool> InsertScanHistoryAsync(ScanHistory scanHistory);
    
    Task<ScanHistory?> GetLastScanHistoryAsync(string directoryPath);
    
    Task<IEnumerable<ScanHistory>> GetScanHistoryAsync(string directoryPath, int limit = 10);
    
    Task<IEnumerable<string>> GetScannedDirectoriesAsync();
    
    Task<IEnumerable<string>> GetImageDirectoriesAsync();
    
    Task<IEnumerable<string>> GetArchiveDirectoriesAsync();
    
    Task<int> CleanupItemsByDirectoryAsync(string directoryPath);

    // Maintenance operations
    Task<int> CleanupDeletedItemsAsync();
    
    Task<int> CleanupSingleImageItemsAsync();
    
    Task<int> CleanupOrphanedThumbnailsAsync();
    
    Task OptimizeDatabaseAsync();
    
    Task<long> GetDatabaseSizeAsync();
}