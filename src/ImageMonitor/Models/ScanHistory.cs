namespace ImageMonitor.Models;

/// <summary>
/// スキャン履歴を表すモデル
/// </summary>
public class ScanHistory
{
    /// <summary>
    /// 一意識別子（パス+タイムスタンプのハッシュ）
    /// </summary>
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// スキャンしたディレクトリパス
    /// </summary>
    public string DirectoryPath { get; set; } = string.Empty;
    
    /// <summary>
    /// スキャン実行日時
    /// </summary>
    public DateTime ScanDate { get; set; }
    
    /// <summary>
    /// 見つかったファイル数
    /// </summary>
    public int FileCount { get; set; }
    
    /// <summary>
    /// 処理されたアイテム数
    /// </summary>
    public int ProcessedCount { get; set; }
    
    /// <summary>
    /// スキャンにかかった時間（ミリ秒）
    /// </summary>
    public long ElapsedMs { get; set; }
    
    /// <summary>
    /// スキャンタイプ（Full/Incremental）
    /// </summary>
    public string ScanType { get; set; } = "Full";
}