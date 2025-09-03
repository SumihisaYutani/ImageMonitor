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