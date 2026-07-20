using TNLAStation.Application.Abstractions;

namespace TNLAStation.Infrastructure.Reserves;

/// <summary>
/// チューナーに繋がっていない構成。予約を作る材料が無いので、依頼は受けるが何も起きない。
/// </summary>
public sealed class NoReserveGenerationTrigger : IReserveGenerationTrigger
{
    public ValueTask RequestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
