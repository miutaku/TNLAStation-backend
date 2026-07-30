# EPGStation config.yml互換性

TNLAStation Backendは、EPGStation v2.10.0形式の`config.yml`を読み込めます。
共通する設定の意味、型、記述例は、先に
[EPGStation v2.10.0 config.yml詳細マニュアル](https://github.com/l3tnun/EPGStation/blob/5cf2ea383d37937eacecf424820dbd7a278d577e/doc/conf-manual.md)
を参照してください。この文書では同マニュアルに掲載された設定を基準に、TNLAStationで実際に
挙動が異なる箇所だけを説明します。

この文書に記載のない設定項目は、EPGStation v2.10.0の`config.yml`と同じ機能・挙動です。

## 調査基準

判定は`config.yml`のloader、内部Optionsへの変換、各機能での利用箇所を追跡して行っています。
「設定ファイルをエラーなく読める」だけでは、同じ機能を持つとは判定していません。

## 基本設定・詳細設定

| EPGStationの設定 | 差異・制限 |
| --- | --- |
| `dbtype` | TNLAStationはPostgreSQLのみを使用。`mysql`または`sqlite`を指定しても、そのDBでは動作しない |
| `mysql` | MySQL接続処理はない |
| `sqlite` | SQLite接続処理および拡張機能・正規表現設定はない |
| `gid` / `uid` | 値は読み込むが、backendプロセスの実行ユーザー・グループは変更しない。コンテナまたはservice manager側で指定する |

`dbtype`のEPGStation v2.10.0公式マニュアル上の選択肢は`mysql`と`sqlite`ですが、
TNLAStationではどちらも利用できません。TNLAStationの起動には
`ConnectionStrings__PostgreSQL`、または後述する`postgres`設定が必要です。

## 外部コマンド・エンコード

| EPGStationの設定 | 差異 |
| --- | --- |
| 各`*Command` | hook processへ親プロセスの環境変数を継承せず、null値を文字列`"null"`として渡す |
| `encodeProcessNum` | 上限到達時は低優先度processを停止せず、空きが出るまでFIFOで待つ |
| `encode` | `cmd`の`%NODE%`は置換しない |

EPGStationのNode.js processを持たないため、`%NODE%`を使う既存commandは実行ファイルを
明示する形へ変更してください。

## TNLAStation固有の差異

### PostgreSQL

TNLAStationは永続化先をPostgreSQLに限定しています。EPGStation v2.10.0の公式マニュアルにはない
次の記述を追加で受け付けます。

```yaml
dbtype: postgres
postgres:
  host: localhost
  port: 5432
  user: tnlastation
  password: change-me
  database: tnlastation
```

秘密情報をYAMLへ置かない場合は、環境変数`ConnectionStrings__PostgreSQL`を使用できます。

### FFmpeg Worker

録画制御を行うbackendと、FFmpeg/FFprobeを起動するFFmpeg Workerを別processに分離しています。
`FfmpegWorker__BaseUrl`でWorkerのURLを指定し、`streamFilePath`に対応するdirectoryを両processで
共有してください。

### 番組追従とイベントリレー

EPG更新で放送時刻が変わった場合、予約と録画終了時刻を更新して番組を追従します。終了時刻未定の
番組は後続番組やEPG更新を見ながら継続し、正式な終了時刻の受信後に再延長された場合も更新後の
時刻へ追従します。

Mirakurunからイベントリレー情報が届いた場合は、リレー先serviceへ予約・録画を引き継ぎます。
これらはTNLAStationの標準動作で、EPGStation v2.10.0の`config.yml`に対応する設定項目はありません。

### TNLAStation用の追加設定

EPGStationの`config.yml`にない運用設定は、ASP.NET Core形式の環境変数で指定します。

| 環境変数 | 内容 |
| --- | --- |
| `FfmpegWorker__BaseUrl` | FFmpeg Workerの共通URL（用途別URL未指定時のfallback） |
| `FfmpegWorker__EncodeBaseUrl` | probe、thumbnail用Worker poolのURL。録画TSエンコードjobはWorkerがPostgreSQLから取得する |
| `FfmpegWorker__StreamingBaseUrl` | ライブ・録画視聴用Worker poolのURL |
| `ConnectionStrings__PostgreSQL` | PostgreSQL connection string |
| `Streaming__IdleTimeoutSeconds` | keepが途切れたstreamを終了するまでの秒数 |
| `Streaming__SegmentSeconds` | HLS segment長 |
| `Streaming__MaxConcurrentStreams` | 同時stream数 |
| `Recording__PollIntervalSeconds` | 録画予約を確認する間隔 |
| `Reserve__HorizonDays` | 予約生成の対象日数 |
| `Reserve__UpdateIntervalMinutes` | 予約を再生成する間隔 |
| `Kodi__PublicBaseUrl` | Kodiへ渡す動画URLの公開base URL |

環境変数では、階層の区切りを`__`で表します。`config.yml`と同じ機能を上書きする環境変数も
利用できますが、共通項目はEPGStation形式の`config.yml`へ記述することを推奨します。
