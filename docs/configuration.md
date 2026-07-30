# 設定リファレンス

TNLAStation BackendはEPGStation 2.10.0形式の`config/config.yml`を第一級の設定として読み込みます。
配布テンプレートをコピーして利用してください。

共通項目の意味と記述例は
[EPGStation v2.10.0 config.yml詳細マニュアル](https://github.com/l3tnun/EPGStation/blob/5cf2ea383d37937eacecf424820dbd7a278d577e/doc/conf-manual.md)
を参照してください。TNLAStationでの対応状況、制限、固有設定は
[EPGStation config.yml互換性](epgstation-config-compatibility.md)にまとめています。

```sh
cp config/config.yml.template config/config.yml
```

`config.yml`がない場合はASP.NET Core標準の`appsettings`と環境変数だけでも起動できますが、
通常運用ではEPGStationと共通の名前・階層を持つ`config.yml`を推奨します。

## 読み込み順

同じ項目が複数の場所にある場合は、後のものが優先されます。

1. `appsettings.json`と`appsettings.{Environment}.json`
2. `config/config.yml`
3. 環境変数

`EPGSTATION_CONFIG`環境変数で別の`config.yml`を明示できます。明示したファイルが存在しない場合は起動を中止します。
`%ROOT%`は`config`ディレクトリの親へ展開されます。

## 基本設定

| `config.yml` | 内容 |
| --- | --- |
| `port` | HTTP待受ポート |
| `https` | HTTPSのport、key、cert、CA |
| `mirakurunPath` | Mirakurunまたは互換チューナーサーバー |
| `subDirectory` | リバースプロキシ配下のサブパス |
| `apiServers` | Swagger UIで使用するserver一覧 |
| `isAllowAllCORS` | 別オリジンからのAPI利用を許可 |
| `recPriority` | 通常録画のチューナー優先度。既定2 |
| `conflictPriority` | 競合録画の優先度。既定1 |
| `streamingPriority` | ライブ視聴の優先度。既定0 |

## PostgreSQL

TNLAStationはPostgreSQLを使用します。秘密情報を設定ファイルへ置かない構成では、
connection stringを環境変数で指定します。

```sh
export ConnectionStrings__PostgreSQL='Host=localhost;Port=5432;Database=tnlastation;Username=tnlastation;Password=...'
```

EPGStation形式でまとめたい場合は`dbtype: postgres`と`postgres`セクションも利用できます。

```yaml
dbtype: postgres
postgres:
  host: localhost
  port: 5432
  user: tnlastation
  password: change-me
  database: tnlastation
```

環境変数は`config.yml`より優先されます。

## EPG取得

| `config.yml` | 内容 |
| --- | --- |
| `needToReplaceEnclosingCharacters` | 番組情報の囲み文字を置換 |
| `epgUpdateIntervalTime` | EPG更新間隔（分） |
| `channelOrder` / `sidOrder` | チャンネルの表示順 |
| `excludeChannels` / `excludeSids` | EPG・録画対象から除外するチャンネル |

## 保存先と容量管理

```yaml
recorded:
  - name: recorded
    path: /recorded
    limitThreshold: 10240
    action: remove
```

| `config.yml` | 内容 |
| --- | --- |
| `recorded` | 録画保存先の配列 |
| `recorded[].limitThreshold` | 空き容量の閾値（MB） |
| `recorded[].action` | `remove`で、保護されていない古い録画から削除 |
| `recorded[].limitCmd` | 閾値を下回ったときの通知コマンドなど |
| `storageLimitCheckIntervalTime` | 容量確認間隔。既定60秒 |
| `uploadTempDir` | アップロード受信中の一時保存先 |

閾値を設定した保存先がなければ容量確認は実行しません。`limitCmd`は親プロセスの環境変数を継承します。

## 録画

| `config.yml` | 内容 |
| --- | --- |
| `recordedFormat` | 録画ファイル名のテンプレート |
| `recordedFileExtension` | 録画ファイルの拡張子 |
| `timeSpecifiedStartMargin` / `timeSpecifiedEndMargin` | 時刻指定予約の開始・終了マージン。既定1秒 |
| `recordedTmp` | 録画中の一時保存先 |
| `isEnabledDropCheck` | TSのerror、drop、scramblingを検査 |
| `dropLog` | drop logの保存先 |

`recordedFormat`では次の変数を利用できます。

`%YEAR%` `%MONTH%` `%DAY%` `%HOUR%` `%MIN%` `%SEC%` `%DOW%` `%TYPE%`
`%CHID%` `%CHNAME%` `%HALF_WIDTH_CHNAME%` `%CH%` `%SID%` `%ID%`
`%TITLE%` `%HALF_WIDTH_TITLE%`

番組表予約は番組の開始・終了時刻どおりに録画し、マージンは時刻指定予約にだけ適用します。

## サムネイル・エンコード

| `config.yml` | 内容 |
| --- | --- |
| `ffmpeg` / `ffprobe` | Workerが使う実行ファイル |
| `encodeProcessNum` | Worker内の同時FFmpegプロセス上限。0は無制限 |
| `concurrentEncodeNum` | 同時エンコード数 |
| `encode` | エンコード名、command、suffix、timeout倍率 |
| `thumbnail` | サムネイル保存先 |
| `thumbnailSize` | 寸法。既定480x270 |
| `thumbnailPosition` | 切り出し位置。既定5秒 |
| `thumbnailCmd` | 独自の生成command |

`thumbnailCmd`では`%FFMPEG%`、`%INPUT%`、`%OUTPUT%`、`%THUMBNAIL_POSITION%`、`%THUMBNAIL_SIZE%`を置換できます。

`encode[].cmd`では`%INPUT%`、`%OUTPUT%`、`%FFMPEG%`、`%FFPROBE%`に加え、
EPGStation互換の録画・番組・チャンネル・drop log環境変数を渡します。
独自commandでは進捗率を追跡しません。

## 配信

`stream.live.ts`と`stream.recorded`へ、m2ts、m2tsll、webm、mp4、hlsのモードを設定します。
テンプレートにある既定値はEPGStationと同じ形式です。

`stream`の項目を省略した場合、`config.yml.template`から既定値を補完します。
空配列`[]`を明示すると、その配信方式を無効化します。
`streamFilePath`はHLSプレイリストとsegmentを置く共有作業ディレクトリです。

## 予約履歴・外部連携

| `config.yml` | 内容 |
| --- | --- |
| `recordedHistoryRetentionPeriodDays` | 重複回避用の録画履歴保持日数。既定90日 |
| `isSuppressReservesUpdateAllLog` | 予約定期更新ログを抑制 |
| `urlscheme` | 外部player起動用URL Scheme |
| `kodiHosts` | Kodi接続先 |
| `reserve*Command` / `recording*Command` / `encodingFinishCommand` | lifecycle hook |

URL Schemeでは`PROTOCOL`、`ADDRESS`、`FILENAME`をfrontendが置換します。
command hookにはEPGStationと同名の環境変数を渡します。親プロセスの環境変数は継承せず、null値は文字列`"null"`になります。

## TNLAStation固有設定

EPGStationの`config.yml`に存在しないプロセス間接続などは、ASP.NET Core形式の環境変数で設定します。

| 環境変数 | 内容 |
| --- | --- |
| `FfmpegWorker__BaseUrl` | TNLAStation FFmpeg WorkerのURL |
| `Streaming__WorkDirectory` | backendとWorkerで共有する作業ディレクトリ |
| `ConnectionStrings__PostgreSQL` | PostgreSQL connection string |
| `ASPNETCORE_HTTP_PORTS` | ASP.NET Coreの待受ポートを上書き |

全設定と既定値は`src/TNLAStation.Infrastructure/Configuration/*Options.cs`、
EPGStation形式からの対応関係は
`src/TNLAStation.Infrastructure/Configuration/EpgStation/EpgStationConfigurationSource.cs`を参照してください。
