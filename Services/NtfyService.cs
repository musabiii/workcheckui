using System.Diagnostics;
using System.Net.Http;
using System.Text;
using WorkCheck.Models;

namespace WorkCheck.Services;

// Push notifications through an ntfy server (ntfy.sh or self-hosted): a plain POST of the text to server/topic
public class NtfyService
{
    public const string DefaultServer = "https://ntfy.sh";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public AppSettings Settings { get; set; }

    public NtfyService(AppSettings settings)
    {
        Settings = settings;
    }

    public async Task SendAsync(string text, bool silent = false)
    {
        if (!Settings.NtfyEnabled || string.IsNullOrWhiteSpace(Settings.NtfyTopic))
            return;

        try
        {
            using var response = await PostAsync(Settings.NtfyServer, Settings.NtfyTopic, text, silent);
            Debug.WriteLine($"[Ntfy] Response: {(int)response.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            Debug.WriteLine($"[Ntfy] Исключение: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> SendTestAsync(string server, string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return (false, "Укажите подписку");

        try
        {
            using var response = await PostAsync(server, topic, "🔔 Тестовое сообщение от WorkCheck", silent: false);
            if (response.IsSuccessStatusCode)
                return (true, null);
            var body = await response.Content.ReadAsStringAsync();
            return (false, $"{(int)response.StatusCode} {body}".Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return (false, ex.Message);
        }
    }

    private static Task<HttpResponseMessage> PostAsync(string server, string topic, string text, bool silent)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, TopicUrl(server, topic))
        {
            Content = new StringContent(text, Encoding.UTF8, "text/plain")
        };
        // Header values must be ASCII, so the title stays in Latin
        request.Headers.Add("Title", "WorkCheck");
        if (silent)
            request.Headers.Add("Priority", "low");
        return Http.SendAsync(request);
    }

    public static string TopicUrl(string server, string topic)
    {
        var baseUrl = string.IsNullOrWhiteSpace(server) ? DefaultServer : server.Trim().TrimEnd('/');
        if (!baseUrl.Contains("://"))
            baseUrl = "https://" + baseUrl;
        return $"{baseUrl}/{Uri.EscapeDataString(topic.Trim())}";
    }
}
