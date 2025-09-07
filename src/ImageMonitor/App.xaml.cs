using ImageMonitor.Services;
using ImageMonitor.ViewModels;

namespace ImageMonitor;

public partial class App : Application
{
    private IHost? _host;
    
    public static IHost? AppHost { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var appStartStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepTimes = new List<(string step, long ms)>();
        
        // Initialize Serilog
        var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var executableDir = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;
        var logDirectory = Path.Combine(executableDir, "Data", "Logs");
        Directory.CreateDirectory(logDirectory);
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
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
        
        stepTimes.Add(("Serilog initialization", appStartStopwatch.ElapsedMilliseconds));

        try
        {
            // デバッグ: WPFイベント監視を設定
            this.Startup += (s, args) => {
                Log.Information($"[DEBUG] WPF Application.Startup event fired");
                Console.WriteLine($"[DEBUG] WPF Application.Startup event fired");
            };
            
            this.Activated += (s, args) => {
                Log.Information($"[DEBUG] WPF Application.Activated event fired");
                Console.WriteLine($"[DEBUG] WPF Application.Activated event fired");
            };
            
            // ウィンドウ生成監視
            this.MainWindow = null; // 明示的にnullに設定
            Log.Information($"[DEBUG] Application.MainWindow set to null explicitly");
            Console.WriteLine($"[DEBUG] Application.MainWindow set to null explicitly");
            
            // Global exception handling
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Log.Fatal(ex, "Unhandled exception occurred. IsTerminating: {IsTerminating}", args.IsTerminating);
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                Log.Fatal(args.Exception, "Unhandled UI exception occurred");
                args.Handled = true; // Prevent application crash for UI exceptions
            };

            Log.Information("Starting ImageMonitor application");
            
            _host = CreateHost();
            stepTimes.Add(("Host creation", appStartStopwatch.ElapsedMilliseconds));
            
            await _host.StartAsync();
            AppHost = _host;
            stepTimes.Add(("Host startup", appStartStopwatch.ElapsedMilliseconds));

            // Initialize services
            var databaseService = _host.Services.GetRequiredService<IDatabaseService>();
            await databaseService.InitializeAsync();
            stepTimes.Add(("Database initialization", appStartStopwatch.ElapsedMilliseconds));

            // Show main window
            Log.Information("[DEBUG] About to get MainWindow from DI container");
            Console.WriteLine("[DEBUG] About to get MainWindow from DI container");
            
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            
            Log.Information($"[DEBUG] MainWindow retrieved from DI - Instance: {mainWindow.GetHashCode()}");
            Console.WriteLine($"[DEBUG] MainWindow retrieved from DI - Instance: {mainWindow.GetHashCode()}");
            
            Log.Information($"[DEBUG] About to call mainWindow.Show()");
            Console.WriteLine($"[DEBUG] About to call mainWindow.Show()");
            
            mainWindow.Show();
            
            Log.Information($"[DEBUG] mainWindow.Show() completed");
            Console.WriteLine($"[DEBUG] mainWindow.Show() completed");
            
            // アプリケーションのウィンドウ数を確認
            var windowCount = Application.Current.Windows.Count;
            Log.Information($"[DEBUG] Total application windows: {windowCount}");
            Console.WriteLine($"[DEBUG] Total application windows: {windowCount}");
            
            // 各ウィンドウの詳細をログ出力
            for (int i = 0; i < Application.Current.Windows.Count; i++)
            {
                var window = Application.Current.Windows[i];
                Log.Information($"[DEBUG] Window {i}: Type={window.GetType().Name}, Title={window.Title}, Hash={window.GetHashCode()}, Visible={window.IsVisible}");
                Console.WriteLine($"[DEBUG] Window {i}: Type={window.GetType().Name}, Title={window.Title}, Hash={window.GetHashCode()}, Visible={window.IsVisible}");
            }
            stepTimes.Add(("Main window creation", appStartStopwatch.ElapsedMilliseconds));

            appStartStopwatch.Stop();
            var totalStartupTime = appStartStopwatch.ElapsedMilliseconds;
            
            // パフォーマンス詳細情報をログ出力
            var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
            {
                var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                var stepDuration = step.ms - prevTime;
                return $"{step.step}: {stepDuration}ms";
            }));
            
            Log.Information("Application startup completed in {TotalTime}ms - Steps: {StepDetails}", 
                totalStartupTime, stepDetails);
            
            if (totalStartupTime > 5000) // 5秒以上は警告
            {
                Log.Warning("Slow application startup: {TotalTime}ms", totalStartupTime);
            }

        }
        catch (Exception ex)
        {
            appStartStopwatch.Stop();
            Log.Fatal(ex, "Application failed to start after {ElapsedTime}ms", appStartStopwatch.ElapsedMilliseconds);
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
                services.AddSingleton<IMessagingService, MessagingService>();
                // services.AddSingleton<IArchiveService, ArchiveService>(); // Will be added later
                
                // ViewModels
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<SettingsViewModel>();
                
                // Views
                services.AddSingleton<MainWindow>();
                services.AddTransient<Views.SettingsWindow>();
            })
            .UseSerilog()
            .Build();
    }
}

