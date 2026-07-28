namespace TNLAStation.Domain;

/// <summary>
/// 受信の取りこぼしの数。
/// </summary>
public sealed record TransportStreamDefects(long ErrorCount, long DropCount, long ScramblingCount);

/// <summary>
/// 録画しながら MPEG-TS の取りこぼしを数える。
///
/// 受信の質は録った後では分からない。再生して初めて音が飛ぶことに気づくのでは遅いので、
/// 書き込みと同時に数え、後から「これは信用できる録画か」を判断できるようにする。
///
/// 保存する中身には触れない。落ちたパケットを補うことはできないし、直せない以上、
/// 数えることが唯一できることになる。
/// </summary>
public sealed class TransportStreamAnalyzer
{
    /// <summary>MPEG-TS のパケット長。固定。</summary>
    private const int PacketSize = 188;

    private const byte SyncByte = 0x47;

    /// <summary>PID ごとの直前の連続性指標。-1 はまだ受け取っていない。</summary>
    private readonly int[] lastContinuity = CreateContinuityTable();

    private readonly byte[] carry = new byte[PacketSize];
    private int carryLength;

    public long ErrorCount { get; private set; }

    public long DropCount { get; private set; }

    public long ScramblingCount { get; private set; }

    public TransportStreamDefects Defects => new(ErrorCount, DropCount, ScramblingCount);

    /// <summary>
    /// 受け取った分を数える。境界をまたいだ端数は次の呼び出しへ持ち越す。読み込みの
    /// 区切りはパケットの区切りと一致しないので、持ち越さないと数え落とす。
    /// </summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (carryLength > 0)
        {
            int needed = Math.Min(PacketSize - carryLength, data.Length);
            data[..needed].CopyTo(carry.AsSpan(carryLength));
            carryLength += needed;
            data = data[needed..];
            if (carryLength < PacketSize)
            {
                return;
            }

            Inspect(carry);
            carryLength = 0;
        }

        while (data.Length >= PacketSize)
        {
            if (data[0] != SyncByte)
            {
                // 同期を見失った。次の同期まで読み飛ばす。ここで数えないのは、失われた
                // 量が分からないため。分からないものを数字にすると、その数字が嘘になる。
                int next = data[1..].IndexOf(SyncByte);
                if (next < 0)
                {
                    return;
                }

                data = data[(next + 1)..];
                continue;
            }

            Inspect(data[..PacketSize]);
            data = data[PacketSize..];
        }

        if (data.Length > 0)
        {
            data.CopyTo(carry);
            carryLength = data.Length;
        }
    }

    private void Inspect(ReadOnlySpan<byte> packet)
    {
        if ((packet[1] & 0x80) != 0)
        {
            ErrorCount++;
            return;
        }

        int pid = ((packet[1] & 0x1f) << 8) | packet[2];
        if (pid is 0x1fff)
        {
            return;
        }

        if ((packet[3] & 0xc0) != 0)
        {
            ScramblingCount++;
        }

        bool hasPayload = (packet[3] & 0x10) != 0;
        int continuity = packet[3] & 0x0f;
        int previous = lastContinuity[pid];
        lastContinuity[pid] = continuity;

        if (previous < 0 || !hasPayload)
        {
            // 中身の無いパケットは指標を進めない決まりなので、飛びとは判断できない。
            return;
        }

        if (continuity == previous)
        {
            return;
        }

        int expected = (previous + 1) & 0x0f;
        if (continuity != expected)
        {
            DropCount++;
        }
    }

    private static int[] CreateContinuityTable()
    {
        int[] table = new int[0x2000];
        Array.Fill(table, -1);
        return table;
    }
}
