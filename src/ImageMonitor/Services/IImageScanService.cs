using ImageMonitor.Models;

namespace ImageMonitor.Services;

public interface IImageScanService : IDisposable
{
    /// <summary>
    /// 指定されたディレクトリをスキャンして画像アイテムを取得します
    /// </summary>
    Task<List<ImageItem>> ScanDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 複数のディレクトリを並行してスキャンします
    /// </summary>
    Task<List<ImageItem>> ScanDirectoriesAsync(IEnumerable<string> directoryPaths, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 単一の画像ファイルを処理してImageItemを作成します
    /// </summary>
    Task<ImageItem?> ProcessImageFileAsync(string filePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 単一のアーカイブファイルを処理してImageItemsを作成します
    /// </summary>
    Task<List<ImageItem>> ProcessArchiveFileAsync(string archivePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ファイルが画像ファイルかどうかを判定します
    /// </summary>
    bool IsImageFile(string filePath);
    
    /// <summary>
    /// ファイルがサポートされているアーカイブファイルかどうかを判定します
    /// </summary>
    bool IsArchiveFile(string filePath);
    
    /// <summary>
    /// スキャン進捗を通知するイベント
    /// </summary>
    event EventHandler<ScanProgressEventArgs>? ScanProgress;
}

public class ScanProgressEventArgs : EventArgs
{
    public string CurrentFile { get; set; } = string.Empty;
    public int ProcessedFiles { get; set; }
    public int TotalFiles { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}