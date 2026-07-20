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
