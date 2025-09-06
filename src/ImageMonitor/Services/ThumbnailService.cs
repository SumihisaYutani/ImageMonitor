using ImageMonitor.Models;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace ImageMonitor.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly ILogger<ThumbnailService> _logger;
    private readonly IConfigurationService _configService;
    private readonly string _thumbnailCacheDir;
    private readonly SemaphoreSlim _operationLock = new(4, 4); // 並行処理数を4に増加
    private readonly ConcurrentDictionary<string, Task<string?>> _pendingThumbnails = new();
    private readonly ConcurrentDictionary<string, DateTime> _thumbnailCache = new();
    private static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
    private static readonly string[] SupportedArchiveExtensions = { ".zip", ".rar" };

    public ThumbnailService(ILogger<ThumbnailService> logger, IConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
        
        // 実行ファイルのディレクトリを取得
        var executablePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var executableDir = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory;
        _thumbnailCacheDir = Path.Combine(executableDir, "Data", "Thumbnails");
        
        if (!Directory.Exists(_thumbnailCacheDir))
        {
            Directory.CreateDirectory(_thumbnailCacheDir);
        }
    }

    public async Task<string?> GenerateThumbnailAsync(string imagePath, int size = 128)
    {
        var thumbnailStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepTimes = new List<(string step, long ms)>();
        
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            _logger.LogWarning("Image file not found: {ImagePath}", imagePath);
            return null;
        }

        var thumbnailPath = GetThumbnailPath(imagePath, size);
        var cacheKey = $"{imagePath}:{size}";
        stepTimes.Add(("Path and cache key generation", thumbnailStopwatch.ElapsedMilliseconds));
        
        // メモリキャッシュから存在確認
        if (_thumbnailCache.TryGetValue(cacheKey, out var cachedTime))
        {
            var imageTime = File.GetLastWriteTime(imagePath);
            if (cachedTime >= imageTime && File.Exists(thumbnailPath))
            {
                thumbnailStopwatch.Stop();
                _logger.LogDebug("Using cached thumbnail: {ThumbnailPath} in {TotalTime}ms", 
                    thumbnailPath, thumbnailStopwatch.ElapsedMilliseconds);
                return thumbnailPath;
            }
            else
            {
                // キャッシュが古い場合は削除
                _thumbnailCache.TryRemove(cacheKey, out _);
            }
        }
        stepTimes.Add(("Memory cache check", thumbnailStopwatch.ElapsedMilliseconds));
        
        // ファイルレベルでの存在確認
        if (File.Exists(thumbnailPath))
        {
            var thumbnailTime = File.GetLastWriteTime(thumbnailPath);
            var imageTime = File.GetLastWriteTime(imagePath);
            
            if (thumbnailTime >= imageTime)
            {
                _thumbnailCache.TryAdd(cacheKey, thumbnailTime);
                thumbnailStopwatch.Stop();
                _logger.LogDebug("Using existing thumbnail: {ThumbnailPath} in {TotalTime}ms", 
                    thumbnailPath, thumbnailStopwatch.ElapsedMilliseconds);
                return thumbnailPath;
            }
        }
        stepTimes.Add(("File cache check", thumbnailStopwatch.ElapsedMilliseconds));

        // 重複生成を防ぐ
        if (_pendingThumbnails.TryGetValue(cacheKey, out var pendingTask))
        {
            _logger.LogDebug("Waiting for pending thumbnail generation: {ImagePath}", imagePath);
            var result = await pendingTask;
            thumbnailStopwatch.Stop();
            _logger.LogDebug("Received pending thumbnail: {ImagePath} in {TotalTime}ms", 
                imagePath, thumbnailStopwatch.ElapsedMilliseconds);
            return result;
        }

        // 新しいサムネイル生成タスクを作成
        var thumbnailTask = GenerateThumbnailWithSemaphoreAsync(imagePath, thumbnailPath, size, cacheKey, stepTimes, thumbnailStopwatch);
        
        if (_pendingThumbnails.TryAdd(cacheKey, thumbnailTask))
        {
            try
            {
                return await thumbnailTask;
            }
            finally
            {
                _pendingThumbnails.TryRemove(cacheKey, out _);
            }
        }
        else
        {
            // 他のスレッドが既に開始している場合は待機
            if (_pendingThumbnails.TryGetValue(cacheKey, out var existingTask))
            {
                var result = await existingTask;
                thumbnailStopwatch.Stop();
                _logger.LogDebug("Received concurrent thumbnail: {ImagePath} in {TotalTime}ms", 
                    imagePath, thumbnailStopwatch.ElapsedMilliseconds);
                return result;
            }
            return await thumbnailTask;
        }
    }

    private async Task<string?> GenerateThumbnailWithSemaphoreAsync(string imagePath, string thumbnailPath, int size, string cacheKey, List<(string step, long ms)> stepTimes, System.Diagnostics.Stopwatch thumbnailStopwatch)
    {
        await _operationLock.WaitAsync();
        stepTimes.Add(("Semaphore acquired", thumbnailStopwatch.ElapsedMilliseconds));
        
        try
        {
            // セマフォ取得後に再度チェック（並行処理対策）
            if (File.Exists(thumbnailPath) && File.GetLastWriteTime(thumbnailPath) >= File.GetLastWriteTime(imagePath))
            {
                var thumbnailTime = File.GetLastWriteTime(thumbnailPath);
                _thumbnailCache.TryAdd(cacheKey, thumbnailTime);
                thumbnailStopwatch.Stop();
                _logger.LogDebug("Using existing thumbnail after semaphore: {ThumbnailPath} in {TotalTime}ms", 
                    thumbnailPath, thumbnailStopwatch.ElapsedMilliseconds);
                return thumbnailPath;
            }

            var result = await GenerateThumbnailInternalAsync(imagePath, thumbnailPath, size);
            stepTimes.Add(("Thumbnail generation", thumbnailStopwatch.ElapsedMilliseconds));
            
            if (result != null)
            {
                _thumbnailCache.TryAdd(cacheKey, DateTime.Now);
            }
            
            thumbnailStopwatch.Stop();
            var totalTime = thumbnailStopwatch.ElapsedMilliseconds;
            
            // パフォーマンス詳細情報をログ出力
            if (totalTime > 1000) // 1秒以上の場合は詳細ログ
            {
                var stepDetails = string.Join(", ", stepTimes.Select((step, i) => 
                {
                    var prevTime = i > 0 ? stepTimes[i-1].ms : 0;
                    var stepDuration = step.ms - prevTime;
                    return $"{step.step}: {stepDuration}ms";
                }));
                
                _logger.LogWarning("Slow thumbnail generation: {ImagePath} in {TotalTime}ms - Steps: {StepDetails}", 
                    imagePath, totalTime, stepDetails);
            }
            else if (totalTime > 200) // 200ms以上は軽いログ
            {
                _logger.LogInformation("Generated thumbnail: {ImagePath} in {TotalTime}ms", 
                    imagePath, totalTime);
            }
            else
            {
                _logger.LogDebug("Generated thumbnail: {ImagePath} in {TotalTime}ms", 
                    imagePath, totalTime);
            }
            
            return result;
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
        var cacheKey = $"{archivePath}:{size}:archive";
        
        // メモリキャッシュから存在確認
        if (_thumbnailCache.TryGetValue(cacheKey, out var cachedTime))
        {
            var archiveTime = File.GetLastWriteTime(archivePath);
            if (cachedTime >= archiveTime && File.Exists(thumbnailPath))
            {
                _logger.LogDebug("Using cached archive thumbnail: {ThumbnailPath}", thumbnailPath);
                return thumbnailPath;
            }
            else
            {
                _thumbnailCache.TryRemove(cacheKey, out _);
            }
        }
        
        // ファイルレベルでの存在確認
        if (File.Exists(thumbnailPath))
        {
            var thumbnailTime = File.GetLastWriteTime(thumbnailPath);
            var archiveTime = File.GetLastWriteTime(archivePath);
            
            if (thumbnailTime >= archiveTime)
            {
                _thumbnailCache.TryAdd(cacheKey, thumbnailTime);
                _logger.LogDebug("Using existing archive thumbnail: {ThumbnailPath}", thumbnailPath);
                return thumbnailPath;
            }
        }

        // 重複生成を防ぐ
        if (_pendingThumbnails.TryGetValue(cacheKey, out var pendingTask))
        {
            _logger.LogDebug("Waiting for pending archive thumbnail generation: {ArchivePath}", archivePath);
            return await pendingTask;
        }

        // 新しいアーカイブサムネイル生成タスクを作成
        var thumbnailTask = GenerateArchiveThumbnailWithSemaphoreAsync(archivePath, thumbnailPath, size, cacheKey);
        
        if (_pendingThumbnails.TryAdd(cacheKey, thumbnailTask))
        {
            try
            {
                return await thumbnailTask;
            }
            finally
            {
                _pendingThumbnails.TryRemove(cacheKey, out _);
            }
        }
        else
        {
            // 他のスレッドが既に開始している場合は待機
            if (_pendingThumbnails.TryGetValue(cacheKey, out var existingTask))
            {
                return await existingTask;
            }
            return await thumbnailTask;
        }
    }

    private async Task<string?> GenerateArchiveThumbnailWithSemaphoreAsync(string archivePath, string thumbnailPath, int size, string cacheKey)
    {
        await _operationLock.WaitAsync();
        try
        {
            // セマフォ取得後に再度チェック（並行処理対策）
            if (File.Exists(thumbnailPath) && File.GetLastWriteTime(thumbnailPath) >= File.GetLastWriteTime(archivePath))
            {
                var thumbnailTime = File.GetLastWriteTime(thumbnailPath);
                _thumbnailCache.TryAdd(cacheKey, thumbnailTime);
                return thumbnailPath;
            }

            var result = await GenerateArchiveThumbnailInternalAsync(archivePath, thumbnailPath, size);
            if (result != null)
            {
                _thumbnailCache.TryAdd(cacheKey, DateTime.Now);
            }
            return result;
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
                try
                {
                    // ストリームの基本検証
                    if (imageStream == null || !imageStream.CanRead)
                    {
                        _logger.LogWarning("Invalid stream for thumbnail generation");
                        return null;
                    }

                    // 安全な画像デコード処理
                    BitmapDecoder decoder;
                    BitmapFrame originalFrame;
                    
                    try
                    {
                        decoder = BitmapDecoder.Create(
                            imageStream, 
                            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, 
                            BitmapCacheOption.OnLoad);
                        
                        if (decoder.Frames.Count == 0)
                        {
                            _logger.LogWarning("No frames found in image for thumbnail generation");
                            return null;
                        }
                        
                        originalFrame = decoder.Frames[0];
                    }
                    catch (OverflowException ex)
                    {
                        _logger.LogWarning(ex, "Image data overflow during decoding - skipping thumbnail generation");
                        return null;
                    }
                    catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x88982F05))
                    {
                        _logger.LogWarning(ex, "Image data out of range - skipping thumbnail generation");
                        return null;
                    }
                    
                    // 基本的な画像サイズ検証
                    var originalWidth = originalFrame.PixelWidth;
                    var originalHeight = originalFrame.PixelHeight;
                    
                    if (originalWidth <= 0 || originalHeight <= 0)
                    {
                        _logger.LogWarning("Invalid image dimensions: {Width}x{Height}", originalWidth, originalHeight);
                        return null;
                    }
                
                    // アスペクト比を維持してサイズ計算
                    // 高品質のためベースサイズを大きく取る（最大512px、要求サイズの2倍以上を保証）
                    int baseSize = Math.Max(512, size * 2);
                    
                    double scale = Math.Min((double)baseSize / originalWidth, (double)baseSize / originalHeight);
                    int width = (int)(originalWidth * scale);
                    int height = (int)(originalHeight * scale);

                    // 高品質サムネイル生成
                    var scaleTransform = new ScaleTransform(scale, scale);
                    scaleTransform.Freeze(); // パフォーマンス向上のためフリーズ
                    var thumbnail = new TransformedBitmap(originalFrame, scaleTransform);
                    
                    // 高品質レンダリング設定
                    RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
                    RenderOptions.SetEdgeMode(thumbnail, EdgeMode.Aliased);
                    
                    // 高品質JPEGエンコーダーで保存
                    var encoder = new JpegBitmapEncoder();
                    encoder.QualityLevel = 95;
                    encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                    
                    using var fileStream = new FileStream(thumbnailPath, FileMode.Create);
                    encoder.Save(fileStream);

                    _logger.LogDebug("Generated high-quality thumbnail: {ThumbnailPath} ({Width}x{Height}) from base size {BaseSize} for requested size {RequestedSize}", 
                        thumbnailPath, width, height, baseSize, size);
                    
                    return thumbnailPath;
                }
                catch (Exception innerEx)
                {
                    _logger.LogWarning(innerEx, "Failed to process thumbnail generation - skipping");
                    return null;
                }
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