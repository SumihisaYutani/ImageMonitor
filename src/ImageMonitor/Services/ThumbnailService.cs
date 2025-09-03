using ImageMonitor.Models;
using System.Security.Cryptography;
using System.Text;

namespace ImageMonitor.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly ILogger<ThumbnailService> _logger;
    private readonly IConfigurationService _configService;
    private readonly string _thumbnailCacheDir;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
    private static readonly string[] SupportedArchiveExtensions = { ".zip", ".rar" };

    public ThumbnailService(ILogger<ThumbnailService> logger, IConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _thumbnailCacheDir = Path.Combine(appDataPath, "ImageMonitor", "Thumbnails");
        
        if (!Directory.Exists(_thumbnailCacheDir))
        {
            Directory.CreateDirectory(_thumbnailCacheDir);
        }
    }

    public async Task<string?> GenerateThumbnailAsync(string imagePath, int size = 128)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            _logger.LogWarning("Image file not found: {ImagePath}", imagePath);
            return null;
        }

        var thumbnailPath = GetThumbnailPath(imagePath, size);
        
        // サムネイルが既に存在し、元ファイルより新しい場合はそれを返す
        if (File.Exists(thumbnailPath))
        {
            var thumbnailTime = File.GetLastWriteTime(thumbnailPath);
            var imageTime = File.GetLastWriteTime(imagePath);
            
            if (thumbnailTime >= imageTime)
            {
                _logger.LogDebug("Using existing thumbnail: {ThumbnailPath}", thumbnailPath);
                return thumbnailPath;
            }
        }

        await _operationLock.WaitAsync();
        try
        {
            // 再度チェック（並行処理対策）
            if (File.Exists(thumbnailPath) && File.GetLastWriteTime(thumbnailPath) >= File.GetLastWriteTime(imagePath))
            {
                return thumbnailPath;
            }

            return await GenerateThumbnailInternalAsync(imagePath, thumbnailPath, size);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<string?> GenerateArchiveThumbnailAsync(string archivePath, int size = 128)
    {
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
        {
            _logger.LogWarning("Archive file not found: {ArchivePath}", archivePath);
            return null;
        }

        var archiveExtension = Path.GetExtension(archivePath).ToLowerInvariant();
        if (!SupportedArchiveExtensions.Contains(archiveExtension))
        {
            _logger.LogWarning("Unsupported archive format: {ArchivePath}", archivePath);
            return null;
        }

        var thumbnailPath = GetThumbnailPath(archivePath, size);
        
        // サムネイルが既に存在し、元ファイルより新しい場合はそれを返す
        if (File.Exists(thumbnailPath))
        {
            var thumbnailTime = File.GetLastWriteTime(thumbnailPath);
            var archiveTime = File.GetLastWriteTime(archivePath);
            
            if (thumbnailTime >= archiveTime)
            {
                _logger.LogDebug("Using existing archive thumbnail: {ThumbnailPath}", thumbnailPath);
                return thumbnailPath;
            }
        }

        await _operationLock.WaitAsync();
        try
        {
            // 再度チェック（並行処理対策）
            if (File.Exists(thumbnailPath) && File.GetLastWriteTime(thumbnailPath) >= File.GetLastWriteTime(archivePath))
            {
                return thumbnailPath;
            }

            return await GenerateArchiveThumbnailInternalAsync(archivePath, thumbnailPath, size);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<string?> GenerateArchiveThumbnailInternalAsync(string archivePath, string thumbnailPath, int size)
    {
        try
        {
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            
            return extension switch
            {
                ".zip" => await GenerateZipThumbnailAsync(archivePath, thumbnailPath, size),
                ".rar" => await GenerateRarThumbnailAsync(archivePath, thumbnailPath, size),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate archive thumbnail: {ArchivePath}", archivePath);
            return null;
        }
    }

    private async Task<string?> GenerateZipThumbnailAsync(string zipPath, string thumbnailPath, int size)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            
            // アーカイブ内の画像ファイルを取得し、名前順でソート（一番若番を取得）
            var imageEntries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name) && 
                              SupportedImageExtensions.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()))
                .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!imageEntries.Any())
            {
                _logger.LogDebug("No image files found in archive: {ZipPath}", zipPath);
                return null;
            }

            // 最初（最も若番）の画像エントリを使用
            var firstImageEntry = imageEntries.First();
            _logger.LogDebug("Using first image for thumbnail: {ImagePath} in {ZipPath}", 
                firstImageEntry.FullName, zipPath);

            using var entryStream = firstImageEntry.Open();
            using var memoryStream = new MemoryStream();
            await entryStream.CopyToAsync(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            return await GenerateThumbnailFromStreamAsync(memoryStream, thumbnailPath, size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate ZIP thumbnail: {ZipPath}", zipPath);
            return null;
        }
    }

    private async Task<string?> GenerateRarThumbnailAsync(string rarPath, string thumbnailPath, int size)
    {
        try
        {
            using var archive = ArchiveFactory.Open(rarPath);
            
            // アーカイブ内の画像ファイルを取得し、名前順でソート（一番若番を取得）
            var imageEntries = archive.Entries
                .Where(entry => !entry.IsDirectory && 
                              !string.IsNullOrEmpty(entry.Key) && 
                              SupportedImageExtensions.Contains(Path.GetExtension(entry.Key).ToLowerInvariant()))
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!imageEntries.Any())
            {
                _logger.LogDebug("No image files found in RAR archive: {RarPath}", rarPath);
                return null;
            }

            // 最初（最も若番）の画像エントリを使用
            var firstImageEntry = imageEntries.First();
            _logger.LogDebug("Using first image for thumbnail: {ImagePath} in {RarPath}", 
                firstImageEntry.Key, rarPath);

            using var entryStream = firstImageEntry.OpenEntryStream();
            using var memoryStream = new MemoryStream();
            await entryStream.CopyToAsync(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            return await GenerateThumbnailFromStreamAsync(memoryStream, thumbnailPath, size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate RAR thumbnail: {RarPath}", rarPath);
            return null;
        }
    }

    private async Task<string?> GenerateThumbnailInternalAsync(string imagePath, string thumbnailPath, int size)
    {
        try
        {
            using var fileStream = File.OpenRead(imagePath);
            return await GenerateThumbnailFromStreamAsync(fileStream, thumbnailPath, size);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail: {ImagePath}", imagePath);
            return null;
        }
    }

    private async Task<string?> GenerateThumbnailFromStreamAsync(Stream imageStream, string thumbnailPath, int size)
    {
        try
        {
            // サムネイルディレクトリを確保
            var thumbnailDir = Path.GetDirectoryName(thumbnailPath);
            if (!string.IsNullOrEmpty(thumbnailDir) && !Directory.Exists(thumbnailDir))
            {
                Directory.CreateDirectory(thumbnailDir);
            }

            return await Task.Run(() =>
            {
                // WPF BitmapImageを使用
                var decoder = BitmapDecoder.Create(imageStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var originalFrame = decoder.Frames[0];
                
                // アスペクト比を維持してサイズ計算
                int originalWidth = originalFrame.PixelWidth;
                int originalHeight = originalFrame.PixelHeight;
                
                double scale = Math.Min((double)size / originalWidth, (double)size / originalHeight);
                int width = (int)(originalWidth * scale);
                int height = (int)(originalHeight * scale);

                // サムネイル生成
                var thumbnail = new TransformedBitmap(originalFrame, new ScaleTransform(scale, scale));
                
                // JPEGエンコーダーで保存
                var encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 85;
                encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                
                using var fileStream = new FileStream(thumbnailPath, FileMode.Create);
                encoder.Save(fileStream);

                _logger.LogDebug("Generated thumbnail: {ThumbnailPath} ({Width}x{Height})", 
                    thumbnailPath, width, height);
                
                return thumbnailPath;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail from stream: {ThumbnailPath}", thumbnailPath);
            return null;
        }
    }

    public async Task<bool> ThumbnailExistsAsync(string filePath, int size = 128)
    {
        var thumbnailPath = GetThumbnailPath(filePath, size);
        
        if (!File.Exists(thumbnailPath))
            return false;
            
        // 元ファイルの方が新しい場合は無効とする
        return await Task.Run(() =>
        {
            try
            {
                var thumbnailTime = File.GetLastWriteTime(thumbnailPath);
                var fileTime = File.GetLastWriteTime(filePath);
                return thumbnailTime >= fileTime;
            }
            catch
            {
                return false;
            }
        });
    }

    public string GetThumbnailPath(string filePath, int size = 128)
    {
        // ファイルパスのハッシュを生成してファイル名に使用
        var fileHash = ComputeFileHash(filePath);
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var archiveSuffix = SupportedArchiveExtensions.Contains(extension) ? "_archive" : "";
        
        var thumbnailFileName = $"{fileHash}_{size}{archiveSuffix}.jpg";
        
        // サイズ別にサブディレクトリを作成
        var sizeDir = Path.Combine(_thumbnailCacheDir, $"size_{size}");
        return Path.Combine(sizeDir, thumbnailFileName);
    }

    public async Task ClearThumbnailCacheAsync()
    {
        try
        {
            if (Directory.Exists(_thumbnailCacheDir))
            {
                await Task.Run(() =>
                {
                    Directory.Delete(_thumbnailCacheDir, true);
                    Directory.CreateDirectory(_thumbnailCacheDir);
                });
                
                _logger.LogInformation("Thumbnail cache cleared");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear thumbnail cache");
        }
    }

    public async Task<long> GetCacheSizeAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_thumbnailCacheDir))
                    return 0L;

                var files = Directory.GetFiles(_thumbnailCacheDir, "*", SearchOption.AllDirectories);
                return files.Sum(file => new FileInfo(file).Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate cache size");
                return 0L;
            }
        });
    }

    public async Task<int> CleanupOldThumbnailsAsync(int daysOld = 30)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_thumbnailCacheDir))
                    return 0;

                var cutoffDate = DateTime.Now.AddDays(-daysOld);
                var files = Directory.GetFiles(_thumbnailCacheDir, "*", SearchOption.AllDirectories);
                int deletedCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        var lastAccess = File.GetLastAccessTime(file);
                        if (lastAccess < cutoffDate)
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old thumbnail: {FilePath}", file);
                    }
                }

                _logger.LogInformation("Cleaned up {Count} old thumbnails", deletedCount);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup old thumbnails");
                return 0;
            }
        });
    }

    private static string ComputeFileHash(string filePath)
    {
        using var md5 = MD5.Create();
        var inputBytes = Encoding.UTF8.GetBytes(filePath.ToLowerInvariant());
        var hashBytes = md5.ComputeHash(inputBytes);
        return Convert.ToHexString(hashBytes)[..16]; // 最初の16文字を使用
    }

    public void Dispose()
    {
        _operationLock?.Dispose();
    }
}