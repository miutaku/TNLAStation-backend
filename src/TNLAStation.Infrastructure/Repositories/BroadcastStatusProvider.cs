using TNLAStation.Application.Abstractions;
using TNLAStation.Domain;
using TNLAStation.Infrastructure.Mirakurun;

namespace TNLAStation.Infrastructure.Repositories;

/// <summary>
/// Mirakurun のチューナー一覧から放送波の有無を作る。
///
/// 上流 (<c>EPGStation/src/index.ts</c> の <c>runOperator</c>) は起動時に 1 度だけ
/// <c>client.getTuners()</c> を読み、<c>ReservationManageModel.setTuners</c> が
/// <c>types</c> を種別ごとに OR して保持する。チューナーの故障や使用中は見ない。
/// ここも同じく「1 度取れたらその結果を持ち続ける」形にしてある — 起動時に Mirakurun が
/// 落ちていても 500 にせず、取れるまで全 false を返す点だけが違う。
/// </summary>
public sealed class MirakurunBroadcastStatusProvider(IMirakurunClient mirakurun) : IBroadcastStatusProvider, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private BroadcastAvailability? cached;

    public async ValueTask<BroadcastAvailability> GetAsync(CancellationToken cancellationToken)
    {
        if (cached is { } current)
        {
            return current;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is { } existing)
            {
                return existing;
            }

            IReadOnlyList<MirakurunTunerDto> tuners = await mirakurun.GetTunersAsync(cancellationToken);
            bool Has(string type) => tuners.Any(tuner =>
                tuner.Types.Contains(type, StringComparer.Ordinal));

            cached = new BroadcastAvailability(Has("GR"), Has("BS"), Has("CS"), Has("SKY"));
            return cached;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            // チューナーがまだ見えていないだけ。次の呼び出しでもう一度試す。
            return AllFalse;
        }
        finally
        {
            gate.Release();
        }
    }

    private static BroadcastAvailability AllFalse { get; } = new(false, false, false, false);

    public void Dispose() => gate.Dispose();
}

/// <summary>Mirakurun に繋がっていない構成。チューナーが 1 本も無いので全部 false。</summary>
public sealed class EmptyBroadcastStatusProvider : IBroadcastStatusProvider
{
    private static readonly BroadcastAvailability None = new(false, false, false, false);

    public ValueTask<BroadcastAvailability> GetAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(None);
}
