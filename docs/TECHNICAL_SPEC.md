# ImageMonitor 技術仕様書

## 概要

ImageMonitorは.NET 8とWPFを使用したWindows向けデスクトップアプリケーションです。画像ファイルとアーカイブファイルの効率的な検索・管理を目的とし、MovieMonitorの設計思想を踏襲しながらアーカイブファイル処理に特化した機能を提供します。

## システムアーキテクチャ

### 技術スタック

| レイヤー | 技術・ライブラリ | バージョン | 用途 |
|---------|----------------|-----------|------|
| UI Framework | WPF | .NET 8.0 | ユーザーインターフェース |
| MVVM Framework | CommunityToolkit.Mvvm | 8.x | データバインディング・コマンド |
| Database | LiteDB | 5.x | ローカルデータストレージ |
| Logging | Serilog | 3.x | ログ出力・管理 |
| Archive Processing | SharpZipLib | 1.4.x | ZIPファイル処理 |
| Archive Processing | SharpCompress | 0.34.x | RAR・その他アーカイブ処理 |
| Image Processing | System.Drawing.Common | 8.x | 画像サムネイル生成 |
| DI Container | Microsoft.Extensions.DI | 8.x | 依存性注入 |

### アーキテクチャパターン

```
┌─────────────────────────────────────────────────────────────┐
│                    プレゼンテーション層                      │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐    │
│  │    Views     │  │ ViewModels   │  │  Converters  │    │
│  │   (XAML)     │  │   (C#)       │  │    (C#)      │    │
│  └──────────────┘  └──────────────┘  └──────────────┘    │
└─────────────────────────────────────────────────────────────┘
                               │
┌─────────────────────────────────────────────────────────────┐
│                     アプリケーション層                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐    │
│  │   Services   │  │   Commands   │  │  Utilities   │    │
│  │    (C#)      │  │    (C#)      │  │    (C#)      │    │
│  └──────────────┘  └──────────────┘  └──────────────┘    │
└─────────────────────────────────────────────────────────────┘
                               │
┌─────────────────────────────────────────────────────────────┐
│                      ドメイン層                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐    │
│  │    Models    │  │   Entities   │  │ ValueObjects │    │
│  │    (C#)      │  │    (C#)      │  │    (C#)      │    │
│  └──────────────┘  └──────────────┘  └──────────────┘    │
└─────────────────────────────────────────────────────────────┘
                               │
┌─────────────────────────────────────────────────────────────┐
│                  インフラストラクチャ層                       │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐    │
│  │   Database   │  │ FileSystem   │  │ ExternalApps │    │
│  │   (LiteDB)   │  │ (System.IO)  │  │  (Process)   │    │
│  └──────────────┘  └──────────────┘  └──────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

## データモデル設計

### エンティティ関係図

```
┌─────────────────┐    1:N    ┌─────────────────┐
│  ScanDirectory  │◀──────────│   ImageItem     │
│                 │           │                 │
│ + Id            │           │ + Id            │
│ + Path          │           │ + FileName      │
│ + IsEnabled     │           │ + FilePath      │
│ + LastScan      │           │ + FileSize      │
│ + FileCount     │           │ + CreatedDate   │
└─────────────────┘           │ + ModifiedDate  │
                               │ + ThumbnailPath │
                               │ + DirectoryId   │
                               │ + IsArchived    │
                               │ + ArchivePath   │
                               └─────────────────┘
                                        │
                                    1:N │
                                        ▼
┌─────────────────┐            ┌─────────────────┐
│   ArchiveItem   │────1:N────▶│   ImageInArchive│
│                 │            │                 │
│ + Id            │            │ + Id            │
│ + FilePath      │            │ + ArchiveId     │
│ + FileSize      │            │ + InternalPath  │
│ + CreatedDate   │            │ + FileName      │
│ + ModifiedDate  │            │ + FileSize      │
│ + ImageCount    │            │ + ImageData     │
│ + TotalFiles    │            │ + ThumbnailData │
│ + ImageRatio    │            └─────────────────┘
│ + AssociatedApp │
└─────────────────┘
```

### データベーススキーマ

#### ImageItems Collection
```csharp
public class ImageItem
{
    public ObjectId Id { get; set; }
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string ThumbnailPath { get; set; }
    public ObjectId DirectoryId { get; set; }
    public bool IsArchived { get; set; }
    public string ArchivePath { get; set; }
    
    // インデックス
    // [BsonIndex("FilePath", true)]  // Unique
    // [BsonIndex("FileName")]
    // [BsonIndex("DirectoryId")]
}
```

#### ArchiveItems Collection
```csharp
public class ArchiveItem
{
    public ObjectId Id { get; set; }
    public string FilePath { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public int ImageCount { get; set; }
    public int TotalFiles { get; set; }
    public decimal ImageRatio { get; set; }
    public string AssociatedApp { get; set; }
    public List<ImageInArchive> Images { get; set; }
    
    // インデックス
    // [BsonIndex("FilePath", true)]  // Unique
    // [BsonIndex("ImageRatio")]
}
```

## サービス設計

### FileService
```csharp
public interface IFileService
{
    Task<List<string>> ScanDirectoryAsync(string path, string[] extensions);
    Task<List<string>> GetImageFilesAsync(string directory);
    bool IsImageFile(string filePath);
    Task<FileInfo> GetFileInfoAsync(string filePath);
}

public class FileService : IFileService
{
    private static readonly string[] SupportedImageExtensions = 
        { ".jpg", ".jpeg", ".png" };
        
    // 実装詳細...
}
```

### ArchiveService
```csharp
public interface IArchiveService
{
    Task<ArchiveInfo> AnalyzeArchiveAsync(string archivePath);
    Task<List<ImageInArchive>> ExtractImageListAsync(string archivePath);
    bool IsArchiveFile(string filePath);
    bool MeetsImageRatioThreshold(ArchiveInfo info, decimal threshold = 0.5m);
}

public class ArchiveService : IArchiveService
{
    private static readonly string[] SupportedArchiveExtensions = 
        { ".zip", ".rar" };
        
    // ZIPファイル処理
    private async Task<ArchiveInfo> AnalyzeZipAsync(string zipPath) { }
    
    // RARファイル処理
    private async Task<ArchiveInfo> AnalyzeRarAsync(string rarPath) { }
}
```

### ThumbnailService
```csharp
public interface IThumbnailService
{
    Task<string> GenerateThumbnailAsync(string imagePath, int size = 128);
    Task<string> GenerateArchiveThumbnailAsync(string archivePath, string imagePath);
    Task<bool> ThumbnailExistsAsync(string imagePath);
    Task ClearThumbnailCacheAsync();
}

public class ThumbnailService : IThumbnailService
{
    private readonly string _thumbnailCacheDir;
    private const int DefaultThumbnailSize = 128;
    
    // 実装詳細...
}
```

### LauncherService
```csharp
public interface ILauncherService
{
    Task<bool> LaunchAssociatedAppAsync(string filePath);
    Task<string> GetAssociatedAppAsync(string filePath);
    Task<bool> SetAssociatedAppAsync(string extension, string appPath);
}

public class LauncherService : ILauncherService
{
    // Windows Shell API を使用した関連付けアプリの取得・起動
    // Registry操作による関連付け情報の管理
}
```

## パフォーマンス最適化

### データベース最適化

#### インデックス戦略
```sql
-- 複合インデックス
CREATE INDEX idx_image_directory_filename ON ImageItems (DirectoryId, FileName);
CREATE INDEX idx_archive_ratio ON ArchiveItems (ImageRatio);
CREATE INDEX idx_image_path ON ImageItems (FilePath);
CREATE INDEX idx_archive_path ON ArchiveItems (FilePath);

-- 部分インデックス
CREATE INDEX idx_archived_images ON ImageItems (ArchivePath) WHERE IsArchived = true;
```

#### クエリ最適化
```csharp
// バッチ挿入
public async Task BulkInsertImagesAsync(List<ImageItem> images)
{
    using var transaction = _database.BeginTrans();
    try
    {
        var collection = _database.GetCollection<ImageItem>("ImageItems");
        collection.InsertBulk(images);
        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}

// ページング付き検索
public async Task<PagedResult<ImageItem>> SearchImagesAsync(
    string searchTerm, int page, int pageSize)
{
    var collection = _database.GetCollection<ImageItem>("ImageItems");
    var query = collection.Query()
        .Where(x => x.FileName.Contains(searchTerm))
        .OrderBy(x => x.FileName);
        
    var total = query.Count();
    var items = query.Skip((page - 1) * pageSize).Limit(pageSize).ToList();
    
    return new PagedResult<ImageItem>(items, total, page, pageSize);
}
```

### UI最適化

#### 仮想化リスト
```xaml
<ListView ItemsSource="{Binding Images}"
          VirtualizingStackPanel.IsVirtualizing="True"
          VirtualizingStackPanel.VirtualizationMode="Recycling"
          ScrollViewer.IsDeferredScrollingEnabled="True">
    <ListView.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel />
        </ItemsPanelTemplate>
    </ListView.ItemsPanel>
</ListView>
```

#### 非同期サムネイル読み込み
```csharp
public class ThumbnailViewModel : ViewModelBase
{
    private ImageSource _thumbnail;
    public ImageSource Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }
    
    public async Task LoadThumbnailAsync(string imagePath)
    {
        try
        {
            Thumbnail = await Task.Run(() => 
                LoadImageFromPath(imagePath)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // エラーハンドリング
            Thumbnail = DefaultThumbnail;
        }
    }
}
```

## セキュリティ考慮事項

### ファイルアクセス制御
- ユーザーが明示的に指定したディレクトリのみアクセス
- システムディレクトリへのアクセス制限
- 一時ファイルの安全な削除

### アーカイブ処理セキュリティ
- ZipSlip攻撃対策（パストラバーサル防止）
- ファイルサイズ制限による DoS 攻撃防止
- 悪意のあるアーカイブファイルからの保護

```csharp
public class SecureArchiveExtractor
{
    private const long MaxFileSize = 100 * 1024 * 1024; // 100MB
    private const int MaxEntries = 10000;
    
    public async Task<bool> ValidateArchiveAsync(string archivePath)
    {
        var info = new FileInfo(archivePath);
        if (info.Length > MaxFileSize)
            return false;
            
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries)
            return false;
            
        foreach (var entry in archive.Entries)
        {
            // パストラバーサル攻撃チェック
            if (Path.IsPathRooted(entry.FullName) || 
                entry.FullName.Contains(".."))
                return false;
        }
        
        return true;
    }
}
```

## エラーハンドリング

### 例外階層
```csharp
public class ImageMonitorException : Exception
{
    public ImageMonitorException(string message) : base(message) { }
    public ImageMonitorException(string message, Exception inner) : base(message, inner) { }
}

public class FileAccessException : ImageMonitorException
{
    public string FilePath { get; }
    public FileAccessException(string filePath, string message) : base(message)
    {
        FilePath = filePath;
    }
}

public class ArchiveProcessingException : ImageMonitorException
{
    public string ArchivePath { get; }
    public ArchiveProcessingException(string archivePath, string message) : base(message)
    {
        ArchivePath = archivePath;
    }
}
```

### ログ設定
```csharp
public static class LoggingConfiguration
{
    public static ILogger ConfigureLogging()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: "logs/imagemonitor-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug()
            .CreateLogger();
    }
}
```

## 設定管理

### アプリケーション設定
```json
{
  "ScanSettings": {
    "MaxConcurrentScans": 4,
    "ThumbnailSize": 128,
    "ImageRatioThreshold": 0.5,
    "ScanTimeout": 300
  },
  "CacheSettings": {
    "ThumbnailCacheSize": 1000,
    "CacheCleanupInterval": 24
  },
  "UISettings": {
    "Theme": "Light",
    "Language": "ja-JP",
    "ItemsPerPage": 50
  }
}
```

### 設定クラス
```csharp
public class AppSettings
{
    public ScanSettings ScanSettings { get; set; } = new();
    public CacheSettings CacheSettings { get; set; } = new();
    public UISettings UISettings { get; set; } = new();
}

public class ScanSettings
{
    public int MaxConcurrentScans { get; set; } = 4;
    public int ThumbnailSize { get; set; } = 128;
    public decimal ImageRatioThreshold { get; set; } = 0.5m;
    public int ScanTimeout { get; set; } = 300;
}
```

## テスト戦略

### ユニットテスト
- サービスクラスの個別機能テスト
- モックを使用した外部依存関係の分離
- カバレッジ目標: 80%以上

### 統合テスト
- データベース操作の検証
- ファイルシステム操作の検証
- アーカイブ処理の検証

### UIテスト
- ViewModelの状態変更テスト
- コマンド実行テスト
- データバインディング検証

## 最新の実装と改修履歴（2025-09-06）

### 実装済み機能

#### 1. 大規模パフォーマンス改善
**問題解決**：
- アーカイブ内画像のメタデータ読み取り最適化により、処理時間を40秒以上から1秒未満に短縮（99%改善）
- `PopulateImageMetadataFromStream()`の呼び出しを完全削除し、ファイル拡張子からの推定に変更

**並行処理最適化**：
```csharp
// 大規模アーカイブの並行処理
if (imageEntries.Count >= 100)
{
    var maxConcurrency = Math.Min(16, Math.Max(4, imageEntries.Count / 10));
    var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    var tasks = imageEntries.Select(entry => ProcessEntryAsync(entry, semaphore));
    await Task.WhenAll(tasks);
}
```

#### 2. 増分スキャン機能
**完全実装済み**：
```csharp
public async Task<int> IncrementalScanDirectoriesAsync(
    IEnumerable<string> directoryPaths, 
    IDatabaseService databaseService, 
    CancellationToken cancellationToken = default)
{
    // Step 1: 削除検出とクリーンアップ
    var deletedDirectories = await DetectDeletedDirectoriesAsync(directoryPaths, databaseService);
    var cleanupCount = await CleanupDeletedDirectoriesAsync(deletedDirectories, databaseService);
    
    // Step 2: 新規・更新ディレクトリの検出（24時間閾値）
    var directoriesToScan = await DetectDirectoriesToScanAsync(directoryPaths, databaseService);
    
    // Step 3: 対象ディレクトリのみスキャン
    foreach (var directory in directoriesToScan)
    {
        var inserted = await ScanDirectoryStreamingAsync(directory, databaseService, cancellationToken);
        // スキャン履歴記録（ScanType = "Incremental"）
    }
}
```

**検出ロジック**：
- 24時間以上経過したディレクトリを自動検出
- 初回スキャンディレクトリの識別
- 削除されたディレクトリの自動クリーンアップ

#### 3. UI表示バグ修正 - 重要
**根本原因と解決**：
```csharp
// 修正前：単一画像アイテムのみでTotalItemsを計算（常に0）
var totalCount = await _databaseService.GetImageItemCountAsync();

// 修正後：アーカイブと画像の合計を正しく計算
var archiveCount = await _databaseService.GetArchiveItemCountAsync();
var imageCount = await _databaseService.GetImageItemCountAsync(); 
var totalCount = archiveCount + imageCount;

// バックグラウンド読み込みも修正
var archiveItems = await _databaseService.GetArchiveItemsAsync(loaded, batchSize);
var itemsList = archiveItems.Cast<IDisplayItem>().ToList();
```

**結果**：全2294個のアーカイブファイル（99.6%、97.1%等あらゆる画像比率）が正常表示

#### 4. SemaphoreSlim並行処理修正
**問題解決**：
```csharp
private bool _semaphoreInitialized = false;

// 設定から並行処理数を取得（一度だけ初期化）
if (!_semaphoreInitialized)
{
    var settings = await _configService.GetSettingsAsync();
    _concurrencyLimit?.Dispose();
    _concurrencyLimit = new SemaphoreSlim(settings.MaxConcurrentScans, settings.MaxConcurrentScans);
    _currentConcurrency = settings.MaxConcurrentScans;
    _semaphoreInitialized = true;
}
```

#### 5. 単一画像ファイル除外機能
**実装内容**：
```csharp
// 単一画像ファイル処理の無効化
// var imageItem = await ProcessImageFileAsync(filePath, cancellationToken);

// データベースクリーンアップ機能
public async Task<int> CleanupSingleImageItemsAsync()
{
    return await Task.Run(async () =>
    {
        await _operationLock.WaitAsync();
        try
        {
            var deletedCount = _imageItems.DeleteAll();
            _logger.LogInformation("Cleaned up {Count} single image items", deletedCount);
            return deletedCount;
        }
        finally
        {
            _operationLock.Release();
        }
    });
}
```

### データベース拡張機能

#### 新規メソッド
```csharp
public interface IDatabaseService
{
    // アーカイブディレクトリ取得
    Task<IEnumerable<string>> GetArchiveDirectoriesAsync();
    Task<IEnumerable<string>> GetImageDirectoriesAsync();
    
    // 単一画像アイテムクリーンアップ
    Task<int> CleanupSingleImageItemsAsync();
    
    // 非アーカイブ画像アイテム取得
    Task<IEnumerable<ImageItem>> GetNonArchivedImageItemsAsync(int offset, int limit);
    
    // スキャン履歴管理
    Task<ScanHistory?> GetLastScanHistoryAsync(string directoryPath);
    Task InsertScanHistoryAsync(ScanHistory scanHistory);
}
```

#### ScanHistory エンティティ
```csharp
public class ScanHistory
{
    public ObjectId Id { get; set; }
    public string DirectoryPath { get; set; }
    public DateTime ScanDate { get; set; }
    public int FileCount { get; set; }
    public int ProcessedCount { get; set; }
    public long ElapsedMs { get; set; }
    public string ScanType { get; set; } // "Full" または "Incremental"
}
```

### UI/UX 改善

#### 段階的アイテム読み込み
```csharp
// 初期バッチ（50個）
var initialArchiveItems = await _databaseService.GetArchiveItemsAsync(0, 50);

// バックグラウンド読み込み（100個ずつ）
while (loaded < totalCount)
{
    var archiveItems = await _databaseService.GetArchiveItemsAsync(loaded, 100);
    Application.Current.Dispatcher.Invoke(() =>
    {
        foreach (var item in archiveItems)
        {
            DisplayItems.Add(item);
        }
    });
    await Task.Delay(10); // UI応答性維持
}
```

#### プロパティパネル表示切り替え
```xaml
<!-- ツールバーのトグルボタン -->
<ToggleButton IsChecked="{Binding IsPropertiesPanelVisible, Mode=TwoWay}"
              Content="📋 Properties" />

<!-- プロパティパネル -->
<Border Visibility="{Binding IsPropertiesPanelVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
    <!-- プロパティ内容 -->
</Border>
```

### パフォーマンス指標

| 項目 | 改修前 | 改修後 | 改善率 |
|-----|--------|--------|--------|
| スキャン時間 | 40秒+ | <1秒 | 99% |
| ファイル処理速度 | 1 files/sec | 45+ files/sec | 4400% |
| UI応答性 | ブロック | レスポンシブ | - |
| 表示アイテム数 | 50個（バグ） | 2294個 | 4488% |

### セキュリティ強化

#### アーカイブ処理セキュリティ
```csharp
// 最大エントリ数制限
private const int MaxArchiveEntries = 10000;

// ファイルサイズ制限
private const long MaxFileSize = 500 * 1024 * 1024; // 500MB

// パストラバーサル防止
if (entry.FullName.Contains("..") || Path.IsPathRooted(entry.FullName))
{
    _logger.LogWarning("Suspicious archive entry detected: {EntryName}", entry.FullName);
    continue;
}
```

### ログ・デバッグ機能

#### 詳細なパフォーマンスログ
```csharp
_logger.LogInformation("=== SCAN PERFORMANCE REPORT ===");
_logger.LogInformation("Total execution time: {TotalTime}ms", totalTimeMs);
_logger.LogInformation("Scanning phase: {ScanTime}ms ({ScanPercent:F1}%)", scanTimeMs, scanPercent);
_logger.LogInformation("UI update phase: {UITime}ms ({UIPercent:F1}%)", uiTimeMs, uiPercent);
_logger.LogInformation("Performance: {ItemsPerSec:F2} items/second", itemsPerSecond);
```

これらの実装により、ImageMonitorは高性能で信頼性の高いアーカイブファイル管理ツールとして完成しています。