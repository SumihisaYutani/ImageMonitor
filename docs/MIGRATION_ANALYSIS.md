# MovieMonitor からの移植分析

## 流用可能なソースコード分析

### 1. ほぼそのまま流用可能（90%以上）

#### インフラストラクチャ・基盤コード
```csharp
// App.xaml.cs - DI設定、アプリケーション初期化
services.AddSingleton<IConfigurationService, ConfigurationService>();
services.AddSingleton<IDatabaseService, DatabaseService>();
services.AddSingleton<IThumbnailService, ThumbnailService>();

// GlobalUsings.cs - 共通using文
global using System.Collections.ObjectModel;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;

// Converters/BooleanToVisibilityConverter.cs
public class BooleanToVisibilityConverter : IValueConverter
{
    // 完全に流用可能
}

// Extensions/LoggerExtensions.cs
public static class LoggerExtensions
{
    // ログ拡張メソッド - 完全に流用可能
}
```

#### 設定管理システム
```csharp
// Services/IConfigurationService.cs & ConfigurationService.cs
public interface IConfigurationService
{
    Task<AppSettings> GetSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
}

// Models/AppSettings.cs (一部プロパティ変更必要)
public class AppSettings
{
    public List<string> ScanDirectories { get; set; } = new();
    public int ThumbnailSize { get; set; } = 320;
    // public string? DefaultPlayer { get; set; }  // 削除
    // public string? FFmpegPath { get; set; }     // 削除
    public string? DefaultArchiveViewer { get; set; } // 追加
    public decimal ImageRatioThreshold { get; set; } = 0.5m; // 追加
}
```

#### データベース基盤
```csharp
// Services/IDatabaseService.cs & DatabaseService.cs
public class DatabaseService : IDatabaseService, IDisposable
{
    private readonly ILiteDatabase _database;
    private readonly ILiteCollection<ImageItem> _imageItems; // VideoFile → ImageItem
    
    public async Task InitializeAsync()
    {
        // インデックス作成ロジック - ほぼそのまま流用可能
        _imageItems.EnsureIndex(x => x.FilePath);
        _imageItems.EnsureIndex(x => x.FileName);
    }
    
    // CRUD操作 - エンティティタイプ変更のみで流用可能
}
```

### 2. 軽微な修正で流用可能（70-90%）

#### MVVM基盤
```csharp
// ViewModels/MainViewModel.cs
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<ImageItem> _imageItems = new(); // VideoFile → ImageItem
    
    [ObservableProperty]
    private string _searchQuery = string.Empty; // そのまま流用
    
    [ObservableProperty]
    private bool _isScanning; // そのまま流用

    [RelayCommand]
    private async Task ScanImagesAsync() // ScanVideosAsync → ScanImagesAsync
    {
        // スキャンロジック構造は同じ、対象ファイルタイプのみ変更
    }

    [RelayCommand]
    private async Task OpenImageAsync(ImageItem imageItem) // PlayVideoAsync → OpenImageAsync
    {
        // 外部アプリ起動ロジック - ほぼ同じ
    }
}
```

#### UI構造（XAML）
```xml
<!-- MainWindow.xaml - レイアウト構造は流用可能 -->
<ListBox ItemsSource="{Binding ImageItems}" 
         SelectedItem="{Binding SelectedImageItem}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Border Style="{StaticResource CardBorderStyle}">
                <Grid>
                    <Image Source="{Binding ThumbnailPath}"/> <!-- そのまま流用 -->
                    <Button Command="{Binding DataContext.OpenImageCommand}"/> <!-- コマンド名のみ変更 -->
                </Grid>
            </Border>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

#### 検索・フィルタリング
```csharp
// Models/SearchFilter.cs - 構造はそのまま、フィルタ条件を画像用に調整
public class SearchFilter
{
    public string Query { get; set; } = string.Empty; // そのまま
    public long? MinFileSize { get; set; } // そのまま
    public long? MaxFileSize { get; set; } // そのまま
    public DateTime? DateFrom { get; set; } // そのまま
    public DateTime? DateTo { get; set; } // そのまま
    // public double? MinDuration { get; set; } // 削除
    // public double? MaxDuration { get; set; } // 削除
    public int? MinWidth { get; set; } // そのまま
    public int? MaxWidth { get; set; } // そのまま
    public int? MinHeight { get; set; } // そのまま
    public int? MaxHeight { get; set; } // そのまま
}
```

### 3. 大幅な改修が必要（50-70%）

#### データモデル
```csharp
// Models/VideoFile.cs → Models/ImageItem.cs
public class ImageItem // VideoFile → ImageItem
{
    [BsonId]
    public string Id { get; set; } = string.Empty; // そのまま
    
    public string FilePath { get; set; } = string.Empty; // そのまま
    public string FileName { get; set; } = string.Empty; // そのまま
    public long FileSize { get; set; } // そのまま
    public int Width { get; set; } // そのまま
    public int Height { get; set; } // そのまま
    public string? ThumbnailPath { get; set; } // そのまま
    public DateTime CreatedAt { get; set; } // そのまま
    public DateTime ModifiedAt { get; set; } // そのまま
    public DateTime ScanDate { get; set; } // そのまま
    public bool IsDeleted { get; set; } // そのまま
    
    // 削除するプロパティ
    // public double Duration { get; set; }
    
    // 追加するプロパティ
    public bool IsArchived { get; set; }
    public string? ArchivePath { get; set; }
    public string? InternalPath { get; set; } // アーカイブ内パス
}
```

#### サムネイル生成サービス
```csharp
// Services/ThumbnailService.cs - 大幅改修必要
public class ThumbnailService : IThumbnailService
{
    // FFMpeg → System.Drawing.Common または ImageSharp
    public async Task<string?> GenerateThumbnailAsync(string imagePath)
    {
        // 完全に新規実装が必要
        using var originalImage = Image.FromFile(imagePath);
        using var thumbnail = originalImage.GetThumbnailImage(320, 240, () => false, IntPtr.Zero);
        
        var thumbnailPath = GetThumbnailPath(imagePath);
        thumbnail.Save(thumbnailPath, ImageFormat.Jpeg);
        return thumbnailPath;
    }
    
    // アーカイブ内画像のサムネイル生成 - 新規機能
    public async Task<string?> GenerateArchiveThumbnailAsync(string archivePath, string internalPath)
    {
        // ZipFile/RarFileを使用した実装が必要
    }
}
```

### 4. 新規実装が必要（0-30%流用）

#### アーカイブサービス（新規）
```csharp
// Services/IArchiveService.cs - 完全新規
public interface IArchiveService
{
    Task<ArchiveInfo> AnalyzeArchiveAsync(string archivePath);
    Task<List<ImageInArchive>> ExtractImageListAsync(string archivePath);
    bool IsArchiveFile(string filePath);
    bool MeetsImageRatioThreshold(ArchiveInfo info, decimal threshold);
}

// 実装も完全新規 - MovieMonitorには存在しない機能
public class ArchiveService : IArchiveService
{
    public async Task<ArchiveInfo> AnalyzeArchiveAsync(string archivePath)
    {
        // ZIP/RAR解析ロジック - 新規実装
    }
}
```

#### スキャンサービス（大幅改修）
```csharp
// Services/VideoScanService.cs → Services/ImageScanService.cs
public class ImageScanService : IImageScanService
{
    // FFProbe → System.Drawing や EXIF読み取りライブラリ
    public async Task<ImageItem?> ProcessImageFileAsync(string filePath)
    {
        // 画像メタデータ読み取り - 完全に新規実装
        using var image = Image.FromFile(filePath);
        
        return new ImageItem
        {
            Width = image.Width,
            Height = image.Height,
            // EXIF情報の読み取りなど
        };
    }
    
    // アーカイブスキャン機能 - 完全新規
    public async Task<List<ImageItem>> ScanArchiveAsync(string archivePath)
    {
        // アーカイブ内画像検索ロジック
    }
}
```

#### 外部アプリ起動サービス（改修）
```csharp
// 動画プレイヤー起動 → アーカイブビューワー起動
public class LauncherService : ILauncherService
{
    public async Task<bool> LaunchArchiveViewerAsync(string archivePath)
    {
        // Process.Start の基本ロジックは同じ
        // ただし対象アプリケーションが変更
    }
}
```

## 流用優先度マトリックス

| コンポーネント | 流用率 | 優先度 | 作業量 |
|----------------|--------|--------|--------|
| App.xaml.cs (DI設定) | 95% | 高 | 小 |
| DatabaseService | 90% | 高 | 小 |
| ConfigurationService | 90% | 高 | 小 |
| MainViewModel | 80% | 高 | 中 |
| SearchFilter | 80% | 中 | 小 |
| XAML UI構造 | 75% | 高 | 中 |
| AppSettings | 70% | 中 | 小 |
| ImageItem Model | 60% | 高 | 中 |
| ThumbnailService | 40% | 高 | 大 |
| ScanService | 30% | 高 | 大 |
| ArchiveService | 0% | 高 | 大 |

## 推奨移植順序

### Phase 1: 基盤構築
1. プロジェクト作成・DI設定
2. データモデル（ImageItem, AppSettings）
3. DatabaseService
4. ConfigurationService

### Phase 2: コア機能
1. ImageScanService（基本画像スキャン）
2. ThumbnailService（基本サムネイル生成）
3. MainViewModel（基本UI）

### Phase 3: 拡張機能
1. ArchiveService（アーカイブ処理）
2. LauncherService（外部アプリ連携）
3. 高度な検索・フィルタ機能

### Phase 4: 最適化・改善
1. パフォーマンスチューニング
2. エラーハンドリング強化
3. UI/UXの最適化