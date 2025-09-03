using ImageMonitor.Models;
using ImageMonitor.Services;

namespace ImageMonitor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IConfigurationService _configService;
    private readonly IDatabaseService _databaseService;
    private readonly IImageScanService _imageScanService;
    private readonly ILauncherService _launcherService;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource _cancellationTokenSource = new();
    private CancellationTokenSource _searchDelayTokenSource = new();

    [ObservableProperty]
    private ObservableCollection<ImageItem> _imageItems = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _includeArchives = true;

    [ObservableProperty]
    private decimal _imageRatioThreshold = 0.5m;

    [ObservableProperty]
    private int _totalItems;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private ImageItem? _selectedImageItem;

    public MainViewModel(
        IConfigurationService configService,
        IDatabaseService databaseService,
        IImageScanService imageScanService,
        ILauncherService launcherService,
        ILogger<MainViewModel> logger)
    {
        _configService = configService;
        _databaseService = databaseService;
        _imageScanService = imageScanService;
        _launcherService = launcherService;
        _logger = logger;
        
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing MainViewModel");
            
            // Load settings
            var settings = await _configService.GetSettingsAsync();
            IncludeArchives = true;
            ImageRatioThreshold = settings.ImageRatioThreshold;
            
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
            
            if (!settings.ScanDirectories.Any())
            {
                StatusText = "No scan directories configured. Please add directories in settings.";
                return;
            }

            // スキャン進捗イベントを購読
            _imageScanService.ScanProgress += OnScanProgress;
            
            try
            {
                var scanResults = await _imageScanService.ScanDirectoriesAsync(
                    settings.ScanDirectories, _cancellationTokenSource.Token);
                
                // データベースに保存
                if (scanResults.Any())
                {
                    StatusText = "Saving to database...";
                    var insertedCount = await _databaseService.BulkInsertImageItemsAsync(scanResults);
                    _logger.LogInformation("Inserted {Count} new image items", insertedCount);
                }
                
                // UI更新
                await LoadImageItemsAsync();
                StatusText = $"Scan completed - Found {scanResults.Count} items";
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
            StatusText = "Searching...";
            
            var filter = new SearchFilter
            {
                Query = SearchQuery,
                IncludeArchives = IncludeArchives,
                ImageRatioThreshold = ImageRatioThreshold,
                PageSize = 1000 // For now, load all results
            };

            var results = await _databaseService.SearchImageItemsAsync(filter);
            
            ImageItems.Clear();
            foreach (var item in results)
            {
                ImageItems.Add(item);
            }
            
            TotalItems = ImageItems.Count;
            StatusText = $"Found {TotalItems} images";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during image search");
            StatusText = $"Search failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenImageAsync(ImageItem? imageItem)
    {
        if (imageItem == null)
            return;

        try
        {
            bool success;
            
            if (imageItem.IsArchived && !string.IsNullOrEmpty(imageItem.ArchivePath))
            {
                // アーカイブビューワーで開く
                _logger.LogInformation("Opening archive: {ArchivePath}", imageItem.ArchivePath);
                success = await _launcherService.LaunchArchiveViewerAsync(imageItem.ArchivePath);
            }
            else
            {
                // 通常の画像ファイルを開く
                _logger.LogInformation("Opening image: {FilePath}", imageItem.FilePath);
                success = await _launcherService.LaunchAssociatedAppAsync(imageItem.FilePath);
            }
            
            if (!success)
            {
                StatusText = "Failed to open file - no associated application found";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open image: {FilePath}", imageItem.FilePath);
            StatusText = $"Failed to open image: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync(ImageItem? imageItem)
    {
        if (imageItem == null)
            return;

        try
        {
            bool success;
            
            if (imageItem.IsArchived && !string.IsNullOrEmpty(imageItem.ArchivePath))
            {
                // アーカイブファイルをエクスプローラーで選択表示
                success = await _launcherService.ShowInExplorerAsync(imageItem.ArchivePath);
            }
            else
            {
                // 通常のファイルをエクスプローラーで選択表示
                success = await _launcherService.ShowInExplorerAsync(imageItem.FilePath);
            }
            
            if (!success)
            {
                StatusText = "Failed to open folder";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder for: {FilePath}", imageItem.FilePath);
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
        try
        {
            StatusText = "Loading images...";
            
            var items = await _databaseService.GetAllImageItemsAsync();
            
            ImageItems.Clear();
            foreach (var item in items)
            {
                ImageItems.Add(item);
            }
            
            TotalItems = ImageItems.Count;
            StatusText = $"Loaded {TotalItems} images";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load image items");
            StatusText = $"Failed to load images: {ex.Message}";
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        // Auto-search when query changes (with debounce in real implementation)
        _ = Task.Delay(500).ContinueWith(async _ => 
        {
            if (SearchQuery == value) // Check if query hasn't changed
            {
                await SearchImagesAsync();
            }
        });
    }

    partial void OnIncludeArchivesChanged(bool value)
    {
        _ = SearchImagesAsync();
    }

    partial void OnImageRatioThresholdChanged(decimal value)
    {
        _ = DelayedSearchAsync();
    }

    private async Task DelayedSearchAsync()
    {
        // Cancel any existing delayed search
        _searchDelayTokenSource.Cancel();
        _searchDelayTokenSource = new CancellationTokenSource();

        try
        {
            // Wait for a short delay to debounce slider changes
            await Task.Delay(300, _searchDelayTokenSource.Token);
            
            // Execute search if not cancelled
            await SearchImagesAsync();
        }
        catch (OperationCanceledException)
        {
            // Cancelled due to rapid slider changes, ignore
        }
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
}