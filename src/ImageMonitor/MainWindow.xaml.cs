using ImageMonitor.ViewModels;
using ImageMonitor.Services;
using ImageMonitor.Models;

namespace ImageMonitor;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        
        InitializeComponent();
        
        // Load window state from settings
        _ = LoadWindowStateAsync();
    }

    private async Task LoadWindowStateAsync()
    {
        try
        {
            if (App.AppHost != null)
            {
                var configService = App.AppHost.Services.GetService<IConfigurationService>();
                if (configService != null)
                {
                    var settings = await configService.GetSettingsAsync();
                    
                    Width = settings.WindowWidth;
                    Height = settings.WindowHeight;
                    
                    if (settings.WindowMaximized)
                    {
                        WindowState = WindowState.Maximized;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't prevent window from showing
            System.Diagnostics.Debug.WriteLine($"Failed to load window state: {ex.Message}");
        }
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        try
        {
            if (App.AppHost != null)
            {
                var configService = App.AppHost.Services.GetService<IConfigurationService>();
                if (configService != null)
                {
                    var settings = await configService.GetSettingsAsync();
                    
                    settings.WindowWidth = ActualWidth;
                    settings.WindowHeight = ActualHeight;
                    settings.WindowMaximized = WindowState == WindowState.Maximized;
                    
                    await configService.SaveSettingsAsync(settings);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save window state: {ex.Message}");
        }
        
        base.OnClosing(e);
    }

    #region Event Handlers

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.AppHost != null)
            {
                var settingsWindow = App.AppHost.Services.GetService<Views.SettingsWindow>();
                if (settingsWindow != null)
                {
                    settingsWindow.Owner = this;
                    var result = settingsWindow.ShowDialog();
                    
                    if (result == true)
                    {
                        // 設定が保存された場合、必要に応じてUIを更新
                        ViewModel.StatusText = "設定が保存されました";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"設定ウィンドウを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OptimizeDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.AppHost != null)
            {
                var databaseService = App.AppHost.Services.GetService<IDatabaseService>();
                if (databaseService != null)
                {
                    var result = MessageBox.Show(
                        "This will optimize the database and may take some time. Continue?",
                        "Database Optimization",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                        
                    if (result == MessageBoxResult.Yes)
                    {
                        ViewModel.StatusText = "Optimizing database...";
                        await databaseService.OptimizeDatabaseAsync();
                        ViewModel.StatusText = "Database optimization completed";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Database optimization failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "ImageMonitor v1.0.0\n\n" +
            "A tool for managing and organizing image files and archives.\n\n" +
            "Built with .NET 8 and WPF",
            "About ImageMonitor",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchQuery = string.Empty;
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedImageItem != null)
        {
            var item = ViewModel.SelectedImageItem;
            var resolution = item is ImageItem imgItem ? imgItem.Resolution : 
                           item is ArchiveItem archItem ? $"{archItem.ImageFiles} images" : "Unknown";
            
            // TODO: Implement detailed properties dialog
            MessageBox.Show(
                $"Name: {item.FileName}\n" +
                $"Size: {item.FormattedFileSize}\n" +
                $"Resolution: {resolution}\n" +
                $"Path: {item.FilePath}",
                "Properties",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }


    #endregion
}