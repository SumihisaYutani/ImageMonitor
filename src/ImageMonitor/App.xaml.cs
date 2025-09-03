using ImageMonitor.Services;
using ImageMonitor.ViewModels;

namespace ImageMonitor;

public partial class App : Application
{
    private IHost? _host;
    
    public static IHost? AppHost { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Initialize Serilog
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logDirectory = Path.Combine(appDataPath, "ImageMonitor", "Logs");
        Directory.CreateDirectory(logDirectory);
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "imagemonitor-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug()
            .CreateLogger();

        try
        {
            Log.Information("Starting ImageMonitor application");
            
            _host = CreateHost();
            await _host.StartAsync();
            AppHost = _host;

            // Initialize services
            var databaseService = _host.Services.GetRequiredService<IDatabaseService>();
            await databaseService.InitializeAsync();

            // Show main window
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application failed to start");
            MessageBox.Show($"Application failed to start: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Current.Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during application shutdown");
        }
        finally
        {
            Log.Information("ImageMonitor application stopped");
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.AddSingleton<IConfigurationService, ConfigurationService>();
                
                // Database
                services.AddSingleton<IDatabaseService, DatabaseService>();
                
                // Services
                services.AddSingleton<IThumbnailService, ThumbnailService>();
                services.AddSingleton<IImageScanService, ImageScanService>();
                services.AddSingleton<ILauncherService, LauncherService>();
                // services.AddSingleton<IArchiveService, ArchiveService>(); // Will be added later
                
                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                
                // Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<Views.SettingsWindow>();
            })
            .UseSerilog()
            .Build();
    }
}

