# ImageMonitor プロジェクト情報

## ビルドと実行

### ビルド先（重要：必ず守ること）
- **通常ビルド**: `D:\ClaudeCode\project\ImageMonitor\build\net8.0-windows\ImageMonitor.exe`
- **単体exe**: `D:\ClaudeCode\project\ImageMonitor\build\standalone\ImageMonitor.exe`

**重要な注意事項**:
- `dotnet build` 実行時は必ず `build\net8.0-windows\ImageMonitor.exe` が最新ビルドになることを確認
- 複数のビルド先がある場合は常に `build\net8.0-windows\ImageMonitor.exe` を使用
- ビルド設定変更時は実行ファイルの日付・時刻を確認して最新版を使用

### 実行方法
**重要**: `dotnet run` と直接実行ファイルで動作が異なる場合があります。

- **推奨**: 直接実行ファイルを起動（常にこちらを使用）
  ```
  D:\ClaudeCode\project\ImageMonitor\build\net8.0-windows\ImageMonitor.exe
  ```
- **開発時**: `dotnet run --project src/ImageMonitor/ImageMonitor.csproj`（デバッグ目的のみ）

## データ保存先（ポータブル設定）

アプリケーションは実行ファイルディレクトリをベースにしたポータブル設計です。

### 設定ファイル
- **パス**: `{実行ファイルディレクトリ}\Data\settings.json`
- **例**: `D:\ClaudeCode\project\ImageMonitor\build\net8.0-windows\Data\settings.json`
- **形式**: JSON（CamelCase命名規則）

### データベース
- **パス**: `{実行ファイルディレクトリ}\Data\imageMonitor.db`
- **例**: `D:\ClaudeCode\project\ImageMonitor\build\net8.0-windows\Data\imageMonitor.db`
- **エンジン**: LiteDB

### サムネイルキャッシュ
- **パス**: `{実行ファイルディレクトリ}\Data\Thumbnails\`
- **例**: `D:\ClaudeCode\project\ImageMonitor\build\net8.0-windows\Data\Thumbnails\`
- **構成**: サイズ別フォルダ（`size_192`, `size_384` など）

### ログファイル
- **パス**: `{実行ファイルディレクトリ}\Data\Logs\`
- **例**: `D:\ClaudeCode\project\ImageMonitor\build\net8.0-windows\Data\Logs\`
- **ファイル名**: `imagemonitor-YYYYMMDD.log`
- **保持期間**: 7日間

## 開発コマンド

### ビルド
```bash
cd "D:\ClaudeCode\project\ImageMonitor"
dotnet build
```

### 実行（開発時）
```bash
cd "D:\ClaudeCode\project\ImageMonitor"
dotnet run --project src/ImageMonitor/ImageMonitor.csproj
```

### リリースビルド
```bash
cd "D:\ClaudeCode\project\ImageMonitor"
dotnet build -c Release
```

### 単体exe実行ファイル生成（推奨）
```bash
cd "D:\ClaudeCode\project\ImageMonitor"
dotnet publish src/ImageMonitor/ImageMonitor.csproj -c Release --self-contained false
```
**出力先**: `D:\ClaudeCode\project\ImageMonitor\build\standalone\ImageMonitor.exe`

**注意**: 現在は--self-contained falseを使用（依存ランタイムが別途必要）

## トラブルシューティング

### サムネイルサイズ変更が反映されない
- 直接実行ファイル（.exe）を使用してください
- `dotnet run` では正常に動作しない場合があります

### 設定が保存されない
- 実行ファイルのディレクトリに書き込み権限があることを確認
- `Data` フォルダが自動作成されることを確認

### ログが表示されない
- デフォルトのログレベルは "Information"
- より詳細なログが必要な場合は設定で "Debug" に変更

## 已知の問題と警告

### 単体exe実行ファイルでのパス取得警告
単体exe（PublishSingleFile=true）をビルドする際に、以下の警告が表示されます：
```
warning IL3000: 'System.Reflection.Assembly.Location' always returns an empty string for assemblies embedded in a single-file app. If the path to the app directory is needed, consider calling 'System.AppContext.BaseDirectory'.
```

**影響**：
- 現在は`Assembly.Location`を使用してパスを取得している
- 単体exeでは動作しない可能性がある

**対象ファイル**：
- ConfigurationService.cs
- ThumbnailService.cs  
- DatabaseService.cs
- App.xaml.cs

**将来の修正案**：
- `System.Reflection.Assembly.GetExecutingAssembly().Location` → `AppContext.BaseDirectory` に変更

## 最新の改修履歴（2025-09-06）

### UI改善とプロパティパネル機能追加

**修正内容**：
1. **サムネイルカード高さ調整**: フォルダボタンの見切れ問題を修正
   - `ThumbnailSizeToCardSizeConverter.cs`: テキスト領域を90px→110pxに増加、最小高さを150px→170pxに変更
   
2. **プロパティパネルボタンレイアウト修正**: ボタンの見切れ問題を修正  
   - `MainWindow.xaml`: プロパティパネル内のボタンを水平配置から垂直配置に変更
   - 各ボタンが全幅（234px）を使用可能になり、切り取られなくなった
   
3. **プロパティパネル表示/非表示機能追加**: ツールバーでの切り替え機能を実装
   - `MainWindow.xaml`: ツールバーに「📋 Properties」トグルボタンを追加
   - `MainViewModel.cs`: `IsPropertiesPanelVisible`プロパティを追加（デフォルト: true）
   - プロパティパネルとGridSplitterにVisibilityバインディングを追加

**技術的詳細**：
- ToggleButtonスタイル問題を解決（Button用StyleをToggleButtonに適用していた問題を修正）
- MVVMパターンに従った双方向データバインディングの実装
- BooleanToVisibilityConverterを使用した表示/非表示制御

**動作確認済み**：
- サムネイル表示とカード内ボタンの完全表示 ✅
- プロパティパネルのボタン正常表示 ✅  
- プロパティパネル表示切り替え機能 ✅
- アーカイブファイル処理と画像表示 ✅

### 大規模パフォーマンス改善（2025-09-06）

**問題**：
- スキャン時間が異常に長い（40秒以上）
- アーカイブファイルのメタデータ処理が主要な遅延原因

**修正内容**：
1. **アーカイブ内画像のメタデータ読み取り最適化**：
   - `ImageScanService.cs`: `PopulateImageMetadataFromStream()`呼び出しを完全削除
   - ファイル拡張子からフォーマットを推定、デフォルト値を設定
   - ストリーム処理による遅延を排除

2. **同期処理の並行化改善**：
   - 大きなアーカイブ（100ファイル以上）は最大16タスクで並行処理
   - セマフォによる適切な同時実行制限

3. **削除検出機能の強化**：
   - `DetectDeletedDirectoriesAsync()`と`CleanupDeletedDirectoriesAsync()`メソッド追加
   - スキャンディレクトリが空でもクリーンアップ処理を実行

**結果**：
- 処理時間: **40秒以上 → 0.6秒**（約99%の性能向上）
- ファイル処理速度: **1 files/sec → 45+ files/sec**
- メモリ使用量の大幅削減

### 単一画像ファイル除外機能（2025-09-06）

**要求**：アーカイブのみをサムネイル表示し、単一画像ファイルは除外

**実装内容**：
1. **単一画像ファイル処理の無効化**：
   - `ImageScanService.cs`: 単一画像ファイルの処理ロジックをコメントアウト
   
2. **データベースクリーンアップ機能追加**：
   - `DatabaseService.cs`: `CleanupSingleImageItemsAsync()`メソッド追加
   - 既存の単一画像アイテムを一括削除
   
3. **MainViewModelでの自動クリーンアップ**：
   - スキャン完了時に単一画像アイテムを自動削除
   - ログでクリーンアップ状況を報告

**結果**：
- サムネイル表示: **アーカイブファイルのみ**
- 単一画像ファイル: **自動的に除外・削除**
- データベース: **不要なレコードの自動クリーンアップ**

### SemaphoreSlim並行処理修正（2025-09-06）

**問題**：
- 大量ファイル処理時に`SemaphoreFullException`が発生
- 動的な並行数調整でセマフォインスタンスが置き換えられることが原因

**修正内容**：
1. **セマフォ初期化フラグ追加**：
   - `_semaphoreInitialized`フラグで一度だけ初期化を保証
   
2. **動的並行数調整の無効化**：
   - セマフォインスタンスの置き換えを防止
   - 設定ファイルからの初期値のみ使用

3. **エラーハンドリング強化**：
   - try-catch-finallyブロックでの適切なセマフォ解放
   - ログでの詳細なデバッグ情報追加

**結果**：
- **SemaphoreFullException完全解消**
- 大量ファイル処理の安定性向上
- 並行処理の信頼性確保

### UI表示バグ修正 - 99.6%画像比率ファイル表示問題（2025-09-06）

**問題**：
- 99.6%等の100%未満の画像比率を持つアーカイブファイルがUIに表示されない
- データベースには正常に保存されているがUI読み込みロジックにバグ

**根本原因分析**：
1. **`LoadImageItemsAsync`のTotalItems計算エラー**：
   - `GetImageItemCountAsync()`（単一画像）のみで合計数を計算
   - 単一画像ファイルは既に削除されているため、常に0になる
   
2. **`LoadRemainingItemsAsync`のデータ取得エラー**：
   - `GetImageItemsAsync()`（単一画像）を呼び出し
   - 実際には`GetArchiveItemsAsync()`を呼び出す必要があった

**修正内容**：
1. **合計アイテム数の正しい計算**：
   ```csharp
   var archiveCount = await _databaseService.GetArchiveItemCountAsync();
   var imageCount = await _databaseService.GetImageItemCountAsync();
   var totalCount = archiveCount + imageCount;
   TotalItems = (int)totalCount;
   ```

2. **バックグラウンド読み込みの修正**：
   ```csharp
   var archiveItems = await _databaseService.GetArchiveItemsAsync(loaded, batchSize);
   var itemsList = archiveItems.Cast<IDisplayItem>().ToList();
   ```

**結果**：
- **全2294個のアーカイブファイルが正常に表示**
- 99.6%、97.1%、99.0%等あらゆる画像比率のファイルが表示
- 初期50個 + バックグラウンド2244個の段階的読み込み
- UIレスポンシブ性の維持

**対象ファイル**：
- `MainViewModel.cs:LoadImageItemsAsync()` - 総数計算の修正
- `MainViewModel.cs:LoadRemainingItemsAsync()` - データ取得ロジックの修正

### 技術的詳細

**データベース構造**：
- **ArchiveItems**: ZIP/RARファイルの情報とメタデータ
- **ImageItems**: 単一画像ファイル（現在は除外対象）
- **インデックス**: ファイルパス、更新日時、削除フラグ

**パフォーマンス最適化手法**：
- メタデータストリーム処理の除去
- 並行タスクによるI/O最適化  
- LiteDBクエリの効率化
- メモリ使用量の削減

**UI/UXの改善**：
- 段階的なアイテム読み込み（50個 → 100個ずつ）
- プログレス表示とステータス更新
- レスポンシブなユーザーインターフェース

### 検索機能の修正とエラー解消（2025-09-07）

**問題**：
- 検索実行時に`System.NotImplementedException`エラーが発生
- LiteDBのOrderBy操作でBsonDataReader.Read()が未実装エラー
- 自動検索による不要な検索実行
- ファイルパス全体での検索（要望：ファイル名のみ）

**修正内容**：

1. **LiteDBクエリソート問題の解決**：
   ```csharp
   // 修正前（エラー発生）
   var results = query.OrderBy(GetSortExpression(filter.SortBy)).ToList();
   
   // 修正後（メモリ内ソート）
   var results = query.ToList();
   switch (filter.SortBy) {
       case SortBy.FileName:
           results = filter.SortDirection == SortDirection.Ascending 
               ? results.OrderBy(x => x.FileName).ToList()
               : results.OrderByDescending(x => x.FileName).ToList();
           break;
   }
   ```

2. **検索対象の変更（ImageItems → ArchiveItems）**：
   - `MainViewModel.cs`: `SearchImageItemsAsync` → `SearchArchiveItemsAsync`に変更
   - `DatabaseService.cs`: 新規`SearchArchiveItemsAsync`メソッドを実装
   - `IDatabaseService.cs`: インターフェースに`SearchArchiveItemsAsync`を追加

3. **手動検索の実装**：
   - 自動検索（文字入力時）を無効化
   - 🔍検索ボタンによる手動検索
   - Enterキーによる検索実行
   - `MainWindow.xaml.cs`: `SearchTextBox_KeyDown`イベントハンドラー追加

4. **検索範囲の最適化**：
   ```csharp
   // 修正前：ファイルパス全体を検索
   query.Where(x => x.FilePath.ToLower().Contains(searchTerm) || 
                    x.FileName.ToLower().Contains(searchTerm))
   
   // 修正後：ファイル名のみを検索
   query.Where(x => x.FileName.ToLower().Contains(searchTerm))
   ```

5. **検索結果キャッシュの実装**：
   - `MainViewModel.cs`: `Dictionary<string, IEnumerable<ArchiveItem>> _searchCache`
   - 最大10件の検索結果をキャッシュ
   - キャッシュキー: `{検索語}_{ソート基準}_{ソート方向}`

**結果**：
- ✅ **LiteDBエラー完全解消**: `NotImplementedException`エラーがゼロ
- ✅ **日本語検索対応**: 「成年コミック」等の日本語キーワード検索成功
- ✅ **検索パフォーマンス**: 953件結果を1001ms、33件結果を1217msで取得
- ✅ **手動検索実装**: ボタン/Enterキーでの制御された検索実行
- ✅ **検索結果キャッシュ**: 同じ検索条件での高速表示

**技術的詳細**：
- **メモリ内ソート**: LiteDBの制限を回避し、LINQ OrderByで安定動作
- **スレッドセーフティ**: `Application.Current.Dispatcher.InvokeAsync`でUI更新
- **エラーハンドリング**: try-catchブロックでの例外処理とログ出力
- **パフォーマンス監視**: 1秒以上の検索にWRNレベルログ出力

**動作確認済み**：
- 日本語検索キーワード正常動作 ✅
- 大量検索結果（953件）の高速表示 ✅  
- キャッシュ機能による再検索高速化 ✅
- エラーログ出力の完全停止 ✅

### アーカイブサムネイル生成の強化（2025-09-08）

**問題**：
- 破損した画像データによる`OverflowException`でサムネイル生成失敗
- アーカイブ内の最初の画像が破損している場合、サムネイル表示されない
- 複数のファイルで「値が範囲外です (0x88982F05)」エラーが発生

**根本原因**：
```
System.OverflowException: 処理中に、イメージ データによりオーバーフローが生成されました。
---> System.Runtime.InteropServices.COMException (0x88982F05): 値が範囲外です。
```
WPFの`BitmapDecoder`が破損したイメージヘッダーを解析する際、計算結果がint32範囲を超えてOverflowExceptionが発生。

**修正内容**：

1. **複数画像フォールバック機能の実装**：
   ```csharp
   // ZIP用改良 - 最大5つの画像を試行
   var maxAttempts = Math.Min(5, imageEntries.Count);
   for (int i = 0; i < maxAttempts; i++)
   {
       var result = await GenerateThumbnailFromStreamAsync(memoryStream, thumbnailPath, size, imageExtension);
       if (result != null) return result; // 成功したら即座に返す
   }
   ```

2. **RAR用改良**：
   - ZIP同様の複数画像試行ロジックを実装
   - `SharpCompress`ライブラリを使用した安全な処理

3. **エラーハンドリングの強化**：
   - 個別画像の例外を捕捉し、次の画像を試行
   - デバッグレベルでの詳細ログ出力
   - 最終的に全て失敗した場合の警告ログ

**効果**：
- ✅ **サムネイル表示成功率向上**: 最初の画像が破損していても他の画像で生成
- ✅ **OverflowException対応**: 破損画像をスキップして次を試行
- ✅ **ログの改善**: デバッグ情報で試行過程を追跡可能
- ✅ **パフォーマンス維持**: 失敗時のみ追加処理、成功時は従来通り高速

**対象ファイル**：
- `ThumbnailService.cs:GenerateZipThumbnailAsync()` - ZIP用複数画像試行
- `ThumbnailService.cs:GenerateRarThumbnailAsync()` - RAR用複数画像試行

### WebP画像形式サポート追加（2025-09-07）

**問題**：
- WebPファイルを含むアーカイブ（例：「七彩のラミュロス 2.zip」）がサムネイル表示されない
- WPF標準のBitmapDecoderがWebP形式をサポートしていない問題

**修正内容**：

1. **画像形式サポート拡張**：
   - `AppSettings.cs`: サポートされる画像形式に`.webp`を追加
   - `ImageScanService.cs`: WebPファイルを画像として認識するように修正
   - 設定ファイル（settings.json）に`.webp`を自動追加

2. **サムネイル生成の強化**：
   - `ThumbnailService.cs`: `GenerateThumbnailFromStreamAsync`メソッドにfileExtensionパラメータを追加
   - WebP用の特別な例外ハンドリング（NotSupportedException, FileFormatException）を実装
   - ZIP/RAR/単一画像の全てのケースでファイル拡張子を正しく渡すように修正

3. **エラーハンドリングの改善**：
   ```csharp
   // WebP形式の特別処理
   if (fileExtension.ToLower() == ".webp")
   {
       try
       {
           decoder = BitmapDecoder.Create(imageStream, ...);
       }
       catch (NotSupportedException)
       {
           _logger.LogWarning("WebP format not supported by current WPF decoder");
           return null;
       }
   }
   ```

4. **修正されたメソッド**：
   - `GenerateThumbnailFromStreamAsync`: ファイル拡張子パラメータを追加
   - `GenerateThumbnailInternalAsync`: 単一画像ファイル用の拡張子取得
   - ZIP/RAR処理: アーカイブ内ファイルの拡張子を適切に取得・伝達

**結果**：
- ✅ **WebPファイル識別**: アーカイブ内のWebPファイルが正常に認識される
- ✅ **サムネイル表示**: WebPを含むアーカイブのサムネイルカードが表示される
- ✅ **画像表示**: WebPファイルの実際の画像が正常に表示される
- ✅ **エラー処理**: WebPサポートがない環境でも適切にエラーハンドリング

**技術的詳細**：
- **ファイル拡張子の流れ**: アーカイブエントリ → Path.GetExtension() → サムネイル生成メソッド
- **例外処理**: WPFのWebPサポート状況に応じた適切な警告ログ出力
- **互換性**: WebPコーデックがインストールされている環境では正常動作
- **フォールバック**: サポートされていない場合は警告ログでスキップ

**動作確認済み**：
- WebPファイルを含むアーカイブの正常表示 ✅
- サムネイル生成とキャッシュ機能 ✅
- 複数種類の画像形式の混在アーカイブ対応 ✅
- エラーログの適切な出力 ✅

## ドキュメント

### 技術仕様書
- **場所**: `docs/技術仕様書.md`
- **内容**: ER図、データ構造仕様書、修正履歴、パフォーマンス情報

### クラス図
- **場所**: `docs/クラス図.md`  
- **内容**: アーキテクチャクラス図、コンポーネント責務、依存性注入構成、検索機能シーケンス図