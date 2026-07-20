# TNLAStation backend

EPGStation 2.10.0 の公開 Web API を段階的に再実装する .NET 10 バックエンドです。
フェーズ1では、既存クライアントとの契約確認に使えるインメモリ Mock API を提供します。

## 実装済み API

| Method | Path | Success | 概要 |
| --- | --- | ---: | --- |
| GET | `/api/config` | 200 | クライアント設定 |
| GET | `/api/recorded` | 200 | 録画済み番組一覧 |
| POST | `/api/recorded` | 201 | 録画済み番組の追加 |
| GET | `/api/reserves` | 200 | 予約一覧 |
| POST | `/api/reserves` | 201 | 手動予約の追加 |
| GET | `/api/version` | 200 | 互換対象バージョン |
| GET | `/api/docs` | 200 | OpenAPI 3.0 document |

`GET /api/recorded` と `GET /api/reserves` では `isHalfWidth` が必須です。
両一覧の `offset` は既定値 `0`、`limit` は既定値 `24` です。
コンテナやプロセスの生存確認には、副作用のない互換 API `/api/version` を使用します。

## JSON 互換方針

シリアライズは UTF-8 JSON、camelCase、任意の `null` プロパティ省略に固定しています。
公式 `api.yml` と EPGStation 2.10.0 のランタイム出力に差がある項目は、既存クライアントが実際に受け取るランタイム側を優先します。
主な差は次のとおりです。

- config は `isEnableLiveStream` ではなく `isEnableTSLiveStream`
- live stream 設定は `streamConfig.live.ts` 配下
- recorded の drop log は `dropLog` ではなく `dropLogFile`
- reserve の `encodeMode*` と `encodeDirectory3` は string
- upstream の `rawExtended`、`encodeDirectory2`、`audioComponentType` 変換挙動も互換アダプターと回帰テストで固定
- 成功 JSON は `Cache-Control`、`Expires`、`Pragma` を EPGStation と同じ no-cache 値に固定

## 構成

```text
src/
  TNLAStation.Domain/          ドメインモデル
  TNLAStation.Application/     Repository 抽象、query/command
  TNLAStation.Infrastructure/  フェーズ1のインメモリ Repository
  TNLAStation.Api/             HTTP、JSON/OpenAPI、互換 DTO 変換
tests/
  TNLAStation.Api.Tests/       xUnit 契約・結合テスト
```

依存方向は `Domain <- Application <- Infrastructure` とし、API が composition root です。
固定データや ID 採番を HTTP handler に直接置かず、Application の Repository 抽象を経由させています。
永続 DB への移行時は Infrastructure 実装を差し替えます。

## 必要環境

- .NET SDK 10.0.100 以降の 10.0 feature band

`global.json` は `latestFeature` roll-forward を許可します。

## 実行

```bash
dotnet restore TNLAStation.sln
dotnet run --project src/TNLAStation.Api
```

起動後、既定の ASP.NET Core URLで `/api/version` または `/api/docs` を開いて確認します。

API プロセスは起動時に migration を自動適用しません。PostgreSQL を構成した後、デプロイごとに
次の one-shot コマンドを API 起動より先に実行してください。

```bash
ConnectionStrings__PostgreSQL='Host=localhost;Database=tnlastation;Username=tnlastation;Password=...' \
  dotnet run --project src/TNLAStation.Migrator
```

複数 API replica を起動する構成でも migrator は一度だけ実行します。初期 migration は
`channels`、`programs`、`epg_sync_state` を作成し、API の通常起動には schema 変更権限を要求しません。

## 設定

ASP.NET Core 標準の設定優先順位を使用し、環境変数は JSON の階層区切りを `__` で表します。
たとえば `Mirakurun:BaseUrl` は `Mirakurun__BaseUrl` で上書きできます。

| 設定 | 環境変数 | コンテナ既定値 |
| --- | --- | --- |
| PostgreSQL connection string | `ConnectionStrings__PostgreSQL` | なし（必ず実行時に注入） |
| Mirakurun URL | `Mirakurun__BaseUrl` | `http://mirakurun:40772` |
| Mirakurun timeout | `Mirakurun__RequestTimeoutSeconds` | `30` |
| FFmpeg | `FFmpeg__ExecutablePath` | `/usr/bin/ffmpeg` |
| FFprobe | `FFmpeg__ProbeExecutablePath` | `/usr/bin/ffprobe` |
| データディレクトリ | `Storage__DataDirectory` | `/var/lib/tnlastation` |

設定例は [.env.example](.env.example) と [config/appsettings.Production.example.json](config/appsettings.Production.example.json) にあります。
`.env` と実値入りの `appsettings.Production.json` は `.gitignore` 対象です。
例示ファイルの connection string は意図的に空で、パスワードなどの秘密値はコミットしません。
本番環境ではデプロイ基盤の secret store を使い、必要な場合だけ権限を絞った `.env` を使用してください。

## Docker

```bash
docker build --pull -t tnlastation-backend:local .
cp .env.example .env
# .env の ConnectionStrings__PostgreSQL を実環境の値で設定する
docker run --rm \
  --env-file .env \
  --publish 8080:8080 \
  --volume "${PWD}/data:/var/lib/tnlastation" \
  tnlastation-backend:local
```

Dockerfile は .NET 10 SDK で publish する multi-stage build です。
最終イメージには ASP.NET Core Runtime、FFmpeg/FFprobe、healthcheck 用 curl だけを追加し、組み込みの非 root UID `1654` で実行します。
healthcheck は独自 API を増やさず、互換エンドポイント `GET /api/version` を使用します。
`/var/lib/tnlastation` は UID `1654` が書き込める永続化対象です。bind mount を使う場合はホスト側の所有権も合わせてください。

設定ファイルを使う場合は、例を untracked ファイルへコピーして read-only mount できます。

```bash
cp config/appsettings.Production.example.json config/appsettings.Production.json
docker run --rm \
  --mount "type=bind,source=${PWD}/config/appsettings.Production.json,target=/app/appsettings.Production.json,readonly" \
  --publish 8080:8080 \
  tnlastation-backend:local
```

## テスト

```bash
dotnet test TNLAStation.sln
```

契約テストは URL、成功 status、exact JSON key、型、null 省略、query 既定値、no-cache header、OpenAPI 公開位置を確認します。
結合テストは POST したデータが Repository 経由で後続 GET に反映されることと、上流互換のエラー/変換挙動を確認します。

## CI

GitHub Actions の [CI workflow](.github/workflows/ci.yml) は次を実行します。

- NuGet restore と direct/transitive package vulnerability audit
- `dotnet format --verify-no-changes`
- Release build と xUnit tests
- Hadolint による Dockerfile の静的検査
- BuildKit による multi-stage container build

PostgreSQL service container は、DBを使用するPhase 2結合テストが追加されるまでは起動しません。
未使用のDBをCIで起動せず、テストが対応した時点で job に追加します。

## 一次資料

契約の基準は EPGStation 2.10.0 (`5cf2ea383d37937eacecf424820dbd7a278d577e`) の次のファイルです。

- `api.yml` / `api.d.ts`
- `src/model/service/api/*.ts`
- `src/model/api/*`
- `src/model/service/ServiceServer.ts`
