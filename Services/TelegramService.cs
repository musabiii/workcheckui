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
}
