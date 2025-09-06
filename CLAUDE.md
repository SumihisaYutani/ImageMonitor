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