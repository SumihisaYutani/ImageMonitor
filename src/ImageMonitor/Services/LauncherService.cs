using Microsoft.Win32;

namespace ImageMonitor.Services;

public class LauncherService : ILauncherService
{
    private readonly ILogger<LauncherService> _logger;
    private readonly IConfigurationService _configService;

    public LauncherService(ILogger<LauncherService> logger, IConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    public async Task<bool> LaunchAssociatedAppAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true,
                Verb = "open"
            };

            using var process = Process.Start(startInfo);
            
            _logger.LogInformation("Launched associated app for: {FilePath}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch associated app for: {FilePath}", filePath);
            return false;
        }
    }

    public async Task<bool> LaunchArchiveViewerAsync(string archivePath)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            _logger.LogWarning("Archive file not found: {ArchivePath}", archivePath);
            return false;
        }

        try
        {
            // 設定で指定された既定のアーカイブビューワーを確認
            var settings = await _configService.GetSettingsAsync();
            
            if (!string.IsNullOrEmpty(settings.DefaultArchiveViewer) && 
                File.Exists(settings.DefaultArchiveViewer))
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = settings.DefaultArchiveViewer,
                    Arguments = $"\"{archivePath}\"",
                    UseShellExecute = false
                };

                using var process = Process.Start(startInfo);
                _logger.LogInformation("Launched archive viewer {Viewer} for: {ArchivePath}", 
                    settings.DefaultArchiveViewer, archivePath);
                return true;
            }

            // 設定がない場合は関連付けアプリを使用
            return await LaunchAssociatedAppAsync(archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch archive viewer for: {ArchivePath}", archivePath);
            return false;
        }
    }

    public async Task<string?> GetAssociatedAppAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                if (string.IsNullOrEmpty(extension))
                {
                    return null;
                }

                // レジストリから関連付けを取得
                using var extKey = Registry.ClassesRoot.OpenSubKey(extension);
                if (extKey == null)
                {
                    return null;
                }

                var progId = extKey.GetValue(null)?.ToString();
                if (string.IsNullOrEmpty(progId))
                {
                    return null;
                }

                using var progIdKey = Registry.ClassesRoot.OpenSubKey($"{progId}\\shell\\open\\command");
                if (progIdKey == null)
                {
                    return null;
                }

                var commandLine = progIdKey.GetValue(null)?.ToString();
                if (string.IsNullOrEmpty(commandLine))
                {
                    return null;
                }

                // コマンドラインから実行ファイルパスを抽出
                var executablePath = ExtractExecutableFromCommandLine(commandLine);
                
                _logger.LogDebug("Associated app for {Extension}: {ExecutablePath}", extension, executablePath);
                return executablePath;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get associated app for: {FilePath}", filePath);
                return null;
            }
        });
    }

    public async Task<bool> ShowInExplorerAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("File not found: {FilePath}", filePath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = false
            };

            using var process = Process.Start(startInfo);
            
            _logger.LogInformation("Showed file in explorer: {FilePath}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show file in explorer: {FilePath}", filePath);
            return false;
        }
    }

    public async Task<bool> OpenFolderAsync(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            _logger.LogWarning("Folder not found: {FolderPath}", folderPath);
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true,
                Verb = "open"
            };

            using var process = Process.Start(startInfo);
            
            _logger.LogInformation("Opened folder: {FolderPath}", folderPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open folder: {FolderPath}", folderPath);
            return false;
        }
    }

    public async Task<bool> LaunchProcessAsync(string executablePath, string arguments = "", string workingDirectory = "")
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            _logger.LogWarning("Executable path is empty");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(startInfo);
            
            _logger.LogInformation("Launched process: {ExecutablePath} {Arguments}", executablePath, arguments);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch process: {ExecutablePath}", executablePath);
            return false;
        }
    }

    private static string? ExtractExecutableFromCommandLine(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return null;
        }

        try
        {
            // コマンドラインが引用符で始まる場合
            if (commandLine.StartsWith("\""))
            {
                var closingQuoteIndex = commandLine.IndexOf("\"", 1);
                if (closingQuoteIndex > 1)
                {
                    return commandLine.Substring(1, closingQuoteIndex - 1);
                }
            }
            else
            {
                // 最初のスペースまでを実行ファイルパスとして取得
                var spaceIndex = commandLine.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    return commandLine.Substring(0, spaceIndex);
                }
                else
                {
                    return commandLine;
                }
            }
        }
        catch
        {
            // パースに失敗した場合は元の文字列を返す
        }

        return commandLine;
    }

    /// <summary>
    /// 指定された拡張子に対してアプリケーションを関連付けます（管理者権限が必要）
    /// </summary>
    public async Task<bool> SetFileAssociationAsync(string extension, string executablePath, string friendlyName)
    {
        try
        {
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            return await Task.Run(() =>
            {
                try
                {
                    // 拡張子キーを作成
                    using var extKey = Registry.ClassesRoot.CreateSubKey(extension);
                    extKey?.SetValue(null, $"{friendlyName}File");

                    // プログラムIDキーを作成
                    var progId = $"{friendlyName}File";
                    using var progIdKey = Registry.ClassesRoot.CreateSubKey(progId);
                    progIdKey?.SetValue(null, friendlyName);

                    // コマンドを設定
                    using var commandKey = Registry.ClassesRoot.CreateSubKey($"{progId}\\shell\\open\\command");
                    commandKey?.SetValue(null, $"\"{executablePath}\" \"%1\"");

                    _logger.LogInformation("Set file association: {Extension} -> {ExecutablePath}", extension, executablePath);
                    return true;
                }
                catch (UnauthorizedAccessException)
                {
                    _logger.LogWarning("Access denied when setting file association. Administrator privileges required.");
                    return false;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set file association: {Extension} -> {ExecutablePath}", extension, executablePath);
            return false;
        }
    }
}