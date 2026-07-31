using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Application.Abstractions;

public interface IConfigRepository
{
    /// <summary>
    /// <paramref name="isSecure"/> は https でのアクセスか。socket.io のポートの解決先が変わる
    /// (EPGStation の <c>ConfigApiModel.getConfig(isSecure)</c> と同じ)。
    /// </summary>
    ValueTask<StationConfiguration> GetAsync(bool isSecure, CancellationToken cancellationToken);
}

/// <summary>
/// <c>/api/config</c> の <c>broadcast</c>。EPGStation は起動時に Mirakurun の
/// <c>/api/tuners</c> を 1 度読み、各チューナーの <c>types</c> を種別ごとに OR したものを返す
/// (<c>ReservationManageModel.setTuners</c>)。故障・使用中の区別はしない。
/// </summary>
public interface IBroadcastStatusProvider
{
    ValueTask<BroadcastAvailability> GetAsync(CancellationToken cancellationToken);
}

public interface IRecordedRepository
{
    ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken);

    ValueTask<long> AddAsync(CreateRecordedCommand command, CancellationToken cancellationToken);

    /// <summary>
    /// 保護されていない録画のうち、行 id がいちばん小さいものの id。空き容量が足りないときに
    /// 何を消すかを決めるためだけに使う。無ければ null。
    ///
    /// EPGStation の <c>RecordedDB.findOld()</c> をそのまま写している。保存先で絞らず (どの保存先が
    /// 足りなくても全体から選ぶ)、録画中も除外しない。並びは <c>orderBy</c> を 2 回呼んでいて
    /// TypeORM では後の呼び出しが前を置き換えるため、実際には <c>recorded.id ASC</c> だけが効く
    /// — 開始時刻順ではなく登録順になる。
    /// </summary>
    ValueTask<long?> FindOldestUnprotectedAsync(CancellationToken cancellationToken);
}

public interface IReserveRepository
{
    ValueTask<Page<Reservation>> ListAsync(ReserveQuery query, CancellationToken cancellationToken);

    ValueTask<long> AddAsync(CreateReserveCommand command, CancellationToken cancellationToken);

    ValueTask<Reservation?> GetAsync(long reserveId, CancellationToken cancellationToken);

    /// <summary>
    /// 予約を取り消す。手動予約は消えるが、ルールが作った予約は消しても次の生成で戻ってくる
    /// ので、除外として残す。EPGStation も同じで、画面の「削除」はこの 2 つを兼ねている。
    /// </summary>
    ValueTask<bool> DeleteAsync(long reserveId, CancellationToken cancellationToken);

    /// <summary>
    /// 録る・録らないの指定。予約の行ではなく、作り直しても変わらない鍵に紐づけて残す。
    /// </summary>
    ValueTask<bool> SetSkipAsync(long reserveId, bool isSkip, CancellationToken cancellationToken);

    /// <summary>
    /// 重複と判断された予約を、それでも録る。判断が外れることはあるので人が覆せるようにし、
    /// 覆した事実も鍵に紐づけて残す。残さないと次の生成でまた重複に戻る。
    /// </summary>
    ValueTask<bool> ClearOverlapAsync(long reserveId, CancellationToken cancellationToken);

    /// <summary>手動予約の内容を差し替える。</summary>
    ValueTask<bool> UpdateAsync(long reserveId, CreateReserveCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// 予約の作り直しを頼む。定期実行を待たずに反映したい場面 (ルールを変えた直後など) がある。
/// </summary>
public interface IReserveGenerationTrigger
{
    ValueTask RequestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 録画スケジューラへ、予約表を今すぐ見直すよう通知する。
/// 予約追加直後の録画開始を、次の定期ポーリングまで待たせないための通知面。
/// </summary>
public interface IRecordingScheduleTrigger
{
    ValueTask RequestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 保存されている予約 1 行そのもの。番組表と結合する前の値なので、番組の側が書き換わっても
/// 前回生成したときの内容が読める。フックの差分判定はこれで行う — 結合後の値で比べると、
/// 番組名が変わったことが「変化なし」に見えてしまい、更新フックが鳴らない。
/// </summary>
public sealed record StoredReserve(
    string Key,
    long Id,
    long? ProgramId,
    long ChannelId,
    string ChannelType,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Name,
    string HalfWidthName,
    bool IsSkip);

/// <summary>
/// 予約生成が読み書きする面。手動予約と skip は入力、予約一覧は出力。
/// </summary>
public interface IReserveStore
{
    ValueTask<IReadOnlyList<ManualReserve>> ListManualReservesAsync(CancellationToken cancellationToken);

    /// <summary>保存されている予約を、生成のたびに変わらない鍵つきでそのまま返す。</summary>
    ValueTask<IReadOnlyList<StoredReserve>> ListStoredAsync(CancellationToken cancellationToken);

    ValueTask<ReserveStates> ListStatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 生成した予約で丸ごと置き換える。番組表が変われば録るものも変わるので、差分ではなく
    /// 作り直す。手動予約そのものはこの入れ替えでは消えない。
    /// </summary>
    ValueTask ReplaceAsync(
        IReadOnlyList<ReserveAssignment> assignments,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// 録画中の番組。録画機構はこれから作るため、いまは常に空を返す実装しかない。
/// 空であることは正常な状態なので、呼び出し側は「無い」ことをエラーとして扱わない。
/// </summary>
public interface IRecordingRepository
{
    ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// エンコードの実行中と待ち行列。
/// </summary>
public interface IEncodeQueueRepository
{
    ValueTask<EncodeTasks> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 視聴・配信セッション。
/// </summary>
public interface IStreamRepository
{
    ValueTask<IReadOnlyList<StreamSession>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 録画へ付ける tag。
/// </summary>
public interface IRecordedTagRepository
{
    ValueTask<Page<RecordedTag>> ListAsync(RecordedTagQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// 録画 1 件を読み書きする面。一覧とは分ける。一覧は保存先を持たない構成でも空で返せるが、
/// 1 件の取得や削除は、録画を実際に持っている実装でしか意味を成さない。
/// </summary>
public interface IRecordedItemRepository
{
    ValueTask<RecordedProgram?> GetAsync(long recordedId, CancellationToken cancellationToken);

    /// <summary>
    /// 録画とファイルを消す。行だけ消すと、どこからも辿れないファイルが容量を食い続ける。
    /// </summary>
    ValueTask<bool> DeleteAsync(long recordedId, CancellationToken cancellationToken);

    ValueTask<bool> SetProtectedAsync(long recordedId, bool isProtected, CancellationToken cancellationToken);

    /// <summary>
    /// 実体が無くなった録画を片付ける。外からファイルを消しても、行だけ残って一覧に並ぶ。
    /// 保護されているものは残す。人が残すと決めたものを、こちらの判断で消さない。
    /// 逆方向 (保存先にあるが DB に登録されていないファイル) も掃除する
    /// (EPGStation の videoFileCleanup 相当)。
    /// </summary>
    ValueTask<RecordedCleanupResult> CleanupAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IRecordedItemRepository.CleanupAsync"/> の結果。EPGStation は件数を返さないが、
/// 呼び出した側に何が起きたか伝えられたほうが親切なので、TNLAStation では両方向の件数を返す。
/// </summary>
public sealed record RecordedCleanupResult(int RemovedRecordedRows, int RemovedOrphanFiles);

/// <summary>
/// tag の作成と付け外し。
/// </summary>
public interface IRecordedTagWriteRepository
{
    ValueTask<long> AddTagAsync(string name, string color, CancellationToken cancellationToken);

    ValueTask<bool> UpdateTagAsync(long tagId, string name, string color, CancellationToken cancellationToken);

    ValueTask<bool> DeleteTagAsync(long tagId, CancellationToken cancellationToken);

    ValueTask<bool> SetTagAsync(
        long recordedId,
        long tagId,
        bool attached,
        CancellationToken cancellationToken);
}

public interface IRuleRepository
{
    ValueTask<Page<RecordingRule>> ListAsync(RuleQuery query, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<RuleKeywordItem>> ListKeywordsAsync(
        RuleQuery query,
        CancellationToken cancellationToken);

    ValueTask<RecordingRule?> GetAsync(long ruleId, CancellationToken cancellationToken);

    ValueTask<long> AddAsync(RecordingRule rule, CancellationToken cancellationToken);

    /// <summary>
    /// Throws when the rule is gone, which EPGStation surfaces as a 500 carrying "RuleIsNotFound".
    /// </summary>
    ValueTask UpdateAsync(RecordingRule rule, CancellationToken cancellationToken);

    /// <summary>
    /// Unlike <see cref="UpdateAsync"/>, EPGStation's enable/disable do not check existence —
    /// toggling a rule that is already gone silently does nothing and still answers 200.
    /// </summary>
    ValueTask SetEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken);

    /// <summary>
    /// Deleting a rule that is already gone succeeds, matching EPGStation.
    /// </summary>
    ValueTask DeleteAsync(long ruleId, CancellationToken cancellationToken);
}

public interface IStorageRepository
{
    ValueTask<IReadOnlyList<StorageUsage>> ListAsync(CancellationToken cancellationToken);
}

public interface IVersionRepository
{
    ValueTask<string> GetAsync(CancellationToken cancellationToken);
}
