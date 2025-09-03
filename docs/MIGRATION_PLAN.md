# ImageMonitor 移植計画書

## プロジェクト概要

**目標**: MovieMonitorのアーキテクチャとコードベースを活用し、画像ファイル・アーカイブファイル対応のImageMonitorを開発する

**期間**: 3-4週間（実働20-25日）
**体制**: 1名での開発想定

## フェーズ別実装計画

### Phase 1: プロジェクト基盤構築（3-4日）

#### Day 1: プロジェクト初期化
- [ ] .NET 8 WPF プロジェクト作成
- [ ] NuGetパッケージ導入
  - [ ] CommunityToolkit.Mvvm
  - [ ] LiteDB
  - [ ] Serilog
  - [ ] Microsoft.Extensions.Hosting
  - [ ] System.Drawing.Common or SixLabors.ImageSharp
  - [ ] SharpZipLib (ZIP対応)
  - [ ] SharpCompress (RAR対応)
- [ ] プロジェクト構造作成（フォルダ構成）
- [ ] GlobalUsings.cs 作成

#### Day 2: 基本設定・DI構成
- [ ] App.xaml.cs の DI設定（MovieMonitorから流用）
- [ ] Serilogの設定
- [ ] AppSettings モデル作成（ImageMonitor用に改修）
- [ ] ConfigurationService 流用・移植

#### Day 3: データモデル設計
- [ ] ImageItem モデル作成（VideoFile → 改修）
- [ ] ArchiveItem モデル作成（新規）
- [ ] ImageInArchive モデル作成（新規）
- [ ] SearchFilter モデル改修

#### Day 4: データベース基盤
- [ ] DatabaseService インターフェース・実装（流用・改修）
- [ ] LiteDB設定、インデックス定義
- [ ] 基本CRUD操作の実装・テスト

### Phase 2: コア機能実装（6-8日）

#### Day 5-6: 画像スキャンサービス
- [ ] IImageScanService インターフェース作成
- [ ] ImageScanService 実装（VideoScanService改修）
- [ ] 画像ファイル検出ロジック
- [ ] 画像メタデータ取得（解像度、EXIF情報）
- [ ] 単体テスト作成

#### Day 7-8: サムネイル生成サービス
- [ ] IThumbnailService インターフェース改修
- [ ] ThumbnailService 実装（FFMpeg → ImageSharp切り替え）
- [ ] 画像リサイズ・圧縮ロジック
- [ ] キャッシュ機能実装
- [ ] 単体テスト作成

#### Day 9-10: 基本UI実装
- [ ] MainViewModel 移植・改修（VideoFile → ImageItem）
- [ ] MainWindow.xaml 移植・改修
- [ ] 画像一覧表示機能
- [ ] 基本的な検索・フィルタ機能

#### Day 11-12: データ連携・永続化
- [ ] スキャン結果のデータベース保存
- [ ] UI → Service → Database の連携確認
- [ ] 設定の保存・読み込み機能
- [ ] エラーハンドリング強化

### Phase 3: アーカイブ機能実装（6-7日）

#### Day 13-14: アーカイブサービス基盤
- [ ] IArchiveService インターフェース作成
- [ ] ArchiveService 実装（ZIP対応）
- [ ] アーカイブファイル検出・解析
- [ ] 画像比率計算ロジック
- [ ] 単体テスト作成

#### Day 15-16: アーカイブ内画像処理
- [ ] アーカイブ内画像一覧取得
- [ ] アーカイブ内画像サムネイル生成
- [ ] アーカイブ情報のデータベース保存
- [ ] RAR形式対応（SharpCompress）

#### Day 17-18: UI統合・外部連携
- [ ] アーカイブファイルのUI表示
- [ ] 関連付けアプリ起動機能（LauncherService）
- [ ] アーカイブフィルタリング機能
- [ ] アーカイブビューワー連携

#### Day 19: 統合テスト
- [ ] アーカイブ機能の統合テスト
- [ ] パフォーマンステスト
- [ ] エラーケース対応

### Phase 4: 品質向上・最適化（3-4日）

#### Day 20-21: パフォーマンス最適化
- [ ] 大量ファイル処理の最適化
- [ ] 非同期処理の改善
- [ ] メモリ使用量の最適化
- [ ] キャッシュ戦略の改善

#### Day 22: エラーハンドリング・ログ強化
- [ ] 例外処理の統一
- [ ] ログ出力の標準化
- [ ] ユーザーエラー表示の改善
- [ ] リカバリ機能の実装

#### Day 23: ドキュメント・テスト
- [ ] APIドキュメント作成
- [ ] 操作マニュアル作成
- [ ] 結合テストケース実行
- [ ] コードレビュー・リファクタリング

## 詳細タスク一覧

### 高優先度タスク（必須機能）

| タスク | 参考元 | 改修度 | 工数 |
|--------|---------|--------|------|
| プロジェクト作成・DI設定 | App.xaml.cs | 10% | 0.5日 |
| ImageItem モデル作成 | VideoFile.cs | 40% | 0.5日 |
| DatabaseService 移植 | DatabaseService.cs | 20% | 1日 |
| ConfigurationService 移植 | ConfigurationService.cs | 10% | 0.5日 |
| ImageScanService 作成 | VideoScanService.cs | 60% | 2日 |
| ThumbnailService 改修 | ThumbnailService.cs | 70% | 2日 |
| MainViewModel 改修 | MainViewModel.cs | 30% | 1.5日 |
| MainWindow UI 改修 | MainWindow.xaml | 30% | 1.5日 |
| ArchiveService 新規作成 | - | 100% | 4日 |
| LauncherService 改修 | - | 50% | 1日 |

### 中優先度タスク（重要機能）

| タスク | 参考元 | 改修度 | 工数 |
|--------|---------|--------|------|
| SearchFilter 改修 | SearchFilter.cs | 40% | 1日 |
| SettingsViewModel 移植 | SettingsViewModel.cs | 20% | 0.5日 |
| Converter類 移植 | Converters/ | 5% | 0.5日 |
| Material Designスタイル | Styles.xaml | 15% | 1日 |
| EXIF情報表示機能 | - | 100% | 1.5日 |
| 重複ファイル検出 | - | 100% | 2日 |

### 低優先度タスク（追加機能）

| タスク | 参考元 | 改修度 | 工数 |
|--------|---------|--------|------|
| ダークテーマ対応 | - | 100% | 1日 |
| 自動スキャン機能 | - | 100% | 2日 |
| 多言語対応 | - | 100% | 3日 |
| プラグイン機能 | - | 100% | 5日 |

## リスク分析と対策

### 技術的リスク

| リスク | 影響度 | 確率 | 対策 |
|--------|--------|------|------|
| アーカイブライブラリの制限 | 高 | 中 | 複数ライブラリの調査・検証 |
| 大容量ファイル処理性能 | 中 | 高 | ストリーミング処理・制限実装 |
| RAR形式の対応不足 | 中 | 中 | SharpCompress以外の選択肢検討 |
| 画像処理ライブラリの選択 | 低 | 低 | ImageSharp vs System.Drawing比較 |

### スケジュールリスク

| リスク | 影響度 | 確率 | 対策 |
|--------|--------|------|------|
| アーカイブ機能の複雑化 | 高 | 中 | 段階的実装・機能絞り込み |
| UI設計の変更要求 | 中 | 中 | モックアップ事前作成 |
| テスト工数の過小評価 | 中 | 高 | テスト自動化・CI導入 |

## 成功基準

### 機能要件

- [ ] JPEG, PNG ファイルの検索・表示
- [ ] ZIP, RAR アーカイブ内画像の検索・表示  
- [ ] アーカイブファイルの関連付けアプリ起動
- [ ] 50%以上画像ファイルのアーカイブのみ対象
- [ ] 高速検索（1000ファイル/分以上のスキャン）
- [ ] サムネイル表示

### 品質要件

- [ ] メモリ使用量 512MB以下（1000アイテム処理時）
- [ ] 応答性能 1秒以内（通常操作）
- [ ] エラー率 1%以下
- [ ] ログ出力の適切性

### 操作性要件

- [ ] MovieMonitor同等の操作感
- [ ] 直感的なUI
- [ ] 適切なエラーメッセージ表示

## 必要リソース

### 開発環境
- Visual Studio 2022 または Visual Studio Code
- .NET 8.0 SDK
- Git

### テスト用データ
- サンプル画像ファイル（各形式100枚以上）
- サンプルアーカイブファイル（ZIP/RAR各10個以上）
- 大容量テストデータ（1GB以上のアーカイブ）

### 参照資料
- MovieMonitorソースコード
- .NET WPF公式ドキュメント
- ImageSharp/System.Drawing ドキュメント
- LiteDBドキュメント

## 完了基準

### Phase 1 完了基準
- [ ] プロジェクトがビルド・実行可能
- [ ] 基本的なDI・設定機能が動作
- [ ] データベース接続が確立

### Phase 2 完了基準  
- [ ] 画像ファイルのスキャン・表示が動作
- [ ] サムネイル生成が動作
- [ ] 基本的な検索・フィルタが動作

### Phase 3 完了基準
- [ ] アーカイブファイルのスキャン・表示が動作
- [ ] アーカイブビューワー起動が動作
- [ ] 画像比率フィルタが動作

### Phase 4 完了基準
- [ ] パフォーマンス目標達成
- [ ] エラーハンドリング完成
- [ ] ドキュメント完成

## 次のステップ

1. **プロジェクトセットアップ**: .NET 8 WPFプロジェクトの作成
2. **MovieMonitorコード取得**: 参考実装の詳細確認
3. **Phase 1開始**: 基盤構築からスタート