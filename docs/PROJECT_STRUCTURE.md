# ImageMonitor プロジェクト構造設計書

## 全体構成

```
ImageMonitor/
├── src/                          # ソースコード
│   ├── ImageMonitor/             # メインアプリケーション
│   │   ├── App.xaml              # アプリケーションエントリーポイント
│   │   ├── App.xaml.cs
│   │   ├── MainWindow.xaml       # メインウィンドウ
│   │   ├── MainWindow.xaml.cs
│   │   ├── Models/               # データモデル
│   │   │   ├── ImageItem.cs      # 画像アイテムモデル
│   │   │   ├── ArchiveItem.cs    # アーカイブアイテムモデル
│   │   │   ├── ScanDirectory.cs  # スキャン対象ディレクトリ
│   │   │   ├── SearchFilter.cs   # 検索フィルター
│   │   │   └── AppSettings.cs    # アプリケーション設定
│   │   ├── Services/             # ビジネスロジック・サービス
│   │   │   ├── IFileService.cs   # ファイル操作インターフェース
│   │   │   ├── FileService.cs    # ファイル操作実装
│   │   │   ├── IArchiveService.cs    # アーカイブ操作インターフェース
│   │   │   ├── ArchiveService.cs     # アーカイブ操作実装
│   │   │   ├── IThumbnailService.cs  # サムネイル生成インターフェース
│   │   │   ├── ThumbnailService.cs   # サムネイル生成実装
│   │   │   ├── IDatabaseService.cs   # データベース操作インターフェース
│   │   │   ├── DatabaseService.cs    # LiteDB操作実装
│   │   │   ├── ILauncherService.cs   # 外部アプリ起動インターフェース
│   │   │   ├── LauncherService.cs    # 外部アプリ起動実装
│   │   │   └── ILoggingService.cs    # ログサービスインターフェース
│   │   ├── ViewModels/           # MVVM ViewModels
│   │   │   ├── MainViewModel.cs  # メインビューモデル
│   │   │   ├── ImageListViewModel.cs     # 画像一覧ビューモデル
│   │   │   ├── SearchViewModel.cs        # 検索ビューモデル
│   │   │   ├── SettingsViewModel.cs      # 設定ビューモデル
│   │   │   └── ViewModelBase.cs          # ベースビューモデル
│   │   ├── Views/                # UI Views
│   │   │   ├── MainView.xaml     # メインビュー
│   │   │   ├── ImageListView.xaml        # 画像一覧ビュー
│   │   │   ├── SearchView.xaml           # 検索ビュー
│   │   │   ├── SettingsView.xaml         # 設定ビュー
│   │   │   └── Controls/                 # カスタムコントロール
│   │   │       ├── ThumbnailControl.xaml    # サムネイル表示
│   │   │       └── FilterControl.xaml       # フィルターコントロール
│   │   ├── Resources/            # リソースファイル
│   │   │   ├── Styles/           # スタイルシート
│   │   │   │   ├── MaterialDesign.xaml   # Material Designテーマ
│   │   │   │   ├── Colors.xaml           # カラーパレット
│   │   │   │   └── Typography.xaml       # フォント設定
│   │   │   ├── Images/           # 画像リソース
│   │   │   │   ├── Icons/        # アイコンファイル
│   │   │   │   └── Logos/        # ロゴファイル
│   │   │   └── Localization/     # 多言語対応（将来用）
│   │   ├── Converters/           # WPF Value Converters
│   │   │   ├── BoolToVisibilityConverter.cs
│   │   │   ├── FileSizeConverter.cs
│   │   │   └── DateTimeConverter.cs
│   │   ├── Utilities/            # ユーティリティクラス
│   │   │   ├── FileTypeHelper.cs     # ファイル種別判定
│   │   │   ├── ArchiveHelper.cs      # アーカイブ操作ヘルパー
│   │   │   ├── ThumbnailCache.cs     # サムネイルキャッシュ
│   │   │   └── PathHelper.cs         # パス操作ヘルパー
│   │   └── ImageMonitor.csproj   # プロジェクトファイル
│   └── ImageMonitor.Tests/       # テストプロジェクト
│       ├── Services/             # サービステスト
│       ├── ViewModels/           # ViewModelテスト
│       ├── Utilities/            # ユーティリティテスト
│       └── ImageMonitor.Tests.csproj
├── docs/                         # ドキュメント
│   ├── PROJECT_STRUCTURE.md      # このファイル
│   ├── TECHNICAL_SPEC.md         # 技術仕様書
│   ├── API_REFERENCE.md          # API リファレンス（将来用）
│   └── DEVELOPMENT_GUIDE.md      # 開発ガイド（将来用）
├── config/                       # 設定ファイル
│   ├── appsettings.json          # アプリケーション設定
│   └── logging.json              # ログ設定
├── build/                        # ビルド関連
│   ├── scripts/                  # ビルドスクリプト
│   └── packages/                 # パッケージ出力
├── assets/                       # 静的アセット
│   ├── icons/                    # アプリケーションアイコン
│   └── samples/                  # サンプルファイル
├── .gitignore                    # Git除外設定
├── .editorconfig                 # エディター設定
├── Directory.Build.props         # MSBuild共通設定
├── ImageMonitor.sln              # ソリューションファイル
├── LICENSE                       # ライセンスファイル
└── README.md                     # プロジェクト概要
```

## レイヤー構成

### 1. プレゼンテーション層（Views/ViewModels）
- **責務**: UI表示、ユーザー操作の処理
- **技術**: WPF、XAML、MVVM パターン
- **コンポーネント**:
  - MainView: アプリケーションのメインUI
  - ImageListView: 画像一覧表示
  - SearchView: 検索・フィルター機能
  - SettingsView: 設定画面

### 2. アプリケーション層（Services）
- **責務**: ビジネスロジック、外部システム連携
- **技術**: 依存性注入、インターフェース分離
- **サービス**:
  - FileService: ファイル操作
  - ArchiveService: アーカイブファイル処理
  - ThumbnailService: サムネイル生成
  - DatabaseService: データベース操作
  - LauncherService: 外部アプリケーション起動

### 3. ドメイン層（Models）
- **責務**: ドメインモデル、ビジネスルール
- **技術**: POCO、データアノテーション
- **モデル**:
  - ImageItem: 画像ファイル情報
  - ArchiveItem: アーカイブファイル情報
  - ScanDirectory: スキャン対象ディレクトリ

### 4. インフラストラクチャ層（Utilities/Database）
- **責務**: 技術的な実装詳細
- **技術**: LiteDB、System.IO、外部ライブラリ
- **コンポーネント**:
  - データベースアクセス
  - ファイルシステムアクセス
  - サムネイルキャッシュ

## 主要な設計パターン

### MVVM（Model-View-ViewModel）
- **View**: XAML による宣言的UI
- **ViewModel**: UI状態管理、コマンド処理
- **Model**: データモデル、ビジネスロジック

### 依存性注入（Dependency Injection）
- **Container**: Microsoft.Extensions.DependencyInjection
- **Lifetime**: Singleton/Scoped/Transient
- **Interface**: テスタビリティの向上

### Repository パターン
- **抽象化**: データアクセスの抽象化
- **実装**: LiteDB による具体実装
- **テスト**: モック可能な設計

## ファイル命名規則

### クラスファイル
- **Models**: `{EntityName}.cs` (例: `ImageItem.cs`)
- **Services**: `{ServiceName}Service.cs` (例: `FileService.cs`)
- **ViewModels**: `{ViewName}ViewModel.cs` (例: `MainViewModel.cs`)
- **Views**: `{ViewName}.xaml` (例: `MainView.xaml`)

### インターフェース
- **命名**: `I{ServiceName}.cs` (例: `IFileService.cs`)
- **配置**: 実装クラスと同じディレクトリ

### テストファイル
- **命名**: `{TargetClass}Tests.cs` (例: `FileServiceTests.cs`)
- **配置**: `src/{ProjectName}.Tests/` 以下

## 依存関係管理

### プロジェクト間依存
```
ImageMonitor.Tests → ImageMonitor (テスト参照)
```

### 外部ライブラリ依存
- **UI**: Microsoft.WindowsDesktop.App
- **MVVM**: CommunityToolkit.Mvvm
- **Database**: LiteDB
- **Logging**: Serilog
- **Archive**: SharpZipLib, SharpCompress
- **Image**: System.Drawing.Common

## ビルド・配布戦略

### 開発環境
- **構成**: Debug
- **出力**: `bin/Debug/net8.0-windows/`
- **特徴**: デバッグ情報含有、ホットリロード対応

### 本番環境
- **構成**: Release
- **出力**: `bin/Release/net8.0-windows/`
- **特徴**: 最適化、単一実行ファイル、自己完結型

### 配布パッケージ
- **形式**: ZIP アーカイブ
- **内容**: 実行ファイル、設定ファイル、ライセンス
- **サイズ**: 約164MB（.NET Runtime含む）