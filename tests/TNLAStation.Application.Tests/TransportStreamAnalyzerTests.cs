using TNLAStation.Domain;

namespace TNLAStation.Application.Tests;

/// <summary>
/// 取りこぼしの判定は、実際の放送では起きたときにしか確かめられない。壊れた並びを
/// 組み立てて、数えかたのほうを固定する。
/// </summary>
public sealed class TransportStreamAnalyzerTests
{
    [Fact]
    public void CleanPacketsAreNotCounted()
    {
        var analyzer = new TransportStreamAnalyzer();

        analyzer.Append(Stream(Packet(0x100, continuity: 0), Packet(0x100, 1), Packet(0x100, 2)));

        Assert.Equal(new TransportStreamDefects(0, 0, 0), analyzer.Defects);
    }

    [Fact]
    public void AGapInTheContinuityCounterIsADrop()
    {
        var analyzer = new TransportStreamAnalyzer();

        analyzer.Append(Stream(Packet(0x100, 0), Packet(0x100, 1), Packet(0x100, 4)));

        Assert.Equal(1, analyzer.DropCount);
    }

    [Fact]
    public void TheCounterWrapsAroundWithoutBeingCountedAsADrop()
    {
        var analyzer = new TransportStreamAnalyzer();

        analyzer.Append(Stream(Packet(0x100, 15), Packet(0x100, 0)));

        Assert.Equal(0, analyzer.DropCount);
    }

    [Fact]
    public void ARepeatedPacketIsNotADrop()
    {
        var analyzer = new TransportStreamAnalyzer();

        analyzer.Append(Stream(Packet(0x100, 3), Packet(0x100, 3)));

        Assert.Equal(0, analyzer.DropCount);
    }

    [Fact]
    public void EachPidIsCountedOnItsOwn()
    {
        var analyzer = new TransportStreamAnalyzer();

        // 別の PID が挟まっても、それぞれの並びは途切れていない。
        analyzer.Append(Stream(
            Packet(0x100, 0),
            Packet(0x101, 7),
            Packet(0x100, 1),
            Packet(0x101, 8)));

        Assert.Equal(0, analyzer.DropCount);
    }

    [Fact]
    public void TheErrorIndicatorIsCounted()
    {
        var analyzer = new TransportStreamAnalyzer();

        analyzer.Append(Stream(Packet(0x100, 0, hasError: true)));

        Assert.Equal(1, analyzer.ErrorCount);
        Assert.Equal(0, analyzer.DropCount);
    }

    [Fact]
    public void PacketsThatStayScrambledAreCounted()
    {
        var analyzer = new TransportStreamAnalyzer();

        analyzer.Append(Stream(Packet(0x100, 0, isScrambled: true)));

        Assert.Equal(1, analyzer.ScramblingCount);
    }

    [Fact]
    public void PaddingIsIgnored()
    {
        var analyzer = new TransportStreamAnalyzer();

        analyzer.Append(Stream(Packet(0x1fff, 0), Packet(0x1fff, 5)));

        Assert.Equal(new TransportStreamDefects(0, 0, 0), analyzer.Defects);
    }

    [Fact]
    public void PacketsSplitAcrossTwoReadsAreStillCounted()
    {
        var analyzer = new TransportStreamAnalyzer();
        byte[] stream = Stream(Packet(0x100, 0), Packet(0x100, 2));

        // 読み込みの区切りはパケットの区切りと一致しない。持ち越さないと数え落とす。
        analyzer.Append(stream.AsSpan(0, 200));
        analyzer.Append(stream.AsSpan(200));

        Assert.Equal(1, analyzer.DropCount);
    }

    [Fact]
    public void PacketsWithoutAPayloadDoNotAdvanceTheCounter()
    {
        var analyzer = new TransportStreamAnalyzer();

        // 中身の無いパケットは指標を進めない決まりなので、飛びとは判断できない。
        analyzer.Append(Stream(Packet(0x100, 0), Packet(0x100, 0, hasPayload: false), Packet(0x100, 1)));

        Assert.Equal(0, analyzer.DropCount);
    }

    [Fact]
    public void DropsAreFoundInAStreamThatLooksLikeARealBroadcast()
    {
        // 実際の放送は、複数の PID と詰め物と中身の無いパケットが混ざって流れてくる。
        // 小さな並びだけで確かめると、混ざったときに数え違える形の誤りを見逃す。
        var packets = new List<byte[]>();
        int[] pids = [0x100, 0x101, 0x110, 0x1fff];
        var continuity = new Dictionary<int, int>();
        for (int index = 0; index < 4_000; index++)
        {
            int pid = pids[index % pids.Length];
            continuity.TryGetValue(pid, out int counter);
            continuity[pid] = (counter + 1) & 0x0f;
            packets.Add(Packet(pid, counter, hasPayload: index % 37 != 0));
        }

        var clean = new TransportStreamAnalyzer();
        clean.Append(Stream([.. packets]));
        Assert.Equal(new TransportStreamDefects(0, 0, 0), clean.Defects);

        // 3 つ落とす。落ちた PID の並びだけが途切れる。
        packets.RemoveAt(2_500);
        packets.RemoveAt(1_500);
        packets.RemoveAt(500);

        var damaged = new TransportStreamAnalyzer();
        damaged.Append(Stream([.. packets]));
        Assert.Equal(3, damaged.DropCount);
        Assert.Equal(0, damaged.ErrorCount);
    }

    private static byte[] Stream(params byte[][] packets)
    {
        byte[] result = new byte[packets.Sum(packet => packet.Length)];
        int offset = 0;
        foreach (byte[] packet in packets)
        {
            packet.CopyTo(result, offset);
            offset += packet.Length;
        }

        return result;
    }

    private static byte[] Packet(
        int pid,
        int continuity,
        bool hasError = false,
        bool isScrambled = false,
        bool hasPayload = true)
    {
        byte[] packet = new byte[188];
        packet[0] = 0x47;
        packet[1] = (byte)(((hasError ? 1 : 0) << 7) | ((pid >> 8) & 0x1f));
        packet[2] = (byte)(pid & 0xff);
        packet[3] = (byte)((isScrambled ? 0x80 : 0x00) | (hasPayload ? 0x10 : 0x20) | (continuity & 0x0f));
        return packet;
    }
}
