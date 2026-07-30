# TNLAStation Backend

[![CI](https://github.com/miutaku/TNLAStation-backend/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/miutaku/TNLAStation-backend/actions/workflows/ci.yml)
[![Release](https://github.com/miutaku/TNLAStation-backend/actions/workflows/release.yml/badge.svg)](https://github.com/miutaku/TNLAStation-backend/actions/workflows/release.yml)
[![GitHub Release](https://img.shields.io/github/v/release/miutaku/TNLAStation-backend?cacheSeconds=300)](https://github.com/miutaku/TNLAStation-backend/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[EPGStation](https://github.com/l3tnun/EPGStation) 2.10.0の公開Web APIを.NET 10で再実装した録画管理バックエンドです。
[TNLAStation Frontend](https://github.com/miutaku/TNLAStation-frontend)など、EPGStation互換クライアントから利用できます。

## 主な機能

- Mirakurun互換チューナーサーバーからのEPG取得
- 番組予約、時刻指定予約、自動予約ルールと競合判定
- 録画、録画履歴、ドロップ検査、ストレージ管理
- ライブ・録画済み番組のストリーミング
- サムネイル生成とエンコード
- IPTVチャンネルリストとXMLTV
- Kodi連携とコマンドフック
- PostgreSQLによる永続化

対応するAPI領域と厳密な互換方針は[EPGStation互換仕様](docs/epgstation-compatibility.md)を参照してください。

## 必要なサービス

- .NET 10 SDK
- PostgreSQL
- Mirakurunまたは互換チューナーサーバー
- TNLAStation FFmpeg Worker

一式を動かす場合は、各サービスをまとめた
[TNLAStation Composeリポジトリ](https://github.com/miutaku/TNLAStation)の利用を推奨します。

## ローカルでの起動

EPGStation互換の設定テンプレートをコピーし、PostgreSQLの接続先を指定してmigrationを適用してからAPIを起動します。

```sh
cp config/config.yml.template config/config.yml
dotnet restore TNLAStation.sln

ConnectionStrings__PostgreSQL='Host=localhost;Database=tnlastation;Username=tnlastation;Password=...' \
  dotnet run --project src/TNLAStation.Migrator

ConnectionStrings__PostgreSQL='Host=localhost;Database=tnlastation;Username=tnlastation;Password=...' \
  dotnet run --project src/TNLAStation.Api
```

起動後は次のURLで確認できます。

- `/api/version` — ヘルスチェックにも使用する互換API
- `/api/docs` — OpenAPI 3.0文書
- `/api-docs` — Swagger UI

詳しい手順は[セットアップガイド](docs/setup.md)を参照してください。

## ドキュメント

- [ドキュメント一覧](docs/README.md)
- [セットアップ](docs/setup.md)
- [設定リファレンス](docs/configuration.md)
- [EPGStation互換仕様](docs/epgstation-compatibility.md)
- [アーキテクチャ](docs/architecture.md)
- [コントリビューション](CONTRIBUTING.md)
- [リリース手順](RELEASING.md)
- [セキュリティポリシー](SECURITY.md)

## ライセンス

[MIT License](LICENSE)
