namespace ImageMonitor.Services;

public interface IThumbnailService
{
    /// <summary>
    /// 通常の画像ファイルのサムネイルを生成します
    /// </summary>
    Task<string?> GenerateThumbnailAsync(string imagePath, int size = 128);
    
    /// <summary>
    /// アーカイブファイル内の最初の画像ファイルのサムネイルを生成します
    /// </summary>
    Task<string?> GenerateArchiveThumbnailAsync(string archivePath, int size = 128);
    
    /// <summary>
    /// 指定されたファイルのサムネイルが既に存在するかチェックします
    /// </summary>
    Task<bool> ThumbnailExistsAsync(string filePath, int size = 128);
    
    /// <summary>
    /// サムネイルファイルのパスを取得します
    /// </summary>
    string GetThumbnailPath(string filePath, int size = 128);
    
    /// <summary>
    /// サムネイルキャッシュをクリアします
    /// </summary>
    Task ClearThumbnailCacheAsync();
    
    /// <summary>
    /// サムネイルキャッシュディレクトリのサイズを取得します
    /// </summary>
    Task<long> GetCacheSizeAsync();
    
    /// <summary>
    /// 古いサムネイルファイルを削除します
    /// </summary>
    Task<int> CleanupOldThumbnailsAsync(int daysOld = 30);
}