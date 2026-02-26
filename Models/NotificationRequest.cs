namespace WorkCheck.Models;

public enum NotificationType
{
    Welcome,
    Pomodoro,
    Pomodoro2,
    ShortBreak,
    Inactivity
}

public class NotificationRequest
{
    public required NotificationType Type { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public string? SecondaryMessage { get; init; }
    public string? Quote { get; init; }
    public bool SendTelegram { get; init; }
    public string? TelegramText { get; init; }
    public bool SilentTelegram { get; init; }
}
