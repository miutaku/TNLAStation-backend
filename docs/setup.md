# セットアップ

## 動作環境

- .NET SDK 10.0.100以降の10.0 feature band
- PostgreSQL
- Mirakurunまたは互換チューナーサーバー
- FFmpeg / FFprobeを実行するTNLAStation FFmpeg Worker

`global.json` は`latestFeature`へのroll-forwardを許可しています。

## 推奨構成

通常は、gateway、PostgreSQL、backend、migrator、FFmpeg Worker、frontendをまとめた
[TNLAStation Composeリポジトリ](https://github.com/miutaku/TNLAStation)を利用してください。
このリポジトリ単体での手順は、backendの開発や個別検証を想定しています。

## ローカル実行

```sh
cp config/config.yml.template config/config.yml
dotnet restore TNLAStation.sln
```

`config/config.yml`のMirakurun、保存先、配信、エンコードなどを環境に合わせて変更します。
TNLAStation固有の接続先とPostgreSQLのconnection stringは環境変数で渡せます。

```sh
export ConnectionStrings__PostgreSQL='Host=localhost;Database=tnlastation;Username=tnlastation;Password=...'
export FfmpegWorker__BaseUrl='http://localhost:8081'
```

### migration

APIは起動時にmigrationを自動適用しません。デプロイごとにAPIより先に一度だけ実行します。

```sh
dotnet run --project src/TNLAStation.Migrator
```

複数のAPI replicaを起動する場合もmigratorは一度だけ実行してください。APIの通常起動にはschema変更権限を要求しません。

### API

```sh
dotnet run --project src/TNLAStation.Api
```

既定のASP.NET Core URLで、`/api/version`、`/api/docs`、`/api-docs`を確認できます。

### FFmpeg Worker

```sh
ConnectionStrings__PostgreSQL="$ConnectionStrings__PostgreSQL" \
  dotnet run --project src/TNLAStation.FfmpegWorker
```

APIは`FfmpegWorker:BaseUrl`を通じてFFmpeg Workerへprobe、サムネイル、変換配信を依頼します。
録画TSエンコードは各WorkerがPostgreSQLの共有queueを直接claimします。
HLSセグメントやサムネイルを受け渡す作業ディレクトリは両プロセスから同じ場所を参照させてください。

## Docker

### Backend

```sh
docker build --pull -t tnlastation-backend:local .
docker run --rm \
  --env ConnectionStrings__PostgreSQL='Host=postgres;Port=5432;Database=tnlastation;Username=tnlastation;Password=...' \
  --env FfmpegWorker__BaseUrl='http://ffmpeg-worker:8080' \
  --mount "type=bind,source=${PWD}/config/config.yml,target=/app/config/config.yml,readonly" \
  --publish 8080:8080 \
  --volume "${PWD}/data:/var/lib/tnlastation" \
  tnlastation-backend:local
```

最終イメージは非root UID `1654`で動作します。bind mountを使う場合は、ホスト側のディレクトリへUID `1654`が書き込めるようにしてください。
`/var/lib/tnlastation`は永続化対象です。healthcheckは`GET /api/version`を使用します。

### FFmpeg Worker

```sh
docker build --pull \
  --file Dockerfile.ffmpeg-worker \
  --tag tnlastation-ffmpeg-worker:local .
```

FFmpeg Worker単体では、backendとの通信、共有作業ディレクトリ、録画ディレクトリ、Mirakurunへの到達性が揃いません。
実運用ではCompose構成を利用してください。

## 動作確認

```sh
curl http://localhost:8080/api/version
```

APIの挙動をブラウザで確認する場合は`http://localhost:8080/api-docs`を開きます。
