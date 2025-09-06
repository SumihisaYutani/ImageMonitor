using ImageMonitor.Models;

namespace ImageMonitor.Services;

public class DatabaseService : IDatabaseService
{
    private readonly ILiteDatabase _database;
    private readonly ILiteCollection<ImageItem> _imageItems;
    private readonly ILiteCollection<ArchiveItem> _archiveItems;
    private readonly ILiteCollection<ScanHistory> _scanHistory;
    private readonly ILogger<DatabaseService> _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
        
        // 実行ファイルのディレクトリを取得
        var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var executableDir = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;
        var appDataDir = Path.Combine(executableDir, "Data");
        
        if (!Directory.Exists(appDataDir))
        {
            Directory.CreateDirectory(appDataDir);
        }
        
        var databasePath = Path.Combine(appDataDir, "imageMonitor.db");
        _logger.LogDebug("Database path: {DatabasePath}", databasePath);
        
        _database = new LiteDatabase(databasePath);
        _imageItems = _database.GetCollection<ImageItem>("ImageItems");
        _archiveItems = _database.GetCollection<ArchiveItem>("ArchiveItems");
        _scanHistory = _database.GetCollection<ScanHistory>("ScanHistory");
    }

    public async Task InitializeAsync()
    {
        await _operationLock.WaitAsync();
        var initStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepTimes = new List<(string step, long ms)>();
        
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
            _imageItems.EnsureIndex(x => x.ArchiveImageRatio);
            _imageItems.EnsureIndex(x => x.Width);
            _imageItems.EnsureIndex(x => x.Height);
            stepTimes.Add(("ImageItems basic indexes", initStopwatch.ElapsedMilliseconds));
            
            // 複合インデックスでパフォーマンス向上（LiteDBでは個別インデックスを使用）
            _imageItems.EnsureIndex(x => x.IsDeleted);
            _imageItems.EnsureIndex(x => x.ScanDate);
            _imageItems.EnsureIndex(x => x.IsArchived);
            _imageItems.EnsureIndex(x => x.FileName);
            _imageItems.EnsureIndex(x => x.FileSize);
            stepTimes.Add(("ImageItems composite indexes", initStopwatch.ElapsedMilliseconds));
            
            // Create indexes for ArchiveItems
            _archiveItems.EnsureIndex(x => x.FilePath, true); // Unique index
            _archiveItems.EnsureIndex(x => x.FileName);
            _archiveItems.EnsureIndex(x => x.FileSize);
            _archiveItems.EnsureIndex(x => x.ImageRatio);
            _archiveItems.EnsureIndex(x => x.ScanDate);
            _archiveItems.EnsureIndex(x => x.IsDeleted);
            stepTimes.Add(("ArchiveItems indexes", initStopwatch.ElapsedMilliseconds));
            
            // Create indexes for ScanHistory
            _scanHistory.EnsureIndex(x => x.DirectoryPath);
            _scanHistory.EnsureIndex(x => x.ScanDate);
            _scanHistory.EnsureIndex(x => x.ScanType);
            stepTimes.Add(("ScanHistory indexes", initStopwatch.ElapsedMilliseconds));
            
            initStopwatch.Stop();
            var totalInitTime = initStopwatch.ElapsedMilliseconds;
            
            // パフォーマンス詳細情報をログ出力
            var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
            {
                var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                var stepDuration = step.ms - prevTime;
                return $"{step.step}: {stepDuration}ms";
            }));
            
            _logger.LogInformation("Database initialization completed in {TotalTime}ms - Steps: {StepDetails}", 
                totalInitTime, stepDetails);
            
            if (totalInitTime > 2000) // 2秒以上は警告
            {
                _logger.LogWarning("Slow database initialization: {TotalTime}ms", totalInitTime);
            }
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
                .OrderBy(x => x.ScanDate) // インデックスを使用したソート
                .ToList();
        });
    }

    public async Task<IEnumerable<ImageItem>> GetImageItemsAsync(int skip = 0, int take = 100)
    {
        return await Task.Run(() =>
        {
            return _imageItems.Query()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.ScanDate)
                .Skip(skip)
                .Limit(take)
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
                    ? ImageItem.GenerateFileId(imageItem.FilePath, imageItem.InternalPath) 
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
        await _operationLock.WaitAsync();
        
        try
        {
            return await Task.Run(() =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                try
                {
                    int insertedCount = 0;
                    var totalCount = 0;
                    
                    // 小さなバッチサイズで頻繁なコミット（メモリ効率とレスポンス向上）
                    var batchSize = 250;
                    var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var currentBatch = new List<ImageItem>(batchSize);
                    
                    _logger.LogInformation("Starting optimized bulk insert with batch size {BatchSize}", batchSize);
                    
                    foreach (var imageItem in imageItems)
                    {
                        // IDの設定
                        imageItem.Id = string.IsNullOrEmpty(imageItem.Id) 
                            ? ImageItem.GenerateFileId(imageItem.FilePath, imageItem.InternalPath) 
                            : imageItem.Id;
                        
                        currentBatch.Add(imageItem);
                        totalCount++;
                        
                        // バッチサイズに達したら処理
                        if (currentBatch.Count >= batchSize)
                        {
                            var batchInserted = ProcessBatch(currentBatch, totalCount);
                            insertedCount += batchInserted;
                            
                            // バッチ処理時間をログ出力
                            var batchTime = batchStopwatch.ElapsedMilliseconds;
                            if (batchTime > 2000) // 2秒以上
                            {
                                _logger.LogWarning("Slow batch insert: {BatchTime}ms for {BatchCount} items", 
                                    batchTime, batchInserted);
                            }
                            else
                            {
                                _logger.LogDebug("Batch inserted {BatchCount} items in {BatchTime}ms", 
                                    batchInserted, batchTime);
                            }
                            
                            currentBatch.Clear();
                            batchStopwatch.Restart();
                            
                            // 処理済みアイテム数を定期的に報告
                            if (totalCount % (batchSize * 4) == 0)
                            {
                                _logger.LogInformation("Progress: {InsertedCount} inserted out of {ProcessedCount} processed items", 
                                    insertedCount, totalCount);
                            }
                        }
                    }
                    
                    // 残りのアイテムを処理
                    if (currentBatch.Count > 0)
                    {
                        var batchInserted = ProcessBatch(currentBatch, totalCount);
                        insertedCount += batchInserted;
                    }
                    
                    stopwatch.Stop();
                    
                    var totalTime = stopwatch.ElapsedMilliseconds;
                    var itemsPerSecond = totalTime > 0 ? (totalCount * 1000.0 / totalTime) : 0;
                    
                    _logger.LogInformation("Bulk insert completed: {InsertedCount}/{TotalCount} items in {Time}ms ({Rate:F1} items/sec)", 
                        insertedCount, totalCount, totalTime, itemsPerSecond);
                        
                    return insertedCount;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "Failed to bulk insert ImageItems after {Time}ms", stopwatch.ElapsedMilliseconds);
                    return 0;
                }
            });
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private int ProcessBatch(List<ImageItem> batch, int totalProcessed)
    {
        if (!batch.Any()) return 0;
        
        var insertedCount = 0;
        var duplicateCount = 0;
        
        try
        {
            // 事前重複チェックで効率化
            var existingIds = new HashSet<string>();
            var idsToCheck = batch.Select(item => item.Id).ToList();
            
            // バッチでIDの存在チェック
            foreach (var id in idsToCheck)
            {
                if (_imageItems.Exists(x => x.Id == id))
                {
                    existingIds.Add(id);
                }
            }
            
            // 新しいアイテムのみを抽出
            var newItems = batch.Where(item => !existingIds.Contains(item.Id)).ToList();
            duplicateCount = batch.Count - newItems.Count;
            
            if (duplicateCount > 0)
            {
                // 重複が多い場合のみ情報レベルでログ出力
                if (duplicateCount > 10)
                {
                    _logger.LogInformation("Skipped {DuplicateCount} duplicate ImageItems in batch (showing summary to reduce log noise)", duplicateCount);
                }
                else
                {
                    _logger.LogDebug("Skipping {DuplicateCount} duplicate ImageItems in batch", duplicateCount);
                }
            }
            
            if (!newItems.Any())
            {
                return 0; // 全て重複の場合はトランザクション不要
            }
            
            _database.BeginTrans();
            
            foreach (var imageItem in newItems)
            {
                try
                {
                    _imageItems.Insert(imageItem);
                    insertedCount++;
                }
                catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY)
                {
                    // 事前チェック後でも稀に発生する可能性があるため、念のため
                    _logger.LogDebug("Unexpected duplicate during batch insert: {FilePath}", imageItem.FilePath);
                }
            }
            
            _database.Commit();
            return insertedCount;
        }
        catch (Exception ex)
        {
            try
            {
                _database.Rollback();
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "Failed to rollback transaction");
            }
            
            _logger.LogError(ex, "Failed to process batch of {Count} items at position {Position}", 
                batch.Count, totalProcessed);
            return 0;
        }
    }

    public async Task<int> StreamInsertImageItemsAsync(IAsyncEnumerable<ImageItem> imageItems, CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken);
        
        try
        {
            return await Task.Run(async () =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                int totalInserted = 0;
                
                // 小さなバッチサイズで頻繁な処理
                var streamBatchSize = 50;
                var currentBatch = new List<ImageItem>(streamBatchSize);
                var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                _logger.LogInformation("Starting streaming bulk insert with batch size {BatchSize}", streamBatchSize);
                
                try
                {
                    await foreach (var imageItem in imageItems.WithCancellation(cancellationToken))
                    {
                        // IDの設定
                        imageItem.Id = string.IsNullOrEmpty(imageItem.Id) 
                            ? ImageItem.GenerateFileId(imageItem.FilePath, imageItem.InternalPath) 
                            : imageItem.Id;
                        
                        currentBatch.Add(imageItem);
                        
                        // バッチサイズに達したら即座に処理
                        if (currentBatch.Count >= streamBatchSize)
                        {
                            var batchInserted = ProcessBatch(currentBatch, totalInserted);
                            totalInserted += batchInserted;
                            
                            var batchTime = batchStopwatch.ElapsedMilliseconds;
                            _logger.LogDebug("Stream batch inserted {BatchCount} items in {BatchTime}ms (total: {TotalInserted})", 
                                batchInserted, batchTime, totalInserted);
                            
                            currentBatch.Clear();
                            batchStopwatch.Restart();
                            
                            // キャンセル確認
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    
                    // 残りのアイテムを処理
                    if (currentBatch.Count > 0)
                    {
                        var batchInserted = ProcessBatch(currentBatch, totalInserted);
                        totalInserted += batchInserted;
                    }
                    
                    stopwatch.Stop();
                    var totalTime = stopwatch.ElapsedMilliseconds;
                    var itemsPerSecond = totalTime > 0 ? (totalInserted * 1000.0 / totalTime) : 0;
                    
                    _logger.LogInformation("Streaming bulk insert completed: {InsertedCount} items in {Time}ms ({Rate:F1} items/sec)", 
                        totalInserted, totalTime, itemsPerSecond);
                        
                    return totalInserted;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Streaming bulk insert cancelled after {InsertedCount} items", totalInserted);
                    throw;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "Failed to stream insert ImageItems after {Time}ms and {InsertedCount} items", 
                        stopwatch.ElapsedMilliseconds, totalInserted);
                    return totalInserted; // 部分的な成功も返す
                }
            }, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<IEnumerable<ImageItem>> SearchImageItemsAsync(SearchFilter filter)
    {
        return await Task.Run(() =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var stepTimes = new List<(string step, long ms)>();
            
            // 基本フィルタを最初に適用（インデックスを最大限活用）
            var query = _imageItems.Query();
            stepTimes.Add(("Query init", stopwatch.ElapsedMilliseconds));
            
            // IsDeletedのフィルタを最初に適用（インデックス使用）
            query = query.Where(x => !x.IsDeleted);
            stepTimes.Add(("IsDeleted filter", stopwatch.ElapsedMilliseconds));
            
            // アーカイブフィルタを早期に適用
            if (!filter.IncludeArchives)
            {
                query = query.Where(x => !x.IsArchived);
            }
            stepTimes.Add(("Archive filter", stopwatch.ElapsedMilliseconds));
            
            // テキスト検索はパフォーマンスコストが高いので他のフィルタの後に適用
            if (!string.IsNullOrEmpty(filter.Query))
            {
                var searchTerm = filter.Query.ToLowerInvariant();
                query = query.Where(x => x.FileName.ToLower().Contains(searchTerm));
            }
            stepTimes.Add(("Text search", stopwatch.ElapsedMilliseconds));
            
            // 数値範囲フィルタ（インデックスを活用）
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
                
            stepTimes.Add(("All filters applied", stopwatch.ElapsedMilliseconds));
            
            // ソートをページングの直前に適用
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
            
            stepTimes.Add(("Sorting applied", stopwatch.ElapsedMilliseconds));
            
            // ページングを最後に適用
            var skip = (filter.PageNumber - 1) * filter.PageSize;
            var results = query.Skip(skip).Limit(filter.PageSize).ToList();
            
            stepTimes.Add(("Paging and execution", stopwatch.ElapsedMilliseconds));
            stopwatch.Stop();
            
            // パフォーマンスデバッグ情報をログ出力
            var totalTime = stopwatch.ElapsedMilliseconds;
            if (totalTime > 100) // 100ms以上の場合は詳細ログ
            {
                _logger.LogWarning("SearchImageItemsAsync slow query: {TotalTime}ms, Results: {Count}", totalTime, results.Count);
                
                var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
                {
                    var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                    var stepDuration = step.ms - prevTime;
                    return $"{step.step}: {stepDuration}ms";
                }));
                
                _logger.LogDebug("Query steps: {StepDetails}", stepDetails);
                _logger.LogDebug("Filter details: Query='{Query}', IncludeArchives={IncludeArchives}, PageSize={PageSize}, SortBy={SortBy}", 
                    filter.Query ?? "<none>", filter.IncludeArchives, filter.PageSize, filter.SortBy);
                    
                // コンソールにも出力（デバッグ用）
                System.Diagnostics.Debug.WriteLine($"[PERF] Slow query: {totalTime}ms, Results: {results.Count}");
                System.Diagnostics.Debug.WriteLine($"[PERF] Steps: {stepDetails}");
            }
            else if (totalTime > 50) // 50ms以上は軽いログ
            {
                _logger.LogInformation("SearchImageItemsAsync: {TotalTime}ms, Results: {Count}", totalTime, results.Count);
            }
            
            return results;
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

    public async Task<IEnumerable<ArchiveItem>> GetArchiveItemsAsync(int offset, int limit)
    {
        await _operationLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var query = _archiveItems.Query()
                    .Where(x => !x.IsDeleted)
                    .OrderByDescending(x => x.ScanDate)
                    .Skip(offset)
                    .Limit(limit);
                return query.ToList();
            });
        }
        finally
        {
            _operationLock.Release();
        }
    }
    
    public async Task<IEnumerable<ImageItem>> GetNonArchivedImageItemsAsync(int offset, int limit)
    {
        await _operationLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var query = _imageItems.Query()
                    .Where(x => !x.IsDeleted && !x.IsArchived)
                    .OrderByDescending(x => x.ScanDate)
                    .Skip(offset)
                    .Limit(limit);
                return query.ToList();
            });
        }
        finally
        {
            _operationLock.Release();
        }
    }
    
    public async Task<bool> UpsertArchiveItemAsync(ArchiveItem archiveItem)
    {
        await _operationLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    archiveItem.Id = string.IsNullOrEmpty(archiveItem.Id) 
                        ? ArchiveItem.GenerateArchiveId(archiveItem.FilePath) 
                        : archiveItem.Id;
                    
                    _archiveItems.Upsert(archiveItem);
                    _logger.LogDebug("Upserted ArchiveItem: {FilePath}", archiveItem.FilePath);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert ArchiveItem: {FilePath}", archiveItem.FilePath);
                    return false;
                }
            });
        }
        finally
        {
            _operationLock.Release();
        }
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

    public async Task<int> CleanupSingleImageItemsAsync()
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                // 単一画像アイテムをすべて削除（アーカイブのみ表示するため）
                var deletedCount = _imageItems.DeleteAll();
                
                _logger.LogInformation("Cleaned up {Count} single image items", deletedCount);
                
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup single image items");
                return 0;
            }
            finally
            {
                _operationLock.Release();
            }
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

    #region ScanHistory operations

    public async Task<bool> InsertScanHistoryAsync(ScanHistory scanHistory)
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                scanHistory.Id = GenerateScanHistoryId(scanHistory.DirectoryPath, scanHistory.ScanDate);
                var result = _scanHistory.Insert(scanHistory);
                _logger.LogDebug("Inserted scan history: {DirectoryPath} - {ScanType} scan completed in {ElapsedMs}ms", 
                    scanHistory.DirectoryPath, scanHistory.ScanType, scanHistory.ElapsedMs);
                return result != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert scan history: {DirectoryPath}", scanHistory.DirectoryPath);
                return false;
            }
            finally
            {
                _operationLock.Release();
            }
        });
    }

    public async Task<ScanHistory?> GetLastScanHistoryAsync(string directoryPath)
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                var result = _scanHistory
                    .Query()
                    .Where(x => x.DirectoryPath == directoryPath)
                    .OrderByDescending(x => x.ScanDate)
                    .FirstOrDefault();
                
                _logger.LogDebug("Retrieved last scan history for {DirectoryPath}: {ScanDate}", 
                    directoryPath, result?.ScanDate.ToString("yyyy-MM-dd HH:mm:ss"));
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get last scan history: {DirectoryPath}", directoryPath);
                return null;
            }
            finally
            {
                _operationLock.Release();
            }
        });
    }

    public async Task<IEnumerable<ScanHistory>> GetScanHistoryAsync(string directoryPath, int limit = 10)
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                var result = _scanHistory
                    .Query()
                    .Where(x => x.DirectoryPath == directoryPath)
                    .OrderByDescending(x => x.ScanDate)
                    .Limit(limit)
                    .ToList();
                
                _logger.LogDebug("Retrieved {Count} scan history entries for {DirectoryPath}", 
                    result.Count, directoryPath);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get scan history: {DirectoryPath}", directoryPath);
                return Enumerable.Empty<ScanHistory>();
            }
            finally
            {
                _operationLock.Release();
            }
        });
    }

    public async Task<IEnumerable<string>> GetScannedDirectoriesAsync()
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                var result = _scanHistory
                    .Query()
                    .Select(x => x.DirectoryPath)
                    .ToList()
                    .Distinct()
                    .ToList();
                
                _logger.LogDebug("Retrieved {Count} scanned directories", result.Count);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get scanned directories");
                return Enumerable.Empty<string>();
            }
            finally
            {
                _operationLock.Release();
            }
        });
    }

    public async Task<IEnumerable<string>> GetImageDirectoriesAsync()
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                var allFilePaths = _imageItems
                    .Query()
                    .Select(x => x.FilePath)
                    .ToList();

                var result = allFilePaths
                    .Select(filePath => Path.GetDirectoryName(filePath))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Distinct()
                    .ToList();

                _logger.LogDebug("Retrieved {Count} image directories", result.Count);
                
                return result!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get image directories");
                return Enumerable.Empty<string>();
            }
            finally
            {
                _operationLock.Release();
            }
        });
    }

    public async Task<IEnumerable<string>> GetArchiveDirectoriesAsync()
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                var allFilePaths = _archiveItems
                    .Query()
                    .Select(x => x.FilePath)
                    .ToList();

                var result = allFilePaths
                    .Select(filePath => Path.GetDirectoryName(filePath))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Distinct()
                    .ToList();

                _logger.LogDebug("Retrieved {Count} archive directories", result.Count);
                
                return result!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get archive directories");
                return Enumerable.Empty<string>();
            }
            finally
            {
                _operationLock.Release();
            }
        });
    }

    public async Task<int> CleanupItemsByDirectoryAsync(string directoryPath)
    {
        return await Task.Run(async () =>
        {
            await _operationLock.WaitAsync();
            try
            {
                var imageDeleteCount = _imageItems.DeleteMany(x => x.FilePath.StartsWith(directoryPath));
                var archiveDeleteCount = _archiveItems.DeleteMany(x => x.FilePath.StartsWith(directoryPath));
                
                var totalDeleted = imageDeleteCount + archiveDeleteCount;
                
                _logger.LogInformation("Cleaned up {TotalDeleted} items from directory: {DirectoryPath} " +
                    "({ImageItems} images, {ArchiveItems} archives)", 
                    totalDeleted, directoryPath, imageDeleteCount, archiveDeleteCount);
                
                return totalDeleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup items by directory: {DirectoryPath}", directoryPath);
                return 0;
            }
            finally
            {
                _operationLock.Release();
            }
        });
    }

    private static string GenerateScanHistoryId(string directoryPath, DateTime scanDate)
    {
        var combined = $"{directoryPath}_{scanDate:yyyyMMddHHmmss}";
        return Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(combined)));
    }

    #endregion

    public void Dispose()
    {
        _operationLock?.Dispose();
        _database?.Dispose();
    }
}