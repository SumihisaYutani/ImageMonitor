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