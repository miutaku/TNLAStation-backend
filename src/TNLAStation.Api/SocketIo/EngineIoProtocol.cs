using System.Globalization;
using System.Text.Json;

namespace TNLAStation.Api.SocketIo;

/// <summary>
/// Engine.IO v4 / Socket.IO v5 のパケット表現。socket.io 4.7.5 (上流が使っている版) の
/// クライアントがそのまま繋がる形にしてある。
///
/// Engine.IO の種別: 0=open 1=close 2=ping 3=pong 4=message 5=upgrade 6=noop。
/// message の中身が Socket.IO のパケットで、その種別は 0=CONNECT 1=DISCONNECT 2=EVENT。
/// long-polling で複数を 1 応答にまとめるときの区切りは U+001E (record separator)。
/// </summary>
internal static class EngineIoProtocol
{
    public const char PayloadSeparator = '';

    /// <summary>上流 (socket.io の既定値) と同じ。クライアントはこの値で ping を待つ。</summary>
    public const int PingIntervalMs = 25_000;

    public const int PingTimeoutMs = 20_000;

    public const int MaxPayload = 1_000_000;

    public const string Ping = "2";

    public const string Pong = "3";

    public const string Noop = "6";

    public const string ProbePing = "2probe";

    public const string ProbePong = "3probe";

    public const string Upgrade = "5";

    /// <summary>engine.io の open パケット。<c>upgrades</c> は long-polling のときだけ意味がある。</summary>
    public static string Open(string sid, bool canUpgrade)
    {
        string upgrades = canUpgrade ? "[\"websocket\"]" : "[]";
        return "0{" +
            $"\"sid\":\"{sid}\"," +
            $"\"upgrades\":{upgrades}," +
            $"\"pingInterval\":{PingIntervalMs.ToString(CultureInfo.InvariantCulture)}," +
            $"\"pingTimeout\":{PingTimeoutMs.ToString(CultureInfo.InvariantCulture)}," +
            $"\"maxPayload\":{MaxPayload.ToString(CultureInfo.InvariantCulture)}" +
            "}";
    }

    /// <summary>名前空間 "/" への接続確認。socket.io v5 は CONNECT の応答に socket の id を返す。</summary>
    public static string Connect(string socketId) => "40{\"sid\":\"" + socketId + "\"}";

    /// <summary>payload を持たないイベント。上流の <c>io.sockets.emit('updateStatus')</c> と同じ形。</summary>
    public static string Event(string name) => "42" + JsonSerializer.Serialize(new[] { name });

    public static string EncodePayload(IReadOnlyList<string> packets) =>
        string.Join(PayloadSeparator, packets);

    public static IEnumerable<string> DecodePayload(string payload) =>
        payload.Split(PayloadSeparator, StringSplitOptions.RemoveEmptyEntries);
}
