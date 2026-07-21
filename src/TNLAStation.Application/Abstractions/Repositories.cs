using TNLAStation.Application.Models;
using TNLAStation.Domain;

namespace TNLAStation.Application.Abstractions;

public interface IConfigRepository
{
    ValueTask<StationConfiguration> GetAsync(CancellationToken cancellationToken);
}

public interface IRecordedRepository
{
    ValueTask<Page<RecordedProgram>> ListAsync(RecordedQuery query, CancellationToken cancellationToken);

    ValueTask<long> AddAsync(CreateRecordedCommand command, CancellationToken cancellationToken);
}

public interface IReserveRepository
{
    ValueTask<Page<Reservation>> ListAsync(ReserveQuery query, CancellationToken cancellationToken);

    ValueTask<long> AddAsync(CreateReserveCommand command, CancellationToken cancellationToken);

    ValueTask<Reservation?> GetAsync(long reserveId, CancellationToken cancellationToken);

    /// <summary>
    /// 予約を取り消す。手動予約は消えるが、ルールが作った予約は消しても次の生成で戻ってくる
    /// ので、除外として残す。上流も同じで、画面の「削除」はこの 2 つを兼ねている。
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
/// 予約生成が読み書きする面。手動予約と skip は入力、予約一覧は出力。
/// </summary>
public interface IReserveStore
{
    ValueTask<IReadOnlyList<ManualReserve>> ListManualReservesAsync(CancellationToken cancellationToken);

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
    /// </summary>
    ValueTask<int> CleanupAsync(CancellationToken cancellationToken);
}

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
    /// Throws when the rule is gone, which EPGStation surfaces as a 500 carrying "RuleIsNull".
    /// </summary>
    ValueTask UpdateAsync(RecordingRule rule, CancellationToken cancellationToken);

    /// <inheritdoc cref="UpdateAsync"/>
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
