# TNLAStation Backend

[EPGStation](https://github.com/l3tnun/EPGStation) 2.10.0 の公開 Web API を再実装する .NET 10 バックエンドです  
[TNLAStation Frontend](https://github.com/miutaku/TNLAStation-frontend) など、EPGStation の API 仕様に沿ったクライアントをそのまま接続できます  
永続化には PostgreSQL を使用します

## 機能

### EPGStation 互換 API

- クライアント設定・バージョン情報（`config`、`version`）
- 番組表・放送局（`schedules`、`channels`）
- 予約（`reserves`）とルールによる自動予約（`rules`）
- 録画中（`recording`）・録画済み（`recorded`）とタグ（`tags`）
- ライブ配信・録画配信（`streams`）
- 動画ファイル（`videos`）とサムネイル（`thumbnails`）
- エンコード（`encode`）
- IPTV 用チャンネルリスト・番組表（`iptv`）
- ストレージ（`storages`）、ドロップログ（`dropLogs`）

ルート・HTTP メソッドは EPGStation 2.10.0 の公開 API と一致させています。3 つのパスの役割も上流と同じです

- `GET /api/docs` — OpenAPI 3.0 document (仕様書そのもの)
- `/api-docs` — Swagger UI (ブラウザで試せる画面)
- `GET /api/debug` — 302 で `/api-docs/?url=/api/docs` へ送るだけのリダイレクト

差が無いことは `tests/TNLAStation.Api.Tests/RouteSurfaceParityTests.cs` が、上流の
`src/model/service/api` のファイル配置から一覧を作り直して毎回照合します
`GET /api/recorded` と `GET /api/reserves` では `isHalfWidth` が必須です。両一覧の `offset` は既定値 `0`、`limit` は既定値 `24` です  
コンテナやプロセスの生存確認には、副作用のない互換 API `/api/version` を使用します

### JSON 互換方針

シリアライズは UTF-8 JSON、camelCase、任意の `null` プロパティ省略に固定しています  
公式 `api.yml` と EPGStation 2.10.0 のランタイム出力に差がある項目は、既存クライアントが実際に受け取るランタイム側を優先します。主な差は次のとおりです

- config は `isEnableLiveStream` ではなく `isEnableTSLiveStream`
- live stream 設定は `streamConfig.live.ts` 配下
- recorded の drop log は `dropLog` ではなく `dropLogFile`
- reserve の `encodeMode*` と `encodeDirectory3` は string
- upstream の `rawExtended`、`encodeDirectory2`、`audioComponentType` 変換挙動も互換アダプターと回帰テストで固定
- 成功 JSON は `Cache-Control`、`Expires`、`Pragma` を EPGStation と同じ no-cache 値に固定
- `POST /api/recorded/cleanup` は EPGStation の videoFileCleanup と同じく双方向に片付ける — DB にあるが実ファイルが無い行を消すだけでなく、`Storage:RecordedDirectories` 配下にあるが DB に登録されていないファイル・空になったディレクトリも削除する。レスポンスは上流と同じく `{ "code": 200 }` だけで、件数は返さない
- 予約・録画・タグ・エンコード・ストリーム・サムネイル・ビデオの単純な変更系 API (delete/protect/relate/keep 等) は、EPGStation の実際のランタイム出力に合わせて `204 No Content` ではなく `200 OK` + `{ "code": 200 }` を返す (`GET` の単体取得・`POST` の新規作成系は従来どおり 404/201 のまま)。手動予約の更新 (`PUT /api/reserves/{id}`) だけ上流に合わせて `201` + `{ "code": 201, "message": "ok" }`、動画アップロード (`POST /api/videos/upload`) だけ `200` + `{ "code": 200, "result": "ok" }` を返す
- 上記のうち、対象が既に無いときに 404 にせず成功として扱うか・失敗にするかは上流のコード次第でエンドポイントごとに違う。タグ・エンコードキャンセル・ストリーム停止は存在チェックをしていないので TNLAStation でも成功として扱うが、以下は上流も明示的に存在チェック (または状態チェック) をしており、無ければ専用のエラー名を持つ例外が汎用の 500 として返る。ここは以前 (誤って) 一律 200/成功にしていたが、上流のコードを直接確認して例外を投げるよう修正した
  - `DELETE /api/reserves/{id}`・`DELETE /api/reserves/{id}/skip`・`DELETE /api/reserves/{id}/overlap`・`PUT /api/reserves/{id}` (`ReservationManageModel` の `cancel`/`removeSkip`/`removeOverlap`/`edit`) — 無ければ `ReservationIsNotFound`。`skip`/`overlap` 解除はさらに、対象がルール予約でない・そもそも該当状態でない場合は上流も何もせず 200 を返すだけなので、その条件も合わせた
  - `DELETE /api/recorded/{recordedId}` (`RecordedManageModel.delete`) — 無ければ `RecordedIdIsNotFound`。加えてプロテクト中なら `RecordedIsProtected` で拒否する (削除前に消えないよう上流が意図的に守っている)
  - `DELETE /api/videos/{videoFileId}` (`VideoApiModel.deleteVideoFile`) — 無ければ `VideoFileIsNotFound`、対応する録画がプロテクト中なら同じく `RecordedIsProtected`
  - `DELETE /api/thumbnails/{thumbnailId}` (`ThumbnailManageModel.delete`) — 無ければ `ThumbnailIsNotFound`
  - `PUT /api/streams/{streamId}/keep` (`StreamManageModel.keep`) — 無ければ `StreamIsUndefined`。同じストリームを対象にする `DELETE /api/streams/{streamId}` (stop) はここを無視して常に 200 のまま
  - `POST /api/videos/{videoFileId}/kodi` (`VideoApiModel.sendToKodi`) — 送り先の kodi 名が設定に無ければ `KodiHostIsUndefined`、動画が無ければ `VideoFileIsUndefined`
  - `PUT /api/rules/{ruleId}` (`RuleManageModel.update`) — 無ければ `RuleIsNotFound`。ただし同じルールを対象にする `PUT /api/rules/{ruleId}/enable`・`/disable`・`DELETE /api/rules/{ruleId}` (`enable`/`disable`/`delete`) は上流も存在チェックをしておらず、対象が無くても何もせず 200 を返すだけ。以前は enable/disable にも (誤って) update と同じ存在チェックを付けてしまっており、しかも投げるエラー名も上流の実際の文字列 `RuleIsNotFound` ではなく推測の `RuleIsNull` になっていた — 両方を上流のソースで確認して直した
  - `GET /api/videos/{videoFileId}/duration` (`VideoApiModel.getDuration`) — 動画が無ければ 404 ではなく `VideoFileIsUndefined` 例外で 500 になる。同じ動画に対するファイル取得 `GET /api/videos/{videoFileId}` は逆に 404 が正しく、そちらとは扱いが違う。以前は両方とも 404 にしてしまっていた
- `POST /api/rules`・`PUT /api/rules/{ruleId}` はこれまでリクエストの中身を一切検査していなかった。上流 (`ReserveOptionChecker.checkRuleOption`、`RuleValidationPolicy` として移植) は追加・更新時に以下を検査しており、落ちると `AddRuleError`/`UpdateRuleError` が汎用の 500 として返る: 時刻指定ルールは keyword/channelIds/times が揃っていて times の start/range が正の範囲内であること、keyword を指定したら name/description/extended のどれかを有効にすること (逆に keyword が無いのに検索対象フラグだけ立てるのも不可)、channelIds と GR/BS/CS/SKY の絞り込みは同時に使えないこと、genre コードが 0x00〜0xf の範囲内であること、times の week が 0 でなく start/range が指定時は 0-23/1-23 の範囲内であること、durationMin/Max が負でなく min <= max であること、periodToAvoidDuplicate は avoidDuplicate が有効なときだけ設定できること、エンコードの mode1/2/3 は config に実在する名前だけ許され directory を指定するなら対応する mode も指定すること。上流に無いチェックを追加しないよう、番組検索そのものの空条件チェック (`EpgSearchPolicy.Validate`) とは別の独立したポリシーとして実装した
- `POST /api/reserves`・`PUT /api/reserves/{id}` (手動予約の追加・編集) も、エンコードオプションを一切検査していなかった。上流 (`checkManualReserveOption` → `checkEncodeOption`) はルールと全く同じエンコード検査を予約側にもかけており、落ちると追加時は `AddReservationOptionError`、編集時は `ReservationEditError` が汎用の 500 として返る。ルールと予約で検査ロジックが完全に同じ (上流も同じ `checkEncodeOption` を共有) ため、`EncodeOptionValidationPolicy` として1本化して両方から使うようにした
- `POST /api/videos/upload` はファイルが無い・紐づけ先の録画が無いときに専用の 400/404 を返していたが、上流 (`videos/upload` の post、`RecordedManageModel.addUploadedVideoFile`) はどちらも汎用の 500 (ファイルが無ければ `FileIsNotFound`、録画が無ければ `RecordedIdIsNull`) として返す。recordedId が数値ですらない場合も上流では `findId` が null を返す先と同じ経路をたどるため、同じ `RecordedIdIsNull` にまとめた
- `DELETE /api/recorded/{recordedId}` は録画中の項目に対しても受け付ける — EPGStation の delete は録画中なら止めてから消す挙動なので、TNLAStation でも録画停止 (`IRecordingStopService`) を先に行ってから削除する。以前は録画中の削除がエラーになっていた
- `GET /api/iptv/channel.m3u8`・`GET /api/iptv/epg.xml` は `isHalfWidth` クエリ (既定 true) に対応し、serviceType がデータ放送等 (映像・音声サービス以外) のチャンネルを除外し、ロゴを持たないチャンネルには `tvg-logo` を付けず、xmltv は番組が 1 つも無いチャンネルの `channel` 要素ごと省く
- `GET /api/config` の `streamConfig` (画面が視聴の形式・画質の選択肢を組み立てるために読む項目) が実装されておらず常に欠落していた。`Streaming:LiveModes`/`Formats` から `streamConfig.live.ts.{hls,mp4,webm,m2tsll}`・`streamConfig.recorded.{ts,encoded}.{hls,mp4,webm}` を組み立てるよう実装した。`live.ts.m2ts` は EPGStation と異なり常に無変換 1 本のみ (cmd 付き複数プロファイルの切り替えは非対応)
- チューナー競合時にどちらの予約を優先するかの判定を EPGStation の `sortReserve` と同じ基準に直した — 手動予約 (ルールに属さない予約、時刻指定を含む) は必ずルール予約より優先し、手動どうしは時刻指定を先に・古い方を先に、ルールどうしは ruleId の小さい方 (先に作ったルール) を先にする。以前は独自の `priority` 数値と番組開始時刻を主な基準にしており、ルールの priority を上げると手動予約から横取りできてしまっていた。`priority` フィールド自体は EPGStation に存在しない TNLAStation 独自の追加項目として残し、上記の基準で決着が付かない場合の補助的な決め手としてのみ使う
- ルールの重複回避 (`avoidDuplicate`) が番組名だけで判定しており、放送局を見ていなかった。上流は「番組名 **かつ** 放送局」が一致する場合だけ重複と見なすため、同名の別局再放送 (同時ネット番組など) を録り逃す実害があった。放送局の一致も条件に加えて修正
- チューナーの相乗り判定が service (ChannelId) 単位になっており、上流の物理チャンネル (周波数) 単位より厳しすぎた。地上デジタルでは同じ物理チャンネルに複数サービスが多重化されていることがあり、上流はその場合 1 本のチューナーで同時に録れる。ChannelId ではなく物理チャンネル文字列で比べるよう修正し、無駄にチューナー本数を要求しないようにした
- チューナー割り当てが「空いていても同じ物理チャンネルのチューナーがあれば積極的にそちらへ相乗りさせる」実装になっており、上流より賢すぎた。上流の `Tuner.add` はチューナー番号の若い順に見て「対応種別かつ (空き OR 既に入っている予約と同じ物理チャンネル)」の最初の 1 本を機械的に使うだけで、相乗りできる空きより若い番号の空きチューナーがあればそちらを素直に使ってしまう (相乗りの機会を積極的には探さない)。まれに、この違いだけでチューナー不足の判定 (`isConflict`) が上流と食い違いうるため、若い番号から単純な first-fit で選ぶよう修正した
- ルール予約の生成時に、既に手動予約が入っている番組をルールの対象から除外していたが、上流はこれをしない。手動予約とルールが同じ番組を両方掴むと、上流では見た目には重複した 2 件の予約が並ぶ (同じ番組なので channel も同じになり、チューナーは相乗りになって競合はしない)。上流が二重登録を防ぐのは「番組指定の手動予約を追加しようとしたとき、既に何か (手動でもルールでも) がその番組を掴んでいれば拒否する」という追加時のチェックだけで、ルール生成側からは防いでいない片方向の仕様のため、それに合わせて除外をやめた
- 予約追加 (`POST /api/reserves`) に上流が持つ検証が無かった。上流は番組指定なら「既に何かがその番組を掴んでいないか」(`ReservationManageModelReservedError`)、時刻指定なら「終了時刻が既に過去でないか」(`TimeSpecifiedOptionError`)・「同じ放送局/開始/終了時刻の手動予約が既に無いか」(`AddReservationConflictError`) をチェックし、失敗はどれも専用の 400 ではなく汎用のサーバエラー (500 + `errors` にエラー名) として返す。同じチェックと同じ失敗時の応答形を追加した

## 動作環境

- [.NET SDK](https://dotnet.microsoft.com/) : 10.0.100 以降の 10.0 feature band（`global.json` は `latestFeature` roll-forward を許可）
- [PostgreSQL](https://www.postgresql.org/) : migration 適用後に接続
- [Mirakurun](https://github.com/Chinachu/Mirakurun) 互換のチューナーサーバー
- [FFmpeg](https://ffmpeg.org/) / FFprobe : このリポジトリ自体には含まれず、`TNLAStation.FfmpegWorker` (別コンテナ) を経由して使う

---

## セットアップ方法

### ローカル実行

```bash
dotnet restore TNLAStation.sln
dotnet run --project src/TNLAStation.Api
```

- 起動後、既定の ASP.NET Core URL で `/api/version`、`/api/docs`、または Swagger UI の `/api-docs` を開いて確認する
- API プロセスは起動時に migration を自動適用しない。PostgreSQL を構成した後、デプロイごとに次の one-shot コマンドを API 起動より先に実行する

  ```bash
  ConnectionStrings__PostgreSQL='Host=localhost;Database=tnlastation;Username=tnlastation;Password=...' \
    dotnet run --project src/TNLAStation.Migrator
  ```

- 複数 API replica を起動する構成でも migrator は一度だけ実行する。初期 migration は `channels`、`programs`、`epg_sync_state` を作成し、API の通常起動には schema 変更権限を要求しない

### 設定

[EPGStation の `config/config.yml`](https://github.com/l3tnun/EPGStation) に倣い、アプリ設定は `config/` 配下の 1 ファイル (`appsettings.Production.json`) に集約しています。テンプレートは [config/appsettings.Production.example.json](config/appsettings.Production.example.json) です

```bash
cp config/appsettings.Production.example.json config/appsettings.Production.json
# 値を実環境に合わせて編集する
```

| セクション | 内容 |
| --- | --- |
| `ConnectionStrings:PostgreSQL` | 例外的に環境変数 `ConnectionStrings__PostgreSQL` で渡す (下記参照) |
| `Mirakurun:BaseUrl` / `RequestTimeoutSeconds` | Mirakurun (または互換チューナーサーバー) の接続先 |
| `Mirakurun:RecPriority` / `ConflictPriority` | 録画時に Mirakurun へ渡すチューナー優先度。他プロセスと取り合いになったときの調停に使う (既定 2 / 1) |
| `Mirakurun:StreamingPriority` | ライブ視聴 (HLS・変換配信・無変換配信) で Mirakurun へ渡すチューナー優先度。録画の `RecPriority`/`ConflictPriority` とは別枠 (既定 0) |
| `FfmpegWorker:BaseUrl` | ffmpeg/ffprobe を実行する `TNLAStation.FfmpegWorker` の接続先 |
| `Ffmpeg:FfmpegPath` / `FfprobePath` | (ffmpeg-worker 側の設定) ffmpeg/ffprobe の実行ファイルパス (既定 `ffmpeg`/`ffprobe`、PATH から解決) |
| `Ffmpeg:WorkDirectory` | (ffmpeg-worker 側の設定) HLS のプレイリスト・セグメントの置き場。backend と共有するボリュームを指す |
| `Ffmpeg:EncodeProcessNum` | (ffmpeg-worker 側の設定) エンコード・サムネイル抽出・probe・HLS/変換配信で同時に起動する ffmpeg/ffprobe プロセス数の上限 (既定 0 = 無制限)。EPGStation は上限に達すると優先度の低いプロセスを kill して割り込ませるが、ここでは単純化して空きが出るまで FIFO で待つ |
| `Storage:RecordedDirectories` | 録画の保存先 (`GET /api/storages` が返す一覧) |
| `Storage:RecordedDirectories[].LimitThresholdMb` / `Action` / `LimitCmd` | 空き容量がこの値 (MB) を下回ったときの自動削除設定。`Action: "remove"` で保護されていない最も古い録画から順に消す。`LimitCmd` は閾値割れ時に叩く通知コマンドなど |
| `Storage:StorageLimitCheckIntervalSeconds` | 上記の空き容量確認の間隔 (既定 60 秒)。`LimitThresholdMb` を設定した保存先が無ければ確認自体をしない |
| `Storage:UploadTempDirectory` | 動画アップロード時、受信中だけ使う一時保存先。設定すると、ここへ書きながら受け取り、完了後に最終保存先へ移す。未設定なら最終保存先へ直接書く |
| `Recording:IsEnabledDropCheck` | 受信 TS のパケット欠け・エラー・スクランブル残りを数えて drop log を残すか (既定 false) |
| `Recording:RecordedFormat` / `RecordedFileExtension` | 録画ファイル名のテンプレート。`%YEAR%` `%MONTH%` `%DAY%` `%HOUR%` `%MIN%` `%SEC%` `%DOW%` `%TYPE%` `%CHID%` `%CHNAME%` `%HALF_WIDTH_CHNAME%` `%CH%` `%SID%` `%ID%` `%TITLE%` `%HALF_WIDTH_TITLE%` が使える |
| `Recording:StartMarginSeconds` / `EndMarginSeconds` | 時刻指定予約 (`IsTimeSpecified`) の開始・終了マージン (既定 1 / 1 秒)。番組表予約には適用しない — 番組表予約は番組の開始・終了ちょうどに録る |
| `Recording:TempDirectory` | 録画中だけ使う一時保存先。設定すると、ここへ書きながら録り、完了後に最終保存先へ移す。未設定なら最終保存先へ直接書く |
| `Recording:DropLogDirectory` | drop log (`.drop.log`) の保存先。未設定なら録画ファイルと同じディレクトリに置く |
| `Thumbnail:Width` / `Height` | サムネイルの幅・高さ (既定 480 / 270)。`Height` を `null` にすると幅だけ合わせてアスペクト比を保つ |
| `Thumbnail:PositionSeconds` | サムネイルを切り出す再生位置 (既定 5 秒)。動画の長さに関わらず常にこの秒数を使う |
| `Thumbnail:Command` | 設定すると固定の ffmpeg 引数の代わりにこのコマンドをそのまま実行する。`%FFMPEG%`/`%INPUT%`/`%OUTPUT%`/`%THUMBNAIL_POSITION%`/`%THUMBNAIL_SIZE%` を置換できる |
| `Encode:ConcurrentEncodeNum` | 同時に処理するエンコードの数 (既定 1、EPGStation の既定は 0 = エンコード機能自体が無効。設定なしでも動くよう意図的に既定値を変えている)。エンコードは CPU を使い切るので、並べすぎると全部が遅くなり、録画そのものにも影響が出る |
| `Encode:Modes[].Command` | 設定すると `Arguments` の代わりにこのコマンドをそのまま実行する。`%INPUT%`/`%OUTPUT%`/`%FFMPEG%`/`%FFPROBE%` を置換できるほか、EPGStation の encode.cmd と同じ環境変数一式 (RECORDEDID/NAME/CHANNELID/GENRE1-3/DROPLOG_* など) も渡す。ffmpeg に限らず任意の実行ファイルを指定できるが、進捗 (パーセント) は追わない |
| `Streaming:LiveModes` / `Formats` | 未設定 (キー自体が無い) ならコード内の既定を使うが、`[]` と明示すると EPGStation の「空配列で配信方式を無効化」と同じくその配信方式自体を無効化する |
| `Encode:Modes[].RateTimeoutMultiplier` | 録画時間 (秒) にこの値を掛けた時間を超えたらタイムアウトして失敗扱いにする (既定 4.0) |
| `Reserve:RecordedHistoryRetentionPeriodDays` | ルール録画の重複回避のための記憶を何日分残すか (既定 90)。録画本体を消してもこの期間内は同じ番組を録り直さない |
| `Reserve:IsSuppressReservesUpdateAllLog` | 予約の定期更新で毎回出るログを抑えるか (既定 false)。更新間隔を短くすると煩わしい場合に使う |
| `Api:SubDirectory` / `IsAllowAllCors` / `Servers` | リバースプロキシのサブパス配下で動かす場合や、別オリジンから叩く場合の設定。既定では使わない |
| `UrlScheme:M2Ts` / `Video` / `Download` (各 `Ios`/`Android`/`Mac`/`Win`) | `GET /api/config` が返す外部プレイヤー起動用 URL Scheme。既定値は EPGStation の doc の既定と同じ (VLC/Infuse 向け)。`PROTOCOL`/`ADDRESS`/`FILENAME` はフロントエンドが置換する |
| `CommandHooks:*` | 予約の追加・更新・削除、録画の開始前後、エンコード完了で外部コマンドを実行する。9 種類のフックがあり、未設定ならそのフックは何もしない (`src/TNLAStation.Infrastructure/Configuration/CommandHookOptions.cs` を参照)。渡す環境変数は EPGStation と同じ名前 (RESERVEID/CHANNELTYPE/NAME/DESCRIPTION など)・同じ値で、親プロセスの環境変数は継承しない (`PATH` とこれらの変数だけ)。`null` は空文字ではなく文字列 `"null"` として渡る。ただし `Storage:RecordedDirectories[].LimitCmd` だけは上流と同じく全環境変数を継承する |
| `Kodi:Hosts` / `PublicBaseUrl` | Kodi 連携 (使わない場合は空のままでよい) |

`Ffmpeg` は `TNLAStation.FfmpegWorker` (別コンテナ) 側が読む設定で、backend とは別の実行プロセスだが、同じ `appsettings.Production.json` を両コンテナで共有してマウントする (詳細は後述の [ffmpeg-worker](#ffmpeg-worker) を参照)

`Streaming`・`Encode`・`Epg`・`Recording`・`Thumbnail` にはこのほかにも調整項目があり (`src/TNLAStation.Infrastructure/Configuration/*Options.cs` を参照)、指定しなければコード内の既定値で動作します

**なぜ PostgreSQL の connection string だけ環境変数のままなのか:** パスワードを含む値なので、ファイルとしてバックアップやバインドマウント経由で漏れる経路を増やしたくありません。加えて `postgres` コンテナ自身も `POSTGRES_PASSWORD` を環境変数でしか受け取れないため、同じ値をどのみち環境変数側でも扱うことになります。それ以外の設定(Mirakurun・Streaming・Storage・Kodi) は秘密情報を含まないため、EPGStation と同じくファイル 1 枚にまとめています

**EPGStation の config 項目のうち、意図的に app 設定として持たないもの:** `port`/`https` は ASP.NET Core の標準的な起動方法 (`ASPNETCORE_HTTP_PORTS` 環境変数、または手前に立てる `gateway` コンテナでの TLS 終端) に委ねています。`dbtype`/`mysql`/`sqlite` は対象外— PostgreSQL 専用として作っています。`gid`/`uid` は Dockerfile の `USER` ディレクティブ (非 root UID) で扱っており、アプリ設定にはありません。`socketioPort`/`clientSocketioPort` は Socket.IO 相当のリアルタイム通知自体を実装していないため未対応です

---

## Docker でのセットアップ

```bash
docker build --pull -t tnlastation-backend:local .
cp config/appsettings.Production.example.json config/appsettings.Production.json
# config/appsettings.Production.json を実環境に合わせて編集する
docker run --rm \
  --env ConnectionStrings__PostgreSQL='Host=postgres;Port=5432;Database=tnlastation;Username=tnlastation;Password=...' \
  --mount "type=bind,source=${PWD}/config/appsettings.Production.json,target=/app/appsettings.Production.json,readonly" \
  --publish 8080:8080 \
  --volume "${PWD}/data:/var/lib/tnlastation" \
  tnlastation-backend:local
```

- Dockerfile は .NET 10 SDK で publish する multi-stage build
- 最終イメージには ASP.NET Core Runtime と healthcheck 用 curl だけを追加し、組み込みの非 root UID `1654` で実行する。FFmpeg/FFprobe はこのイメージには含まれない
- healthcheck は独自 API を増やさず、互換エンドポイント `GET /api/version` を使用する
- `/var/lib/tnlastation` は UID `1654` が書き込める永続化対象。bind mount を使う場合はホスト側の所有権も合わせる
- `appsettings.Production.json` は `.gitignore` 対象。テンプレートの値はそのままコミットしてよいプレースホルダのみ

### ffmpeg-worker

視聴・サムネイル・エンコードで実際に ffmpeg/ffprobe を実行するのは、この API サーバーではなく
`Dockerfile.ffmpeg-worker` でビルドする別コンテナ (`TNLAStation.FfmpegWorker`) です。API サーバーは
`FfmpegWorker:BaseUrl` へ HTTP でこれらを依頼し、HLS のセグメントやサムネイルは `/var/lib/tnlastation`
ボリュームを両コンテナで共有して受け渡します。単体の `docker run` では動かず、`TNLAStation` の
`compose.yaml` が両コンテナ・共有ボリューム・Mirakurun への到達性をまとめて用意します

```bash
docker build --pull -f Dockerfile.ffmpeg-worker -t tnlastation-ffmpeg-worker:local .
```

---

## 品質確認

```bash
dotnet test TNLAStation.sln
```

- 仕様準拠テストは URL、成功 status、exact JSON key、型、null 省略、query 既定値、no-cache header、OpenAPI 公開位置を確認する
- 結合テストは POST したデータが Repository 経由で後続 GET に反映されることと、上流互換のエラー/変換挙動を確認する
- GitHub Actions の [CI workflow](.github/workflows/ci.yml) では、NuGet restore と依存関係の脆弱性監査、`dotnet format --verify-no-changes`、Release build と xUnit tests、Hadolint による Dockerfile 静的検査、BuildKit による multi-stage container build を順に実行する
  - PostgreSQL service container を常時起動し、DB を使用する結合テストを実行する

---

## ディレクトリ

```text
src/
  TNLAStation.Domain/          ドメインモデル
  TNLAStation.Application/     Repository 抽象、query/command
  TNLAStation.Infrastructure/  PostgreSQL (EF Core) の Repository、Mirakurun/録画/変換/Kodi 連携
  TNLAStation.Api/             HTTP、JSON/OpenAPI、互換 DTO 変換
  TNLAStation.FfmpegWorker/    ffmpeg/ffprobe を実行する別コンテナ (視聴・サムネイル・エンコード)
tests/
  TNLAStation.Api.Tests/            xUnit 仕様準拠・結合テスト
  TNLAStation.FfmpegWorker.Tests/   ffmpeg-worker のプロセス管理・取り消しの試験
```

依存方向は `Domain <- Application <- Infrastructure` とし、API が composition root です  
固定データや ID 採番を HTTP handler に直接置かず、Application の Repository 抽象を経由させています

## Tips

### API仕様の一次資料

API仕様は EPGStation 2.10.0 (`5cf2ea383d37937eacecf424820dbd7a278d577e`) の次のファイルです

- `api.yml` / `api.d.ts`
- `src/model/service/api/*.ts`
- `src/model/api/*`
- `src/model/service/ServiceServer.ts`

## Licence

[MIT Licence](LICENSE)
