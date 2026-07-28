using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TNLAStation.Application.Abstractions;
using TNLAStation.Infrastructure.Configuration;

namespace TNLAStation.Infrastructure.Kodi;

public sealed class KodiClient(HttpClient httpClient, IOptions<KodiOptions> options) : IKodiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly KodiOptions options = options.Value;

    public IReadOnlyList<string> HostNames => [.. options.ConfiguredHosts.Select(host => host.Name)];

    public string? PublicBaseUrl =>
        string.IsNullOrWhiteSpace(options.PublicBaseUrl) ? null : options.PublicBaseUrl;

    public async ValueTask<bool> PlayAsync(string hostName, string url, CancellationToken cancellationToken)
    {
        KodiHostOptions? host = options.ConfiguredHosts.FirstOrDefault(
            item => string.Equals(item.Name, hostName, StringComparison.Ordinal));
        if (host is null)
        {
            return false;
        }

        // 長さを決めてから送る。長さの分からない本文を受け付けない組み込みの HTTP サーバーが
        // あり、Kodi のものもそれに当たる。
        string body = JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "Player.Open",
                @params = new { item = new { file = url } },
            },
            JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, host.Url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(host.User))
        {
            string credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{host.User}:{host.Password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return true;
    }
}
