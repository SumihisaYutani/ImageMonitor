using ImageMonitor.Models;
using System.Drawing.Imaging;

namespace ImageMonitor.Services;

public class ImageScanService : IImageScanService
{
    private readonly ILogger<ImageScanService> _logger;
    private readonly IConfigurationService _configService;
    private readonly IThumbnailService _thumbnailService;
    private SemaphoreSlim _concurrencyLimit;
    
    private static readonly string[] SupportedImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
    private static readonly string[] SupportedArchiveExtensions = { ".zip", ".rar" };

    public event EventHandler<ScanProgressEventArgs>? ScanProgress;

    public ImageScanService(
        ILogger<ImageScanService> logger, 
        IConfigurationService configService,
        IThumbnailService thumbnailService)
    {
        _logger = logger;
        _configService = configService;
        _thumbnailService = thumbnailService;
        
        // 並行処理数を制限（設定から取得、デフォルトは4）
        _concurrencyLimit = new SemaphoreSlim(4, 4);
    }

    public async Task<List<ImageItem>> ScanDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory not found: {DirectoryPath}", directoryPath);
            return new List<ImageItem>();
        }

        _logger.LogInformation("Starting directory scan: {DirectoryPath}", directoryPath);
        
        try
        {
            var allFiles = new List<string>();
            
            // 画像ファイルを取得
            foreach (var extension in SupportedImageExtensions)
            {
                var pattern = $"*{extension}";
                var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
                allFiles.AddRange(files);
            }
            
            // アーカイブファイルを取得
            foreach (var extension in SupportedArchiveExtensions)
            {
                var pattern = $"*{extension}";
                var files = Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories);
                allFiles.AddRange(files);
            }

            _logger.LogInformation("Found {Count} files to process in {DirectoryPath}", allFiles.Count, directoryPath);
            
            return await ProcessFilesAsync(allFiles, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning directory: {DirectoryPath}", directoryPath);
            return new List<ImageItem>();
        }
    }

    public async Task<List<ImageItem>> ScanDirectoriesAsync(IEnumerable<string> directoryPaths, CancellationToken cancellationToken = default)
    {
        var allResults = new List<ImageItem>();
        var validDirectories = directoryPaths.Where(Directory.Exists).ToList();
        
        _logger.LogInformation("Scanning {Count} directories", validDirectories.Count);

        foreach (var directory in validDirectories)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            var results = await ScanDirectoryAsync(directory, cancellationToken);
            allResults.AddRange(results);
        }

        _logger.LogInformation("Scan completed. Found {Count} total items", allResults.Count);
        return allResults;
    }

    private async Task<List<ImageItem>> ProcessFilesAsync(List<string> filePaths, CancellationToken cancellationToken)
    {
        var results = new List<ImageItem>();
        var totalFiles = filePaths.Count;
        var processedFiles = 0;

        // 設定から並行処理数を取得
        var settings = await _configService.GetSettingsAsync();
        
        // セマフォアを設定値で初期化（既存のものは破棄）
        _concurrencyLimit?.Dispose();
        _concurrencyLimit = new SemaphoreSlim(settings.MaxConcurrentScans, settings.MaxConcurrentScans);
        var tasks = filePaths.Select(async filePath =>
        {
            await _concurrencyLimit.WaitAsync(cancellationToken);
            try
            {
                var items = new List<ImageItem>();
                
                if (IsImageFile(filePath))
                {
                    var imageItem = await ProcessImageFileAsync(filePath, cancellationToken);
                    if (imageItem != null)
                        items.Add(imageItem);
                }
                else if (IsArchiveFile(filePath))
                {
                    var archiveItems = await ProcessArchiveFileAsync(filePath, cancellationToken);
                    items.AddRange(archiveItems);
                }

                // 進捗報告
                var processed = Interlocked.Increment(ref processedFiles);
                OnScanProgress(new ScanProgressEventArgs
                {
                    CurrentFile = Path.GetFileName(filePath),
                    ProcessedFiles = processed,
                    TotalFiles = totalFiles,
                    Message = $"Processing {Path.GetFileName(filePath)}...",
                    IsCompleted = processed == totalFiles
                });

                return items;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process file: {FilePath}", filePath);
                Interlocked.Increment(ref processedFiles);
                return new List<ImageItem>();
            }
            finally
            {
                _concurrencyLimit.Release();
            }
        });

        var allResults = await Task.WhenAll(tasks);
        
        foreach (var result in allResults)
        {
            results.AddRange(result);
        }

        return results;
    }

    public async Task<ImageItem?> ProcessImageFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!IsImageFile(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            
            var imageItem = new ImageItem
            {
                Id = ImageItem.GenerateFileId(filePath),
                FilePath = filePath,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                CreatedAt = fileInfo.CreationTime,
                ModifiedAt = fileInfo.LastWriteTime,
                ScanDate = DateTime.Now,
                IsArchived = false
            };

            // 画像メタデータを取得
            await PopulateImageMetadataAsync(imageItem, filePath, cancellationToken);
            
            // サムネイルを生成
            var settings = await _configService.GetSettingsAsync();
            imageItem.ThumbnailPath = await _thumbnailService.GenerateThumbnailAsync(filePath, settings.ThumbnailSize);

            _logger.LogDebug("Processed image file: {FilePath} ({Width}x{Height})", 
                filePath, imageItem.Width, imageItem.Height);

            return imageItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process image file: {FilePath}", filePath);
            return null;
        }
    }

    public async Task<List<ImageItem>> ProcessArchiveFileAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        if (!IsArchiveFile(archivePath) || !File.Exists(archivePath))
        {
            return new List<ImageItem>();
        }

        try
        {
            _logger.LogDebug("Processing archive file: {ArchivePath}", archivePath);
            
            var results = new List<ImageItem>();
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();

            List<string> imageFilesInArchive;
            
            if (extension == ".zip")
            {
                imageFilesInArchive = await GetImageFilesFromZipAsync(archivePath, cancellationToken);
            }
            else if (extension == ".rar")
            {
                imageFilesInArchive = await GetImageFilesFromRarAsync(archivePath, cancellationToken);
            }
            else
            {
                return results;
            }

            if (!imageFilesInArchive.Any())
            {
                _logger.LogDebug("No image files found in archive: {ArchivePath}", archivePath);
                return results;
            }

            // アーカイブ内の画像ファイル比率をチェック
            var settings = await _configService.GetSettingsAsync();
            var totalFilesInArchive = await GetTotalFilesInArchiveAsync(archivePath);
            var imageRatio = (decimal)imageFilesInArchive.Count / totalFilesInArchive;
            
            if (imageRatio < settings.ImageRatioThreshold)
            {
                _logger.LogDebug("Archive image ratio {Ratio:P1} is below threshold {Threshold:P1}: {ArchivePath}", 
                    imageRatio, settings.ImageRatioThreshold, archivePath);
                return results;
            }

            var archiveFileInfo = new FileInfo(archivePath);
            
            // アーカイブ内の各画像ファイルに対してImageItemを作成
            foreach (var internalPath in imageFilesInArchive)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var imageItem = new ImageItem
                    {
                        Id = ImageItem.GenerateFileId($"{archivePath}#{internalPath}"),
                        FilePath = archivePath, // アーカイブファイルのパス
                        FileName = Path.GetFileName(internalPath),
                        FileSize = await GetArchiveEntryFileSizeAsync(archivePath, internalPath),
                        CreatedAt = archiveFileInfo.CreationTime,
                        ModifiedAt = archiveFileInfo.LastWriteTime,
                        ScanDate = DateTime.Now,
                        IsArchived = true,
                        ArchivePath = archivePath,
                        InternalPath = internalPath
                    };

                    // アーカイブ内画像のメタデータを取得
                    await PopulateArchiveImageMetadataAsync(imageItem, archivePath, internalPath, cancellationToken);
                    
                    results.Add(imageItem);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process archive entry: {ArchivePath}#{InternalPath}", 
                        archivePath, internalPath);
                }
            }

            // アーカイブ全体のサムネイルを生成（最初の画像ファイルを使用）
            if (results.Any())
            {
                var thumbnailPath = await _thumbnailService.GenerateArchiveThumbnailAsync(archivePath, settings.ThumbnailSize);
                
                // すべてのアーカイブ内アイテムに同じサムネイルとImageRatioを設定
                foreach (var item in results)
                {
                    item.ThumbnailPath = thumbnailPath;
                    item.ArchiveImageRatio = imageRatio;
                }
            }

            _logger.LogInformation("Processed archive: {ArchivePath} - {Count} images (ratio: {Ratio:P1})", 
                archivePath, results.Count, imageRatio);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process archive file: {ArchivePath}", archivePath);
            return new List<ImageItem>();
        }
    }

    private async Task<List<string>> GetImageFilesFromZipAsync(string zipPath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var imageFiles = new List<string>();
            
            using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                if (!string.IsNullOrEmpty(entry.Name) && IsImageFile(entry.Name))
                {
                    imageFiles.Add(entry.FullName);
                }
            }
            
            return imageFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }, cancellationToken);
    }

    private async Task<List<string>> GetImageFilesFromRarAsync(string rarPath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var imageFiles = new List<string>();
            
            using var archive = ArchiveFactory.Open(rarPath);
            foreach (var entry in archive.Entries)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                if (!entry.IsDirectory && !string.IsNullOrEmpty(entry.Key) && IsImageFile(entry.Key))
                {
                    imageFiles.Add(entry.Key);
                }
            }
            
            return imageFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }, cancellationToken);
    }

    private async Task<int> GetTotalFilesInArchiveAsync(string archivePath)
    {
        return await Task.Run(() =>
        {
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            
            if (extension == ".zip")
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
                return archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
            }
            else if (extension == ".rar")
            {
                using var archive = ArchiveFactory.Open(archivePath);
                return archive.Entries.Count(e => !e.IsDirectory);
            }
            
            return 0;
        });
    }

    private async Task<long> GetArchiveEntryFileSizeAsync(string archivePath, string internalPath)
    {
        return await Task.Run(() =>
        {
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            
            try
            {
                if (extension == ".zip")
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(archivePath);
                    var entry = archive.GetEntry(internalPath);
                    return entry?.Length ?? 0L;
                }
                else if (extension == ".rar")
                {
                    using var archive = ArchiveFactory.Open(archivePath);
                    var entry = archive.Entries.FirstOrDefault(e => e.Key == internalPath);
                    return entry?.Size ?? 0L;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get archive entry size: {ArchivePath}#{InternalPath}", 
                    archivePath, internalPath);
            }
            
            return 0L;
        });
    }

    private async Task PopulateImageMetadataAsync(ImageItem imageItem, string filePath, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                using var fileStream = File.OpenRead(filePath);
                
                // ファイルサイズの検証
                if (fileStream.Length < 100)
                {
                    throw new ArgumentException("File too small to be a valid image");
                }
                
                // 安全な画像処理
                PopulateImageMetadataFromStream(imageItem, fileStream, filePath);
                
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read image metadata: {FilePath}", filePath);
            
            // メタデータ取得に失敗した場合はデフォルト値を設定
            imageItem.Width = 0;
            imageItem.Height = 0;
            imageItem.ImageFormat = Path.GetExtension(filePath).TrimStart('.');
            imageItem.HasExifData = false;
        }
    }

    private async Task PopulateArchiveImageMetadataAsync(ImageItem imageItem, string archivePath, string internalPath, CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(archivePath).ToLowerInvariant();
            
            // タイムアウト付きでアーカイブ処理を実行（10秒タイムアウト）
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            await Task.Run(() =>
            {
                combinedCts.Token.ThrowIfCancellationRequested();
                
                try
                {
                    if (extension == ".zip")
                    {
                        using var archive = ZipFile.OpenRead(archivePath);
                        var entry = archive.GetEntry(internalPath);
                        if (entry != null && entry.Length > 0)
                        {
                            using var stream = entry.Open();
                            PopulateImageMetadataFromStream(imageItem, stream, internalPath);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Entry not found or empty: {internalPath}");
                        }
                    }
                    else if (extension == ".rar")
                    {
                        using var archive = ArchiveFactory.Open(archivePath);
                        var entry = archive.Entries.FirstOrDefault(e => e.Key == internalPath);
                        if (entry != null && entry.Size > 0)
                        {
                            using var stream = entry.OpenEntryStream();
                            PopulateImageMetadataFromStream(imageItem, stream, internalPath);
                        }
                        else
                        {
                            throw new InvalidOperationException($"Entry not found or empty: {internalPath}");
                        }
                    }
                }
                catch (OutOfMemoryException)
                {
                    _logger.LogWarning("Out of memory when processing archive: {ArchivePath}#{InternalPath}", archivePath, internalPath);
                    throw;
                }
                catch (AccessViolationException)
                {
                    _logger.LogWarning("Access violation when processing archive: {ArchivePath}#{InternalPath}", archivePath, internalPath);
                    throw;
                }
                
            }, combinedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Archive processing cancelled by user: {ArchivePath}#{InternalPath}", archivePath, internalPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Archive processing timed out: {ArchivePath}#{InternalPath}", archivePath, internalPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read archive image metadata: {ArchivePath}#{InternalPath}", 
                archivePath, internalPath);
        }
        finally
        {
            // エラー時は常にデフォルト値を確保
            if (imageItem.Width == 0 || imageItem.Height == 0)
            {
                imageItem.Width = 0;
                imageItem.Height = 0;
                imageItem.ImageFormat = Path.GetExtension(internalPath).TrimStart('.');
                imageItem.HasExifData = false;
            }
        }
    }

    private void PopulateImageMetadataFromStream(ImageItem imageItem, Stream stream, string fileName)
    {
        try
        {
            // ストリームの基本検証
            if (stream == null || !stream.CanRead)
            {
                throw new ArgumentException("Invalid stream");
            }
            
            // Lengthプロパティがサポートされている場合のみチェック
            if (stream.CanSeek && stream.Length == 0)
            {
                throw new ArgumentException("Empty stream");
            }

            // ストリームを完全にメモリに読み込んでからデコード（メモリ破損対策）
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            
            // 最小限の画像ファイルサイズチェック
            if (memoryStream.Length < 100) // 100バイト未満は無効と判定
            {
                throw new ArgumentException("File too small to be a valid image");
            }
            
            // BitmapCacheOption.OnDemandを使用してメモリ使用量を削減
            var decoder = BitmapDecoder.Create(memoryStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            
            if (decoder.Frames.Count == 0)
            {
                throw new InvalidOperationException("No frames found in image");
            }
            
            var frame = decoder.Frames[0];
            
            // 異常な画像サイズをチェック
            if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || 
                frame.PixelWidth > 50000 || frame.PixelHeight > 50000)
            {
                throw new ArgumentException("Invalid image dimensions");
            }
            
            imageItem.Width = frame.PixelWidth;
            imageItem.Height = frame.PixelHeight;
            imageItem.ImageFormat = decoder.CodecInfo?.FriendlyName ?? Path.GetExtension(fileName);
            
            // EXIF情報を安全に取得
            try
            {
                if (frame.Metadata is BitmapMetadata metadata && !string.IsNullOrEmpty(metadata.DateTaken))
                {
                    imageItem.HasExifData = true;
                    
                    if (DateTime.TryParse(metadata.DateTaken, out var dateTime))
                    {
                        imageItem.DateTaken = dateTime;
                    }
                }
            }
            catch
            {
                // EXIF取得エラーは無視（メタデータは必須ではない）
                _logger.LogDebug("Failed to read EXIF data for: {FileName}", fileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read image metadata from stream: {FileName}", fileName);
            
            // エラー時はデフォルト値を設定
            imageItem.Width = 0;
            imageItem.Height = 0;
            imageItem.ImageFormat = Path.GetExtension(fileName).TrimStart('.');
            imageItem.HasExifData = false;
        }
    }

    public bool IsImageFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedImageExtensions.Contains(extension);
    }

    public bool IsArchiveFile(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return SupportedArchiveExtensions.Contains(extension);
    }

    private void OnScanProgress(ScanProgressEventArgs e)
    {
        ScanProgress?.Invoke(this, e);
    }

    public void Dispose()
    {
        _concurrencyLimit?.Dispose();
    }
}