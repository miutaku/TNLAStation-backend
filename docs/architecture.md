# アーキテクチャ

## プロジェクト構成

```text
src/
  TNLAStation.Domain/          ドメインモデル
  TNLAStation.Application/     Repository抽象、query、command
  TNLAStation.Infrastructure/  PostgreSQL、Mirakurun、録画、変換、Kodi連携
  TNLAStation.Api/             HTTP、JSON、OpenAPI、互換DTO変換
  TNLAStation.Migrator/        schema migrationを適用するone-shotプロセス
  TNLAStation.FfmpegWorker/    ffmpeg/ffprobeを実行する別プロセス
tests/
  TNLAStation.Application.Tests/
  TNLAStation.Infrastructure.Tests/
  TNLAStation.Api.Tests/
  TNLAStation.FfmpegWorker.Tests/
```

## 依存方向

依存方向は`Domain <- Application <- Infrastructure`です。APIと各実行プロジェクトがcomposition rootになります。
HTTP handlerへ固定データやID採番を直接置かず、Applicationの抽象を介してInfrastructureへ接続します。

## 実行プロセス

| プロセス | 責務 |
| --- | --- |
| API | 互換API、予約・録画制御、EPG同期、ストリーム配信 |
| Migrator | PostgreSQL schemaの更新 |
| FFmpeg Worker | probe、サムネイル、エンコード、HLS・変換配信 |
| PostgreSQL | 番組、予約、録画、ルール、履歴の永続化 |
| Mirakurun | EPGと放送TSの供給 |

APIとFFmpeg WorkerはHTTPで通信し、作業ファイルと録画ファイルは共有ボリュームで受け渡します。
APIイメージにはFFmpegを含めません。

## migration

API起動とmigrationを分離し、複数replicaが同時にschemaを更新しない構成にしています。
デプロイ時はMigrator成功後にAPIを起動します。

## 品質保証

CIでは次を実行します。

1. NuGet restoreと脆弱性監査
2. `dotnet format --verify-no-changes`
3. Release build
4. xUnit unit・integration・互換テスト
5. Hadolint
6. backendとFFmpeg Workerのcontainer build

APIテストはURL、status、JSON key、型、null省略、query既定値、cache header、OpenAPI公開位置を確認します。
PostgreSQL service containerを使用する結合テストでは、書き込みが後続の読み取りへ反映されることも検証します。
