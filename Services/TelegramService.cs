using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using WorkCheck.Models;

namespace WorkCheck.Services;

public class TelegramService
{
    private static HttpClient? _httpClientDirect;
    private static HttpClient? _httpClientProxy;
    private static readonly object _lock = new();
    private static string _lastProxyUrl = "";

    public AppSettings Settings { get; set; }

    public TelegramService(AppSettings settings)
    {
        Settings = settings;
    }

    private HttpClient GetHttpClient(string proxyUrl)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(proxyUrl))
            {
                if (_httpClientProxy != null && _lastProxyUrl == proxyUrl)
                    return _httpClientProxy;

                _httpClientProxy?.Dispose();
                _httpClientProxy = CreateSocks5HttpClient(proxyUrl);
                _lastProxyUrl = proxyUrl;
                return _httpClientProxy;
            }
            else
            {
                if (_httpClientDirect != null)
                    return _httpClientDirect;

                _httpClientDirect = new HttpClient
                {
                    DefaultRequestVersion = HttpVersion.Version20,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
                };
                return _httpClientDirect;
            }
        }
    }

    private static HttpClient CreateSocks5HttpClient(string proxyUrl)
    {
        var proxy = ParseProxyUrl(proxyUrl);
        Debug.WriteLine($"[Telegram] SOCKS5 HttpClient: host={proxy.Host}, port={proxy.Port}, user={proxy.Username}");

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(proxy.Host, proxy.Port, cancellationToken);
                var stream = tcpClient.GetStream();

                // SOCKS5 greeting
                await stream.WriteAsync(new byte[] { 0x05, 0x02, 0x00, 0x02 });
                var greetResp = new byte[2];
                await ReadFullAsync(stream, greetResp);
                if (greetResp[0] != 0x05) throw new Exception("Invalid SOCKS5 response");

                // Authenticate if required
                if (greetResp[1] == 0x02 && proxy.Username != null)
                {
                    Debug.WriteLine($"[Telegram] SOCKS5: authenticating as '{proxy.Username}'");
                    var userBytes = Encoding.UTF8.GetBytes(proxy.Username);
                    var passBytes = Encoding.UTF8.GetBytes(proxy.Password ?? "");
                    var authPacket = new byte[3 + userBytes.Length + passBytes.Length];
                    authPacket[0] = 0x01;
                    authPacket[1] = (byte)userBytes.Length;
                    Buffer.BlockCopy(userBytes, 0, authPacket, 2, userBytes.Length);
                    authPacket[2 + userBytes.Length] = (byte)passBytes.Length;
                    Buffer.BlockCopy(passBytes, 0, authPacket, 3 + userBytes.Length, passBytes.Length);
                    await stream.WriteAsync(authPacket);
                    var authResp = new byte[2];
                    await ReadFullAsync(stream, authResp);
                    if (authResp[1] != 0x00) throw new Exception("SOCKS5 auth failed");
                    Debug.WriteLine("[Telegram] SOCKS5: auth SUCCESS");
                }
                else if (greetResp[1] != 0x00 && greetResp[1] != 0xFF)
                {
                    throw new Exception("SOCKS5: требуется аутентификация без учётных данных");
                }

                // CONNECT request
                var hostBytes = Encoding.UTF8.GetBytes(context.DnsEndPoint.Host);
                var connectReq = new byte[4 + 1 + hostBytes.Length + 2];
                connectReq[0] = 0x05; connectReq[1] = 0x01; connectReq[2] = 0x00; connectReq[3] = 0x03;
                connectReq[4] = (byte)hostBytes.Length;
                Buffer.BlockCopy(hostBytes, 0, connectReq, 5, hostBytes.Length);
                connectReq[5 + hostBytes.Length] = (byte)(context.DnsEndPoint.Port >> 8);
                connectReq[5 + hostBytes.Length + 1] = (byte)(context.DnsEndPoint.Port & 0xFF);
                await stream.WriteAsync(connectReq);

                // Read CONNECT response (need max 10 bytes: 4+1+255+2)
                var connectResp = new byte[10];
                await ReadFullAsync(stream, connectResp);
                if (connectResp[1] != 0x00) throw new Exception($"SOCKS5 CONNECT failed, code={connectResp[1]}");
                Debug.WriteLine("[Telegram] SOCKS5: CONNECT success");

                return new NetworkStream(tcpClient.Client, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "api.telegram.org",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        return httpClient;
    }

    private static async Task ReadFullAsync(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset));
            if (read == 0) break;
            offset += read;
        }
    }

    private sealed record ProxyInfo(string Host, int Port, string? Username, string? Password);

    private static ProxyInfo ParseProxyUrl(string proxyUrl)
    {
        var s = proxyUrl.Trim();

        var schemeIdx = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx >= 0)
            s = s[(schemeIdx + 3)..];

        string? username = null;
        string? password = null;

        var atIdx = s.LastIndexOf('@');
        if (atIdx >= 0)
        {
            var userInfo = s[..atIdx];
            s = s[(atIdx + 1)..];

            var colonIdx = userInfo.IndexOf(':');
            if (colonIdx >= 0)
            {
                username = userInfo[..colonIdx];
                password = userInfo[(colonIdx + 1)..];
            }
            else
            {
                username = userInfo;
            }
        }

        var portIdx = s.LastIndexOf(':');
        if (portIdx < 0 || !int.TryParse(s[(portIdx + 1)..], out var port))
            throw new FormatException("Неверный формат прокси. Ожидается host:port или user:pass@host:port");

        var host = s[..portIdx];
        return new ProxyInfo(host, port, username, password);
    }

    public async Task SendAsync(string text, bool silent = false)
    {
        if (!Settings.TelegramEnabled
            || string.IsNullOrWhiteSpace(Settings.TelegramBotToken)
            || string.IsNullOrWhiteSpace(Settings.TelegramChatId))
            return;

        try
        {
            var url = $"https://api.telegram.org/bot{Settings.TelegramBotToken}/sendMessage?chat_id={Settings.TelegramChatId}&text={Uri.EscapeDataString(text)}";
            if (silent)
                url += "&disable_notification=true";

            var client = GetHttpClient(Settings.ProxyUrl);
            var response = await client.GetStringAsync(url);
            Debug.WriteLine($"[Telegram] Response: {response[..Math.Min(200, response.Length)]}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Telegram] Исключение: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> SendTestAsync(string token, string chatId, string proxyUrl = "")
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            return (false, "Укажите токен и Chat ID");

        try
        {
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var client = GetHttpClient(proxyUrl);

            var body = new StringContent($"{{\"chat_id\":\"{chatId}\",\"text\":\"🔔 Тестовое сообщение от WorkCheck\"}}", Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(url, body);
            var content = await response.Content.ReadAsStringAsync();

            if (content.Contains("\"ok\":true"))
                return (true, null);
            return (false, content);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
