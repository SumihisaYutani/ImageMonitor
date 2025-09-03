using ImageMonitor.Models;

namespace ImageMonitor.Services;

public interface IDatabaseService : IDisposable
{
    Task InitializeAsync();
    
    // ImageItem operations
    Task<IEnumerable<ImageItem>> GetAllImageItemsAsync();
    
    Task<ImageItem?> GetImageItemByIdAsync(string id);
    
    Task<ImageItem?> GetImageItemByPathAsync(string filePath);
    
    Task<bool> InsertImageItemAsync(ImageItem imageItem);
    
    Task<bool> UpdateImageItemAsync(ImageItem imageItem);
    
    Task<bool> DeleteImageItemAsync(string id);
    
    Task<int> BulkInsertImageItemsAsync(IEnumerable<ImageItem> imageItems);
    
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
    
    // Maintenance operations
    Task<int> CleanupDeletedItemsAsync();
    
    Task<int> CleanupOrphanedThumbnailsAsync();
    
    Task OptimizeDatabaseAsync();
    
    Task<long> GetDatabaseSizeAsync();
}