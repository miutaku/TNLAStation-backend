# EPGStation互換仕様

TNLAStation Backendは、EPGStation 2.10.0
（commit `5cf2ea383d37937eacecf424820dbd7a278d577e`）の公開Web APIを互換対象とします。

## 対応領域

- `config`、`version`
- `schedules`、`channels`
- `reserves`、`rules`
- `recording`、`recorded`、`tags`
- `streams`
- `videos`、`thumbnails`
- `encode`
- `iptv`
- `storages`、`dropLogs`

ルートとHTTP methodは`RouteSurfaceParityTests`で上流の`src/model/service/api`から生成した一覧と照合します。

## APIドキュメント

| URL | 内容 |
| --- | --- |
| `GET /api/docs` | OpenAPI 3.0文書 |
| `/api-docs` | Swagger UI |
| `GET /api/debug` | Swagger UIへのリダイレクト |

`GET /api/recorded`と`GET /api/reserves`では`isHalfWidth`が必須です。
両一覧の`offset`は既定0、`limit`は既定24です。

## JSONとHTTP

- UTF-8 JSON、camelCase、任意のnullプロパティ省略
- 成功JSONにはEPGStationと同じno-cache headerを付与
- 公式`api.yml`と実ランタイムが異なる場合は、既存クライアントが受け取るランタイムを優先
- 単純な変更系APIは`204`ではなく`200`と`{ "code": 200 }`
- 手動予約更新は`201`と`{ "code": 201, "message": "ok" }`
- 動画アップロードは`200`と`{ "code": 200, "result": "ok" }`

代表的なランタイム差分は次のとおりです。

- configは`isEnableLiveStream`ではなく`isEnableTSLiveStream`
- live stream設定は`streamConfig.live.ts`配下
- recordedのdrop logは`dropLogFile`
- reserveの`encodeMode*`と`encodeDirectory3`はstring
- `rawExtended`、`encodeDirectory2`、`audioComponentType`の変換もランタイムに準拠

## 存在しない対象とエラー

EPGStationが存在確認をしない操作は、対象がなくても成功します。タグ操作、エンコードキャンセル、
ストリーム停止などが該当します。一方、上流が確認する操作は同じエラー名の汎用500を返します。

| 操作 | エラー |
| --- | --- |
| 予約の削除・編集 | `ReservationIsNotFound` |
| 録画の削除 | `RecordedIdIsNotFound` / `RecordedIsProtected` |
| 動画の削除 | `VideoFileIsNotFound` / `RecordedIsProtected` |
| サムネイルの削除 | `ThumbnailIsNotFound` |
| ストリームkeep | `StreamIsUndefined` |
| Kodi送信 | `KodiHostIsUndefined` / `VideoFileIsUndefined` |
| ルール更新 | `RuleIsNotFound` |
| 動画duration取得 | `VideoFileIsUndefined` |

同じresourceでも操作によって存在確認の有無が異なる点を、上流どおり維持します。

## 予約・ルール

- ルール追加・更新はキーワード、検索対象、放送波・局、ジャンル、時間帯、長さ、重複回避、エンコード設定を検証
- 手動予約の追加・編集はエンコードモードと保存先の組み合わせを検証
- 番組指定予約の重複、終了済み時刻指定予約、同一時刻指定予約を上流と同じエラーで拒否
- 手動予約はルール予約より優先
- 手動予約同士は時刻指定、作成順を優先
- ルール予約同士は小さいruleIdを優先
- 同じ物理チャンネルの複数serviceは1チューナーへ相乗り可能
- チューナーは上流と同じfirst-fitで割り当て
- ルールの重複回避は番組名と放送局の両方で判定

手動予約とルール予約が同じ番組を掴んだ場合、ルール生成側では除外せず、上流と同じく両方を保持します。

## 録画・ファイル

- 録画中のrecorded削除は、録画を停止してから削除
- protected recordedとその動画は削除不可
- `POST /api/recorded/cleanup`はDB上の欠損行と保存先の孤立ファイル・空ディレクトリを双方向に整理
- IPTVは映像・音声以外のserviceを除外し、ロゴなし局へ`text-logo`を付けず、番組なし局をXMLTVから除外
- `GET /api/config`の`streamConfig`は実際に利用可能な配信方式から生成
- `live.ts.m2ts`は常に無変換1モード

## 一次資料

- `api.yml`
- `api.d.ts`
- `src/model/service/api/*.ts`
- `src/model/api/*`
- `src/model/service/ServiceServer.ts`

互換性に関わる変更では、上記の定義だけでなくEPGStationの実ランタイムとソース上の分岐を確認します。
