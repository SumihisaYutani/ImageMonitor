namespace ImageMonitor.Services;

public interface ILauncherService
{
    /// <summary>
    /// ファイルに関連付けられたアプリケーションで開きます
    /// </summary>
    Task<bool> LaunchAssociatedAppAsync(string filePath);
    
    /// <summary>
    /// アーカイブファイルを関連付けられたビューワーで開きます
    /// </summary>
    Task<bool> LaunchArchiveViewerAsync(string archivePath);
    
    /// <summary>
    /// 指定されたファイルの関連付けアプリケーションを取得します
    /// </summary>
    Task<string?> GetAssociatedAppAsync(string filePath);
    
    /// <summary>
    /// ファイルエクスプローラーでファイルを選択状態で開きます
    /// </summary>
    Task<bool> ShowInExplorerAsync(string filePath);
    
    /// <summary>
    /// 指定されたフォルダをエクスプローラーで開きます
    /// </summary>
    Task<bool> OpenFolderAsync(string folderPath);
    
    /// <summary>
    /// 外部プロセスを起動します
    /// </summary>
    Task<bool> LaunchProcessAsync(string executablePath, string arguments = "", string workingDirectory = "");
}