using ImageMonitor.Models;
using ImageMonitor.Services;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace ImageMonitor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigurationService _configService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<string> _scanDirectories = new();

    [ObservableProperty]
    private string _selectedDirectory = string.Empty;

    [ObservableProperty]
    private decimal _imageRatioThreshold = 0.5m;

    [ObservableProperty]
    private bool _enableThumbnailGeneration = true;

    [ObservableProperty]
    private int _thumbnailSize = 128;

    [ObservableProperty]
    private double _windowWidth = 1200;

    [ObservableProperty]
    private double _windowHeight = 800;

    [ObservableProperty]
    private bool _windowMaximized = false;

    [ObservableProperty]
    private bool _enableLogging = true;

    [ObservableProperty]
    private string _logLevel = "Information";

    [ObservableProperty]
    private int _maxLogFiles = 7;

    public IReadOnlyList<string> LogLevels { get; } = new[]
    {
        "Verbose", "Debug", "Information", "Warning", "Error", "Fatal"
    };

    public IReadOnlyList<int> ThumbnailSizes { get; } = new[]
    {
        64, 96, 128, 192, 256
    };

    public SettingsViewModel(IConfigurationService configService, ILogger<SettingsViewModel> logger)
    {
        _configService = configService;
        _logger = logger;

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
    {
            var settings = await _configService.GetSettingsAsync();
            
            ScanDirectories.Clear();
            foreach (var dir in settings.ScanDirectories)
            {
                ScanDirectories.Add(dir);
            }

            ImageRatioThreshold = settings.ImageRatioThreshold;
            EnableThumbnailGeneration = settings.EnableThumbnailGeneration;
            ThumbnailSize = settings.ThumbnailSize;
            WindowWidth = settings.WindowWidth;
            WindowHeight = settings.WindowHeight;
            WindowMaximized = settings.WindowMaximized;
            EnableLogging = settings.EnableLogging;
            LogLevel = settings.LogLevel;
            MaxLogFiles = settings.MaxLogFiles;

            _logger.LogDebug("Settings loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
        }
    }

    [RelayCommand]
    private void AddDirectory()
    {
        var dialog = new CommonOpenFileDialog
        {
            Title = "スキャン対象フォルダを選択",
            IsFolderPicker = true,
            AddToMostRecentlyUsedList = false,
            AllowNonFileSystemItems = false,
            EnsureFileExists = true,
            EnsurePathExists = true,
            EnsureReadOnly = false,
            EnsureValidNames = true,
            Multiselect = false,
            ShowPlacesList = true
        };

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            var selectedPath = dialog.FileName;
            if (!string.IsNullOrEmpty(selectedPath) && !ScanDirectories.Contains(selectedPath))
            {
                ScanDirectories.Add(selectedPath);
                _logger.LogInformation("Added scan directory: {Directory}", selectedPath);
            }
        }
    }

    [RelayCommand]
    private void RemoveDirectory()
    {
        if (!string.IsNullOrEmpty(SelectedDirectory) && ScanDirectories.Contains(SelectedDirectory))
        {
            ScanDirectories.Remove(SelectedDirectory);
            _logger.LogInformation("Removed scan directory: {Directory}", SelectedDirectory);
            SelectedDirectory = string.Empty;
        }
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        try
        {
            var settings = new AppSettings
            {
                ScanDirectories = ScanDirectories.ToList(),
                ImageRatioThreshold = ImageRatioThreshold,
                EnableThumbnailGeneration = EnableThumbnailGeneration,
                ThumbnailSize = ThumbnailSize,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                WindowMaximized = WindowMaximized,
                EnableLogging = EnableLogging,
                LogLevel = LogLevel,
                MaxLogFiles = MaxLogFiles
            };

            await _configService.SaveSettingsAsync(settings);
            _logger.LogInformation("Settings saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            throw;
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        try
        {
            var defaultSettings = new AppSettings();
            
            ScanDirectories.Clear();
            foreach (var dir in defaultSettings.ScanDirectories)
            {
                ScanDirectories.Add(dir);
            }

            ImageRatioThreshold = defaultSettings.ImageRatioThreshold;
            EnableThumbnailGeneration = defaultSettings.EnableThumbnailGeneration;
            ThumbnailSize = defaultSettings.ThumbnailSize;
            WindowWidth = defaultSettings.WindowWidth;
            WindowHeight = defaultSettings.WindowHeight;
            WindowMaximized = defaultSettings.WindowMaximized;
            EnableLogging = defaultSettings.EnableLogging;
            LogLevel = defaultSettings.LogLevel;
            MaxLogFiles = defaultSettings.MaxLogFiles;

            _logger.LogInformation("Settings reset to defaults");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset settings");
        }
    }

    [RelayCommand]
    private async Task ClearThumbnailCacheAsync()
    {
        try
        {
            // ThumbnailServiceを使ってキャッシュをクリア
            if (App.AppHost != null)
            {
                var thumbnailService = App.AppHost.Services.GetService<IThumbnailService>();
                if (thumbnailService != null)
                {
                    await thumbnailService.ClearThumbnailCacheAsync();
                    _logger.LogInformation("Thumbnail cache cleared");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear thumbnail cache");
            throw;
        }
    }

    [RelayCommand]
    private async Task CleanupOldThumbnailsAsync()
    {
        try
        {
            if (App.AppHost != null)
            {
                var thumbnailService = App.AppHost.Services.GetService<IThumbnailService>();
                if (thumbnailService != null)
                {
                    var deletedCount = await thumbnailService.CleanupOldThumbnailsAsync(30);
                    _logger.LogInformation("Cleaned up {Count} old thumbnails", deletedCount);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old thumbnails");
            throw;
        }
    }
}