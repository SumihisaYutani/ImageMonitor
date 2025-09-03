using ImageMonitor.Models;

namespace ImageMonitor.Services;

public interface IConfigurationService
{
    Task<AppSettings> GetSettingsAsync();
    
    Task SaveSettingsAsync(AppSettings settings);
    
    Task<T> GetSettingAsync<T>(string key, T defaultValue);
    
    Task SetSettingAsync<T>(string key, T value);
    
    Task ResetToDefaultsAsync();
    
    event EventHandler<AppSettings>? SettingsChanged;
}