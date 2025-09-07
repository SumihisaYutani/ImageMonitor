using ImageMonitor.Models;
using System.Drawing.Imaging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SharpCompress.Archives;

namespace ImageMonitor.Services;

public class ImageScanService : IImageScanService
{
    private readonly ILogger<ImageScanService> _logger;
    private readonly IConfigurationService _configService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IDatabaseService _databaseService;
    private SemaphoreSlim _concurrencyLimit;
    
    // 動的並行度調整（無効化：SemaphoreSlim差し替えを防ぐため）
    private int _currentConcurrency = 2;
    private DateTime _lastPerformanceCheck = DateTime.Now;
    private double _lastThroughput = 0;
    private bool _semaphoreInitialized = false;
    
    // メタデータキャッシュ (LRUキャッシュ)
    private readonly ConcurrentDictionary<string, (ImageItem metadata, DateTime lastAccess)> _metadataCache = new();
    private readonly int _maxCacheSize = 1000;
    
    // パイプライン処理用のチャンネル
    private readonly System.Threading.Channels.Channel<string> _fileProcessingChannel;
    
    // メモリプールとオブジェクト再利用
    private readonly System.Buffers.ArrayPool<byte> _byteArrayPool = System.Buffers.ArrayPool<byte>.Shared;
    private readonly Queue<ImageItem> _imageItemPool = new();
    private readonly object _poolLock = new object();
    
    private static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
    private static readonly string[] SupportedArchiveExtensions = { ".zip", ".rar" };

    public event EventHandler<ScanProgressEventArgs>? ScanProgress;

    public ImageScanService(
        ILogger<ImageScanService> logger, 
        IConfigurationService configService,
        IThumbnailService thumbnailService,
        IDatabaseService databaseService)
    {
        _logger = logger;
        _configService = configService;
        _thumbnailService = thumbnailService;
        _databaseService = databaseService;
        
        // 並行処理数を制限（HDD用に保守的に設定、動的調整可能）
        _concurrencyLimit = new SemaphoreSlim(2, 2);
        
        // パイプライン処理用チャンネル初期化
        var options = new System.Threading.Channels.BoundedChannelOptions(1000)
        {
            FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _fileProcessingChannel = System.Threading.Channels.Channel.CreateBounded<string>(options);
    }

    public async Task<List<ImageItem>> ScanDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory not found: {DirectoryPath}", directoryPath);
            return new List<ImageItem>();
        }

        var scanStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepTimes = new List<(string step, long ms)>();

        _logger.LogInformation("Starting directory scan: {DirectoryPath}", directoryPath);
        
        try
        {
            var allFiles = new List<string>();
            
            _logger.LogInformation("Starting file discovery in: {DirectoryPath}", directoryPath);
            
            // 画像ファイルを取得（タイムアウト付き）
            foreach (var extension in SupportedImageExtensions)
            {
                var pattern = $"*{extension}";
                _logger.LogDebug("Searching for pattern: {Pattern} in {DirectoryPath}", pattern, directoryPath);
                
                try
                {
                    var fileDiscoveryStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
                    fileDiscoveryStopwatch.Stop();
                    
                    _logger.LogInformation("Found {FileCount} files with pattern {Pattern} in {ElapsedMs}ms", 
                        files.Length, pattern, fileDiscoveryStopwatch.ElapsedMilliseconds);
                    
                    allFiles.AddRange(files);
                    
                    // 異常に長い処理時間の警告
                    if (fileDiscoveryStopwatch.ElapsedMilliseconds > 30000) // 30秒以上
                    {
                        _logger.LogWarning("Slow file discovery: {Pattern} took {ElapsedMs}ms in {DirectoryPath}", 
                            pattern, fileDiscoveryStopwatch.ElapsedMilliseconds, directoryPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to search for pattern {Pattern} in {DirectoryPath}", pattern, directoryPath);
                }
            }
            stepTimes.Add(("Image file discovery", scanStopwatch.ElapsedMilliseconds));
            
            // アーカイブファイルを取得
            foreach (var extension in SupportedArchiveExtensions)
            {
                var pattern = $"*{extension}";
                var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
                allFiles.AddRange(files);
            }
            stepTimes.Add(("Archive file discovery", scanStopwatch.ElapsedMilliseconds));

            _logger.LogInformation("Found {Count} files to process in {DirectoryPath}", allFiles.Count, directoryPath);
            
            var result = await ProcessFilesAsync(allFiles, cancellationToken);
            stepTimes.Add(("File processing", scanStopwatch.ElapsedMilliseconds));
            
            scanStopwatch.Stop();
            var totalScanTime = scanStopwatch.ElapsedMilliseconds;
            
            // パフォーマンス詳細情報をログ出力
            var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
            {
                var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                var stepDuration = step.ms - prevTime;
                return $"{step.step}: {stepDuration}ms";
            }));
            
            _logger.LogInformation("Directory scan completed: {DirectoryPath} in {TotalTime}ms - {FileCount} files, {ResultCount} items - Steps: {StepDetails}", 
                directoryPath, totalScanTime, allFiles.Count, result.Count, stepDetails);
            
            if (totalScanTime > 30000) // 30秒以上は警告
            {
                _logger.LogWarning("Slow directory scan: {TotalTime}ms for {DirectoryPath}", totalScanTime, directoryPath);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            scanStopwatch.Stop();
            _logger.LogError(ex, "Error scanning directory: {DirectoryPath} after {ElapsedTime}ms", 
                directoryPath, scanStopwatch.ElapsedMilliseconds);
            return new List<ImageItem>();
        }
    }

    public async Task<List<ImageItem>> ScanDirectoriesAsync(IEnumerable<string> directoryPaths, CancellationToken cancellationToken = default)
    {
        var directoryList = directoryPaths.ToList();
        var validDirectories = directoryList.Where(Directory.Exists).ToList();
        _logger.LogInformation("Scanning {Count} directories in parallel", validDirectories.Count);

        // 削除されたディレクトリの検出とクリーンアップ
        _logger.LogDebug("Starting deletion detection for removed directories");
        var deletedDirectories = await DetectDeletedDirectoriesAsync(directoryList, _databaseService);
        _logger.LogDebug("DetectDeletedDirectoriesAsync completed. Found {Count} deleted directories", deletedDirectories.Count);
        if (deletedDirectories.Any())
        {
            var cleanupCount = await CleanupDeletedDirectoriesAsync(deletedDirectories, _databaseService);
            _logger.LogInformation("Cleaned up {CleanupCount} items from {DeletedCount} deleted directories", 
                cleanupCount, deletedDirectories.Count);
        }

        // ディレクトリレベルでの並列処理
        var directoryTasks = validDirectories.Select(async directory =>
        {
            if (cancellationToken.IsCancellationRequested)
                return new List<ImageItem>();
                
            return await ScanDirectoryAsync(directory, cancellationToken);
        });

        var directoryResults = await Task.WhenAll(directoryTasks);
        var allResults = directoryResults.SelectMany(r => r).ToList();

        _logger.LogInformation("Scan completed. Found {Count} total items", allResults.Count);
        return allResults;
    }

    public async Task<int> ScanDirectoryStreamingAsync(string directoryPath, IDatabaseService databaseService, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory not found for streaming scan: {DirectoryPath}", directoryPath);
            return 0;
        }

        var streamingScanStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepTimes = new List<(string step, long ms)>();

        _logger.LogInformation("Starting streaming directory scan: {DirectoryPath}", directoryPath);
        
        try
        {
            var allFiles = new List<string>();
            
            // 画像ファイルを取得
            foreach (var extension in SupportedImageExtensions)
            {
                var pattern = $"*{extension}";
                var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
                allFiles.AddRange(files);
            }
            stepTimes.Add(("Image file discovery", streamingScanStopwatch.ElapsedMilliseconds));
            
            // アーカイブファイルを取得
            foreach (var extension in SupportedArchiveExtensions)
            {
                var pattern = $"*{extension}";
                var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
                allFiles.AddRange(files);
            }
            stepTimes.Add(("Archive file discovery", streamingScanStopwatch.ElapsedMilliseconds));

            _logger.LogInformation("Found {Count} files to process in streaming mode: {DirectoryPath}", allFiles.Count, directoryPath);
            
            // ストリーミング処理でファイルを処理
            var processedCount = await ProcessFilesStreamingAsync(allFiles, databaseService, cancellationToken);
            stepTimes.Add(("Streaming processing", streamingScanStopwatch.ElapsedMilliseconds));
            
            streamingScanStopwatch.Stop();
            var totalStreamingTime = streamingScanStopwatch.ElapsedMilliseconds;
            
            // パフォーマンス詳細情報をログ出力
            var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
            {
                var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                var stepDuration = step.ms - prevTime;
                return $"{step.step}: {stepDuration}ms";
            }));
            
            // パフォーマンス詳細計算
            var totalTimeSec = totalStreamingTime / 1000.0;
            var filesPerSecond = allFiles.Count > 0 && totalStreamingTime > 0 ? (allFiles.Count * 1000.0 / totalStreamingTime) : 0;
            var itemsPerSecond = processedCount > 0 && totalStreamingTime > 0 ? (processedCount * 1000.0 / totalStreamingTime) : 0;
            
            _logger.LogInformation("=== STREAMING SCAN PERFORMANCE REPORT ===");
            _logger.LogInformation("Streaming scan completed: {DirectoryPath} in {TotalTime}ms ({TotalTimeSec:F1} seconds)", 
                directoryPath, totalStreamingTime, totalTimeSec);
            _logger.LogInformation("Files processed: {FileCount} ({FilesPerSec:F2} files/sec)", 
                allFiles.Count, filesPerSecond);
            _logger.LogInformation("Items created: {ProcessedCount} ({ItemsPerSec:F2} items/sec)", 
                processedCount, itemsPerSecond);
            _logger.LogInformation("Processing efficiency: {EfficiencyPercent:F1}% (items per file)", 
                allFiles.Count > 0 ? (processedCount * 100.0 / allFiles.Count) : 0);
            _logger.LogInformation("Step breakdown: {StepDetails}", stepDetails);
            _logger.LogInformation("=== END STREAMING SCAN REPORT ===");
            
            if (totalStreamingTime > 60000) // 60秒以上は警告
            {
                _logger.LogWarning("Slow streaming scan detected: {TotalTime}ms for {DirectoryPath}", totalStreamingTime, directoryPath);
            }
            else if (totalStreamingTime < 10000) // 10秒未満は高速
            {
                _logger.LogInformation("High-speed scan completed: {TotalTime}ms for {DirectoryPath}", totalStreamingTime, directoryPath);
            }
            
            return processedCount;
        }
        catch (Exception ex)
        {
            streamingScanStopwatch.Stop();
            _logger.LogError(ex, "Error in streaming directory scan: {DirectoryPath} after {ElapsedTime}ms", 
                directoryPath, streamingScanStopwatch.ElapsedMilliseconds);
            return 0;
        }
    }

    private async Task<int> ProcessFilesStreamingAsync(List<string> filePaths, IDatabaseService databaseService, CancellationToken cancellationToken)
    {
        var totalFiles = filePaths.Count;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        _logger.LogInformation("Starting streaming file processing: {TotalFiles} files", totalFiles);

        // 設定から並行処理数を取得（一度だけ初期化）
        if (!_semaphoreInitialized)
        {
            var settings = await _configService.GetSettingsAsync();
            _concurrencyLimit?.Dispose();
            _concurrencyLimit = new SemaphoreSlim(settings.MaxConcurrentScans, settings.MaxConcurrentScans);
            _currentConcurrency = settings.MaxConcurrentScans;
            _semaphoreInitialized = true;
            _logger.LogDebug("Semaphore initialized with concurrency: {Concurrency}", _currentConcurrency);
        }

        // バッチオープン最適化: ファイルタイプ別に分離して効率的に処理
        var imageFiles = filePaths.Where(IsImageFile).ToList();
        var archiveFiles = filePaths.Where(IsArchiveFile).ToList();
        
        _logger.LogInformation("Processing {ImageCount} image files and {ArchiveCount} archive files with batch optimization", 
            imageFiles.Count, archiveFiles.Count);

        // 非同期ストリームを作成（バッチ最適化版）
        var imageItemStream = ProcessFilesAsyncEnumerableOptimized(imageFiles, archiveFiles, cancellationToken);
        
        // データベースにストリーミング保存
        var insertedCount = await databaseService.StreamInsertImageItemsAsync(imageItemStream, cancellationToken);
        
        stopwatch.Stop();
        var totalTime = stopwatch.ElapsedMilliseconds;
        var filesPerSecond = totalTime > 0 ? (totalFiles * 1000.0 / totalTime) : 0;
        
        _logger.LogInformation("Streaming file processing completed: {TotalFiles} files in {Time}ms ({Rate:F1} files/sec, {InsertedCount} items inserted)", 
            totalFiles, totalTime, filesPerSecond, insertedCount);

        return insertedCount;
    }

    /// <summary>
    /// バッチオープン最適化版のファイル処理AsyncEnumerable
    /// 画像ファイルとアーカイブファイルを効率的に並列処理
    /// </summary>
    private async IAsyncEnumerable<ImageItem> ProcessFilesAsyncEnumerableOptimized(List<string> imageFiles, List<string> archiveFiles, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processedFiles = 0;
        var totalFiles = imageFiles.Count + archiveFiles.Count;
        var allTasks = new List<Task<IEnumerable<ImageItem>>>();
        var processingStartTime = DateTime.Now;
        
        _logger.LogDebug("Starting optimized async enumerable processing: {ImageCount} images, {ArchiveCount} archives", 
            imageFiles.Count, archiveFiles.Count);

        // 画像ファイル処理タスク
        if (imageFiles.Count > 0)
        {
            var imageProcessingTasks = imageFiles.Select(async filePath =>
            {
                await _concurrencyLimit.WaitAsync(cancellationToken);
                try
                {
                    // 単一画像ファイルの処理を無効化（アーカイブのみ表示するため）
                    // var imageItem = await ProcessImageFileAsync(filePath, cancellationToken);
                    
                    var processed = Interlocked.Increment(ref processedFiles);
                    OnScanProgress(new ScanProgressEventArgs
                    {
                        CurrentFile = Path.GetFileName(filePath),
                        ProcessedFiles = processed,
                        TotalFiles = totalFiles,
                        Message = $"Skipping image {Path.GetFileName(filePath)} (archives only)...",
                        IsCompleted = processed == totalFiles,
                        ItemsFound = 0
                    });

                    // HDD最適化: 動的並行度調整（無効化：SemaphoreSlim差し替えを防ぐため）
                    // AdjustConcurrencyForHDD(processed, DateTime.Now - processingStartTime);

                    return Array.Empty<ImageItem>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process image file: {FilePath}", filePath);
                    Interlocked.Increment(ref processedFiles);
                    return Array.Empty<ImageItem>();
                }
                finally
                {
                    _concurrencyLimit.Release();
                }
            });

            allTasks.AddRange(imageProcessingTasks.Cast<Task<IEnumerable<ImageItem>>>());
        }

        // アーカイブファイル処理（バッチ最適化）
        if (archiveFiles.Count > 0)
        {
            var archiveProcessingTask = Task.Run(async () =>
            {
                var archiveResults = await ProcessArchiveFilesBatchOptimizedAsync(archiveFiles, cancellationToken);
                
                // 進捗更新
                var processed = Interlocked.Add(ref processedFiles, archiveFiles.Count);
                OnScanProgress(new ScanProgressEventArgs
                {
                    CurrentFile = $"Batch processed {archiveFiles.Count} archives",
                    ProcessedFiles = processed,
                    TotalFiles = totalFiles,
                    Message = $"Completed batch processing of {archiveFiles.Count} archives",
                    IsCompleted = processed == totalFiles,
                    ItemsFound = archiveResults.Count
                });

                return (IEnumerable<ImageItem>)archiveResults;
            });

            allTasks.Add(archiveProcessingTask);
        }

        // 順次完了したタスクからyield
        var completedTasks = new HashSet<Task<IEnumerable<ImageItem>>>();
        var runningTasks = new HashSet<Task<IEnumerable<ImageItem>>>(allTasks);

        while (runningTasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var completedTask = await Task.WhenAny(runningTasks);
            runningTasks.Remove(completedTask);
            completedTasks.Add(completedTask);
            
            IEnumerable<ImageItem> items;
            try
            {
                items = await completedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Task failed in optimized streaming processing");
                items = Array.Empty<ImageItem>();
            }
            
            // アイテムを順次yield
            foreach (var item in items)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private async IAsyncEnumerable<ImageItem> ProcessFilesAsyncEnumerable(List<string> filePaths, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var processedFiles = 0;
        var totalFiles = filePaths.Count;
        
        var tasks = filePaths.Select(async filePath =>
        {
            await _concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var items = new List<ImageItem>();
                
                if (IsImageFile(filePath))
                {
                    // 単一画像ファイルの処理を無効化（アーカイブのみ表示するため）
                    // var imageItem = await ProcessImageFileAsync(filePath, cancellationToken);
                    // if (imageItem != null)
                    //     items.Add(imageItem);
                }
                else if (IsArchiveFile(filePath))
                {
                    var archiveItems = await ProcessArchiveFileAsync(filePath, cancellationToken);
                    items.AddRange(archiveItems);
                }

                // 進捗報告
                var processed = Interlocked.Increment(ref processedFiles);
                OnScanProgress(new ScanProgressEventArgs
                {
                    CurrentFile = Path.GetFileName(filePath),
                    ProcessedFiles = processed,
                    TotalFiles = totalFiles,
                    Message = $"Processing {Path.GetFileName(filePath)}...",
                    IsCompleted = processed == totalFiles,
                    ItemsFound = items.Count
                });

                return items;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file: {FilePath}", filePath);
                Interlocked.Increment(ref processedFiles);
                return new List<ImageItem>();
            }
            finally
            {
                _concurrencyLimit.Release();
            }
        });

        // 並列処理の結果を順次yield
        var completedTasks = new HashSet<Task<List<ImageItem>>>();
        var runningTasks = new HashSet<Task<List<ImageItem>>>(tasks);

        while (runningTasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var completedTask = await Task.WhenAny(runningTasks);
            runningTasks.Remove(completedTask);
            completedTasks.Add(completedTask);
            
            List<ImageItem> items;
            try
            {
                items = await completedTask;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Task failed in streaming processing");
                items = new List<ImageItem>();
            }
            
            // アイテムを順次yield（例外処理は上位で実施済み）
            foreach (var item in items)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private async Task<List<ImageItem>> ProcessFilesAsync(List<string> filePaths, CancellationToken cancellationToken)
    {
        var totalFiles = filePaths.Count;
        var processedFiles = 0;
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting optimized ProcessFilesAsync: {TotalFiles} files", totalFiles);

        // 設定から並行処理数を取得（一度だけ初期化）
        if (!_semaphoreInitialized)
        {
            var settings = await _configService.GetSettingsAsync();
            _concurrencyLimit?.Dispose();
            _concurrencyLimit = new SemaphoreSlim(settings.MaxConcurrentScans, settings.MaxConcurrentScans);
            _currentConcurrency = settings.MaxConcurrentScans;
            _semaphoreInitialized = true;
            _logger.LogDebug("Semaphore initialized with concurrency: {Concurrency}", _currentConcurrency);
        }

        // 結果を蓄積するリスト
        var allResults = new List<ImageItem>();
        var resultLock = new object();
        
        // 並列処理のタスクを作成
        var tasks = filePaths.Select(async filePath =>
        {
            await _concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var items = new List<ImageItem>();
                
                if (IsImageFile(filePath))
                {
                    // 単一画像ファイルの処理を無効化（アーカイブのみ表示するため）
                    // var imageItem = await ProcessImageFileAsync(filePath, cancellationToken);
                    // if (imageItem != null)
                    //     items.Add(imageItem);
                }
                else if (IsArchiveFile(filePath))
                {
                    var archiveItems = await ProcessArchiveFileAsync(filePath, cancellationToken);
                    items.AddRange(archiveItems);
                }

                // 結果をスレッドセーフに追加
                if (items.Any())
                {
                    lock (resultLock)
                    {
                        allResults.AddRange(items);
                    }
                }

                // 進捗報告
                var processed = Interlocked.Increment(ref processedFiles);
                OnScanProgress(new ScanProgressEventArgs
                {
                    CurrentFile = Path.GetFileName(filePath),
                    ProcessedFiles = processed,
                    TotalFiles = totalFiles,
                    Message = $"Processing {Path.GetFileName(filePath)}...",
                    IsCompleted = processed == totalFiles,
                    ItemsFound = allResults.Count
                });

                return items.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file: {FilePath}", filePath);
                Interlocked.Increment(ref processedFiles);
                return 0;
            }
            finally
            {
                _concurrencyLimit.Release();
            }
        });

        // 全タスクの完了を待機
        var taskResults = await Task.WhenAll(tasks);
        var totalItemsProcessed = taskResults.Sum();
        
        stopwatch.Stop();
        var totalTime = stopwatch.ElapsedMilliseconds;
        var filesPerSecond = totalTime > 0 ? (totalFiles * 1000.0 / totalTime) : 0;
        
        _logger.LogInformation("ProcessFilesAsync completed: {TotalFiles} files in {Time}ms ({Rate:F1} files/sec, {Results} results)", 
            totalFiles, totalTime, filesPerSecond, allResults.Count);

        return allResults;
    }

    public async Task<ImageItem?> ProcessImageFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!IsImageFile(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            
            // オブジェクトプールから取得
            var imageItem = RentImageItem();
            imageItem.Id = ImageItem.GenerateFileId(filePath);
            imageItem.FilePath = filePath;
            imageItem.FileName = fileInfo.Name;
            imageItem.FileSize = fileInfo.Length;
            imageItem.CreatedAt = fileInfo.CreationTime;
            imageItem.ModifiedAt = fileInfo.LastWriteTime;
            imageItem.ScanDate = DateTime.Now;
            imageItem.IsArchived = false;

            // 並列処理: メタデータ取得とサムネイル生成を同時実行
            var settings = await _configService.GetSettingsAsync();
            
            var metadataTask = PopulateImageMetadataAsync(imageItem, filePath, cancellationToken);
            var thumbnailTask = _thumbnailService.GenerateThumbnailAsync(filePath, settings.ThumbnailSize);
            
            // 並列実行完了を待機
            await Task.WhenAll(metadataTask, thumbnailTask);
            
            // サムネイルパスを設定
            imageItem.ThumbnailPath = await thumbnailTask;

            _logger.LogDebug("Processed image file: {FilePath} ({Width}x{Height})", 
                filePath, imageItem.Width, imageItem.Height);

            return imageItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process image file: {FilePath}", filePath);
            return null;
        }
    }

    public async Task<List<ImageItem>> ProcessArchiveFileAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        if (!IsArchiveFile(archivePath) || !File.Exists(archivePath))
        {
            return new List<ImageItem>();
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepTimes = new List<(string step, long ms)>();

        try
        {
            _logger.LogDebug("Processing archive file: {ArchivePath}", archivePath);
            stepTimes.Add(("Start", stopwatch.ElapsedMilliseconds));
            
            var results = new List<ImageItem>();
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();

            List<string> imageFilesInArchive;
            
            if (extension == ".zip")
            {
                imageFilesInArchive = await GetImageFilesFromZipAsync(archivePath, cancellationToken);
            }
            else if (extension == ".rar")
            {
                imageFilesInArchive = await GetImageFilesFromRarAsync(archivePath, cancellationToken);
            }
            else
            {
                return results;
            }
            
            stepTimes.Add(("List archive entries", stopwatch.ElapsedMilliseconds));

            if (!imageFilesInArchive.Any())
            {
                _logger.LogDebug("No image files found in archive: {ArchivePath}", archivePath);
                return results;
            }

            // アーカイブ情報を取得
            var settings = await _configService.GetSettingsAsync();
            var totalFilesInArchive = await GetTotalFilesInArchiveAsync(archivePath);
            var imageRatio = (decimal)imageFilesInArchive.Count / totalFilesInArchive;
            
            // 強制内部処理：50%未満の画像比率のアーカイブをフィルタリング
            const decimal MinimumImageRatio = 0.5m; // 50%
            if (imageRatio < MinimumImageRatio)
            {
                _logger.LogInformation("Skipping archive with low image ratio: {ArchivePath} - {Ratio:P1} (minimum: {MinRatio:P0})", 
                    archivePath, imageRatio, MinimumImageRatio);
                stepTimes.Add(("Filtered out by ratio", stopwatch.ElapsedMilliseconds));
                return results; // 空のリストを返す
            }
            
            var archiveFileInfo = new FileInfo(archivePath);
            stepTimes.Add(("Get archive info and ratio check", stopwatch.ElapsedMilliseconds));
            
            // 最適化: アーカイブを1回だけ開いて全画像情報を並列処理
            var archiveResults = await ProcessArchiveEntriesBatchAsync(archivePath, imageFilesInArchive, archiveFileInfo, cancellationToken);
            results.AddRange(archiveResults);
            
            stepTimes.Add(("Process archive entries", stopwatch.ElapsedMilliseconds));
            
            // 大きなアーカイブの処理完了ログ
            if (imageFilesInArchive.Count > 100)
            {
                var processingTime = stopwatch.ElapsedMilliseconds - (stepTimes.Count > 1 ? stepTimes[stepTimes.Count - 2].ms : 0);
                _logger.LogInformation("Completed processing large archive: {ArchivePath} - {ImageCount} images in {ProcessingTime}ms", 
                    Path.GetFileName(archivePath), results.Count, processingTime);
            }

            // アーカイブ全体のサムネイルを生成（最初の画像ファイルを使用）
            if (results.Any())
            {
                var thumbnailPath = await _thumbnailService.GenerateArchiveThumbnailAsync(archivePath, settings.ThumbnailSize);
                
                // すべてのアーカイブ内アイテムに同じサムネイルとImageRatioを設定
                foreach (var item in results)
                {
                    item.ThumbnailPath = thumbnailPath;
                    item.ArchiveImageRatio = imageRatio;
                }
                
                // ArchiveItemを作成してデータベースに保存
                var archiveItem = new ArchiveItem
                {
                    Id = ArchiveItem.GenerateArchiveId(archivePath),
                    FilePath = archivePath,
                    FileName = Path.GetFileName(archivePath),
                    FileSize = archiveFileInfo.Length,
                    CreatedAt = archiveFileInfo.CreationTime,
                    ModifiedAt = archiveFileInfo.LastWriteTime,
                    ScanDate = DateTime.Now,
                    ArchiveType = Path.GetExtension(archivePath).ToLowerInvariant(),
                    TotalFiles = totalFilesInArchive,
                    ImageFiles = results.Count,
                    ImageRatio = imageRatio,
                    ThumbnailPath = thumbnailPath,
                    Images = results.Select(item => new ImageInArchive
                    {
                        InternalPath = item.InternalPath ?? "",
                        FileName = item.FileName,
                        FileSize = item.FileSize,
                        Width = item.Width,
                        Height = item.Height
                    }).ToList()
                };
                
                // ArchiveItemをデータベースに保存
                await _databaseService.UpsertArchiveItemAsync(archiveItem);
            }
            
            stepTimes.Add(("Generate thumbnail", stopwatch.ElapsedMilliseconds));
            stopwatch.Stop();
            
            // パフォーマンスデバッグ情報をログ出力
            var totalTime = stopwatch.ElapsedMilliseconds;
            if (totalTime > 2000) // 2秒以上
            {
                _logger.LogWarning("ProcessArchiveFileAsync slow: {TotalTime}ms for {ArchivePath} ({ImageCount} images)", 
                    totalTime, archivePath, results.Count);
                    
                var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
                {
                    var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                    var stepDuration = step.ms - prevTime;
                    return $"{step.step}: {stepDuration}ms";
                }));
                
                _logger.LogDebug("Archive processing steps: {StepDetails}", stepDetails);
            }
            else if (totalTime > 500) // 500ms以上
            {
                _logger.LogInformation("ProcessArchiveFileAsync: {TotalTime}ms for {ArchivePath} ({ImageCount} images)", 
                    totalTime, Path.GetFileName(archivePath), results.Count);
            }

            _logger.LogInformation("Processed archive: {ArchivePath} - {Count} images (ratio: {Ratio:P1})", 
                archivePath, results.Count, imageRatio);

            // アーカイブ内の個別ImageItemは返さない（ArchiveItemのみを保存）
            return new List<ImageItem>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process archive file: {ArchivePath}", archivePath);
            return new List<ImageItem>();
        }
    }

    private async Task<List<string>> GetImageFilesFromZipAsync(string zipPath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var imageFiles = new List<string>();
            
            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            
            // 高速フィルタリング: サイズ・拡張子・パス検証
            imageFiles = archive.Entries
                .Where(entry => !cancellationToken.IsCancellationRequested && 
                               IsValidArchiveEntry(entry.FullName, entry.Length))
                .Select(entry => entry.FullName)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            return imageFiles;
        }, cancellationToken);
    }

    private async Task<List<string>> GetImageFilesFromRarAsync(string rarPath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            using var archive = ArchiveFactory.Open(rarPath);
            
            // 高速フィルタリング: サイズ・拡張子・パス検証  
            var imageFiles = archive.Entries
                .Where(entry => !cancellationToken.IsCancellationRequested && 
                               !entry.IsDirectory &&
                               !string.IsNullOrEmpty(entry.Key) &&
                               IsValidArchiveEntry(entry.Key, entry.Size))
                .Select(entry => entry.Key)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            
            return imageFiles;
        }, cancellationToken);
    }

    private async Task<List<ImageItem>> ProcessArchiveEntriesBatchAsync(string archivePath, List<string> imageFilesInArchive, FileInfo archiveFileInfo, CancellationToken cancellationToken)
    {
        var results = new List<ImageItem>();
        var extension = Path.GetExtension(archivePath).ToLowerInvariant();
        
        // 動的並列度調整: アーカイブサイズと画像数に基づく最適化
        var concurrencyLimit = CalculateOptimalConcurrency(imageFilesInArchive.Count, archiveFileInfo.Length);
        using var semaphore = new SemaphoreSlim(concurrencyLimit, concurrencyLimit);
        
        // 大きなアーカイブの場合、進捗ログを有効化
        var isLargeArchive = imageFilesInArchive.Count > 100;
        if (isLargeArchive)
        {
            _logger.LogInformation("Processing large archive: {ArchivePath} - {ImageCount} images with {Concurrency} concurrent tasks", 
                Path.GetFileName(archivePath), imageFilesInArchive.Count, concurrencyLimit);
        }

        if (extension == ".zip")
        {
            results = await ProcessZipArchiveBatchAsync(archivePath, imageFilesInArchive, archiveFileInfo, semaphore, cancellationToken);
        }
        else if (extension == ".rar")
        {
            results = await ProcessRarArchiveBatchAsync(archivePath, imageFilesInArchive, archiveFileInfo, semaphore, cancellationToken);
        }

        return results;
    }

    private int CalculateOptimalConcurrency(int imageCount, long archiveSize)
    {
        // ベース並列度 (CPU コア数に基づく)
        var baseConcurrency = Environment.ProcessorCount;
        
        // アーカイブサイズに基づく調整 (MB単位)
        var archiveSizeMB = archiveSize / (1024 * 1024);
        
        int concurrency;
        
        if (imageCount < 50)
        {
            // 小さなアーカイブ: 高並列度で高速処理
            concurrency = Math.Min(baseConcurrency * 2, 16);
        }
        else if (imageCount < 200)
        {
            // 中サイズアーカイブ: 標準並列度
            concurrency = baseConcurrency;
        }
        else if (imageCount < 500)
        {
            // 大サイズアーカイブ: 並列度を抑制
            concurrency = Math.Max(baseConcurrency / 2, 4);
        }
        else
        {
            // 巨大アーカイブ: 最小並列度でメモリ使用量を抑制
            concurrency = Math.Max(baseConcurrency / 4, 2);
        }
        
        // アーカイブファイルサイズでさらに調整
        if (archiveSizeMB > 500) // 500MB以上
        {
            concurrency = Math.Max(concurrency / 2, 2);
        }
        else if (archiveSizeMB > 100) // 100MB以上
        {
            concurrency = Math.Max(concurrency * 3 / 4, 3);
        }
        
        // 最小2、最大16に制限
        return Math.Max(2, Math.Min(concurrency, 16));
    }

    private async Task<List<ImageItem>> ProcessZipArchiveBatchAsync(string archivePath, List<string> imageFilesInArchive, FileInfo archiveFileInfo, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        var results = new List<ImageItem>();
        
        return await Task.Run(async () =>
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
            
            // 全エントリを一度に取得してディクショナリ化（高速ルックアップ用）
            var entryLookup = archive.Entries.ToDictionary(e => e.FullName, e => e);
            
            // 並列でメタデータを処理
            var tasks = imageFilesInArchive.Select(async internalPath =>
            {
                await semaphore.WaitAsync(cancellationToken);
                ImageItem? result = null;
                try
                {
                    if (cancellationToken.IsCancellationRequested || !entryLookup.TryGetValue(internalPath, out var entry))
                        return null;

                    var imageItem = new ImageItem
                    {
                        Id = ImageItem.GenerateFileId(archivePath, internalPath),
                        FilePath = archivePath,
                        FileName = Path.GetFileName(internalPath),
                        FileSize = entry.Length,
                        CreatedAt = archiveFileInfo.CreationTime,
                        ModifiedAt = archiveFileInfo.LastWriteTime,
                        ScanDate = DateTime.Now,
                        IsArchived = true,
                        ArchivePath = archivePath,
                        InternalPath = internalPath
                    };

                    // 高速化: アーカイブ内画像のメタデータ読み取りをスキップ
                    // ファイル拡張子からフォーマットを推定し、デフォルト値を設定
                    imageItem.ImageFormat = Path.GetExtension(internalPath).TrimStart('.').ToLowerInvariant();
                    imageItem.HasExifData = false;
                    imageItem.Width = 0;  // アーカイブ内画像は実際のサイズ不明
                    imageItem.Height = 0;
                    
                    // 必要に応じて後でサムネイル生成時に実際のサイズを取得
                    result = imageItem;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process archive entry: {ArchivePath}#{InternalPath}", archivePath, internalPath);
                }
                finally
                {
                    try
                    {
                        semaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // セマフォが既に破棄されている場合は無視
                    }
                    catch (SemaphoreFullException ex)
                    {
                        _logger.LogWarning(ex, "Semaphore release failed for archive entry: {ArchivePath}#{InternalPath}", archivePath, internalPath);
                    }
                }
                return result;
            });

            var processedItems = await Task.WhenAll(tasks);
            return processedItems.Where(item => item != null).ToList()!;
        }, cancellationToken);
    }

    private async Task<List<ImageItem>> ProcessRarArchiveBatchAsync(string archivePath, List<string> imageFilesInArchive, FileInfo archiveFileInfo, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        return await Task.Run(async () =>
        {
            using var archive = ArchiveFactory.Open(archivePath);
            
            // 全エントリを一度に取得してディクショナリ化
            var entryLookup = archive.Entries.Where(e => !e.IsDirectory).ToDictionary(e => e.Key, e => e);
            
            var tasks = imageFilesInArchive.Select(async internalPath =>
            {
                await semaphore.WaitAsync(cancellationToken);
                ImageItem? result = null;
                try
                {
                    if (cancellationToken.IsCancellationRequested || !entryLookup.TryGetValue(internalPath, out var entry))
                        return null;

                    var imageItem = new ImageItem
                    {
                        Id = ImageItem.GenerateFileId(archivePath, internalPath),
                        FilePath = archivePath,
                        FileName = Path.GetFileName(internalPath),
                        FileSize = entry.Size,
                        CreatedAt = archiveFileInfo.CreationTime,
                        ModifiedAt = archiveFileInfo.LastWriteTime,
                        ScanDate = DateTime.Now,
                        IsArchived = true,
                        ArchivePath = archivePath,
                        InternalPath = internalPath
                    };

                    // 高速化: アーカイブ内画像のメタデータ読み取りをスキップ
                    // ファイル拡張子からフォーマットを推定し、デフォルト値を設定
                    imageItem.ImageFormat = Path.GetExtension(internalPath).TrimStart('.').ToLowerInvariant();
                    imageItem.HasExifData = false;
                    imageItem.Width = 0;  // アーカイブ内画像は実際のサイズ不明
                    imageItem.Height = 0;
                    
                    // 必要に応じて後でサムネイル生成時に実際のサイズを取得
                    result = imageItem;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process archive entry: {ArchivePath}#{InternalPath}", archivePath, internalPath);
                }
                finally
                {
                    try
                    {
                        semaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        // セマフォが既に破棄されている場合は無視
                    }
                    catch (SemaphoreFullException ex)
                    {
                        _logger.LogWarning(ex, "Semaphore release failed for archive entry: {ArchivePath}#{InternalPath}", archivePath, internalPath);
                    }
                }
                return result;
            });

            var processedItems = await Task.WhenAll(tasks);
            return processedItems.Where(item => item != null).ToList()!;
        }, cancellationToken);
    }

    /// <summary>
    /// アーカイブファイルのバッチオープン最適化
    /// 複数のアーカイブを効率的に並列処理し、I/Oボトルネックを最小化
    /// </summary>
    private async Task<List<ImageItem>> ProcessArchiveFilesBatchOptimizedAsync(List<string> archiveFiles, CancellationToken cancellationToken)
    {
        var results = new List<ImageItem>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        _logger.LogInformation("Starting batch optimized archive processing for {Count} archives", archiveFiles.Count);

        // アーカイブファイルをサイズ順にソート（小さいファイルから処理して早期完了を狙う）
        var archiveInfos = archiveFiles
            .Select(path => new { Path = path, Info = new FileInfo(path) })
            .Where(x => x.Info.Exists)
            .OrderBy(x => x.Info.Length)  // サイズ順でソート
            .ToList();

        // HDD最適化: ディスクI/O効率を考慮した動的バッチサイズ決定
        var totalArchiveSize = archiveInfos.Sum(x => x.Info.Length);
        var batchSize = CalculateOptimalBatchSizeForHDD(archiveInfos.Count, totalArchiveSize);
        
        _logger.LogInformation("Processing {Count} archives in batches of {BatchSize} (total size: {TotalSizeMB:F1}MB)", 
            archiveInfos.Count, batchSize, totalArchiveSize / (1024.0 * 1024.0));

        // バッチごとに処理
        for (int i = 0; i < archiveInfos.Count; i += batchSize)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var batch = archiveInfos.Skip(i).Take(batchSize).ToList();
            var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _logger.LogDebug("Processing batch {BatchNum}/{TotalBatches} ({BatchCount} archives)", 
                (i / batchSize) + 1, (archiveInfos.Count + batchSize - 1) / batchSize, batch.Count);

            // HDD最適化：並列度をさらに抑制してディスクI/Oの競合回避
            var hddOptimalConcurrency = Math.Min(Math.Min(batch.Count, Environment.ProcessorCount / 2), 2); // HDD用に大幅減少
            using var batchSemaphore = new SemaphoreSlim(hddOptimalConcurrency, hddOptimalConcurrency);

            var batchTasks = batch.Select(async archiveInfo =>
            {
                await batchSemaphore.WaitAsync(cancellationToken);
                try
                {
                    // 個別アーカイブ処理（既存のメソッドを使用）
                    return await ProcessArchiveFileAsync(archiveInfo.Path, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process archive in batch: {ArchivePath}", archiveInfo.Path);
                    return new List<ImageItem>();
                }
                finally
                {
                    batchSemaphore.Release();
                }
            });

            var batchResults = await Task.WhenAll(batchTasks);
            var batchItems = batchResults.SelectMany(r => r).ToList();
            results.AddRange(batchItems);
            
            batchStopwatch.Stop();
            _logger.LogDebug("Batch {BatchNum} completed in {ElapsedMs}ms, found {ItemCount} items", 
                (i / batchSize) + 1, batchStopwatch.ElapsedMilliseconds, batchItems.Count);

            // HDD最適化: バッチ間での適切な休憩（ディスクヘッド移動時間確保）
            if (i + batchSize < archiveInfos.Count)
            {
                var restTime = CalculateHDDRestTime(totalArchiveSize, batch.Count);
                if (restTime > 0)
                {
                    _logger.LogDebug("HDD optimization: resting {RestTime}ms between batches", restTime);
                    await Task.Delay(restTime, cancellationToken);
                }
            }
        }

        stopwatch.Stop();
        _logger.LogInformation("Batch archive processing completed in {ElapsedMs}ms, processed {ArchiveCount} archives, found {TotalItems} items", 
            stopwatch.ElapsedMilliseconds, archiveInfos.Count, results.Count);

        return results;
    }

    /// <summary>
    /// HDD用：ディスクI/O効率を最大化するバッチサイズを計算
    /// HDDの特性（シーケンシャル読み取り優先、ランダムアクセス回避）を考慮
    /// </summary>
    private int CalculateOptimalBatchSizeForHDD(int archiveCount, long totalArchiveSize)
    {
        // HDDの基本特性
        var hddOptimalBatchSize = Math.Max(Environment.ProcessorCount / 2, 2); // HDD用に並列度を抑制
        
        var totalSizeMB = totalArchiveSize / (1024 * 1024);
        int batchSize;
        
        // HDDの場合：小さなバッチで連続読み取りを優先
        if (totalSizeMB < 50) // 50MB未満: 全て一括処理（ディスクヘッド移動最小化）
        {
            batchSize = archiveCount;
        }
        else if (totalSizeMB < 200) // 200MB未満: 小さなバッチ
        {
            batchSize = Math.Max(hddOptimalBatchSize, 2);
        }
        else if (totalSizeMB < 1000) // 1GB未満: 最小バッチ
        {
            batchSize = 2;
        }
        else // 1GB以上: 単一ファイル処理（ディスクI/O集中回避）
        {
            batchSize = 1;
        }
        
        // アーカイブ数による最終調整
        if (archiveCount <= 3)
        {
            batchSize = 1; // 少数のアーカイブは順次処理
        }
        
        return Math.Max(1, Math.Min(batchSize, archiveCount));
    }

    /// <summary>
    /// HDD用：バッチ間の最適な休憩時間を計算
    /// ディスクヘッドの移動時間とキャッシュクリア時間を考慮
    /// </summary>
    private int CalculateHDDRestTime(long totalArchiveSize, int batchSize)
    {
        var totalSizeMB = totalArchiveSize / (1024 * 1024);
        
        // HDDの場合：大きなファイルほど長い休憩が必要
        if (totalSizeMB > 1000) // 1GB以上
        {
            return 200; // 200ms休憩
        }
        else if (totalSizeMB > 500) // 500MB以上
        {
            return 100; // 100ms休憩
        }
        else if (totalSizeMB > 100) // 100MB以上
        {
            return 50; // 50ms休憩
        }
        else
        {
            return 0; // 休憩不要
        }
    }

    /// <summary>
    /// アーカイブファイルの最適なバッチサイズを計算（SSD用）
    /// </summary>
    private int CalculateOptimalBatchSize(int archiveCount, long totalArchiveSize)
    {
        // 基本バッチサイズ
        var baseBatchSize = Environment.ProcessorCount;
        
        // アーカイブサイズに基づく調整
        var totalSizeMB = totalArchiveSize / (1024 * 1024);
        
        int batchSize;
        
        if (totalSizeMB < 100) // 100MB未満: 大きなバッチ
        {
            batchSize = Math.Min(archiveCount, baseBatchSize * 3);
        }
        else if (totalSizeMB < 500) // 500MB未満: 中程度のバッチ
        {
            batchSize = Math.Min(archiveCount, baseBatchSize * 2);
        }
        else if (totalSizeMB < 2000) // 2GB未満: 小さなバッチ
        {
            batchSize = Math.Min(archiveCount, baseBatchSize);
        }
        else // 2GB以上: 最小バッチ
        {
            batchSize = Math.Min(archiveCount, Math.Max(baseBatchSize / 2, 2));
        }
        
        // アーカイブ数に基づく最終調整
        if (archiveCount <= 5)
        {
            batchSize = archiveCount; // 少数のアーカイブは全て同時処理
        }
        
        return Math.Max(1, batchSize);
    }

    private async Task<int> GetTotalFilesInArchiveAsync(string archivePath)
    {
        return await Task.Run(() =>
        {
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            
            if (extension == ".zip")
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
                return archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
            }
            else if (extension == ".rar")
            {
                using var archive = ArchiveFactory.Open(archivePath);
                return archive.Entries.Count(e => !e.IsDirectory);
            }
            
            return 0;
        });
    }


    private async Task PopulateImageMetadataAsync(ImageItem imageItem, string filePath, CancellationToken cancellationToken)
    {
        try
        {
            // HDD最適化：大きなバッファサイズでディスクI/O効率化
            var hddOptimalBufferSize = 64 * 1024; // 64KB バッファ（HDDのクラスターサイズに最適化）
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: hddOptimalBufferSize, useAsync: true);
            
            // ファイルサイズの検証
            if (fileStream.Length < 100)
            {
                throw new ArgumentException("File too small to be a valid image");
            }
            
            // キャッシュチェック
            var fileInfo = new FileInfo(filePath);
            var cacheKey = GenerateMetadataCacheKey(filePath, fileInfo.Length, fileInfo.LastWriteTime);
            if (TryGetCachedMetadata(cacheKey, out var cachedMetadata))
            {
                imageItem.Width = cachedMetadata.Width;
                imageItem.Height = cachedMetadata.Height;
                imageItem.ImageFormat = cachedMetadata.ImageFormat;
                imageItem.HasExifData = cachedMetadata.HasExifData;
                imageItem.DateTaken = cachedMetadata.DateTaken;
                return;
            }
            
            // ArrayPoolを使用してメモリ効率を向上
            var bufferSize = (int)fileStream.Length;
            var buffer = _byteArrayPool.Rent(bufferSize);
            
            try
            {
                await fileStream.ReadAsync(buffer, 0, bufferSize, cancellationToken);
                
                using var memoryStream = new MemoryStream(buffer, 0, bufferSize);
                PopulateImageMetadataFromStream(imageItem, memoryStream, filePath);
            }
            finally
            {
                _byteArrayPool.Return(buffer);
            }
            
            // キャッシュに保存
            CacheMetadata(cacheKey, imageItem);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read image metadata: {FilePath}", filePath);
            
            // メタデータ取得に失敗した場合はデフォルト値を設定
            imageItem.Width = 0;
            imageItem.Height = 0;
            imageItem.ImageFormat = Path.GetExtension(filePath).TrimStart('.');
            imageItem.HasExifData = false;
        }
    }


    private void PopulateImageMetadataFromStream(ImageItem imageItem, Stream stream, string fileName)
    {
        try
        {
            // ストリームの基本検証
            if (stream == null || !stream.CanRead)
            {
                throw new ArgumentException("Invalid stream");
            }
            
            // Lengthプロパティがサポートされている場合のみチェック
            if (stream.CanSeek && stream.Length == 0)
            {
                throw new ArgumentException("Empty stream");
            }

            // キャッシュチェック (ファイル名と日付でキー生成、stream.Lengthは非対応のため除外)
            var cacheKey = GenerateMetadataCacheKey(fileName, 0, DateTime.Now.Date);
            if (TryGetCachedMetadata(cacheKey, out var cachedMetadata))
            {
                imageItem.Width = cachedMetadata.Width;
                imageItem.Height = cachedMetadata.Height;
                imageItem.ImageFormat = cachedMetadata.ImageFormat;
                imageItem.HasExifData = cachedMetadata.HasExifData;
                imageItem.DateTaken = cachedMetadata.DateTaken;
                return; // キャッシュヒット
            }

            // 高速化: ヘッダー部分のみ読み取りでメタデータ取得を試行
            if (TryReadImageMetadataFast(stream, imageItem, fileName))
            {
                // 高速読み取り成功時はキャッシュに保存
                CacheMetadata(cacheKey, imageItem);
                return;
            }

            // フォールバック: 完全読み取り
            ReadImageMetadataComplete(stream, imageItem, fileName);
            
            // 完全読み取り成功時もキャッシュに保存
            CacheMetadata(cacheKey, imageItem);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read image metadata from stream: {FileName}", fileName);
            
            // エラー時はデフォルト値を設定
            imageItem.Width = 0;
            imageItem.Height = 0;
            imageItem.ImageFormat = Path.GetExtension(fileName).TrimStart('.');
            imageItem.HasExifData = false;
        }
    }

    private bool TryReadImageMetadataFast(Stream stream, ImageItem imageItem, string fileName)
    {
        try
        {
            if (!stream.CanSeek)
                return false;

            var originalPosition = stream.Position;
            
            // ヘッダー部分のみ読み取り (最初の8KB)
            const int HeaderSize = 8192;
            var headerBuffer = new byte[Math.Min(HeaderSize, (int)stream.Length)];
            stream.Read(headerBuffer, 0, headerBuffer.Length);
            
            using var headerStream = new MemoryStream(headerBuffer);
            
            // 高速画像形式判定とサイズ取得
            if (TryReadImageDimensionsFast(headerStream, imageItem, fileName))
            {
                return true;
            }
            
            // ストリーム位置を復元
            stream.Position = originalPosition;
            return false;
        }
        catch
        {
            // 高速読み取り失敗時は完全読み取りにフォールバック
            if (stream.CanSeek)
                stream.Position = 0;
            return false;
        }
    }

    private bool TryReadImageDimensionsFast(Stream headerStream, ImageItem imageItem, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        
        try
        {
            headerStream.Position = 0;
            
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return TryReadJpegDimensions(headerStream, imageItem);
                case ".png":
                    return TryReadPngDimensions(headerStream, imageItem);
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private bool TryReadJpegDimensions(Stream stream, ImageItem imageItem)
    {
        // JPEG形式の簡易パーシング
        var buffer = new byte[4];
        
        // JPEG署名チェック (FF D8)
        if (stream.Read(buffer, 0, 2) != 2 || buffer[0] != 0xFF || buffer[1] != 0xD8)
            return false;
            
        while (stream.Position < stream.Length - 1)
        {
            if (stream.Read(buffer, 0, 2) != 2)
                break;
                
            if (buffer[0] != 0xFF)
                continue;
                
            var marker = buffer[1];
            
            // SOF0, SOF1, SOF2 マーカー (Start of Frame)
            if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
            {
                // セグメント長を読み飛ばし
                if (stream.Read(buffer, 0, 2) != 2)
                    break;
                    
                // 精度を読み飛ばし
                if (stream.ReadByte() == -1)
                    break;
                    
                // 高さ (2バイト)
                if (stream.Read(buffer, 0, 2) != 2)
                    break;
                var height = (buffer[0] << 8) | buffer[1];
                
                // 幅 (2バイト)  
                if (stream.Read(buffer, 0, 2) != 2)
                    break;
                var width = (buffer[0] << 8) | buffer[1];
                
                if (width > 0 && height > 0 && width <= 50000 && height <= 50000)
                {
                    imageItem.Width = width;
                    imageItem.Height = height;
                    imageItem.ImageFormat = "JPEG";
                    imageItem.HasExifData = false;
                    return true;
                }
                break;
            }
            
            // 他のセグメントをスキップ
            if (stream.Read(buffer, 0, 2) != 2)
                break;
            var segmentLength = (buffer[0] << 8) | buffer[1] - 2;
            stream.Seek(segmentLength, SeekOrigin.Current);
        }
        
        return false;
    }

    private bool TryReadPngDimensions(Stream stream, ImageItem imageItem)
    {
        // PNG署名チェック
        var buffer = new byte[8];
        if (stream.Read(buffer, 0, 8) != 8)
            return false;
            
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        if (buffer[0] != 0x89 || buffer[1] != 0x50 || buffer[2] != 0x4E || buffer[3] != 0x47)
            return false;
            
        // IHDR chunk を探す
        if (stream.Read(buffer, 0, 8) != 8)
            return false;
            
        // IHDR の次の4バイトが "IHDR"
        if (buffer[4] != 'I' || buffer[5] != 'H' || buffer[6] != 'D' || buffer[7] != 'R')
            return false;
            
        // 幅 (4バイト, big-endian)
        if (stream.Read(buffer, 0, 4) != 4)
            return false;
        var width = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
        
        // 高さ (4バイト, big-endian)
        if (stream.Read(buffer, 0, 4) != 4)
            return false;
        var height = (buffer[0] << 24) | (buffer[1] << 16) | (buffer[2] << 8) | buffer[3];
        
        if (width > 0 && height > 0 && width <= 50000 && height <= 50000)
        {
            imageItem.Width = width;
            imageItem.Height = height;
            imageItem.ImageFormat = "PNG";
            imageItem.HasExifData = false;
            return true;
        }
        
        return false;
    }

    private void ReadImageMetadataComplete(Stream stream, ImageItem imageItem, string fileName)
    {
        // ストリームを完全にメモリに読み込んでからデコード（メモリ破損対策）
        using var memoryStream = new MemoryStream();
        if (stream.CanSeek)
            stream.Position = 0;
        stream.CopyTo(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        
        // 最小限の画像ファイルサイズチェック
        const int MinImageSize = 100;
        if (memoryStream.Length < MinImageSize)
        {
            throw new ArgumentException($"File too small to be a valid image ({memoryStream.Length} bytes)");
        }
        
        // BitmapCacheOption.OnDemandを使用してメモリ使用量を削減
        var decoder = BitmapDecoder.Create(memoryStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        
        if (decoder.Frames.Count == 0)
        {
            throw new InvalidOperationException("No frames found in image");
        }
        
        var frame = decoder.Frames[0];
        
        // 異常な画像サイズをチェック
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || 
            frame.PixelWidth > 50000 || frame.PixelHeight > 50000)
        {
            throw new ArgumentException("Invalid image dimensions");
        }
        
        imageItem.Width = frame.PixelWidth;
        imageItem.Height = frame.PixelHeight;
        imageItem.ImageFormat = decoder.CodecInfo?.FriendlyName ?? Path.GetExtension(fileName);
        
        // EXIF情報を安全に取得
        try
        {
            if (frame.Metadata is BitmapMetadata metadata && !string.IsNullOrEmpty(metadata.DateTaken))
            {
                imageItem.HasExifData = true;
                
                if (DateTime.TryParse(metadata.DateTaken, out var dateTime))
                {
                    imageItem.DateTaken = dateTime;
                }
            }
        }
        catch
        {
            // EXIF取得エラーは無視（メタデータは必須ではない）
            _logger.LogDebug("Failed to read EXIF data for: {FileName}", fileName);
        }
    }

    private string GenerateMetadataCacheKey(string filePath, long fileSize, DateTime lastModified)
    {
        return $"{filePath}|{fileSize}|{lastModified:yyyyMMddHHmmss}";
    }

    private bool TryGetCachedMetadata(string cacheKey, out ImageItem cachedItem)
    {
        cachedItem = null;
        
        if (_metadataCache.TryGetValue(cacheKey, out var cached))
        {
            // キャッシュヒット時にアクセス時刻を更新
            _metadataCache[cacheKey] = (cached.metadata, DateTime.Now);
            cachedItem = new ImageItem
            {
                Width = cached.metadata.Width,
                Height = cached.metadata.Height,
                ImageFormat = cached.metadata.ImageFormat,
                HasExifData = cached.metadata.HasExifData,
                DateTaken = cached.metadata.DateTaken
            };
            return true;
        }
        
        return false;
    }

    private void CacheMetadata(string cacheKey, ImageItem metadata)
    {
        // キャッシュサイズ制限チェック
        if (_metadataCache.Count >= _maxCacheSize)
        {
            CleanupCache();
        }
        
        // 基本メタデータのみキャッシュ（メモリ効率化）
        var cacheItem = new ImageItem
        {
            Width = metadata.Width,
            Height = metadata.Height,
            ImageFormat = metadata.ImageFormat,
            HasExifData = metadata.HasExifData,
            DateTaken = metadata.DateTaken
        };
        
        _metadataCache[cacheKey] = (cacheItem, DateTime.Now);
    }

    private void CleanupCache()
    {
        // LRU: 古いアクセスのアイテムを削除
        var itemsToRemove = _metadataCache
            .OrderBy(kvp => kvp.Value.lastAccess)
            .Take(_metadataCache.Count - _maxCacheSize + 100) // 100個余分に削除
            .Select(kvp => kvp.Key)
            .ToList();
            
        foreach (var key in itemsToRemove)
        {
            _metadataCache.TryRemove(key, out _);
        }
    }

    // 事前ファイルフィルタリング最適化
    private bool IsValidImageFile(string filePath, out FileInfo fileInfo)
    {
        fileInfo = null;
        
        try
        {
            // 拡張子チェック
            if (!IsImageFile(filePath))
                return false;
                
            fileInfo = new FileInfo(filePath);
            
            // ファイル存在チェック
            if (!fileInfo.Exists)
                return false;
                
            // サイズチェック (最小100バイト、最大100MB)
            if (fileInfo.Length < 100 || fileInfo.Length > 100 * 1024 * 1024)
                return false;
                
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidArchiveFile(string filePath, out FileInfo fileInfo)
    {
        fileInfo = null;
        
        try
        {
            // 拡張子チェック
            if (!IsArchiveFile(filePath))
                return false;
                
            fileInfo = new FileInfo(filePath);
            
            // ファイル存在チェック
            if (!fileInfo.Exists)
                return false;
                
            // サイズチェック (最小1KB、最大2GB)
            if (fileInfo.Length < 1024 || fileInfo.Length > 2L * 1024 * 1024 * 1024)
                return false;
                
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool IsValidArchiveEntry(string entryPath, long entrySize)
    {
        // 基本チェック
        if (string.IsNullOrEmpty(entryPath) || entrySize <= 0)
            return false;
            
        // 拡張子チェック (高速)
        var extension = Path.GetExtension(entryPath).ToLowerInvariant();
        if (!SupportedImageExtensions.Contains(extension))
            return false;
            
        // サイズチェック (100B - 50MB)
        if (entrySize < 100 || entrySize > 50 * 1024 * 1024)
            return false;
            
        // パス検証 (悪意のあるパスを除外)
        if (entryPath.Contains("..") || entryPath.StartsWith("/") || entryPath.Contains("\0"))
            return false;
            
        // システムファイル・隠しファイルを除外
        var fileName = Path.GetFileName(entryPath);
        if (fileName.StartsWith(".") || fileName.StartsWith("__MACOSX") || fileName.StartsWith("Thumbs.db"))
            return false;
            
        return true;
    }

    private ImageItem RentImageItem()
    {
        lock (_poolLock)
        {
            if (_imageItemPool.Count > 0)
            {
                var item = _imageItemPool.Dequeue();
                // オブジェクトをクリア
                ResetImageItem(item);
                return item;
            }
        }
        return new ImageItem();
    }

    private void ReturnImageItem(ImageItem item)
    {
        if (item == null) return;
        
        lock (_poolLock)
        {
            if (_imageItemPool.Count < 100) // プールサイズ制限
            {
                _imageItemPool.Enqueue(item);
            }
        }
    }

    private void ResetImageItem(ImageItem item)
    {
        item.Id = null;
        item.FilePath = null;
        item.FileName = null;
        item.FileSize = 0;
        item.Width = 0;
        item.Height = 0;
        item.ImageFormat = null;
        item.CreatedAt = DateTime.MinValue;
        item.ModifiedAt = DateTime.MinValue;
        item.ScanDate = DateTime.MinValue;
        item.ThumbnailPath = null;
        item.IsArchived = false;
        item.ArchivePath = null;
        item.InternalPath = null;
        item.HasExifData = false;
        item.DateTaken = null;
        item.ArchiveImageRatio = null;
    }

    public bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedImageExtensions.Contains(extension);
    }

    public bool IsArchiveFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedArchiveExtensions.Contains(extension);
    }

    private void OnScanProgress(ScanProgressEventArgs e)
    {
        ScanProgress?.Invoke(this, e);
    }

    /// <summary>
    /// HDD向け動的並行度調整
    /// ディスクの転送速度に応じて並行処理数を動的に調整
    /// </summary>
    private void AdjustConcurrencyForHDD(int processedFiles, TimeSpan elapsed)
    {
        var now = DateTime.Now;
        
        // 5秒間隔で性能チェック
        if ((now - _lastPerformanceCheck).TotalSeconds < 5) return;
        
        if (processedFiles > 0 && elapsed.TotalSeconds > 0)
        {
            var currentThroughput = processedFiles / elapsed.TotalSeconds;
            
            _logger.LogDebug("Performance check: {Throughput:F2} files/sec, current concurrency: {Concurrency}", 
                currentThroughput, _currentConcurrency);
            
            // DISABLED: SemaphoreSlim差し替えによるSemaphoreFullExceptionを防ぐため
            // 動的並行度調整は無効化し、固定値を使用
            /*
            // 前回より性能が悪化している場合は並行度を下げる
            if (_lastThroughput > 0 && currentThroughput < _lastThroughput * 0.8)
            {
                if (_currentConcurrency > 1)
                {
                    _currentConcurrency--;
                    var newSemaphore = new SemaphoreSlim(_currentConcurrency, _currentConcurrency);
                    var oldSemaphore = _concurrencyLimit;
                    _concurrencyLimit = newSemaphore;
                    oldSemaphore?.Dispose();
                    
                    _logger.LogInformation("HDD optimization: Reduced concurrency to {Concurrency} due to performance degradation", 
                        _currentConcurrency);
                }
            }
            // 性能が向上している場合は並行度を上げる（最大4まで）
            else if (_lastThroughput > 0 && currentThroughput > _lastThroughput * 1.2 && _currentConcurrency < 4)
            {
                _currentConcurrency++;
                var newSemaphore = new SemaphoreSlim(_currentConcurrency, _currentConcurrency);
                var oldSemaphore = _concurrencyLimit;
                _concurrencyLimit = newSemaphore;
                oldSemaphore?.Dispose();
                
                _logger.LogInformation("HDD optimization: Increased concurrency to {Concurrency} due to performance improvement", 
                    _currentConcurrency);
            }
            */
            
            _lastThroughput = currentThroughput;
        }
        
        _lastPerformanceCheck = now;
    }

    /// <summary>
    /// 増分スキャン：新規ディレクトリのみをスキャンし、削除されたディレクトリのデータを削除
    /// </summary>
    public async Task<int> IncrementalScanDirectoriesAsync(IEnumerable<string> directoryPaths, IDatabaseService databaseService, CancellationToken cancellationToken = default)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("=== INCREMENTAL SCAN STARTED ===");
        
        var directoryList = directoryPaths.ToList();
        var totalInserted = 0;
        
        try
        {
            // Step 1: 削除されたディレクトリの検出とクリーンアップ
            _logger.LogDebug("Starting DetectDeletedDirectoriesAsync");
            var deletedDirectories = await DetectDeletedDirectoriesAsync(directoryList, databaseService);
            _logger.LogDebug("DetectDeletedDirectoriesAsync completed. Found {Count} deleted directories", deletedDirectories.Count);
            if (deletedDirectories.Any())
            {
                var cleanupCount = await CleanupDeletedDirectoriesAsync(deletedDirectories, databaseService);
                _logger.LogInformation("Cleaned up {CleanupCount} items from {DeletedCount} deleted directories", 
                    cleanupCount, deletedDirectories.Count);
            }
            
            // Step 2: 新規・更新が必要なディレクトリの検出
            _logger.LogDebug("Starting DetectDirectoriesToScanAsync");
            var directoriesToScan = await DetectDirectoriesToScanAsync(directoryList, databaseService);
            _logger.LogDebug("DetectDirectoriesToScanAsync completed. Found {Count} directories to scan", directoriesToScan.Count);
            
            if (!directoriesToScan.Any())
            {
                _logger.LogInformation("No new directories to scan. Incremental scan completed.");
                return 0;
            }
            
            _logger.LogInformation("Incremental scan: {NewCount} directories need scanning out of {TotalCount} total directories", 
                directoriesToScan.Count, directoryList.Count);
            
            // Step 3: 新規ディレクトリのみをスキャン
            foreach (var directory in directoriesToScan)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                _logger.LogInformation("Incremental scanning directory: {DirectoryPath}", directory);
                
                var scanStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var inserted = await ScanDirectoryStreamingAsync(directory, databaseService, cancellationToken);
                scanStopwatch.Stop();
                
                // スキャン履歴を記録
                var scanHistory = new ScanHistory
                {
                    DirectoryPath = directory,
                    ScanDate = DateTime.Now,
                    FileCount = 0, // TODO: 実際のファイル数を記録
                    ProcessedCount = inserted,
                    ElapsedMs = scanStopwatch.ElapsedMilliseconds,
                    ScanType = "Incremental"
                };
                
                await databaseService.InsertScanHistoryAsync(scanHistory);
                totalInserted += inserted;
                
                _logger.LogInformation("Incremental scan completed for {DirectoryPath}: {InsertedCount} items in {ElapsedMs}ms", 
                    directory, inserted, scanStopwatch.ElapsedMilliseconds);
            }
            
            totalStopwatch.Stop();
            var totalTime = totalStopwatch.ElapsedMilliseconds;
            var directoriesPerSecond = totalTime > 0 ? (directoriesToScan.Count * 1000.0 / totalTime) : 0;
            
            _logger.LogInformation("=== INCREMENTAL SCAN COMPLETED ===");
            _logger.LogInformation("Incremental scan results: {DirectoryCount} directories, {TotalInserted} items inserted in {TotalTime}ms ({DirsPerSec:F2} dirs/sec)", 
                directoriesToScan.Count, totalInserted, totalTime, directoriesPerSecond);
            
            return totalInserted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Incremental scan failed");
            throw;
        }
    }

    /// <summary>
    /// 削除されたディレクトリを検出
    /// </summary>
    private async Task<List<string>> DetectDeletedDirectoriesAsync(List<string> currentDirectories, IDatabaseService databaseService)
    {
        // スキャン履歴からではなく、実際のデータベース内のアイテムから既存ディレクトリを取得
        var existingImageDirectories = await databaseService.GetImageDirectoriesAsync();
        var existingArchiveDirectories = await databaseService.GetArchiveDirectoriesAsync();
        var allExistingDirectories = existingImageDirectories.Concat(existingArchiveDirectories).Distinct().ToList();
        
        _logger.LogDebug("Retrieved {Count} existing directories from database items", allExistingDirectories.Count());
        
        var deletedDirectories = new List<string>();
        
        foreach (var existingDir in allExistingDirectories)
        {
            // 物理的に存在しないディレクトリを検出
            if (!Directory.Exists(existingDir))
            {
                deletedDirectories.Add(existingDir);
                continue;
            }
            
            // 現在の設定ディレクトリまたはそのサブディレクトリかどうかをチェック
            bool isWithinScanDirectories = currentDirectories.Any(scanDir => 
                existingDir.Equals(scanDir, StringComparison.OrdinalIgnoreCase) ||
                existingDir.StartsWith(scanDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            
            if (!isWithinScanDirectories)
            {
                deletedDirectories.Add(existingDir);
            }
        }
        
        _logger.LogDebug("Detected {DeletedCount} deleted directories out of {ExistingCount} existing directories", 
            deletedDirectories.Count, allExistingDirectories.Count());
        
        return deletedDirectories;
    }

    /// <summary>
    /// スキャンが必要なディレクトリを検出（新規または長期間スキャンしていない）
    /// </summary>
    private async Task<List<string>> DetectDirectoriesToScanAsync(List<string> directoryPaths, IDatabaseService databaseService)
    {
        var directoriesToScan = new List<string>();
        var scanThreshold = DateTime.Now.AddHours(-24); // 24時間以上経過したら再スキャン
        
        foreach (var directory in directoryPaths)
        {
            if (!Directory.Exists(directory))
            {
                _logger.LogWarning("Directory does not exist, skipping: {DirectoryPath}", directory);
                continue;
            }
            
            var lastScan = await databaseService.GetLastScanHistoryAsync(directory);
            
            if (lastScan == null)
            {
                // 初回スキャン
                _logger.LogDebug("New directory detected: {DirectoryPath}", directory);
                directoriesToScan.Add(directory);
            }
            else if (lastScan.ScanDate < scanThreshold)
            {
                // 24時間以上経過している場合は再スキャン
                _logger.LogDebug("Directory needs rescan (last scan: {LastScan}): {DirectoryPath}", 
                    lastScan.ScanDate.ToString("yyyy-MM-dd HH:mm:ss"), directory);
                directoriesToScan.Add(directory);
            }
            else
            {
                _logger.LogDebug("Directory recently scanned (last scan: {LastScan}), skipping: {DirectoryPath}", 
                    lastScan.ScanDate.ToString("yyyy-MM-dd HH:mm:ss"), directory);
            }
        }
        
        return directoriesToScan;
    }

    /// <summary>
    /// 削除されたディレクトリのデータをクリーンアップ
    /// </summary>
    private async Task<int> CleanupDeletedDirectoriesAsync(List<string> deletedDirectories, IDatabaseService databaseService)
    {
        var totalCleaned = 0;
        
        foreach (var directory in deletedDirectories)
        {
            try
            {
                // 先にサムネイルファイルを削除（データベースからアイテムを削除する前に）
                var thumbnailCleaned = await CleanupThumbnailsForDirectoryAsync(directory);
                _logger.LogDebug("Cleaned up {ThumbnailCount} thumbnail files for directory: {DirectoryPath}", 
                    thumbnailCleaned, directory);
                
                // その後でデータベースからアイテムを削除
                var cleaned = await databaseService.CleanupItemsByDirectoryAsync(directory);
                totalCleaned += cleaned;
                
                _logger.LogInformation("Cleaned up deleted directory: {DirectoryPath} ({CleanedCount} items)", 
                    directory, cleaned);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup deleted directory: {DirectoryPath}", directory);
            }
        }
        
        return totalCleaned;
    }

    /// <summary>
    /// 削除されたディレクトリのサムネイルファイルを削除
    /// </summary>
    private async Task<int> CleanupThumbnailsForDirectoryAsync(string directoryPath)
    {
        var deletedCount = 0;
        
        try
        {
            // サムネイルディレクトリを取得
            var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var executableDir = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;
            var thumbnailsDir = Path.Combine(executableDir, "Data", "Thumbnails");
            
            if (!Directory.Exists(thumbnailsDir)) return 0;
            
            _logger.LogDebug("Cleaning up thumbnails for deleted directory: {DirectoryPath}", directoryPath);
            
            // データベースから削除されたディレクトリのImageItemを取得
            var deletedItems = await _databaseService.GetAllImageItemsAsync();
            var itemsToDelete = deletedItems.Where(item => 
                item.FilePath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(item.ArchivePath) && item.ArchivePath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
            ).ToList();
            
            _logger.LogDebug("Found {ItemCount} items to cleanup thumbnails for in directory: {DirectoryPath}", 
                itemsToDelete.Count, directoryPath);
            
            // すべてのサムネイルサイズディレクトリをチェック
            var sizeDirectories = Directory.GetDirectories(thumbnailsDir, "size_*");
            
            foreach (var sizeDir in sizeDirectories)
            {
                foreach (var item in itemsToDelete)
                {
                    // 通常の画像ファイルのサムネイル
                    var thumbnailFileName = $"{item.Id}_192.jpg";
                    var thumbnailPath = Path.Combine(sizeDir, thumbnailFileName);
                    
                    if (File.Exists(thumbnailPath))
                    {
                        try
                        {
                            File.Delete(thumbnailPath);
                            deletedCount++;
                            _logger.LogDebug("Deleted thumbnail file: {ThumbnailPath}", thumbnailPath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete thumbnail file: {ThumbnailPath}", thumbnailPath);
                        }
                    }
                    
                    // アーカイブファイルのサムネイル
                    if (item.IsArchived)
                    {
                        var archiveThumbnailFileName = $"{item.Id}_192_archive.jpg";
                        var archiveThumbnailPath = Path.Combine(sizeDir, archiveThumbnailFileName);
                        
                        if (File.Exists(archiveThumbnailPath))
                        {
                            try
                            {
                                File.Delete(archiveThumbnailPath);
                                deletedCount++;
                                _logger.LogDebug("Deleted archive thumbnail file: {ThumbnailPath}", archiveThumbnailPath);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete archive thumbnail file: {ThumbnailPath}", archiveThumbnailPath);
                            }
                        }
                    }
                }
            }
            
            _logger.LogInformation("Cleaned up {DeletedCount} thumbnail files for directory: {DirectoryPath}", 
                deletedCount, directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup thumbnails for directory: {DirectoryPath}", directoryPath);
        }
        
        return deletedCount;
    }

    public void Dispose()
    {
        _concurrencyLimit?.Dispose();
    }
}