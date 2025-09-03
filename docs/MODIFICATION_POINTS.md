# ImageMonitor 改修ポイント詳細

## 1. データモデルの改修

### VideoFile → ImageItem への変換

```csharp
// 削除するプロパティ
public double Duration { get; set; }        // 動画の再生時間
public string FormattedDuration { get; set; }

// 追加するプロパティ  
public bool IsArchived { get; set; }         // アーカイブ内のファイルかどうか
public string? ArchivePath { get; set; }     // 所属するアーカイブファイルのパス
public string? InternalPath { get; set; }    // アーカイブ内での相対パス
public string? ImageFormat { get; set; }     // 画像フォーマット (JPEG, PNG, etc.)
public int? ColorDepth { get; set; }         // 色深度
public bool HasExifData { get; set; }        // EXIF情報の有無
public DateTime? DateTaken { get; set; }     // 撮影日時（EXIF由来）

// 変更するプロパティの意味
public int Width { get; set; }               // 動画解像度 → 画像解像度
public int Height { get; set; }              // 動画解像度 → 画像解像度
```

### 新規エンティティの追加

```csharp
// アーカイブファイル情報
public class ArchiveItem
{
    public string Id { get; set; }
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string ArchiveType { get; set; }     // ZIP, RAR, etc.
    public int TotalFiles { get; set; }         // アーカイブ内総ファイル数
    public int ImageFiles { get; set; }         // アーカイブ内画像ファイル数
    public decimal ImageRatio { get; set; }     // 画像ファイル比率
    public string? AssociatedApp { get; set; }  // 関連付けアプリケーション
    public List<ImageInArchive> Images { get; set; } = new();
}

// アーカイブ内画像情報
public class ImageInArchive
{
    public string InternalPath { get; set; }    // アーカイブ内パス
    public string FileName { get; set; }
    public long FileSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? ThumbnailData { get; set; }  // Base64エンコードされたサムネイル
}
```

## 2. サービスの改修・追加

### ThumbnailService の大幅改修

```csharp
// 変更前（MovieMonitor）: FFMpeg使用
await FFMpegArguments
    .FromFileInput(videoPath)
    .OutputToFile(thumbnailPath, options => options
        .WithVideoFilters("scale=320:240")
        .WithFrameOutputCount(1))
    .ProcessAsynchronously();

// 変更後（ImageMonitor）: System.Drawing または ImageSharp使用
public async Task<string?> GenerateThumbnailAsync(string imagePath)
{
    using var originalImage = await Image.LoadAsync(imagePath);
    originalImage.Mutate(x => x.Resize(new ResizeOptions
    {
        Size = new Size(320, 240),
        Mode = ResizeMode.Max
    }));
    
    var thumbnailPath = GetThumbnailPath(imagePath);
    await originalImage.SaveAsJpegAsync(thumbnailPath);
    return thumbnailPath;
}

// 新機能: アーカイブ内画像のサムネイル生成
public async Task<string?> GenerateArchiveThumbnailAsync(string archivePath, string internalPath)
{
    using var archive = ZipFile.OpenRead(archivePath);
    var entry = archive.GetEntry(internalPath);
    if (entry == null) return null;
    
    using var stream = entry.Open();
    using var image = await Image.LoadAsync(stream);
    
    image.Mutate(x => x.Resize(320, 240));
    
    var thumbnailPath = GetArchiveThumbnailPath(archivePath, internalPath);
    await image.SaveAsJpegAsync(thumbnailPath);
    return thumbnailPath;
}
```

### VideoScanService → ImageScanService への改修

```csharp
// 変更前: FFProbe でメタデータ取得
var mediaInfo = await FFProbe.AnalyseAsync(filePath);
var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

// 変更後: 画像メタデータ取得
public async Task<ImageItem?> ProcessImageFileAsync(string filePath)
{
    try
    {
        using var image = await Image.LoadAsync(filePath);
        var fileInfo = new FileInfo(filePath);
        
        var imageItem = new ImageItem
        {
            Id = GenerateFileId(filePath),
            FilePath = filePath,
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            Width = image.Width,
            Height = image.Height,
            ImageFormat = image.Metadata.DecodedImageFormat?.Name,
            CreatedAt = fileInfo.CreationTime,
            ModifiedAt = fileInfo.LastWriteTime,
            ScanDate = DateTime.Now
        };
        
        // EXIF情報の取得
        if (image.Metadata.ExifProfile != null)
        {
            imageItem.HasExifData = true;
            // 撮影日時の取得など
            if (image.Metadata.ExifProfile.TryGetValue(ExifTag.DateTime, out var dateTime))
            {
                imageItem.DateTaken = DateTime.Parse(dateTime.Value);
            }
        }
        
        return imageItem;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "画像ファイル処理でエラーが発生: {FilePath}", filePath);
        return null;
    }
}
```

### 新規サービス: ArchiveService

```csharp
public class ArchiveService : IArchiveService
{
    private static readonly string[] SupportedArchiveExtensions = { ".zip", ".rar" };
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png" };
    private const decimal ImageRatioThreshold = 0.5m;
    
    public async Task<ArchiveInfo> AnalyzeArchiveAsync(string archivePath)
    {
        var extension = Path.GetExtension(archivePath).ToLowerInvariant();
        
        return extension switch
        {
            ".zip" => await AnalyzeZipAsync(archivePath),
            ".rar" => await AnalyzeRarAsync(archivePath),
            _ => throw new NotSupportedException($"Unsupported archive format: {extension}")
        };
    }
    
    private async Task<ArchiveInfo> AnalyzeZipAsync(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var allEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var imageEntries = allEntries.Where(e => IsImageFile(e.Name)).ToList();
        
        var archiveInfo = new ArchiveInfo
        {
            FilePath = zipPath,
            ArchiveType = "ZIP",
            TotalFiles = allEntries.Count,
            ImageFiles = imageEntries.Count,
            ImageRatio = allEntries.Count > 0 ? (decimal)imageEntries.Count / allEntries.Count : 0
        };
        
        // 50%以上が画像ファイルの場合のみ詳細解析
        if (archiveInfo.ImageRatio >= ImageRatioThreshold)
        {
            archiveInfo.Images = await ExtractImageMetadataFromZipAsync(archive, imageEntries);
        }
        
        return archiveInfo;
    }
    
    public bool MeetsImageRatioThreshold(ArchiveInfo info)
    {
        return info.ImageRatio >= ImageRatioThreshold;
    }
}
```

## 3. UI/XAML の改修

### MainWindow.xaml の変更点

```xml
<!-- 変更前: 動画用のコンテキストメニュー -->
<MenuItem Header="動画を再生" Command="{Binding PlayVideoCommand}" />
<MenuItem Header="フォルダを開く" Command="{Binding OpenFolderCommand}" />

<!-- 変更後: 画像・アーカイブ用のコンテキストメニュー -->
<MenuItem Header="画像を表示" Command="{Binding OpenImageCommand}" />
<MenuItem Header="アーカイブを開く" Command="{Binding OpenArchiveCommand}" />
<MenuItem Header="フォルダを開く" Command="{Binding OpenFolderCommand}" />

<!-- 新規追加: アーカイブフィルタ -->
<CheckBox Content="アーカイブファイルを含める" 
          IsChecked="{Binding IncludeArchives}" />
<Slider Minimum="0" Maximum="1" Value="{Binding ImageRatioThreshold}"
        ToolTip="アーカイブ内画像比率の閾値" />
```

### データテンプレートの変更

```xml
<!-- 動画情報表示 → 画像情報表示 -->
<DataTemplate x:Key="ImageItemTemplate">
    <Border Style="{StaticResource CardBorderStyle}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            
            <!-- サムネイル表示（変更なし） -->
            <Image Grid.Row="0" Source="{Binding ThumbnailPath}" />
            
            <!-- 情報表示（変更部分） -->
            <StackPanel Grid.Row="2" Orientation="Vertical">
                <TextBlock Text="{Binding FileName}" FontWeight="Bold"/>
                <TextBlock Text="{Binding Resolution}"/> <!-- Width x Height -->
                <TextBlock Text="{Binding FormattedFileSize}"/>
                <!-- 削除: Duration表示 -->
                <!-- 追加: アーカイブ情報 -->
                <TextBlock Text="{Binding ArchiveInfo}" 
                          Visibility="{Binding IsArchived, Converter={StaticResource BoolToVisibilityConverter}}"/>
            </StackPanel>
        </Grid>
    </Border>
</DataTemplate>
```

## 4. 検索・フィルタ機能の改修

### SearchFilter の変更

```csharp
// 削除するフィルタ
public double? MinDuration { get; set; }
public double? MaxDuration { get; set; }

// 追加するフィルタ
public bool IncludeArchives { get; set; } = true;        // アーカイブファイルを含めるか
public decimal ImageRatioThreshold { get; set; } = 0.5m; // アーカイブ内画像比率閾値
public List<string> ImageFormats { get; set; } = new();  // 対象画像形式
public bool HasExifData { get; set; }                    // EXIF情報有無でのフィルタ
public DateTime? DateTakenFrom { get; set; }             // 撮影日時範囲
public DateTime? DateTakenTo { get; set; }
```

### 検索クエリの変更

```csharp
// MovieMonitor の検索クエリ
public async Task<IEnumerable<VideoFile>> SearchVideoFilesAsync(SearchFilter filter)
{
    var query = _videoFiles.Query().Where(x => !x.IsDeleted);
    
    if (filter.MinDuration.HasValue)
        query = query.Where(x => x.Duration >= filter.MinDuration.Value);
}

// ImageMonitor の検索クエリ
public async Task<IEnumerable<ImageItem>> SearchImageItemsAsync(SearchFilter filter)
{
    var query = _imageItems.Query().Where(x => !x.IsDeleted);
    
    if (!filter.IncludeArchives)
        query = query.Where(x => !x.IsArchived);
        
    if (filter.ImageFormats.Any())
        query = query.Where(x => filter.ImageFormats.Contains(x.ImageFormat));
        
    if (filter.HasExifData)
        query = query.Where(x => x.HasExifData);
}
```

## 5. 設定管理の改修

### AppSettings の変更

```csharp
// 削除する設定
public string? DefaultPlayer { get; set; }
public string? FFmpegPath { get; set; }

// 追加する設定
public string? DefaultArchiveViewer { get; set; }     // 既定のアーカイブビューワー
public decimal ImageRatioThreshold { get; set; } = 0.5m; // アーカイブ画像比率閾値
public List<string> SupportedImageFormats { get; set; } = new() 
{ 
    ".jpg", ".jpeg", ".png" 
};
public List<string> SupportedArchiveFormats { get; set; } = new() 
{ 
    ".zip", ".rar" 
};
public bool GenerateArchiveThumbnails { get; set; } = true; // アーカイブサムネイル生成
public int MaxArchiveEntries { get; set; } = 10000;        // 処理可能な最大エントリ数
```

## 6. エラーハンドリングの改修

### 新規例外クラス

```csharp
public class ArchiveProcessingException : ImageMonitorException
{
    public string ArchivePath { get; }
    public ArchiveProcessingException(string archivePath, string message) 
        : base(message)
    {
        ArchivePath = archivePath;
    }
}

public class ImageProcessingException : ImageMonitorException  
{
    public string ImagePath { get; }
    public ImageProcessingException(string imagePath, string message) 
        : base(message)
    {
        ImagePath = imagePath;
    }
}
```

## 7. パフォーマンス最適化の改修

### アーカイブ処理の最適化

```csharp
// 大容量アーカイブファイルの処理制限
public async Task<bool> ValidateArchiveAsync(string archivePath)
{
    var fileInfo = new FileInfo(archivePath);
    
    // ファイルサイズ制限（例: 1GB）
    if (fileInfo.Length > 1024 * 1024 * 1024)
    {
        _logger.LogWarning("アーカイブファイルが大きすぎます: {FilePath} ({Size:N0} bytes)", 
            archivePath, fileInfo.Length);
        return false;
    }
    
    // エントリ数制限の事前チェック
    using var archive = ZipFile.OpenRead(archivePath);
    if (archive.Entries.Count > _settings.MaxArchiveEntries)
    {
        _logger.LogWarning("アーカイブのエントリ数が上限を超過: {FilePath} ({Count} entries)", 
            archivePath, archive.Entries.Count);
        return false;
    }
    
    return true;
}
```

### キャッシュ戦略の変更

```csharp
// アーカイブ内画像のキャッシュ戦略
public class ArchiveThumbnailCache
{
    private readonly Dictionary<string, string> _archiveImageCache = new();
    
    public string GetCacheKey(string archivePath, string internalPath)
    {
        // アーカイブパス + 内部パス + ファイル更新日時のハッシュ
        var archiveModified = File.GetLastWriteTime(archivePath);
        return $"{archivePath}#{internalPath}#{archiveModified:yyyyMMddHHmmss}".GetHashCode().ToString();
    }
}
```

## 改修作業の優先度

| 改修項目 | 緊急度 | 複雑度 | 推定工数 |
|----------|--------|--------|----------|
| データモデル変更 | 高 | 中 | 2-3日 |
| ArchiveService新規作成 | 高 | 高 | 5-7日 |
| ThumbnailService改修 | 高 | 中 | 3-4日 |
| ImageScanService改修 | 高 | 中 | 3-4日 |
| UI/XAML改修 | 中 | 低 | 2-3日 |
| 検索・フィルタ改修 | 中 | 中 | 2-3日 |
| 設定管理改修 | 低 | 低 | 1-2日 |
| エラーハンドリング | 低 | 低 | 1-2日 |

**総推定工数**: 19-30日（1人での作業想定）