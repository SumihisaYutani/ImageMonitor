using ImageMonitor.Models;
using ImageMonitor.Services;
using System.Windows;

namespace ImageMonitor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigurationService _configService;
    private readonly IDatabaseService _databaseService;
    private readonly IImageScanService _imageScanService;
    private readonly ILauncherService _launcherService;
    private readonly IMessagingService _messagingService;
    private readonly IThumbnailService _thumbnailService;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource _cancellationTokenSource = new();

    [ObservableProperty]
    private ObservableCollection<IDisplayItem> _displayItems = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private SortBy _sortBy = SortBy.FileName;

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Ascending;

    [ObservableProperty]
    private int _totalItems;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private IDisplayItem? _selectedDisplayItem;
    
    // Backwards compatibility property for UI code
    public IDisplayItem? SelectedImageItem => SelectedDisplayItem;

    [ObservableProperty]
    private int _thumbnailSize = 192;

    [ObservableProperty]
    private bool _isPropertiesPanelVisible = true;

    // ランダムリスト機能関連
    [ObservableProperty]
    private List<IDisplayItem> _randomList = new();
    
    [ObservableProperty]
    private int _currentRandomIndex = -1;
    
    [ObservableProperty]
    private bool _isRandomListActive = false;

    // 検索結果キャッシュ
    private readonly Dictionary<string, IEnumerable<ArchiveItem>> _searchCache = new();
    private string _lastSearchQuery = string.Empty;


    public MainViewModel(
        IConfigurationService configService,
        IDatabaseService databaseService,
        IImageScanService imageScanService,
        ILauncherService launcherService,
        IMessagingService messagingService,
        IThumbnailService thumbnailService,
        ILogger<MainViewModel> logger)
    {
        _configService = configService;
        _databaseService = databaseService;
        _imageScanService = imageScanService;
        _launcherService = launcherService;
        _messagingService = messagingService;
        _thumbnailService = thumbnailService;
        _logger = logger;
        
        // Subscribe to thumbnail size changes
        _messagingService.ThumbnailSizeChanged += OnThumbnailSizeChanged;
        
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing MainViewModel");
            
            // Load settings
            var settings = await _configService.GetSettingsAsync();
            SortBy = SortBy.FileName;
            SortDirection = SortDirection.Ascending;
            ThumbnailSize = settings.ThumbnailSize;
            
            _logger.LogDebug("MainViewModel initialized with ThumbnailSize: {ThumbnailSize}", ThumbnailSize);
            
            // Load existing items from database
            await LoadImageItemsAsync();
            
            _logger.LogInformation("MainViewModel initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MainViewModel");
            StatusText = $"Initialization failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ScanImagesAsync()
    {
        if (IsScanning)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();
            return;
        }

        try
        {
            IsScanning = true;
            StatusText = "Scanning for images...";
            _logger.LogInformation("Starting image scan");
            
            var settings = await _configService.GetSettingsAsync();
            
            _logger.LogInformation("ScanImagesAsync - DirectoryCount: {DirectoryCount}", settings.ScanDirectories.Count);
            
            if (!settings.ScanDirectories.Any())
            {
                _logger.LogInformation("No scan directories configured. Running cleanup for removed directories.");
                StatusText = "Cleaning up removed directories...";
                
                // スキャンディレクトリがない場合でも削除検出とクリーンアップを実行
                var cleanupResults = await _imageScanService.ScanDirectoriesAsync(
                    new List<string>(), _cancellationTokenSource.Token);
                
                // UI更新
                await LoadImageItemsAsync();
                
                StatusText = "Cleanup completed. No scan directories configured.";
                return;
            }

            // 総合時間計測開始
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            _logger.LogInformation("=== SCAN PERFORMANCE MEASUREMENT STARTED ===");
            
            // スキャン進捗イベントを購読
            _imageScanService.ScanProgress += OnScanProgress;
            
            try
            {
                var scanStopwatch = System.Diagnostics.Stopwatch.StartNew();
                int insertedCount = 0;
                
                _logger.LogInformation("Starting full scan for {DirectoryCount} directories", settings.ScanDirectories.Count);
                StatusText = "Performing full scan...";
                var scanResults = await _imageScanService.ScanDirectoriesAsync(
                    settings.ScanDirectories, _cancellationTokenSource.Token);
                scanStopwatch.Stop();
                
                var scanTimeMs = scanStopwatch.ElapsedMilliseconds;
                
                // 実際に処理されたアーカイブアイテム数を取得
                var archiveCount = await _databaseService.GetArchiveItemCountAsync();
                var imageCount = await _databaseService.GetImageItemCountAsync();
                var totalProcessedItems = archiveCount + imageCount;
                
                _logger.LogInformation("Full scanning phase completed in {ScanTime}ms - Found {ItemCount} items (Archives: {ArchiveCount}, Images: {ImageCount})", 
                    scanTimeMs, totalProcessedItems, archiveCount, imageCount);
                
                // データベース保存時間計測
                if (scanResults.Any())
                {
                    StatusText = "Saving to database...";
                    var dbStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    insertedCount = await _databaseService.BulkInsertImageItemsAsync(scanResults);
                    dbStopwatch.Stop();
                
                    _logger.LogInformation("Database insertion completed in {DbTime}ms - Inserted {Count} items", 
                        dbStopwatch.ElapsedMilliseconds, insertedCount);
                }
                
                // 単一画像アイテムをクリーンアップ（アーカイブのみ表示するため）
                var cleanupCount = await _databaseService.CleanupSingleImageItemsAsync();
                if (cleanupCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} single image items", cleanupCount);
                }

                // UI更新時間計測
                var uiStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await LoadImageItemsAsync();
                uiStopwatch.Stop();
                
                totalStopwatch.Stop();
                var totalTimeMs = totalStopwatch.ElapsedMilliseconds;
                var totalTimeSec = totalTimeMs / 1000.0;
                var itemsPerSecond = insertedCount > 0 && totalTimeMs > 0 ? (insertedCount * 1000.0 / totalTimeMs) : 0;
                
                // 詳細パフォーマンスレポート
                _logger.LogInformation("=== SCAN PERFORMANCE REPORT ===");
                _logger.LogInformation("Total execution time: {TotalTime}ms ({TotalTimeSec:F1} seconds)", totalTimeMs, totalTimeSec);
                _logger.LogInformation("Scanning phase: {ScanTime}ms ({ScanPercent:F1}%)", 
                    scanStopwatch.ElapsedMilliseconds, (scanStopwatch.ElapsedMilliseconds * 100.0 / totalTimeMs));
                _logger.LogInformation("UI update phase: {UiTime}ms ({UiPercent:F1}%)", 
                    uiStopwatch.ElapsedMilliseconds, (uiStopwatch.ElapsedMilliseconds * 100.0 / totalTimeMs));
                _logger.LogInformation("Performance: {ItemsPerSec:F2} items/second", itemsPerSecond);
                _logger.LogInformation("Items inserted: {InsertedCount} (Database total: {TotalItems})", insertedCount, totalProcessedItems);
                _logger.LogInformation("Scan type: Full");
                _logger.LogInformation("=== END PERFORMANCE REPORT ===");
                
                StatusText = $"Full scan completed in {totalTimeSec:F1}s - Found {totalProcessedItems} items ({itemsPerSecond:F1} items/sec)";
            }
            finally
            {
                _imageScanService.ScanProgress -= OnScanProgress;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
            _logger.LogInformation("Image scan cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during image scan");
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task SearchImagesAsync()
    {
        try
        {
            var searchQuery = SearchQuery?.Trim() ?? string.Empty;
            
            // 空の検索クエリの場合は全てのアイテムを表示
            if (string.IsNullOrEmpty(searchQuery))
            {
                await LoadImageItemsAsync();
                return;
            }
            
            StatusText = "Searching...";
            
            // キャッシュをチェック
            var cacheKey = $"{searchQuery}_{SortBy}_{SortDirection}";
            if (_searchCache.TryGetValue(cacheKey, out var cachedResults))
            {
                _logger.LogDebug("Using cached search results for query: {Query}", searchQuery);
                
                // キャッシュされた結果をUIに表示（パフォーマンス最適化）
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DisplayItems = new ObservableCollection<IDisplayItem>(cachedResults);
                    TotalItems = DisplayItems.Count;
                    StatusText = $"Found {TotalItems} images (cached)";
                });
                return;
            }
            
            var filter = new SearchFilter
            {
                Query = searchQuery,
                IncludeArchives = true, // Always include archives
                SortBy = SortBy,
                SortDirection = SortDirection,
                PageSize = 1000 // For now, load all results
            };

            var results = await _databaseService.SearchArchiveItemsAsync(filter);
            var resultsList = results.ToList(); // リストに変換してキャッシュ
            
            // キャッシュに保存（最大10件まで）
            if (_searchCache.Count >= 10)
            {
                var oldestKey = _searchCache.Keys.First();
                _searchCache.Remove(oldestKey);
            }
            _searchCache[cacheKey] = resultsList;
            
            // UIスレッドでObservableCollectionを更新（パフォーマンス最適化）
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                DisplayItems = new ObservableCollection<IDisplayItem>(resultsList);
                TotalItems = DisplayItems.Count;
                StatusText = $"Found {TotalItems} images";
            });
            
            _lastSearchQuery = searchQuery;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during image search");
            StatusText = $"Search failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        SearchQuery = string.Empty;
        await LoadImageItemsAsync();
    }

    [RelayCommand]
    private async Task OpenImageAsync(IDisplayItem? displayItem)
    {
        _logger.LogInformation("OpenImageAsync called with displayItem: {Item}", displayItem?.FilePath ?? "null");
        
        if (displayItem == null)
            return;

        try
        {
            bool success;
            
            if (displayItem.IsArchived && displayItem is ImageItem imageItem && !string.IsNullOrEmpty(imageItem.ArchivePath))
            {
                // アーカイブビューワーで開く
                _logger.LogInformation("Opening archive: {ArchivePath}", imageItem.ArchivePath);
                success = await _launcherService.LaunchArchiveViewerAsync(imageItem.ArchivePath);
            }
            else
            {
                // 通常の画像ファイルを開く
                _logger.LogInformation("Opening image: {FilePath}", displayItem.FilePath);
                success = await _launcherService.LaunchAssociatedAppAsync(displayItem.FilePath);
            }
            
            if (!success)
            {
                StatusText = "Failed to open file - no associated application found";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open image: {FilePath}", displayItem.FilePath);
            StatusText = $"Failed to open image: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(IDisplayItem? displayItem)
    {
        _logger.LogInformation("OpenFolderAsync called with displayItem: {Item}", displayItem?.FilePath ?? "null");
        
        if (displayItem == null)
            return;

        try
        {
            bool success = false;

            // まずはファイル選択表示を試みる（成功すれば目的達成）
            var targetFile = displayItem.IsArchived && displayItem is ImageItem imageItem && !string.IsNullOrEmpty(imageItem.ArchivePath)
                ? imageItem.ArchivePath!
                : displayItem.FilePath;

            success = await _launcherService.ShowInExplorerAsync(targetFile);

            // 失敗時はフォルダを開くフォールバック
            if (!success)
            {
                var folder = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrEmpty(folder))
                {
                    success = await _launcherService.OpenFolderAsync(folder);
                }
            }

            if (!success)
            {
                StatusText = "Failed to open folder";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder for: {FilePath}", displayItem.FilePath);
            StatusText = $"Failed to open folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadImageItemsAsync();
    }

    private async Task LoadImageItemsAsync()
    {
        var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepTimes = new List<(string step, long ms)>();
        
        try
        {
            _logger.LogInformation("[PERF] Starting LoadImageItemsAsync");
            StatusText = "Loading images...";
            
            _logger.LogDebug("Getting total count...");
            // Get total count first - combine both archive and image items
            var archiveCount = await _databaseService.GetArchiveItemCountAsync();
            var imageCount = await _databaseService.GetImageItemCountAsync();
            var totalCount = archiveCount + imageCount;
            TotalItems = (int)totalCount;
            stepTimes.Add(("Database count query", loadStopwatch.ElapsedMilliseconds));
            _logger.LogDebug("Total count: {Count} (Archives: {ArchiveCount}, Images: {ImageCount})", totalCount, archiveCount, imageCount);
            
            // Load first batch immediately
            const int initialBatchSize = 50;
            _logger.LogDebug("Loading initial batch of {Size} archive items", initialBatchSize);
            var initialArchiveItems = await _databaseService.GetArchiveItemsAsync(0, initialBatchSize);
            var initialArchiveItemsList = initialArchiveItems.ToList();
            stepTimes.Add(("Initial archive items query", loadStopwatch.ElapsedMilliseconds));
            _logger.LogDebug("Retrieved {Count} initial archive items", initialArchiveItemsList.Count);
            
            // Also load non-archived ImageItems
            var regularImageItems = await _databaseService.GetNonArchivedImageItemsAsync(0, initialBatchSize);
            var regularImageItemsList = regularImageItems.ToList();
            stepTimes.Add(("Regular image items query", loadStopwatch.ElapsedMilliseconds));
            _logger.LogDebug("Retrieved {Count} regular image items", regularImageItemsList.Count);
            
            // UIレベルでソートを適用（データベースソートを除去したため）
            var sortedArchiveItems = ApplyUILevelSort(initialArchiveItemsList.Cast<IDisplayItem>()).ToList();
            var sortedImageItems = ApplyUILevelSort(regularImageItemsList.Cast<IDisplayItem>()).ToList();
            
            // UIスレッドでObservableCollectionを一括更新（パフォーマンス最適化）
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var allItems = sortedArchiveItems.Concat(sortedImageItems).ToList();
                DisplayItems = new ObservableCollection<IDisplayItem>(allItems);
                _logger.LogDebug("Replaced DisplayItems with {Count} items (Archives: {ArchiveCount}, Images: {ImageCount})", 
                    allItems.Count, sortedArchiveItems.Count, sortedImageItems.Count);
            });
            
            stepTimes.Add(("UI collection update", loadStopwatch.ElapsedMilliseconds));
            
            StatusText = $"Loaded {DisplayItems.Count} of {TotalItems} items";
            
            loadStopwatch.Stop();
            var totalLoadTime = loadStopwatch.ElapsedMilliseconds;
            
            // パフォーマンス詳細情報をログ出力
            var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
            {
                var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                var stepDuration = step.ms - prevTime;
                return $"{step.step}: {stepDuration}ms";
            }));
            
            if (totalLoadTime > 1000) // 1秒以上は警告
            {
                _logger.LogWarning("[PERF] Slow initial load: {TotalTime}ms - Steps: {StepDetails}", totalLoadTime, stepDetails);
            }
            else
            {
                _logger.LogInformation("[PERF] Initial load completed: {TotalTime}ms - Steps: {StepDetails}", totalLoadTime, stepDetails);
            }
            
            // Load remaining items progressively in background
            if (totalCount > initialBatchSize)
            {
                _logger.LogDebug("Starting background loading for remaining items");
                _ = Task.Run(async () => await LoadRemainingItemsAsync(initialBatchSize));
            }
            else
            {
                _logger.LogDebug("All items loaded in initial batch");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load image items");
            StatusText = $"Failed to load images: {ex.Message}";
        }
    }
    
    private async Task LoadRemainingItemsAsync(int skip)
    {
        var backgroundStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var batchTimes = new List<(int batch, long ms, int itemCount)>();
        
        try
        {
            _logger.LogInformation("[PERF] Starting reliable offset-based pagination");
            const int batchSize = 300; // さらに大きなバッチサイズ
            var totalCount = TotalItems;
            var loaded = skip;
            
            _logger.LogInformation("[PERF] Loading all {TotalItems} items with offset-based pagination, starting from offset: {Skip}", 
                TotalItems, skip);
            
            var batchNumber = 1;
            while (loaded < totalCount)
            {
                var batchStart = backgroundStopwatch.ElapsedMilliseconds;
                
                // offset-basedクエリを使用（確実で信頼性あり）
                var archiveItems = await _databaseService.GetArchiveItemsAfterIdAsync(loaded.ToString(), batchSize);
                var itemsList = archiveItems.Cast<IDisplayItem>().ToList();
                
                var batchEnd = backgroundStopwatch.ElapsedMilliseconds;
                batchTimes.Add((batchNumber, batchEnd - batchStart, itemsList.Count));
                
                if (!itemsList.Any())
                    break;
                
                // UIレベルでソートしてから追加
                var sortedItems = ApplyUILevelSort(itemsList).ToList();
                
                // Add sorted items to UI thread in batch (performance optimization)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var currentItems = DisplayItems.ToList();
                    currentItems.AddRange(sortedItems);
                    DisplayItems = new ObservableCollection<IDisplayItem>(currentItems);
                    StatusText = $"Loaded {DisplayItems.Count} of {TotalItems} images";
                });
                
                loaded += itemsList.Count;
                batchNumber++;
                
                // Small delay to prevent UI blocking
                await Task.Delay(2); // さらに短縮
            }
            
            backgroundStopwatch.Stop();
            var totalBackgroundTime = backgroundStopwatch.ElapsedMilliseconds;
            
            // バックグラウンド読み込みパフォーマンスログ
            var batchDetails = string.Join(", ", batchTimes.Select(b => $"Batch {b.batch}: {b.ms}ms ({b.itemCount} items)"));
            
            if (totalBackgroundTime > 5000) // 5秒以上は警告
            {
                _logger.LogWarning("[PERF] Slow background load: {TotalTime}ms - Batches: {BatchDetails}", totalBackgroundTime, batchDetails);
            }
            else
            {
                _logger.LogInformation("[PERF] Background load completed: {TotalTime}ms - Batches: {BatchDetails}", totalBackgroundTime, batchDetails);
            }
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = $"Loaded all {DisplayItems.Count} images";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load remaining items");
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = $"Partial load completed - {DisplayItems.Count} images";
            });
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        // 自動検索は無効化 - 手動検索のみ
        // Auto-search disabled - manual search only
    }

    [RelayCommand]
    private async Task ToggleSortDirectionAsync()
    {
        SortDirection = SortDirection == SortDirection.Ascending 
            ? SortDirection.Descending 
            : SortDirection.Ascending;
        await SearchImagesAsync();
    }

    partial void OnSortByChanged(SortBy value)
    {
        _ = SearchImagesAsync();
    }

    partial void OnSortDirectionChanged(SortDirection value)
    {
        _ = SearchImagesAsync();
    }

    private void OnScanProgress(object? sender, ScanProgressEventArgs e)
    {
        // UIスレッドで実行
        Application.Current.Dispatcher.Invoke(() =>
        {
            StatusText = e.Message;
            
            if (e.IsCompleted)
            {
                _logger.LogInformation("Scan completed: {ProcessedFiles}/{TotalFiles} files processed", 
                    e.ProcessedFiles, e.TotalFiles);
            }
        });
    }

    private async void OnThumbnailSizeChanged(object? sender, ThumbnailSizeChangedEventArgs e)
    {
        try
        {
            _logger.LogDebug("OnThumbnailSizeChanged event received with size: {Size}", e.NewSize);
            _logger.LogDebug("Current ThumbnailSize property: {CurrentSize}", ThumbnailSize);
            _logger.LogInformation("Thumbnail size changed to {Size}, regenerating thumbnails...", e.NewSize);
            StatusText = "Regenerating thumbnails with new size...";
            
            // Update the thumbnail size property first to update UI layout
            var oldSize = ThumbnailSize;
            ThumbnailSize = e.NewSize;
            _logger.LogDebug("ThumbnailSize property updated from {OldSize} to {NewSize}", oldSize, ThumbnailSize);
            
            // Clear existing thumbnail cache to force regeneration with new size
            await _thumbnailService.ClearThumbnailCacheAsync();
            _logger.LogDebug("Thumbnail cache cleared");
            
            // Regenerate thumbnails for all items currently in view
            await RegenerateThumbnailsAsync();
            _logger.LogDebug("Thumbnails regenerated");
            
            StatusText = $"Thumbnails updated to size {e.NewSize}px";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update thumbnails with new size");
            StatusText = "Failed to update thumbnail size";
        }
    }

    private async Task RegenerateThumbnailsAsync()
    {
        try
        {
            var settings = await _configService.GetSettingsAsync();
            var thumbnailTasks = new List<Task>();
            
            foreach (var item in DisplayItems)
            {
                var task = Task.Run(async () =>
                {
                    try
                    {
                        // サムネイル再生成を実装
                        if (item is ArchiveItem archiveItem)
                        {
                            await _thumbnailService.GenerateArchiveThumbnailAsync(archiveItem.FilePath, ThumbnailSize);
                        }
                        else if (item is ImageItem imageItem)
                        {
                            await _thumbnailService.GenerateThumbnailAsync(imageItem.FilePath, ThumbnailSize);
                        }
                        _logger.LogDebug("Regenerated thumbnail for {FilePath} at size {Size}", item.FilePath, ThumbnailSize);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to regenerate thumbnail for {FilePath}", item.FilePath);
                    }
                });
                
                thumbnailTasks.Add(task);
                
                // Process in batches to avoid overwhelming the system
                if (thumbnailTasks.Count >= 10)
                {
                    await Task.WhenAll(thumbnailTasks);
                    thumbnailTasks.Clear();
                }
            }
            
            // Process remaining tasks
            if (thumbnailTasks.Count > 0)
            {
                await Task.WhenAll(thumbnailTasks);
            }
            
            // サムネイル再生成後にThumbnailPathを更新してUIを更新
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 各アイテムのThumbnailPathを新しいサイズで更新
                var updatedItems = DisplayItems.Select(item =>
                {
                    if (item is ArchiveItem archiveItem)
                    {
                        archiveItem.ThumbnailPath = _thumbnailService.GetThumbnailPath(archiveItem.FilePath, ThumbnailSize);
                        return archiveItem;
                    }
                    else if (item is ImageItem imageItem)
                    {
                        imageItem.ThumbnailPath = _thumbnailService.GetThumbnailPath(imageItem.FilePath, ThumbnailSize);
                        return imageItem;
                    }
                    return item;
                }).ToList();
                
                // UIを完全に更新
                DisplayItems = new ObservableCollection<IDisplayItem>(updatedItems);
                _logger.LogInformation("UI refreshed after thumbnail regeneration for {Count} items with new thumbnail paths (size: {Size})", 
                    updatedItems.Count, ThumbnailSize);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during thumbnail regeneration");
        }
    }

    /// <summary>
    /// UIレベルでソートを適用する（データベースソート除去のため）
    /// </summary>
    private IEnumerable<IDisplayItem> ApplyUILevelSort(IEnumerable<IDisplayItem> items)
    {
        return SortBy switch
        {
            SortBy.FileName => SortDirection == SortDirection.Ascending 
                ? items.OrderBy(x => x.FileName) 
                : items.OrderByDescending(x => x.FileName),
            SortBy.FileSize => SortDirection == SortDirection.Ascending 
                ? items.OrderBy(x => x.FileSize) 
                : items.OrderByDescending(x => x.FileSize),
            SortBy.CreatedAt => SortDirection == SortDirection.Ascending 
                ? items.OrderBy(x => x.CreatedAt) 
                : items.OrderByDescending(x => x.CreatedAt),
            SortBy.ModifiedAt => SortDirection == SortDirection.Ascending 
                ? items.OrderBy(x => x.ModifiedAt) 
                : items.OrderByDescending(x => x.ModifiedAt),
            SortBy.DateTaken => SortDirection == SortDirection.Ascending 
                ? items.OrderBy(x => x.CreatedAt)  // DateTakenがないのでCreatedAtを使用
                : items.OrderByDescending(x => x.CreatedAt),
            SortBy.Width => SortDirection == SortDirection.Ascending 
                ? items.OrderBy(x => x.FileSize)  // WidthがないのでFileSizeを使用
                : items.OrderByDescending(x => x.FileSize),
            SortBy.Height => SortDirection == SortDirection.Ascending 
                ? items.OrderBy(x => x.FileSize)  // HeightがないのでFileSizeを使用
                : items.OrderByDescending(x => x.FileSize),
            // デフォルト: ScanDateで最新順（データベースソートから移行）
            _ => items.OrderByDescending(x => x.ScanDate)
        };
    }

    /// <summary>
    /// 表示されているサムネイルからランダムリストを作成
    /// </summary>
    [RelayCommand]
    private async Task CreateRandomListAsync()
    {
        try
        {
            if (!DisplayItems.Any())
            {
                StatusText = "ランダムリストを作成するアイテムがありません";
                return;
            }

            // 現在表示されているアイテムからランダムリストを作成
            var random = new Random();
            RandomList = DisplayItems.ToList().OrderBy(x => random.Next()).ToList();
            CurrentRandomIndex = 0;
            IsRandomListActive = true;

            // 最初のアイテムを選択（ファイルは開かない）
            if (RandomList.Any())
            {
                SelectedDisplayItem = RandomList[0];
            }

            StatusText = $"ランダムリスト作成完了: {RandomList.Count}個のアイテム";
            _logger.LogInformation("Random list created with {Count} items", RandomList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create random list");
            StatusText = $"ランダムリスト作成失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// ランダムリストの次のアイテムを開く
    /// </summary>
    [RelayCommand]
    private async Task OpenNextRandomAsync()
    {
        try
        {
            if (!IsRandomListActive || !RandomList.Any())
            {
                StatusText = "ランダムリストが作成されていません";
                return;
            }

            if (CurrentRandomIndex >= RandomList.Count - 1)
            {
                StatusText = "ランダムリストの最後のアイテムです";
                return;
            }

            CurrentRandomIndex++;
            var nextItem = RandomList[CurrentRandomIndex];
            SelectedDisplayItem = nextItem;
            await OpenImageAsync(nextItem);

            StatusText = $"ランダムリスト: {CurrentRandomIndex + 1}/{RandomList.Count}";
            _logger.LogDebug("Opened next random item: {Index}/{Total}", CurrentRandomIndex + 1, RandomList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open next random item");
            StatusText = $"次のアイテムを開けませんでした: {ex.Message}";
        }
    }

    /// <summary>
    /// ランダムリストの前のアイテムを開く
    /// </summary>
    [RelayCommand]
    private async Task OpenPreviousRandomAsync()
    {
        try
        {
            if (!IsRandomListActive || !RandomList.Any())
            {
                StatusText = "ランダムリストが作成されていません";
                return;
            }

            if (CurrentRandomIndex <= 0)
            {
                StatusText = "ランダムリストの最初のアイテムです";
                return;
            }

            CurrentRandomIndex--;
            var previousItem = RandomList[CurrentRandomIndex];
            SelectedDisplayItem = previousItem;
            await OpenImageAsync(previousItem);

            StatusText = $"ランダムリスト: {CurrentRandomIndex + 1}/{RandomList.Count}";
            _logger.LogDebug("Opened previous random item: {Index}/{Total}", CurrentRandomIndex + 1, RandomList.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open previous random item");
            StatusText = $"前のアイテムを開けませんでした: {ex.Message}";
        }
    }
}
