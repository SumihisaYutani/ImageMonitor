# ImageMonitor

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey.svg)

ImageMonitorは、画像ファイルとアーカイブファイルを効率的に検索・整理するためのWindows向けデスクトップアプリケーションです。

## 特徴

- **高速スキャン**: 複数ディレクトリの画像ファイルを自動スキャン
- **アーカイブ対応**: ZIP、RARファイル内の画像を検索対象に含める
- **サムネイル生成**: 画像ファイルのサムネイルを自動生成・表示
- **高度な検索**: LiteDBを使用した高速検索・フィルタリング
- **レスポンシブUI**: Material Design風の直感的なインターフェース
- **関連付け連携**: アーカイブファイルに関連付けられたビューワーアプリとの連携
- **軽量・高速**: パフォーマンス最適化により大量ファイルでも快適動作

## サポートファイル形式

### 画像ファイル
- JPEG (.jpg, .jpeg)
- PNG (.png)
- *将来予定: GIF, BMP, RAW形式等*

### アーカイブファイル
- ZIP (.zip)
- RAR (.rar)
- *将来予定: 7z, tar等*

> **注意**: アーカイブファイルは内容の50%以上が画像ファイルの場合のみ検索対象となります。

## システム要件

- **OS**: Windows 10/11 (64-bit)
- **Framework**: .NET 8.0 Runtime (アプリに同梱)
- **メモリ**: 最小 512MB RAM
- **ストレージ**: 約200MB（アプリケーション本体）

## インストール

### バイナリリリース（推奨）
1. [Releases](https://github.com/SumihisaYutani/ImageMonitor/releases)から最新版をダウンロード
2. ZIPファイルを展開
3. `ImageMonitor.exe`を実行

### ソースからビルド
```bash
git clone https://github.com/SumihisaYutani/ImageMonitor.git
cd ImageMonitor
dotnet build --configuration Release
```

## 📚 使用方法

### 🚀 クイックスタート
**5分で使い始める**: [はじめに（GETTING_STARTED.md）](docs/GETTING_STARTED.md)

### 📖 詳細マニュアル
**完全ガイド**: [ユーザーマニュアル（USER_MANUAL.md）](docs/USER_MANUAL.md)

### ⚡ 基本的な流れ
1. **設定**: ファイル → 設定 → スキャン対象フォルダを追加
2. **スキャン**: 「Scan」ボタンでファイル検索実行  
3. **検索**: Include Archives・Image Ratioでフィルタリング
4. **閲覧**: サムネイルをダブルクリックで画像表示

### アーカイブ連携機能
- アーカイブファイルに関連付けられたビューワーアプリが自動起動
- 画像ビューワー、アーカイブマネージャー等との連携
- システムの既定アプリ設定に従って動作

## パフォーマンス

- **スキャン速度**: 約1,000ファイル/分 (SSD環境)
- **検索速度**: インデックス使用によりミリ秒レベル
- **メモリ使用量**: 最大512MB (1,000アイテム処理時)
- **実行ファイルサイズ**: 約164MB (ポータブル版)

## アーキテクチャ

- **UI**: WPF + Material Design
- **データベース**: LiteDB (NoSQL、複合インデックス対応)
- **画像処理**: System.Drawing.Common
- **アーカイブ処理**: SharpZipLib, SharpCompress
- **ログ**: Serilog
- **MVVM**: CommunityToolkit.Mvvm

## 開発状況

### 実装済み機能
- [x] 画像ファイルスキャン
- [x] アーカイブファイル検出
- [x] サムネイル生成
- [x] 基本検索・フィルタ機能
- [x] データベース最適化

### 今後の予定
- [ ] 自動スキャン機能
- [ ] ダークテーマ対応
- [ ] EXIF情報表示
- [ ] 重複ファイル検出
- [ ] 追加画像形式対応
- [ ] 追加アーカイブ形式対応

## ライセンス

このプロジェクトはMITライセンスの下で公開されています。詳細は[LICENSE](LICENSE)ファイルを参照してください。

## コントリビューション

プルリクエストやイシューの報告を歓迎します。開発に参加される場合は、以下を参照してください：

1. このリポジトリをフォーク
2. 機能ブランチを作成 (`git checkout -b feature/amazing-feature`)
3. 変更をコミット (`git commit -m 'Add amazing feature'`)
4. ブランチをプッシュ (`git push origin feature/amazing-feature`)
5. プルリクエストを作成

## 関連プロジェクト

- [MovieMonitor](https://github.com/SumihisaYutani/MovieMonitor) - 動画ファイル版

## サポート

バグ報告や機能要求は[Issues](https://github.com/SumihisaYutani/ImageMonitor/issues)で受け付けています。

---

**注意**: このアプリケーションは画像ファイルとアーカイブファイルの管理を目的としており、ファイルの変更や削除は行いません。