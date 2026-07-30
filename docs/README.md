# TNLAStation Backend ドキュメント

EPGStationのドキュメント構成を参考に、導入手順と詳細な仕様を目的別に分けています。

## 利用者向け

| ドキュメント | 内容 |
| --- | --- |
| [セットアップ](setup.md) | ローカル実行、Docker、migration、FFmpeg Worker |
| [設定リファレンス](configuration.md) | PostgreSQL、Mirakurun、録画、配信、保存先などの設定 |
| [config.yml互換性](epgstation-config-compatibility.md) | EPGStation v2.10.0公式設定一覧への対応状況とTNLAStation固有差分 |
| [EPGStation互換仕様](epgstation-compatibility.md) | API公開位置、JSON、エラー、予約・録画動作の互換方針 |

## 開発者・メンテナー向け

| ドキュメント | 内容 |
| --- | --- |
| [アーキテクチャ](architecture.md) | プロジェクト構成、依存方向、プロセス分割 |
| [コントリビューション](../CONTRIBUTING.md) | 開発手順とPull Requestの方針 |
| [リリース](../RELEASING.md) | リリースブランチ、検証、タグ、公開 |
| [セキュリティ](../SECURITY.md) | 脆弱性の報告方法 |

APIの機械可読な仕様は、起動中のサーバーが公開する `/api/docs` と
Swagger UIの `/api-docs` を参照してください。
