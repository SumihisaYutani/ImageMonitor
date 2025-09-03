using ImageMonitor.Models;

namespace ImageMonitor.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<ConfigurationService> _logger;
    private AppSettings? _cachedSettings;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);

    public event EventHandler<AppSettings>? SettingsChanged;

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDataDir = Path.Combine(appDataPath, "ImageMonitor");
        
        if (!Directory.Exists(appDataDir))
        {
            Directory.CreateDirectory(appDataDir);
        }
        
        _settingsFilePath = Path.Combine(appDataDir, "settings.json");
        _logger.LogDebug("Settings file path: {SettingsFilePath}", _settingsFilePath);
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        await _settingsLock.WaitAsync();
        try
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    _cachedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    _logger.LogDebug("Settings loaded from file");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize settings file, using defaults");
                    _cachedSettings = new AppSettings();
                }
            }
            else
            {
                _logger.LogDebug("Settings file not found, using defaults");
                _cachedSettings = new AppSettings();
            }

            return _cachedSettings;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        await _settingsLock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = System.Text.Json.JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(_settingsFilePath, json);
            
            _cachedSettings = settings;
            _logger.LogDebug("Settings saved to file");
            
            SettingsChanged?.Invoke(this, settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to file");
            throw;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task<T> GetSettingAsync<T>(string key, T defaultValue)
    {
        var settings = await GetSettingsAsync();
        
        try
        {
            var property = typeof(AppSettings).GetProperty(key);
            if (property != null)
            {
                var value = property.GetValue(settings);
                if (value is T typedValue)
                {
                    return typedValue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get setting {Key}, using default value", key);
        }
        
        return defaultValue;
    }

    public async Task SetSettingAsync<T>(string key, T value)
    {
        var settings = await GetSettingsAsync();
        
        try
        {
            var property = typeof(AppSettings).GetProperty(key);
            if (property != null && property.CanWrite)
            {
                property.SetValue(settings, value);
                await SaveSettingsAsync(settings);
            }
            else
            {
                _logger.LogWarning("Property {Key} not found or not writable", key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set setting {Key}", key);
            throw;
        }
    }

    public async Task ResetToDefaultsAsync()
    {
        _logger.LogInformation("Resetting settings to defaults");
        
        var defaultSettings = new AppSettings();
        await SaveSettingsAsync(defaultSettings);
    }

    public void Dispose()
    {
        _settingsLock.Dispose();
    }
}