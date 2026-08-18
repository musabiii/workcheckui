using System.Diagnostics;
using System.Net.Http;
using WorkCheck.Models;

namespace WorkCheck.Services;

public class TelegramService
{
    private static readonly HttpClient Http = new();

    public AppSettings Settings { get; set; }

    public TelegramService(AppSettings settings)
    {
        Settings = settings;
    }

    public async Task SendAsync(string text, bool silent = false)
    {
        if (!Settings.TelegramEnabled
            || string.IsNullOrWhiteSpace(Settings.TelegramBotToken)
            || string.IsNullOrWhiteSpace(Settings.TelegramChatId))
            return;

        try
        {
            var url = $"https://api.telegram.org/bot{Settings.TelegramBotToken}/sendMessage";
            var parameters = new Dictionary<string, string>
            {
                ["chat_id"] = Settings.TelegramChatId,
                ["text"] = text,
                ["disable_notification"] = silent.ToString().ToLower()
            };

            using var response = await Http.PostAsync(url, new FormUrlEncodedContent(parameters));
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[Telegram] Ошибка {response.StatusCode}: {body}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Telegram] Исключение: {ex.Message}");
        }
    }

    public async Task<(bool Success, string? Error)> SendTestAsync(string token, string chatId)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
            return (false, "Укажите токен и Chat ID");

        try
        {
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var parameters = new Dictionary<string, string>
            {
                ["chat_id"] = chatId,
                ["text"] = "🔔 Тестовое сообщение от WorkCheck"
            };

            using var response = await Http.PostAsync(url, new FormUrlEncodedContent(parameters));
            if (response.IsSuccessStatusCode)
                return (true, null);

            var body = await response.Content.ReadAsStringAsync();
            return (false, $"{(int)response.StatusCode} {response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
