using ImageMonitor.Models;

namespace ImageMonitor.Services;

public class DatabaseService : IDatabaseService
{
    private readonly ILiteDatabase _database;
    private readonly ILiteCollection<ImageItem> _imageItems;
    private readonly ILiteCollection<ArchiveItem> _archiveItems;
    private readonly ILogger<DatabaseService> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDataDir = Path.Combine(appDataPath, "ImageMonitor");
        
        if (!Directory.Exists(appDataDir))
        {
            Directory.CreateDirectory(appDataDir);
        }
        
        var databasePath = Path.Combine(appDataDir, "imageMonitor.db");
        _logger.LogDebug("Database path: {DatabasePath}", databasePath);
        
        _database = new LiteDatabase(databasePath);
        _imageItems = _database.GetCollection<ImageItem>("ImageItems");
        _archiveItems = _database.GetCollection<ArchiveItem>("ArchiveItems");
    }

    public async Task InitializeAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            _logger.LogInformation("Initializing database...");
            
            // Create indexes for ImageItems
            _imageItems.EnsureIndex(x => x.FilePath, true); // Unique index
            _imageItems.EnsureIndex(x => x.FileName);
            _imageItems.EnsureIndex(x => x.FileSize);
            _imageItems.EnsureIndex(x => x.CreatedAt);
            _imageItems.EnsureIndex(x => x.ModifiedAt);
            _imageItems.EnsureIndex(x => x.ScanDate);
            _imageItems.EnsureIndex(x => x.IsDeleted);
            _imageItems.EnsureIndex(x => x.IsArchived);
            _imageItems.EnsureIndex(x => x.ArchivePath);
            _imageItems.EnsureIndex(x => x.Width);
            _imageItems.EnsureIndex(x => x.Height);
            
            // Create indexes for ArchiveItems
            _archiveItems.EnsureIndex(x => x.FilePath, true); // Unique index
            _archiveItems.EnsureIndex(x => x.FileName);
            _archiveItems.EnsureIndex(x => x.FileSize);
            _archiveItems.EnsureIndex(x => x.ImageRatio);
            _archiveItems.EnsureIndex(x => x.ScanDate);
            _archiveItems.EnsureIndex(x => x.IsDeleted);
            
            _logger.LogInformation("Database initialization completed");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    #region ImageItem Operations

    public async Task<IEnumerable<ImageItem>> GetAllImageItemsAsync()
    {
        return await Task.Run(() =>
        {
            return _imageItems.Query()
                .Where(x => !x.IsDeleted)
                .ToList();
        });
    }

    public async Task<ImageItem?> GetImageItemByIdAsync(string id)
    {
        return await Task.Run(() => _imageItems.FindById(id));
    }

    public async Task<ImageItem?> GetImageItemByPathAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            return _imageItems.Query()
                .Where(x => x.FilePath == filePath)
                .FirstOrDefault();
        });
    }

    public async Task<bool> InsertImageItemAsync(ImageItem imageItem)
    {
        return await Task.Run(() =>
        {
            try
            {
                imageItem.Id = string.IsNullOrEmpty(imageItem.Id) 
                    ? ImageItem.GenerateFileId(imageItem.FilePath) 
                    : imageItem.Id;
                    
                var result = _imageItems.Insert(imageItem);
                _logger.LogDebug("Inserted ImageItem: {FilePath}", imageItem.FilePath);
                return result != null;
            }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
            {
                _logger.LogWarning("ImageItem already exists: {FilePath}", imageItem.FilePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert ImageItem: {FilePath}", imageItem.FilePath);
                return false;
            }
        });
    }

    public async Task<bool> UpdateImageItemAsync(ImageItem imageItem)
    {
        return await Task.Run(() =>
        {
            try
            {
                var result = _imageItems.Update(imageItem);
                _logger.LogDebug("Updated ImageItem: {FilePath}", imageItem.FilePath);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update ImageItem: {FilePath}", imageItem.FilePath);
                return false;
            }
        });
    }

    public async Task<bool> DeleteImageItemAsync(string id)
    {
        return await Task.Run(() =>
        {
            try
            {
                var result = _imageItems.Delete(id);
                _logger.LogDebug("Deleted ImageItem: {Id}", id);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete ImageItem: {Id}", id);
                return false;
            }
        });
    }

    public async Task<int> BulkInsertImageItemsAsync(IEnumerable<ImageItem> imageItems)
    {
        return await Task.Run(() =>
        {
            try
            {
                _database.BeginTrans();
                int insertedCount = 0;
                
                foreach (var imageItem in imageItems)
                {
                    try
                    {
                        imageItem.Id = string.IsNullOrEmpty(imageItem.Id) 
                            ? ImageItem.GenerateFileId(imageItem.FilePath) 
                            : imageItem.Id;
                            
                        _imageItems.Insert(imageItem);
                        insertedCount++;
                    }
                    catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
                    {
                        _logger.LogDebug("Skipping duplicate ImageItem: {FilePath}", imageItem.FilePath);
                    }
                }
                
                _database.Commit();
                _logger.LogInformation("Bulk inserted {Count} ImageItems", insertedCount);
                return insertedCount;
            }
            catch (Exception ex)
            {
                _database.Rollback();
                _logger.LogError(ex, "Failed to bulk insert ImageItems");
                return 0;
            }
        });
    }

    public async Task<IEnumerable<ImageItem>> SearchImageItemsAsync(SearchFilter filter)
    {
        return await Task.Run(() =>
        {
            var query = _imageItems.Query().Where(x => !x.IsDeleted);
            
            // Apply filters
            if (!string.IsNullOrEmpty(filter.Query))
            {
                query = query.Where(x => x.FileName.Contains(filter.Query) || 
                                        x.FilePath.Contains(filter.Query));
            }
            
            if (!filter.IncludeArchives)
            {
                query = query.Where(x => !x.IsArchived);
            }
            
            if (filter.MinFileSize.HasValue)
                query = query.Where(x => x.FileSize >= filter.MinFileSize.Value);
                
            if (filter.MaxFileSize.HasValue)
                query = query.Where(x => x.FileSize <= filter.MaxFileSize.Value);
                
            if (filter.DateFrom.HasValue)
                query = query.Where(x => x.CreatedAt >= filter.DateFrom.Value);
                
            if (filter.DateTo.HasValue)
                query = query.Where(x => x.CreatedAt <= filter.DateTo.Value);
                
            if (filter.MinWidth.HasValue)
                query = query.Where(x => x.Width >= filter.MinWidth.Value);
                
            if (filter.MaxWidth.HasValue)
                query = query.Where(x => x.Width <= filter.MaxWidth.Value);
                
            if (filter.MinHeight.HasValue)
                query = query.Where(x => x.Height >= filter.MinHeight.Value);
                
            if (filter.MaxHeight.HasValue)
                query = query.Where(x => x.Height <= filter.MaxHeight.Value);
                
            if (filter.ImageFormats.Any())
                query = query.Where(x => filter.ImageFormats.Contains(x.ImageFormat ?? ""));
                
            if (filter.HasExifData.HasValue)
                query = query.Where(x => x.HasExifData == filter.HasExifData.Value);
                
            if (filter.DateTakenFrom.HasValue)
                query = query.Where(x => x.DateTaken >= filter.DateTakenFrom.Value);
                
            if (filter.DateTakenTo.HasValue)
                query = query.Where(x => x.DateTaken <= filter.DateTakenTo.Value);
            
            // Apply sorting
            query = filter.SortBy switch
            {
                SortBy.FileName => filter.SortDirection == SortDirection.Ascending 
                    ? query.OrderBy(x => x.FileName) 
                    : query.OrderByDescending(x => x.FileName),
                SortBy.FileSize => filter.SortDirection == SortDirection.Ascending 
                    ? query.OrderBy(x => x.FileSize) 
                    : query.OrderByDescending(x => x.FileSize),
                SortBy.CreatedAt => filter.SortDirection == SortDirection.Ascending 
                    ? query.OrderBy(x => x.CreatedAt) 
                    : query.OrderByDescending(x => x.CreatedAt),
                SortBy.ModifiedAt => filter.SortDirection == SortDirection.Ascending 
                    ? query.OrderBy(x => x.ModifiedAt) 
                    : query.OrderByDescending(x => x.ModifiedAt),
                SortBy.DateTaken => filter.SortDirection == SortDirection.Ascending 
                    ? query.OrderBy(x => x.DateTaken) 
                    : query.OrderByDescending(x => x.DateTaken),
                SortBy.Width => filter.SortDirection == SortDirection.Ascending 
                    ? query.OrderBy(x => x.Width) 
                    : query.OrderByDescending(x => x.Width),
                SortBy.Height => filter.SortDirection == SortDirection.Ascending 
                    ? query.OrderBy(x => x.Height) 
                    : query.OrderByDescending(x => x.Height),
                _ => query.OrderBy(x => x.FileName)
            };
            
            // Apply paging
            var skip = (filter.PageNumber - 1) * filter.PageSize;
            return query.Skip(skip).Limit(filter.PageSize).ToList();
        });
    }

    public async Task<long> GetImageItemCountAsync()
    {
        return await Task.Run(() => _imageItems.Count(x => !x.IsDeleted));
    }

    #endregion

    #region ArchiveItem Operations

    public async Task<IEnumerable<ArchiveItem>> GetAllArchiveItemsAsync()
    {
        return await Task.Run(() =>
        {
            return _archiveItems.Query()
                .Where(x => !x.IsDeleted)
                .ToList();
        });
    }

    public async Task<ArchiveItem?> GetArchiveItemByIdAsync(string id)
    {
        return await Task.Run(() => _archiveItems.FindById(id));
    }

    public async Task<ArchiveItem?> GetArchiveItemByPathAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            return _archiveItems.Query()
                .Where(x => x.FilePath == filePath)
                .FirstOrDefault();
        });
    }

    public async Task<bool> InsertArchiveItemAsync(ArchiveItem archiveItem)
    {
        return await Task.Run(() =>
        {
            try
            {
                archiveItem.Id = string.IsNullOrEmpty(archiveItem.Id) 
                    ? ArchiveItem.GenerateArchiveId(archiveItem.FilePath) 
                    : archiveItem.Id;
                    
                var result = _archiveItems.Insert(archiveItem);
                _logger.LogDebug("Inserted ArchiveItem: {FilePath}", archiveItem.FilePath);
                return result != null;
            }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
            {
                _logger.LogWarning("ArchiveItem already exists: {FilePath}", archiveItem.FilePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert ArchiveItem: {FilePath}", archiveItem.FilePath);
                return false;
            }
        });
    }

    public async Task<bool> UpdateArchiveItemAsync(ArchiveItem archiveItem)
    {
        return await Task.Run(() =>
        {
            try
            {
                var result = _archiveItems.Update(archiveItem);
                _logger.LogDebug("Updated ArchiveItem: {FilePath}", archiveItem.FilePath);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update ArchiveItem: {FilePath}", archiveItem.FilePath);
                return false;
            }
        });
    }

    public async Task<bool> DeleteArchiveItemAsync(string id)
    {
        return await Task.Run(() =>
        {
            try
            {
                var result = _archiveItems.Delete(id);
                _logger.LogDebug("Deleted ArchiveItem: {Id}", id);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete ArchiveItem: {Id}", id);
                return false;
            }
        });
    }

    public async Task<long> GetArchiveItemCountAsync()
    {
        return await Task.Run(() => _archiveItems.Count(x => !x.IsDeleted));
    }

    #endregion

    #region Maintenance Operations

    public async Task<int> CleanupDeletedItemsAsync()
    {
        return await Task.Run(() =>
        {
            var deletedImages = _imageItems.DeleteMany(x => x.IsDeleted);
            var deletedArchives = _archiveItems.DeleteMany(x => x.IsDeleted);
            
            var totalDeleted = deletedImages + deletedArchives;
            _logger.LogInformation("Cleaned up {Count} deleted items", totalDeleted);
            
            return totalDeleted;
        });
    }

    public async Task<int> CleanupOrphanedThumbnailsAsync()
    {
        return await Task.Run(() =>
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var thumbnailDir = Path.Combine(appDataPath, "ImageMonitor", "Thumbnails");
            
            if (!Directory.Exists(thumbnailDir))
                return 0;
                
            var allThumbnailPaths = _imageItems.FindAll()
                .Where(x => !string.IsNullOrEmpty(x.ThumbnailPath))
                .Select(x => x.ThumbnailPath)
                .ToHashSet();
                
            var thumbnailFiles = Directory.GetFiles(thumbnailDir, "*", SearchOption.AllDirectories);
            int deletedCount = 0;
            
            foreach (var thumbnailFile in thumbnailFiles)
            {
                if (!allThumbnailPaths.Contains(thumbnailFile))
                {
                    try
                    {
                        File.Delete(thumbnailFile);
                        deletedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete orphaned thumbnail: {Path}", thumbnailFile);
                    }
                }
            }
            
            _logger.LogInformation("Cleaned up {Count} orphaned thumbnails", deletedCount);
            return deletedCount;
        });
    }

    public async Task OptimizeDatabaseAsync()
    {
        await _operationLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                _database.Rebuild();
                _logger.LogInformation("Database optimization completed");
            });
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<long> GetDatabaseSizeAsync()
    {
        return await Task.Run(() =>
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var databasePath = Path.Combine(appDataPath, "ImageMonitor", "imageMonitor.db");
            
            if (File.Exists(databasePath))
            {
                return new FileInfo(databasePath).Length;
            }
            
            return 0;
        });
    }

    #endregion

    public void Dispose()
    {
        _operationLock?.Dispose();
        _database?.Dispose();
    }
}